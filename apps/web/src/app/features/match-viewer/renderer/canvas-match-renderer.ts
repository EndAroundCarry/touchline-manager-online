import { HighlightEntity, HighlightKeyframe, Passage } from '../../../core/match/match.models';
import { frameAt, sampleAt } from './keyframe-interpolator';
import {
  PitchGeometry,
  SHADOW_MIN_ALTITUDE,
  altitudeLift,
  altitudeScale,
  pitchGeometry,
  pitchRect,
  toCanvasPoint,
} from './pitch-layout';
import {
  celebrationStartMilliseconds,
  isDiveAction,
  isStrikeAction,
  nearestPlayerToBall,
  trailStrength,
  wantsTrail,
} from './renderer-effects';
import {
  CardKind,
  FrameEntity,
  FrameMetrics,
  PitchPoint,
  PitchRect,
  RendererEntity,
  RendererOptions,
  RendererTrack,
  TeamKit,
  TeamKits,
} from './renderer.models';

/**
 * Draws one highlight on a canvas, interpolating the server's keyframes (`§9.1`, `§9.4`).
 *
 * Framework-neutral: it knows a canvas and a highlight and nothing about Angular, so the renderer can be
 * reused by a later native client and tested without a component. It scales to the device pixel ratio
 * without touching normalized geometry, and it keeps no timers of its own — whoever owns the animation loop
 * decides when a frame is drawn, which is what makes "stop on pause, route change, tab hidden,
 * destruction" something the caller can guarantee.
 *
 * Stage 6 fills the picture in: a mown pitch with the markings a real one has, kit-coloured tokens with
 * shirt numbers and name tags, distinct goalkeeper kits, a ball that lifts off its own shadow by the
 * altitude the engine sent, and the action effects — a shot's projectile streak, a
 * booking's card, a goal's pulsating flash. Teams are distinguished by shape as well as colour (a circle
 * for the home side, a square for the away side) and every player carries their shirt number, because the
 * requirement is explicit that colour is never the only signal (`§9.4`, `§11.3`).
 *
 * The only work it does outside a frame is the pointer listener that makes hovering a player raise their
 * name; that listener draws a single frame and touches no Angular state, so hovering never enters change
 * detection.
 */
export class CanvasMatchRenderer {
  private readonly context: CanvasRenderingContext2D;
  private readonly entities: readonly RendererEntity[];
  private readonly tracks: readonly RendererTrack[];
  private readonly ballTrack: readonly HighlightKeyframe[] | null;
  private readonly kits: TeamKits;
  private readonly cards: ReadonlyMap<string, CardKind>;
  private readonly reducedMotion: boolean;
  private readonly celebrateStart: number | null;

  private cssWidth = 0;
  private cssHeight = 0;
  private lastTimeMs = 0;
  private hoveredEntityId: string | null = null;
  private metricsValue: FrameMetrics = {
    entities: 0,
    interpolated: 0,
    effects: 0,
    milliseconds: 0,
  };

  /** Initializes the renderer for one passage on one canvas. */
  constructor(
    private readonly canvas: HTMLCanvasElement,
    passage: Passage,
    options: RendererOptions = {},
  ) {
    const context = canvas.getContext('2d');

    if (context === null) {
      throw new Error('A passage cannot be drawn: the canvas has no 2D context.');
    }

    this.context = context;
    this.entities = toRendererEntities(passage.entities);
    this.tracks = toRendererTracks(passage.tracks);
    this.ballTrack =
      passage.tracks.find((track) => track.entityId === BALL_ENTITY_ID)?.keyframes ?? null;
    this.kits = {
      home: kitFor(options.kits?.home, passage.homeColour),
      away: kitFor(options.kits?.away, passage.awayColour),
    };
    this.cards = options.cards ?? new Map<string, CardKind>();
    this.reducedMotion = options.reducedMotion ?? false;
    this.celebrateStart = celebrationStartMilliseconds(passage);

    this.canvas.addEventListener('pointermove', this.pointerListener);
    this.canvas.addEventListener('pointerleave', this.pointerLeaveListener);
  }

  /** Gets how much the last frame drew, for the renderer's own instrumentation. */
  get metrics(): FrameMetrics {
    return this.metricsValue;
  }

