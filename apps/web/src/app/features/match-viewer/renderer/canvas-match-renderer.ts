import { FilmTimeline } from '../../../core/match/film-timeline';
import { KeyframeSample, TrackInterpolator, emptySample } from './keyframe-interpolator';
import {
  PitchGeometry,
  SHADOW_MIN_ALTITUDE,
  altitudeLift,
  altitudeScale,
  canvasX,
  canvasY,
  pitchGeometry,
  pitchRect,
} from './pitch-layout';
import {
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
  PitchRect,
  RendererEntity,
  RendererOptions,
  TeamKit,
  TeamKits,
} from './renderer.models';

/**
 * Draws one whole film on a canvas, interpolating the timeline's tracks (`§9.1`, `§9.4`, `replay-v4`).
 *
 * Framework-neutral: it knows a canvas and a film timeline and nothing about Angular, so the renderer can be
 * reused by a later native client and tested without a component. It scales to the device pixel ratio
 * without touching normalized geometry, and it keeps no timers of its own — whoever owns the animation loop
 * decides when a frame is drawn, which is what makes "stop on pause, route change, tab hidden,
 * destruction" something the caller can guarantee.
 *
 * There is **one renderer for a presentation**. It is given the whole film and drawn at a film moment, so a
 * passage boundary is invisible to it: nothing is rebuilt, nothing is drawn at a stale time. A player is a
 * slot on the pitch whose occupant the timeline's roster names, so a substitution changes the name and the
 * number on the token and a player sent off stops being drawn.
 *
 * The pitch is drawn **once** onto an offscreen layer and copied to the canvas each frame, and redrawn only
 * when the canvas changes size, which a `ResizeObserver` reports; a frame never reads the page's layout. The
 * entities, the list of who is visible, and the effect lists are made once and reused, so a frame allocates
 * next to nothing.
 *
 * Stage 6 filled the picture in: a mown pitch with the markings a real one has, kit-coloured tokens with
 * shirt numbers and name tags, distinct goalkeeper kits, a ball that lifts off its own shadow by the
 * altitude the engine sent, and the action effects — a shot's projectile streak, a
 * booking's card, a goal's pulsating flash. Teams are distinguished by border as well as colour and every
 * player carries their shirt number, because the requirement is explicit that colour is never the only signal
 * (`§9.4`, `§11.3`).
 *
 * The only work it does outside a frame is the pointer listener that makes hovering a player raise their
 * name, and the resize observer; each draws a single frame and touches no Angular state, so neither enters
 * change detection.
 */
export class CanvasMatchRenderer {
  private readonly context: CanvasRenderingContext2D;
  private readonly kits: TeamKits;
  private readonly inks: KitInks;
  private readonly reducedMotion: boolean;
  private readonly interpolators: readonly TrackInterpolator[];
  private readonly ballInterpolator: TrackInterpolator;
  private readonly live: readonly LiveEntity[];
  private readonly liveBall: LiveEntity;
  private readonly createPitchLayer: () => HTMLCanvasElement | null;

  // What the last frame saw, reused by the next so the hot path allocates nothing.
  private readonly frame: LiveEntity[] = [];
  private readonly players: LiveEntity[] = [];
  private readonly scratch: KeyframeSample = emptySample();
  private readonly trailFrom: KeyframeSample = emptySample();
  private readonly trailTo: KeyframeSample = emptySample();
  private ball: LiveEntity | null = null;

  private observer: ResizeObserver | null = null;
  private layer: HTMLCanvasElement | null = null;
  private layerContext: CanvasRenderingContext2D | null = null;
  private layoutDirty = true;
  private cssWidth: number;
  private cssHeight: number;
  private ratio = 1;
  private rect: PitchRect = { x: 0, y: 0, width: 0, height: 0 };
  private numberFont = '';
  private numberFontRadius = 0;
  private lastTimeMs = 0;
  private lastOverlay = 0;
  private hasRendered = false;
  private hoveredEntityId: string | null = null;
  private metricsValue: FrameMetrics = {
    entities: 0,
    interpolated: 0,
    effects: 0,
    milliseconds: 0,
  };

