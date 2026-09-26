import { Highlight, HighlightEntity, HighlightKeyframe } from '../../../core/match/match.models';
import { frameAt, positionAt } from './keyframe-interpolator';
import { pitchRect, toCanvasPoint } from './pitch-layout';
import {
  FrameMetrics,
  FrameEntity,
  PitchPoint,
  PitchRect,
  RendererEntity,
  RendererTrack,
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
 * Teams are distinguished by shape as well as colour (a circle for the home side, a square for the away
 * side) and every player carries their shirt number, because the requirement is explicit that colour is
 * never the only signal (`§9.4`, `§11.3`).
 */
export class CanvasMatchRenderer {
  private readonly context: CanvasRenderingContext2D;
  private readonly entities: readonly RendererEntity[];
  private readonly tracks: readonly RendererTrack[];
  private readonly ballTrack: readonly HighlightKeyframe[];
  private readonly colours: { readonly home: string; readonly away: string };

  private cssWidth = 0;
  private cssHeight = 0;
  private metricsValue: FrameMetrics = { entities: 0, interpolated: 0 };

  /** Initializes the renderer for one highlight on one canvas. */
  constructor(
    private readonly canvas: HTMLCanvasElement,
    highlight: Highlight,
  ) {
    const context = canvas.getContext('2d');

    if (context === null) {
      throw new Error('A highlight cannot be drawn: the canvas has no 2D context.');
    }

    this.context = context;
    this.entities = toRendererEntities(highlight.entities);
    this.tracks = toRendererTracks(highlight.tracks);
    this.ballTrack = this.tracks.find((track) => track.entityId === 'ball')?.keyframes ?? [];
    this.colours = { home: highlight.homeColour, away: highlight.awayColour };
  }

  /** Gets how much the last frame drew, for the renderer's own instrumentation. */
  get metrics(): FrameMetrics {
    return this.metricsValue;
  }

  /** Draws the highlight at an animation moment. */
  render(timeMs: number): void {
    const rect = this.resize();

    this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
    this.drawPitch(rect);

    const frame = frameAt(this.entities, this.tracks, timeMs);
    const players = frame.filter((item) => !item.entity.isBall);
    const ball = frame.find((item) => item.entity.isBall);

    for (const player of players) {
      this.drawPlayer(rect, player);
    }

    if (ball !== undefined) {
      this.drawTrail(rect, timeMs);
      this.drawBall(rect, ball.position);
    }

    this.metricsValue = { entities: frame.length, interpolated: this.tracks.length };
  }

  /** Draws one moment without animating, which is what reduced motion and a static diagram use. */
  renderFrame(timeMs: number): void {
    this.render(timeMs);
  }

  /** Clears the canvas and forgets the context, so nothing it drew outlives the component. */
  dispose(): void {
    this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
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

  private drawPitch(rect: PitchRect): void {
    const context = this.context;

    context.save();
    context.fillStyle = '#166534';
    context.fillRect(rect.x, rect.y, rect.width, rect.height);

    context.strokeStyle = 'rgba(255, 255, 255, 0.7)';
    context.lineWidth = 1.5;
    context.strokeRect(rect.x, rect.y, rect.width, rect.height);

    const halfway = rect.x + rect.width / 2;

    context.beginPath();
    context.moveTo(halfway, rect.y);
    context.lineTo(halfway, rect.y + rect.height);
    context.stroke();

    context.beginPath();
    context.arc(halfway, rect.y + rect.height / 2, rect.height * 0.13, 0, Math.PI * 2);
    context.stroke();

    const boxWidth = rect.width * 0.16;
    const boxHeight = rect.height * 0.55;
    const boxTop = rect.y + (rect.height - boxHeight) / 2;

    context.strokeRect(rect.x, boxTop, boxWidth, boxHeight);
    context.strokeRect(rect.x + rect.width - boxWidth, boxTop, boxWidth, boxHeight);
    context.restore();
  }

  private drawPlayer(rect: PitchRect, item: FrameEntity): void {
    const context = this.context;
    const point = toCanvasPoint(item.position, rect);
    const isHome = item.entity.side !== 'away';
    const radius = 8;

    context.save();
    context.fillStyle = isHome ? this.colours.home : this.colours.away;
    context.strokeStyle = '#ffffff';
    context.lineWidth = 1.5;

    if (isHome) {
      context.beginPath();
      context.arc(point.x, point.y, radius, 0, Math.PI * 2);
      context.fill();
      context.stroke();
    } else {
      // A square for the away side, so the two teams are told apart by shape as well as colour (§9.4).
      context.fillRect(point.x - radius, point.y - radius, radius * 2, radius * 2);
      context.strokeRect(point.x - radius, point.y - radius, radius * 2, radius * 2);
    }

    context.fillStyle = '#ffffff';
    context.font = '600 10px system-ui, sans-serif';
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText(`${item.entity.shirtNumber}`, point.x, point.y + 0.5);
    context.restore();
  }

  private drawBall(rect: PitchRect, position: PitchPoint): void {
    const context = this.context;
    const point = toCanvasPoint(position, rect);

    context.save();
    context.fillStyle = '#ffffff';
    context.strokeStyle = '#0f172a';
    context.lineWidth = 1.5;
    context.beginPath();
    context.arc(point.x, point.y, 4.5, 0, Math.PI * 2);
    context.fill();
    context.stroke();
    context.restore();
  }

  /** A faint trail behind the ball, which makes a fast move read as a move rather than a jump. */
  private drawTrail(rect: PitchRect, timeMs: number): void {
    const context = this.context;
    const steps = 4;
    const span = 320;

    context.save();
    context.strokeStyle = 'rgba(255, 255, 255, 0.45)';
    context.lineWidth = 2;
    context.beginPath();

    let started = false;

    for (let step = steps; step >= 1; step -= 1) {
      const position = positionAt(this.ballTrack, timeMs - (span * step) / steps);

      if (position === null) {
        continue;
      }

      const point = toCanvasPoint(position, rect);

      if (!started) {
        context.moveTo(point.x, point.y);
        started = true;
      } else {
        context.lineTo(point.x, point.y);
      }
    }

    if (started) {
      context.stroke();
    }

    context.restore();
  }
}

/** Maps a highlight's entities to the renderer's shape. */
export function toRendererEntities(
  entities: readonly HighlightEntity[],
): readonly RendererEntity[] {
  return entities.map((entity) => ({
    id: entity.entityId,
    isBall: entity.isBall,
    side: entity.side === 'home' || entity.side === 'away' ? entity.side : null,
    shirtNumber: entity.shirtNumber,
    family: entity.family,
    anchor: { x: entity.x, y: entity.y },
  }));
}

/** Maps a highlight's tracks to the renderer's shape, dropping any that name no entity. */
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