  /** Draws the highlight at an animation moment. */
  render(timeMs: number): void {
    const started = now();
    const rect = this.resize();

    this.lastTimeMs = timeMs;
    this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.drawPitch(rect);

    const frame = frameAt(this.entities, this.tracks, timeMs);
    const players = frame.filter((item) => !item.entity.isBall);
    const ball = frame.find((item) => item.entity.isBall);
    const possession = nearestPlayerToBall(frame);
    let effects = 0;

    const radius = playerRadius(rect);

    for (const player of players) {
      this.drawPlayer(rect, player, radius, ball);
    }

    if (ball !== undefined) {
      if (wantsTrail(ball.action, ball.z, ball.speed)) {
        this.drawTrail(rect, Math.max(0.4, trailStrength(ball.z, ball.speed)));
        effects += 1;
      }

      if (this.isStriking(ball, players)) {
        this.drawShotLine(rect, ball, timeMs);
        effects += 1;
      }

      this.drawBall(rect, ball);
    }

    // The tags are drawn last of the moving parts, so a name is never hidden behind a token that has run
    // into it. Possession first, then the hovered player: the pointer's own choice outranks the ball's.
    if (possession !== null) {
      this.drawNameTag(rect, possession, radius);
    }

    if (this.hoveredEntityId !== null) {
      const hovered = players.find((item) => item.entity.id === this.hoveredEntityId);

      if (hovered !== undefined) {
        this.drawHover(rect, hovered, radius);
      }
    }

    if (this.celebrateStart !== null && timeMs >= this.celebrateStart) {
      this.drawCelebration(rect, timeMs - this.celebrateStart);
      effects += 1;
    }

    this.metricsValue = {
      entities: frame.length,
      interpolated: this.tracks.length,
      effects,
      milliseconds: now() - started,
    };
  }

  /** Clears the canvas, releases the pointer listeners, so nothing it drew or held outlives the component. */
  dispose(): void {
    this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.canvas.removeEventListener('pointermove', this.pointerListener);
    this.canvas.removeEventListener('pointerleave', this.pointerLeaveListener);
    this.hoveredEntityId = null;
  }

  /**
   * Sizes the backing store to the device pixel ratio and returns the pitch's rectangle in CSS pixels.
   *
   * Geometry stays in normalized coordinates and only the transform changes, so the same highlight is drawn
   * the same way on a 1x and a 3x screen (`§9.4`).
   */
  private resize(): PitchRect {
    const cssWidth = this.canvas.clientWidth || this.canvas.width || 640;
    const cssHeight = this.canvas.clientHeight || this.canvas.height || 416;
    const ratio = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1;
    const pixelWidth = Math.max(1, Math.round(cssWidth * ratio));
    const pixelHeight = Math.max(1, Math.round(cssHeight * ratio));

    if (this.canvas.width !== pixelWidth || this.canvas.height !== pixelHeight) {
      this.canvas.width = pixelWidth;
      this.canvas.height = pixelHeight;
    }

    this.context.setTransform(ratio, 0, 0, ratio, 0, 0);

    this.cssWidth = cssWidth;
    this.cssHeight = cssHeight;

    return pitchRect(cssWidth, cssHeight);
  }

  /** Draws the pitch: mown stripes, every law-of-the-game marking, and both goals with their nets. */
  private drawPitch(rect: PitchRect): void {
    const context = this.context;
    const geometry = pitchGeometry(rect);

    context.save();

    context.fillStyle = GRASS_DARK;
    context.fillRect(rect.x, rect.y, rect.width, rect.height);

    context.fillStyle = GRASS_LIGHT;

    for (let stripe = 0; stripe < geometry.stripeCount; stripe += 1) {
      if (stripe % 2 === 0) {
        continue;
      }

      context.fillRect(
        rect.x + stripe * geometry.stripeWidth,
        rect.y,
        geometry.stripeWidth,
        rect.height,
      );
    }

    context.strokeStyle = LINE_COLOUR;
    context.lineWidth = geometry.lineWidth;

    // The touchlines, the halfway line, and the centre circle and spot.
    context.strokeRect(rect.x, rect.y, rect.width, rect.height);

    context.beginPath();
    context.moveTo(geometry.centreX, rect.y);
    context.lineTo(geometry.centreX, rect.y + rect.height);
    context.stroke();

    context.beginPath();
    context.arc(geometry.centreX, geometry.centreY, geometry.centreCircleRadius, 0, Math.PI * 2);
    context.stroke();

    context.fillStyle = LINE_COLOUR;
    context.beginPath();
    context.arc(geometry.centreX, geometry.centreY, geometry.centreSpotRadius, 0, Math.PI * 2);
    context.fill();

    this.drawEndMarkings(rect, geometry, true);
    this.drawEndMarkings(rect, geometry, false);

    // The corner arcs, quarter circles struck from each corner flag.
    const corners: readonly (readonly [number, number, number, number])[] = [
      [rect.x, rect.y, 0, Math.PI / 2],
      [rect.x + rect.width, rect.y, Math.PI / 2, Math.PI],
      [rect.x + rect.width, rect.y + rect.height, Math.PI, Math.PI * 1.5],
      [rect.x, rect.y + rect.height, Math.PI * 1.5, Math.PI * 2],
    ];

    for (const [cornerX, cornerY, from, to] of corners) {
      context.beginPath();
      context.arc(cornerX, cornerY, geometry.cornerArcRadius, from, to);
      context.stroke();
    }

    this.drawGoal(rect, geometry, true);
    this.drawGoal(rect, geometry, false);
    context.restore();
  }