  /** Initializes the renderer for one film on one canvas. */
  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly timeline: FilmTimeline,
    options: RendererOptions = {},
  ) {
    const context = canvas.getContext('2d');

    if (context === null) {
      throw new Error('A film cannot be drawn: the canvas has no 2D context.');
    }

    this.context = context;
    this.kits = {
      home: kitFor(options.kits?.home, timeline.homeColour),
      away: kitFor(options.kits?.away, timeline.awayColour),
    };
    this.inks = {
      homePrimary: readableInk(this.kits.home.primary),
      homeSecondary: readableInk(this.kits.home.secondary),
      awayPrimary: readableInk(this.kits.away.primary),
      awaySecondary: readableInk(this.kits.away.secondary),
    };
    this.reducedMotion = options.reducedMotion ?? false;
    this.createPitchLayer = options.createPitchLayer ?? defaultPitchLayer;
    this.interpolators = timeline.slots.map((slot) => new TrackInterpolator(slot.track, true));
    this.ballInterpolator = new TrackInterpolator(timeline.ball, false);
    this.live = timeline.slots.map(() => liveEntity(timeline.ballEntity));
    this.liveBall = liveEntity(timeline.ballEntity);

    // The size is read once, here; after that the observer says when it changes, so a frame never asks the
    // page for its layout.
    this.cssWidth = canvas.clientWidth || canvas.width || 640;
    this.cssHeight = canvas.clientHeight || canvas.height || 416;

    this.canvas.addEventListener('pointermove', this.pointerListener);
    this.canvas.addEventListener('pointerleave', this.pointerLeaveListener);

    if (typeof ResizeObserver !== 'undefined') {
      this.observer = new ResizeObserver((entries) => this.onResize(entries));
      this.observer.observe(canvas);
    }
  }

  /** Gets how much the last frame drew, for the renderer's own instrumentation. */
  get metrics(): FrameMetrics {
    return this.metricsValue;
  }

  /**
   * Gets what the last frame drew — every visible player, then the ball — for instrumentation.
   *
   * These are the renderer's own reused entries, overwritten by the next frame, so a reader copies what it
   * needs. The fluidity harness reads them to measure how far each token moved between two drawn frames.
   */
  get drawn(): readonly FrameEntity[] {
    return this.frame;
  }

  /**
   * Draws the film at a film moment.
   *
   * @param timeMs The film moment, in milliseconds from kick-off.
   * @param overlayAlpha How much of a dark overlay to lay over the picture, 0…1, which is how the viewer
   *   fades over a cut or a jump in the highlights reel.
   */
  render(timeMs: number, overlayAlpha = 0): void {
    const started = now();
    const context = this.context;

    this.ensureLayout();

    const rect = this.rect;

    this.lastTimeMs = timeMs;
    this.lastOverlay = overlayAlpha;
    this.hasRendered = true;

    context.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.drawPitchLayer(rect);
    this.sampleFrame(timeMs);

    const frame = this.frame;
    const ball = this.ball;
    const possession = nearestPlayerToBall(frame);
    const cards = this.timeline.cardsAt(timeMs);
    const radius = playerRadius(rect);
    let effects = 0;

    this.numberFont = this.fontFor(radius);

    for (const player of this.players) {
      this.drawPlayer(rect, player, radius, ball, cards);
    }

    if (ball !== null) {
      if (wantsTrail(ball.action, ball.z, ball.speed)) {
        this.drawTrail(rect, Math.max(0.4, trailStrength(ball.z, ball.speed)));
        effects += 1;
      }

      if (this.isStriking(ball)) {
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
      for (const player of this.players) {
        if (player.entity.id === this.hoveredEntityId) {
          this.drawHover(rect, player, radius);

          break;
        }
      }
    }

    const celebrating = this.timeline.celebrationAt(timeMs);

    if (celebrating >= 0) {
      this.drawCelebration(rect, celebrating);
      effects += 1;
    }

    if (this.timeline.isHalfTimeAt(timeMs)) {
      this.drawHalfTime(rect);
      effects += 1;
    }

    if (overlayAlpha > 0) {
      this.drawOverlay(overlayAlpha);
    }

    this.metricsValue = {
      entities: frame.length,
      interpolated: frame.length,
      effects,
      milliseconds: now() - started,
    };
  }

  /** Clears the canvas, releases the listeners and the observer, so nothing it drew or held outlives the component. */
  dispose(): void {
    this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.canvas.removeEventListener('pointermove', this.pointerListener);
    this.canvas.removeEventListener('pointerleave', this.pointerLeaveListener);
    this.observer?.disconnect();
    this.observer = null;
    this.layer = null;
    this.layerContext = null;
    this.hoveredEntityId = null;
    this.hasRendered = false;
  }

  /** Takes the size the page reports, and draws the frame again so a resize never leaves a stretched picture. */
  private onResize(entries: readonly ResizeObserverEntry[]): void {
    const box = entries[entries.length - 1]?.contentRect;

    // A canvas that is not laid out — the text-only view — reports nothing worth resizing to.
    if (box === undefined || box.width <= 0 || box.height <= 0) {
      return;
    }

    if (box.width === this.cssWidth && box.height === this.cssHeight) {
      return;
    }

    this.cssWidth = box.width;
    this.cssHeight = box.height;
    this.layoutDirty = true;

    if (this.hasRendered) {
      this.render(this.lastTimeMs, this.lastOverlay);
    }
  }

  /**
   * Sizes the backing store to the device pixel ratio and redraws the pitch layer, but only when the size or
   * the ratio changed since the last frame.
   *
   * Geometry stays in normalized coordinates and only the transform changes, so the same film is drawn the
   * same way on a 1x and a 3x screen (`§9.4`).
   */
  private ensureLayout(): void {
    const ratio = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1;

    if (!this.layoutDirty && ratio === this.ratio) {
      return;
    }

    const pixelWidth = Math.max(1, Math.round(this.cssWidth * ratio));
    const pixelHeight = Math.max(1, Math.round(this.cssHeight * ratio));

    if (this.canvas.width !== pixelWidth || this.canvas.height !== pixelHeight) {
      this.canvas.width = pixelWidth;
      this.canvas.height = pixelHeight;
    }

    this.ratio = ratio;
    this.rect = pitchRect(this.cssWidth, this.cssHeight);
    this.layoutDirty = false;
    this.context.setTransform(ratio, 0, 0, ratio, 0, 0);
    this.redrawPitchLayer(pixelWidth, pixelHeight);
  }

  /** Draws the pitch onto its own surface, where there is one to draw it onto. */
  private redrawPitchLayer(pixelWidth: number, pixelHeight: number): void {
    this.layer ??= this.createPitchLayer();
    this.layerContext ??= this.layer?.getContext('2d') ?? null;

    const layer = this.layer;
    const layerContext = this.layerContext;

    if (layer === null || layerContext === null) {
      return;
    }

    if (layer.width !== pixelWidth || layer.height !== pixelHeight) {
      layer.width = pixelWidth;
      layer.height = pixelHeight;
    }

    layerContext.setTransform(this.ratio, 0, 0, this.ratio, 0, 0);
    layerContext.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.drawPitch(layerContext, this.rect);
  }

  /** Puts the pitch on the canvas: a copy of the layer, or — with no layer — the pitch drawn in place. */
  private drawPitchLayer(rect: PitchRect): void {
    if (this.layer !== null && this.layerContext !== null) {
      const context = this.context;

      // The layer is in device pixels, so it is copied under the identity transform.
      context.setTransform(1, 0, 0, 1, 0, 0);
      context.drawImage(this.layer, 0, 0);
      context.setTransform(this.ratio, 0, 0, this.ratio, 0, 0);

      return;
    }

    this.drawPitch(this.context, rect);
  }

  /** Reads every visible slot and the ball at a film moment into the frame, in the order they are drawn. */
  private sampleFrame(timeMs: number): void {
    this.frame.length = 0;
    this.players.length = 0;
    this.ball = null;

    for (let slot = 0; slot < this.live.length; slot += 1) {
      const stint = this.timeline.stintAt(slot, timeMs);

      if (stint === null || !this.interpolators[slot].sample(timeMs, this.scratch)) {
        continue;
      }

      const entity = this.live[slot];

      entity.entity = stint.entity;
      fillEntity(entity, this.scratch);
      this.frame.push(entity);
      this.players.push(entity);
    }

    if (this.ballInterpolator.sample(timeMs, this.scratch)) {
      fillEntity(this.liveBall, this.scratch);
      this.frame.push(this.liveBall);
      this.ball = this.liveBall;
    }
  }

  /** The font a shirt number is drawn in, rebuilt only when the token's size changes. */
  private fontFor(radius: number): string {
    if (radius !== this.numberFontRadius) {
      this.numberFontRadius = radius;
      this.numberFont = `600 ${Math.max(7, Math.round(radius * 1.15))}px system-ui, sans-serif`;
    }

    return this.numberFont;
  }

  /** Draws the pitch: mown stripes, every law-of-the-game marking, and both goals with their nets. */
  private drawPitch(context: CanvasRenderingContext2D, rect: PitchRect): void {
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

    this.drawEndMarkings(context, rect, geometry, true);
    this.drawEndMarkings(context, rect, geometry, false);

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

    this.drawGoal(context, rect, geometry, true);
    this.drawGoal(context, rect, geometry, false);
    context.restore();
  }

  /** Draws one end's penalty area, six-yard box, penalty spot, and the penalty arc outside the box. */
  private drawEndMarkings(
    context: CanvasRenderingContext2D,
    rect: PitchRect,
    geometry: PitchGeometry,
    isLeft: boolean,
  ): void {
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
  private drawGoal(
    context: CanvasRenderingContext2D,
    rect: PitchRect,
    geometry: PitchGeometry,
    isLeft: boolean,
  ): void {
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
    item: LiveEntity,
    radius: number,
    ball: LiveEntity | null,
    cards: ReadonlyMap<string, CardKind>,
  ): void {
    const context = this.context;
    const isHome = item.entity.side !== 'away';
    const isKeeper = item.entity.family === 'goalkeeper';
    // A keeper wears the club's second colour, which is how a goalkeeper is told apart at a glance without
    // either side's outfield kit being touched.
    const kit = isHome ? this.kits.home : this.kits.away;
    const fill = isKeeper ? kit.secondary : kit.primary;
    const ink = isHome
      ? isKeeper
        ? this.inks.homeSecondary
        : this.inks.homePrimary
      : isKeeper
        ? this.inks.awaySecondary
        : this.inks.awayPrimary;
    // Both teams are dots. They are told apart by kit colour and by a border — the home side white, the away
    // side dark ink — rather than by shape, which the product owner overrode for `replay-v3`.
    const border = isHome ? '#ffffff' : AWAY_INK;
    const diving = isDiveAction(item.action);
    const groundX = canvasX(item.position.x, rect);
    const groundY = canvasY(item.position.y, rect);
    const lift = item.z > 0 ? altitudeLift(rect, item.z) : 0;
    const pointY = groundY - lift;

    // A jumping player lifts off a short ground shadow; a keeper going down has none, because a dive is a
    // lateral move rather than a leap and the ball is the thing with height.
    if (lift > 1 && !diving) {
      this.drawGroundShadow(groundX, groundY, radius);
    }

    context.save();
    context.fillStyle = fill;
    context.strokeStyle = border;
    context.lineWidth = 2;
    context.beginPath();
    context.arc(groundX, pointY, radius, 0, Math.PI * 2);
    context.fill();
    context.stroke();

    context.fillStyle = ink;
    context.font = this.numberFont;
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText(`${item.entity.shirtNumber}`, groundX, pointY + 0.5);
    context.restore();

    if (diving) {
      this.drawDiveStreak(groundX, groundY, radius, diveDirection(item, ball));
    }

    const card =
      item.entity.participantId === null ? undefined : cards.get(item.entity.participantId);

    if (card !== undefined) {
      this.drawCard(groundX, pointY, radius, card);
    }
  }

  /** A short shadow on the grass under a player who has jumped. */
  private drawGroundShadow(x: number, y: number, radius: number): void {
    const context = this.context;

    context.save();
    context.fillStyle = 'rgba(2, 6, 23, 0.28)';
    context.beginPath();
    context.ellipse(x, y + radius * 0.55, radius * 0.9, radius * 0.34, 0, 0, Math.PI * 2);
    context.fill();
    context.restore();
  }

  /** A short streak toward the ball where a keeper is going down, standing in for the missing shadow. */
  private drawDiveStreak(x: number, y: number, radius: number, direction: number): void {
    const context = this.context;

    context.save();
    context.strokeStyle = 'rgba(248, 250, 252, 0.55)';
    context.lineWidth = Math.max(1.5, radius * 0.35);
    context.beginPath();
    context.moveTo(x - direction * radius * 0.6, y + radius * 0.7);
    context.lineTo(x + direction * radius * 2.4, y + radius * 0.7);
    context.stroke();
    context.restore();
  }

  /** Draws a booking as a small card floating above the offending player's token. */
  private drawCard(pointX: number, pointY: number, radius: number, card: CardKind): void {
    const context = this.context;
    const width = Math.max(5, radius * 0.7);
    const height = width * 1.4;
    const x = pointX + radius * 0.7;
    const y = pointY - radius - height - 2;

    context.save();
    context.fillStyle = CARD_COLOURS[card];
    context.strokeStyle = 'rgba(15, 23, 42, 0.9)';
    context.lineWidth = 1;
    context.fillRect(x, y, width, height);
    context.strokeRect(x, y, width, height);
    context.restore();
  }

  /** Draws the ball at its altitude, above its own shadow, with the aerial trail behind it. */
  private drawBall(rect: PitchRect, ball: LiveEntity): void {
    const context = this.context;
    const groundX = canvasX(ball.position.x, rect);
    const groundY = canvasY(ball.position.y, rect);
    const lift = altitudeLift(rect, ball.z);
    const radius = ballRadius(rect) * altitudeScale(ball.z);
    const centreX = groundX;
    const centreY = groundY - lift;

    // A ball on the grass — a rolled pass — casts no shadow at all; one in the air casts a small ellipse
    // that shrinks and fades as it climbs, which is what sells the height (`replay-v3`).
    if (ball.z >= SHADOW_MIN_ALTITUDE) {
      context.save();
      context.fillStyle = `rgba(2, 6, 23, ${(0.32 - (ball.z / 100) * 0.14).toFixed(3)})`;
      context.beginPath();
      context.ellipse(
        groundX,
        groundY,
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
    context.arc(centreX, centreY, radius, 0, Math.PI * 2);
    context.fillStyle = BALL_COLOUR;
    context.fill();
    context.clip();

    context.fillStyle = BALL_PATCH_COLOUR;
    context.beginPath();

    for (let corner = 0; corner < 5; corner += 1) {
      const angle = -Math.PI / 2 + (corner * Math.PI * 2) / 5;
      const x = centreX + Math.cos(angle) * radius * 0.34;
      const y = centreY + Math.sin(angle) * radius * 0.34;

      if (corner === 0) {
        context.moveTo(x, y);
      } else {
        context.lineTo(x, y);
      }
    }

    context.closePath();
    context.fill();

    // Four short spokes towards the rim, which is what a white ball with dark panels reads as at token size.
    for (let spoke = 0; spoke < 4; spoke += 1) {
      const angle = (spoke * Math.PI) / 2;

      context.lineWidth = Math.max(0.8, radius * 0.16);
      context.strokeStyle = BALL_PATCH_COLOUR;
      context.beginPath();
      context.moveTo(
        centreX + Math.cos(angle) * radius * 0.42,
        centreY + Math.sin(angle) * radius * 0.42,
      );
      context.lineTo(centreX + Math.cos(angle) * radius, centreY + Math.sin(angle) * radius);
      context.stroke();
    }

    context.restore();

    context.save();
    context.beginPath();
    context.arc(centreX, centreY, radius, 0, Math.PI * 2);
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
    const from = this.trailFrom;
    const to = this.trailTo;

    context.save();

    for (let step = steps; step >= 2; step -= 1) {
      if (
        !this.ballInterpolator.sampleAt(this.lastTimeMs - (span * step) / steps, from) ||
        !this.ballInterpolator.sampleAt(this.lastTimeMs - (span * (step - 1)) / steps, to)
      ) {
        continue;
      }

      const alpha = strength * 0.45 * (1 - step / (steps + 1));

      context.strokeStyle = `rgba(248, 250, 252, ${alpha.toFixed(3)})`;
      context.lineWidth = 1.5;
      context.beginPath();
      context.moveTo(canvasX(from.x, rect), canvasY(from.y, rect) - altitudeLift(rect, from.z));
      context.lineTo(canvasX(to.x, rect), canvasY(to.y, rect) - altitudeLift(rect, to.z));
      context.stroke();
    }

    context.restore();
  }

  /** Draws the bright projectile streak a struck ball leaves behind it. */
  private drawShotLine(rect: PitchRect, ball: LiveEntity, timeMs: number): void {
    const context = this.context;
    const from = this.trailFrom;

    if (!this.ballInterpolator.sampleAt(timeMs - 240, from)) {
      return;
    }

    const startX = canvasX(from.x, rect);
    const startY = canvasY(from.y, rect) - altitudeLift(rect, from.z);
    const endX = canvasX(ball.position.x, rect);
    const endY = canvasY(ball.position.y, rect) - altitudeLift(rect, ball.z);

    context.save();
    context.strokeStyle = 'rgba(253, 224, 71, 0.85)';
    context.lineWidth = 2;
    context.beginPath();
    context.moveTo(startX, startY);
    context.lineTo(endX, endY);
    context.stroke();

    context.fillStyle = 'rgba(253, 224, 71, 0.9)';
    context.beginPath();
    context.arc(endX, endY, 2.4, 0, Math.PI * 2);
    context.fill();
    context.restore();
  }

  /** Draws a name tag above the player the ball is at. */
  private drawNameTag(rect: PitchRect, item: FrameEntity, radius: number): void {
    const name = item.entity.name;

    if (name === null || name.length === 0) {
      return;
    }

    this.drawTag(canvasX(item.position.x, rect), canvasY(item.position.y, rect), radius, name);
  }

  /** Draws the pointer's own highlight and name tag over a player. */
  private drawHover(rect: PitchRect, item: FrameEntity, radius: number): void {
    const context = this.context;
    const x = canvasX(item.position.x, rect);
    const y = canvasY(item.position.y, rect);

    context.save();
    context.strokeStyle = 'rgba(250, 204, 21, 0.9)';
    context.lineWidth = 2;
    context.beginPath();
    context.arc(x, y, radius + 3, 0, Math.PI * 2);
    context.stroke();
    context.restore();

    if (item.entity.name !== null && item.entity.name.length > 0) {
      this.drawTag(x, y, radius + 3, item.entity.name);
    }
  }

  /** Draws a player's name in a dark pill just above their token. */
  private drawTag(pointX: number, pointY: number, radius: number, name: string): void {
    const context = this.context;
    const label = name.length > 18 ? `${name.slice(0, 17)}…` : name;
    const width = label.length * 5.2 + 10;
    const height = 13;
    const x = pointX - width / 2;
    const y = pointY - radius - height - 6;

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
    context.fillText(label, pointX, y + height / 2 + 0.5);
    context.restore();
  }

  /** Draws the goal celebration: a pulsating flash and the GOAL! badge. */
  private drawCelebration(rect: PitchRect, elapsed: number): void {
    const fade = Math.max(0, 1 - elapsed / CELEBRATION_FADE_MILLISECONDS);

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

  /** Draws the half-time card over the pitch while the film holds at the interval. */
  private drawHalfTime(rect: PitchRect): void {
    const context = this.context;
    const size = Math.max(16, Math.min(34, rect.height / 14));
    const x = rect.x + rect.width / 2;
    const y = rect.y + rect.height * 0.3;
    const width = size * 6.4;
    const height = size * 1.9;

    context.save();
    context.fillStyle = 'rgba(15, 23, 42, 0.82)';
    context.fillRect(x - width / 2, y - height / 2, width, height);
    context.strokeStyle = 'rgba(148, 163, 184, 0.9)';
    context.lineWidth = 2;
    context.strokeRect(x - width / 2, y - height / 2, width, height);
    context.fillStyle = '#e2e8f0';
    context.font = `800 ${Math.round(size)}px system-ui, sans-serif`;
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText('HALF TIME', x, y + size * 0.04);
    context.restore();
  }

  /** Lays a dark overlay over the whole canvas, which is how a cut is faded over. */
  private drawOverlay(alpha: number): void {
    const context = this.context;

    context.save();
    context.fillStyle = `rgba(2, 6, 23, ${Math.min(1, alpha).toFixed(3)})`;
    context.fillRect(0, 0, this.cssWidth, this.cssHeight);
    context.restore();
  }

  /** Whether any player or the ball itself is tagged as striking the ball at goal. */
  private isStriking(ball: LiveEntity): boolean {
    if (isStrikeAction(ball.action)) {
      return true;
    }

    for (const player of this.players) {
      if (isStrikeAction(player.action)) {
        return true;
      }
    }

    return false;
  }

  /** Finds the entity under the pointer, so hovering raises a name. */
  private readonly pointerListener = (event: PointerEvent): void => {
    if (this.players.length === 0) {
      return;
    }

    const bounds = this.canvas.getBoundingClientRect();
    const pointerX = event.clientX - bounds.left;
    const pointerY = event.clientY - bounds.top;
    const rect = pitchRect(this.cssWidth, this.cssHeight);
    const radius = playerRadius(rect) + 4;
    let hovered: string | null = null;

    // The frame the last render drew is where the tokens are, so there is nothing to sample again.
    for (const player of this.players) {
      const x = canvasX(player.position.x, rect);
      const y = canvasY(player.position.y, rect);

      if (Math.hypot(x - pointerX, y - pointerY) <= radius) {
        hovered = player.entity.id;

        break;
      }
    }

    if (hovered !== this.hoveredEntityId) {
      this.hoveredEntityId = hovered;
      this.render(this.lastTimeMs, this.lastOverlay);
    }
  };

  private readonly pointerLeaveListener = (): void => {
    if (this.hoveredEntityId !== null) {
      this.hoveredEntityId = null;
      this.render(this.lastTimeMs, this.lastOverlay);
    }
  };
}

/** A token the renderer keeps and refills each frame, rather than making one per entity per frame. */
interface LiveEntity {
  entity: RendererEntity;
  position: { x: number; y: number };
  z: number;
  speed: number;
  action: string | null;
}

/** The ink each kit colour is read in, worked out once rather than for every token on every frame. */
interface KitInks {
  readonly homePrimary: string;
  readonly homeSecondary: string;
  readonly awayPrimary: string;
  readonly awaySecondary: string;
}

const GRASS_DARK = '#15632f';
const GRASS_LIGHT = '#1a7d3c';
const LINE_COLOUR = 'rgba(255, 255, 255, 0.78)';
const BALL_COLOUR = '#f8fafc';
const BALL_PATCH_COLOUR = '#111827';
const TAG_BACKGROUND = 'rgba(2, 6, 23, 0.85)';
const TAG_BORDER = 'rgba(248, 250, 252, 0.3)';
const CARD_COLOURS: Record<CardKind, string> = { yellow: '#facc15', red: '#ef4444' };
const CELEBRATION_FADE_MILLISECONDS = 4_000;
const DEFAULT_KEEPER_TRIM = '#f8fafc';

/** The away side's border: dark ink against the home side's white, so the two dots are told apart. */
const AWAY_INK = '#0f172a';

/** Makes the offscreen surface the pitch is drawn onto, where the platform has a document to make one in. */
function defaultPitchLayer(): HTMLCanvasElement | null {
  return typeof document === 'undefined' ? null : document.createElement('canvas');
}

function liveEntity(entity: RendererEntity): LiveEntity {
  return { entity, position: { x: 0, y: 0 }, z: 0, speed: 0, action: null };
}

function fillEntity(entity: LiveEntity, sample: KeyframeSample): void {
  entity.position.x = sample.x;
  entity.position.y = sample.y;
  entity.z = sample.z;
  entity.speed = sample.speed;
  entity.action = sample.action;
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
function diveDirection(item: LiveEntity, ball: LiveEntity | null): number {
  if (ball === null) {
    return 1;
  }

  const away = Math.sign(ball.position.x - item.position.x);

  return away === 0 ? 1 : away;
}

/** The kit the renderer draws a side in, preferring the complete kit and falling back to the film's colour. */
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