  /** Draws one end's penalty area, six-yard box, penalty spot, and the penalty arc outside the box. */
  private drawEndMarkings(rect: PitchRect, geometry: PitchGeometry, isLeft: boolean): void {
    const context = this.context;
    const boxX = isLeft ? rect.x : rect.x + rect.width - geometry.penaltyBoxDepth;
    const boxY = geometry.centreY - geometry.penaltyBoxHeight / 2;
    const sixYardX = isLeft ? rect.x : rect.x + rect.width - geometry.sixYardBoxDepth;
    const sixYardY = geometry.centreY - geometry.sixYardBoxHeight / 2;
    const spotX = isLeft
      ? rect.x + geometry.penaltySpotInset
      : rect.x + rect.width - geometry.penaltySpotInset;

    context.strokeRect(boxX, boxY, geometry.penaltyBoxDepth, geometry.penaltyBoxHeight);
    context.strokeRect(sixYardX, sixYardY, geometry.sixYardBoxDepth, geometry.sixYardBoxHeight);

    context.fillStyle = LINE_COLOUR;
    context.beginPath();
    context.arc(spotX, geometry.centreY, geometry.penaltySpotRadius, 0, Math.PI * 2);
    context.fill();

    // The arc is the part of the 9.15 m circle around the spot that is outside the penalty area, so it is
    // struck between the two angles where the circle crosses the box's edge.
    const exit =
      (geometry.penaltyBoxDepth - geometry.penaltySpotInset) / geometry.centreCircleRadius;

    if (Math.abs(exit) < 1) {
      const angle = Math.acos(exit);

      context.beginPath();

      if (isLeft) {
        context.arc(spotX, geometry.centreY, geometry.centreCircleRadius, -angle, angle);
      } else {
        context.arc(
          spotX,
          geometry.centreY,
          geometry.centreCircleRadius,
          Math.PI - angle,
          Math.PI + angle,
        );
      }

      context.stroke();
    }
  }

  /** Draws one goal and its net, outside the goal line. */
  private drawGoal(rect: PitchRect, geometry: PitchGeometry, isLeft: boolean): void {
    const context = this.context;
    const top = geometry.centreY - geometry.goalWidth / 2;
    const x = isLeft ? rect.x - geometry.goalDepth : rect.x + rect.width;
    const lineWidth = geometry.lineWidth;

    context.save();
    context.fillStyle = 'rgba(255, 255, 255, 0.14)';
    context.fillRect(x, top, geometry.goalDepth, geometry.goalWidth);

    // The net: a few cross-hatched strands, enough to read as a net at any pitch size.
    context.strokeStyle = 'rgba(255, 255, 255, 0.3)';
    context.lineWidth = lineWidth * 0.5;
    context.beginPath();

    for (let strand = 1; strand < 5; strand += 1) {
      const strandX = x + (geometry.goalDepth * strand) / 5;
      const strandY = top + (geometry.goalWidth * strand) / 5;

      context.moveTo(strandX, top);
      context.lineTo(strandX, top + geometry.goalWidth);
      context.moveTo(x, strandY);
      context.lineTo(x + geometry.goalDepth, strandY);
    }

    context.stroke();

    // The posts and the crossbar, drawn heavier than the net.
    context.strokeStyle = 'rgba(255, 255, 255, 0.9)';
    context.lineWidth = Math.max(1.5, lineWidth * 1.4);
    context.strokeRect(x, top, geometry.goalDepth, geometry.goalWidth);
    context.restore();
  }

  /** Draws one player: a kit-coloured dot, a contrasting border, the shirt number, and any card. */
  private drawPlayer(
    rect: PitchRect,
    item: FrameEntity,
    radius: number,
    ball: FrameEntity | undefined,
  ): void {
    const context = this.context;
    const isHome = item.entity.side !== 'away';
    const kit = isHome ? this.kits.home : this.kits.away;
    const isKeeper = item.entity.family === 'goalkeeper';
    // A keeper wears the club's second colour, which is how a goalkeeper is told apart at a glance without
    // either side's outfield kit being touched.
    const fill = isKeeper ? kit.secondary : kit.primary;
    // Both teams are dots. They are told apart by kit colour and by a border — the home side white, the away
    // side dark ink — rather than by shape, which the product owner overrode for `replay-v3`.
    const border = isHome ? '#ffffff' : AWAY_INK;
    const diving = isDiveAction(item.action);
    const ground = toCanvasPoint(item.position, rect);
    const lift = item.z > 0 ? altitudeLift(rect, item.z) : 0;
    const point = { x: ground.x, y: ground.y - lift };

    // A jumping player lifts off a short ground shadow; a keeper going down has none, because a dive is a
    // lateral move rather than a leap and the ball is the thing with height.
    if (lift > 1 && !diving) {
      this.drawGroundShadow(ground, radius);
    }

    context.save();
    context.fillStyle = fill;
    context.strokeStyle = border;
    context.lineWidth = 2;
    context.beginPath();
    context.arc(point.x, point.y, radius, 0, Math.PI * 2);
    context.fill();
    context.stroke();

    context.fillStyle = readableInk(fill);
    context.font = `600 ${Math.max(7, Math.round(radius * 1.15))}px system-ui, sans-serif`;
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText(`${item.entity.shirtNumber}`, point.x, point.y + 0.5);
    context.restore();

    if (diving) {
      this.drawDiveStreak(ground, radius, diveDirection(item, ball));
    }

    const card =
      item.entity.participantId === null ? undefined : this.cards.get(item.entity.participantId);

    if (card !== undefined) {
      this.drawCard(point, radius, card);
    }
  }

  /** A short shadow on the grass under a player who has jumped. */
  private drawGroundShadow(ground: PitchPoint, radius: number): void {
    const context = this.context;

    context.save();
    context.fillStyle = 'rgba(2, 6, 23, 0.28)';
    context.beginPath();
    context.ellipse(
      ground.x,
      ground.y + radius * 0.55,
      radius * 0.9,
      radius * 0.34,
      0,
      0,
      Math.PI * 2,
    );
    context.fill();
    context.restore();
  }

  /** A short streak toward the ball where a keeper is going down, standing in for the missing shadow. */
  private drawDiveStreak(ground: PitchPoint, radius: number, direction: number): void {
    const context = this.context;

    context.save();
    context.strokeStyle = 'rgba(248, 250, 252, 0.55)';
    context.lineWidth = Math.max(1.5, radius * 0.35);
    context.beginPath();
    context.moveTo(ground.x - direction * radius * 0.6, ground.y + radius * 0.7);
    context.lineTo(ground.x + direction * radius * 2.4, ground.y + radius * 0.7);
    context.stroke();
    context.restore();
  }

  /** Draws a booking as a small card floating above the offending player's token. */
  private drawCard(point: PitchPoint, radius: number, card: CardKind): void {
    const context = this.context;
    const width = Math.max(5, radius * 0.7);
    const height = width * 1.4;
    const x = point.x + radius * 0.7;
    const y = point.y - radius - height - 2;

    context.save();
    context.fillStyle = CARD_COLOURS[card];
    context.strokeStyle = 'rgba(15, 23, 42, 0.9)';
    context.lineWidth = 1;
    context.fillRect(x, y, width, height);
    context.strokeRect(x, y, width, height);
    context.restore();
  }

  /** Draws the ball at its altitude, above its own shadow, with the aerial trail behind it. */
  private drawBall(rect: PitchRect, ball: FrameEntity): void {
    const context = this.context;
    const ground = toCanvasPoint(ball.position, rect);
    const lift = altitudeLift(rect, ball.z);
    const radius = ballRadius(rect) * altitudeScale(ball.z);
    const centre = { x: ground.x, y: ground.y - lift };

    // A ball on the grass — a rolled pass — casts no shadow at all; one in the air casts a small ellipse
    // that shrinks and fades as it climbs, which is what sells the height (`replay-v3`).
    if (ball.z >= SHADOW_MIN_ALTITUDE) {
      context.save();
      context.fillStyle = `rgba(2, 6, 23, ${(0.32 - (ball.z / 100) * 0.14).toFixed(3)})`;
      context.beginPath();
      context.ellipse(
        ground.x,
        ground.y,
        radius * (0.95 - (ball.z / 100) * 0.3),
        radius * 0.36,
        0,
        0,
        Math.PI * 2,
      );
      context.fill();
      context.restore();
    }

    // A white ball with dark patches, so it reads as a football rather than a dot.
    context.save();
    context.beginPath();
    context.arc(centre.x, centre.y, radius, 0, Math.PI * 2);
    context.fillStyle = BALL_COLOUR;
    context.fill();
    context.clip();

    context.fillStyle = BALL_PATCH_COLOUR;
    context.beginPath();

    for (let corner = 0; corner < 5; corner += 1) {
      const angle = -Math.PI / 2 + (corner * Math.PI * 2) / 5;
      const x = centre.x + Math.cos(angle) * radius * 0.34;
      const y = centre.y + Math.sin(angle) * radius * 0.34;

      if (corner === 0) {
        context.moveTo(x, y);
      } else {
        context.lineTo(x, y);
      }
    }

    context.closePath();
    context.fill();

    // Four short spokes towards the rim, which is what a white ball with dark panels reads as at token size.
    for (const angle of [0, Math.PI / 2, Math.PI, Math.PI * 1.5]) {
      context.lineWidth = Math.max(0.8, radius * 0.16);
      context.strokeStyle = BALL_PATCH_COLOUR;
      context.beginPath();
      context.moveTo(
        centre.x + Math.cos(angle) * radius * 0.42,
        centre.y + Math.sin(angle) * radius * 0.42,
      );
      context.lineTo(centre.x + Math.cos(angle) * radius, centre.y + Math.sin(angle) * radius);
      context.stroke();
    }

    context.restore();

    context.save();
    context.beginPath();
    context.arc(centre.x, centre.y, radius, 0, Math.PI * 2);
    context.strokeStyle = BALL_PATCH_COLOUR;
    context.lineWidth = 1.2;
    context.stroke();
    context.restore();
  }

  /** Draws the faint path a fast-moving or airborne ball has just travelled. */
  private drawTrail(rect: PitchRect, strength: number): void {
    const context = this.context;
    const span = 380;
    const steps = 6;

    context.save();

    for (let step = steps; step >= 2; step -= 1) {
      const from = sampleAt(this.ballTrack ?? [], this.lastTimeMs - (span * step) / steps);
      const to = sampleAt(this.ballTrack ?? [], this.lastTimeMs - (span * (step - 1)) / steps);

      if (from === null || to === null) {
        continue;
      }

      const alpha = strength * 0.45 * (1 - step / (steps + 1));

      context.strokeStyle = `rgba(248, 250, 252, ${alpha.toFixed(3)})`;
      context.lineWidth = 1.5;
      context.beginPath();
      context.moveTo(
        toCanvasPoint(from, rect).x,
        toCanvasPoint(from, rect).y - altitudeLift(rect, from.z),
      );
      context.lineTo(
        toCanvasPoint(to, rect).x,
        toCanvasPoint(to, rect).y - altitudeLift(rect, to.z),
      );
      context.stroke();
    }

    context.restore();
  }

  /** Draws the bright projectile streak a struck ball leaves behind it. */
  private drawShotLine(rect: PitchRect, ball: FrameEntity, timeMs: number): void {
    const context = this.context;
    const from = sampleAt(this.ballTrack ?? [], timeMs - 240);

    if (from === null) {
      return;
    }

    const start = toCanvasPoint(from, rect);
    const end = toCanvasPoint(ball.position, rect);
    const endY = end.y - altitudeLift(rect, ball.z);
    const startY = start.y - altitudeLift(rect, from.z);

    context.save();
    context.strokeStyle = 'rgba(253, 224, 71, 0.85)';
    context.lineWidth = 2;
    context.beginPath();
    context.moveTo(start.x, startY);
    context.lineTo(end.x, endY);
    context.stroke();

    context.fillStyle = 'rgba(253, 224, 71, 0.9)';
    context.beginPath();
    context.arc(end.x, endY, 2.4, 0, Math.PI * 2);
    context.fill();
    context.restore();
  }

  /** Draws a name tag above the player the ball is at. */
  private drawNameTag(rect: PitchRect, item: FrameEntity, radius: number): void {
    const name = item.entity.name;

    if (name === null || name.length === 0) {
      return;
    }

    const point = toCanvasPoint(item.position, rect);

    this.drawTag(point, radius, name);
  }

  /** Draws the pointer's own highlight and name tag over a player. */
  private drawHover(rect: PitchRect, item: FrameEntity, radius: number): void {
    const context = this.context;
    const point = toCanvasPoint(item.position, rect);

    context.save();
    context.strokeStyle = 'rgba(250, 204, 21, 0.9)';
    context.lineWidth = 2;
    context.beginPath();
    context.arc(point.x, point.y, radius + 3, 0, Math.PI * 2);
    context.stroke();
    context.restore();

    if (item.entity.name !== null && item.entity.name.length > 0) {
      this.drawTag(point, radius + 3, item.entity.name);
    }
  }

  /** Draws a player's name in a dark pill just above their token. */
  private drawTag(point: PitchPoint, radius: number, name: string): void {
    const context = this.context;
    const label = name.length > 18 ? `${name.slice(0, 17)}\u2026` : name;
    const width = label.length * 5.2 + 10;
    const height = 13;
    const x = point.x - width / 2;
    const y = point.y - radius - height - 6;

    context.save();
    context.fillStyle = TAG_BACKGROUND;
    context.fillRect(x, y, width, height);
    context.strokeStyle = TAG_BORDER;
    context.lineWidth = 1;
    context.strokeRect(x, y, width, height);
    context.fillStyle = '#f8fafc';
    context.font = '600 9px system-ui, sans-serif';
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText(label, point.x, y + height / 2 + 0.5);
    context.restore();
  }

  /** Draws the goal celebration: a pulsating flash and the GOAL! badge. */
  private drawCelebration(rect: PitchRect, elapsed: number): void {
    const fade = Math.max(0, 1 - elapsed / CELEBRATION_MILLISECONDS);

    if (fade <= 0) {
      return;
    }

    const context = this.context;
    const pulse = this.reducedMotion ? 1 : 0.55 + 0.45 * Math.sin(elapsed / 90);
    const size = this.reducedMotion ? 42 : 38 + 8 * pulse;
    const x = rect.x + rect.width / 2;
    const y = rect.y + rect.height * 0.3;
    const width = size * 3.4;
    const height = size * 1.5;

    context.save();
    context.fillStyle = `rgba(255, 255, 255, ${(0.13 * fade * pulse).toFixed(3)})`;
    context.fillRect(0, 0, this.cssWidth, this.cssHeight);

    context.fillStyle = `rgba(5, 46, 22, ${(0.88 * fade).toFixed(3)})`;
    context.fillRect(x - width / 2, y - height / 2, width, height);
    context.strokeStyle = `rgba(52, 211, 153, ${(0.95 * fade).toFixed(3)})`;
    context.lineWidth = 2;
    context.strokeRect(x - width / 2, y - height / 2, width, height);

    context.fillStyle = `rgba(167, 243, 208, ${fade.toFixed(3)})`;
    context.font = `900 ${Math.round(size)}px system-ui, sans-serif`;
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText('GOAL!', x, y + size * 0.05);
    context.restore();
  }

  /** Whether any player or the ball itself is tagged as striking the ball at goal. */
  private isStriking(ball: FrameEntity, players: readonly FrameEntity[]): boolean {
    return isStrikeAction(ball.action) || players.some((player) => isStrikeAction(player.action));
  }

  /** Finds the entity under the pointer, so hovering raises a name. */
  private readonly pointerListener = (event: PointerEvent): void => {
    if (this.tracks.length === 0) {
      return;
    }

    const bounds = this.canvas.getBoundingClientRect();
    const pointer = { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    const rect = pitchRect(this.cssWidth || 640, this.cssHeight || 416);
    const radius = playerRadius(rect) + 4;
    let hovered: string | null = null;

    for (const item of frameAt(this.entities, this.tracks, this.lastTimeMs)) {
      const point = toCanvasPoint(item.position, rect);

      if (Math.hypot(point.x - pointer.x, point.y - pointer.y) <= radius) {
        hovered = item.entity.id;

        break;
      }
    }

    if (hovered !== this.hoveredEntityId) {
      this.hoveredEntityId = hovered;
      this.render(this.lastTimeMs);
    }
  };

  private readonly pointerLeaveListener = (): void => {
    if (this.hoveredEntityId !== null) {
      this.hoveredEntityId = null;
      this.render(this.lastTimeMs);
    }
  };
}

const BALL_ENTITY_ID = 'ball';
const GRASS_DARK = '#15632f';
const GRASS_LIGHT = '#1a7d3c';
const LINE_COLOUR = 'rgba(255, 255, 255, 0.78)';
const BALL_COLOUR = '#f8fafc';
const BALL_PATCH_COLOUR = '#111827';
const TAG_BACKGROUND = 'rgba(2, 6, 23, 0.85)';
const TAG_BORDER = 'rgba(248, 250, 252, 0.3)';
const CARD_COLOURS: Record<CardKind, string> = { yellow: '#facc15', red: '#ef4444' };
const CELEBRATION_MILLISECONDS = 4_000;
const DEFAULT_KEEPER_TRIM = '#f8fafc';

/** The away side's border: dark ink against the home side's white, so the two dots are told apart. */
const AWAY_INK = '#0f172a';

/** Maps a passage's entities to the renderer's shape. */
export function toRendererEntities(
  entities: readonly HighlightEntity[],
): readonly RendererEntity[] {
  return entities.map((entity) => ({
    id: entity.entityId,
    isBall: entity.isBall,
    side: entity.side === 'home' || entity.side === 'away' ? entity.side : null,
    participantId: entity.participantId ?? null,
    shirtNumber: entity.shirtNumber,
    family: entity.family,
    name: entity.name ?? null,
    anchor: { x: entity.x, y: entity.y },
  }));
}

/** Maps a passage's tracks to the renderer's shape, dropping any that name no entity. */
export function toRendererTracks(
  tracks: readonly {
    readonly entityId: string;
    readonly keyframes: readonly HighlightKeyframe[];
  }[],
): readonly RendererTrack[] {
  return tracks
    .filter((track) => track.keyframes.length > 0)
    .map((track) => ({ entityId: track.entityId, keyframes: track.keyframes }));
}

/** The ink that stays readable on a kit colour, dark on light kits and light on dark ones. */
export function readableInk(hex: string): string {
  const value = hex.replace('#', '');
  const full =
    value.length === 3
      ? value
          .split('')
          .map((character) => character + character)
          .join('')
      : value;

  if (full.length !== 6) {
    return '#ffffff';
  }

  const red = Number.parseInt(full.slice(0, 2), 16);
  const green = Number.parseInt(full.slice(2, 4), 16);
  const blue = Number.parseInt(full.slice(4, 6), 16);

  if (Number.isNaN(red) || Number.isNaN(green) || Number.isNaN(blue)) {
    return '#ffffff';
  }

  // Rec. 709 relative luminance, which is the same weighting the contrast rules use.
  const luminance = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255;

  return luminance > 0.6 ? '#0f172a' : '#ffffff';
}

/** A player token's radius, scaled to the pitch so a small canvas does not turn the tokens into a carpet. */
function playerRadius(rect: PitchRect): number {
  return Math.max(5, Math.min(10, rect.height / 58));
}

/** The ball's base radius before altitude scaling. */
function ballRadius(rect: PitchRect): number {
  return Math.max(3.5, Math.min(6.5, rect.height / 105));
}

/** Which way a keeper is going down: toward the ball, or a fixed way when the ball is not there. */
function diveDirection(item: FrameEntity, ball: FrameEntity | undefined): number {
  if (ball === undefined) {
    return 1;
  }

  const away = Math.sign(ball.position.x - item.position.x);

  return away === 0 ? 1 : away;
}

/** The kit the renderer draws a side in, preferring the complete kit and falling back to the passage. */
function kitFor(kit: TeamKit | undefined, primary: string): TeamKit {
  return {
    primary: kit?.primary ?? primary,
    secondary: kit?.secondary ?? DEFAULT_KEEPER_TRIM,
  };
}

/** A monotonic clock, so the metrics can be measured where the platform has one. */
function now(): number {
  return typeof performance === 'undefined' ? 0 : performance.now();
}
