import { HighlightKeyframe } from '../../../core/match/match.models';
import { FrameEntity, PitchPoint, RendererEntity, RendererTrack } from './renderer.models';

/**
 * Turns semantic keyframes into a position at an arbitrary moment (`§9.1`, `§9.4`).
 *
 * The client interpolates rather than being sent frames, so the payload is a few kilobytes and the replay
 * is identical on a 60 Hz and a 144 Hz display. Everything here is pure and takes the animation time it is
 * given, so a test can ask for any moment without a canvas or a clock.
 *
 * Movement is drawn with a Catmull-Rom spline through the keyframes (Stage 6): the compressed track keeps
 * only the samples where the velocity or the direction changed, and a spline through those samples rounds
 * the corner a straight line would leave, which is what makes a run and a flight read as movement rather
 * than as a chain of straight segments. The spline is *interpolating* — it passes exactly through every
 * keyframe — so a semantic anchor (the strike, the dive) still lands at the moment the engine authored it.
 * Altitude is splined the same way and then clamped, so a ball can neither sink under the pitch nor leave
 * the altitude band the contract gives it. A two-keyframe track has no neighbouring samples to bend around,
 * so it stays a straight line: inventing a curve between two points is inventing movement that was never
 * recorded.
 */

/** The normalized coordinate scale the engine speaks. */
const PITCH_SCALE = 10_000;

/** The altitude band the contract gives `z`. */
const ALTITUDE_SCALE = 100;

/** One entity's state at one moment: where it is, how high it is, and what it is doing. */
export interface KeyframeSample {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly speed: number;
  readonly action: string | null;
}

/** The Catmull-Rom basis at `t`, through the four control values (`0…1` between `p1` and `p2`). */
export function catmullRom(p0: number, p1: number, p2: number, p3: number, t: number): number {
  const t2 = t * t;
  const t3 = t2 * t;

  return (
    0.5 *
    (2 * p1 +
      (-p0 + p2) * t +
      (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
      (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
  );
}

/**
 * One entity's state at a moment, interpolated between the two keyframes that bracket it.
 *
 * Before the first keyframe and after the last it holds, rather than extrapolating: a track is the movement
 * the engine decided on, and inventing movement past its ends would show something that never happened.
 */
export function sampleAt(
  keyframes: readonly HighlightKeyframe[],
  timeMs: number,
): KeyframeSample | null {
  if (keyframes.length === 0) {
    return null;
  }

  const first = keyframes[0];
  const last = keyframes[keyframes.length - 1];

  if (timeMs <= first.timeMilliseconds) {
    return sampleFrom(first);
  }

  if (timeMs >= last.timeMilliseconds) {
    return sampleFrom(last);
  }

  for (let index = 1; index < keyframes.length; index += 1) {
    const to = keyframes[index];

    if (timeMs > to.timeMilliseconds) {
      continue;
    }

    const from = keyframes[index - 1];
    const span = to.timeMilliseconds - from.timeMilliseconds;

    if (span <= 0) {
      return sampleFrom(to);
    }

    const progress = (timeMs - from.timeMilliseconds) / span;

    // Two keyframes have no neighbours to bend around, so the segment is the straight line they describe.
    if (keyframes.length === 2) {
      return {
        x: from.x + (to.x - from.x) * progress,
        y: from.y + (to.y - from.y) * progress,
        z: clampAltitude(altitudeOf(from) + (altitudeOf(to) - altitudeOf(from)) * progress),
        speed: from.speed ?? 0,
        action: from.action ?? null,
      };
    }

    const before = keyframes[index - 2] ?? from;
    const after = keyframes[index + 1] ?? to;

    return {
      x: clampCoordinate(catmullRom(before.x, from.x, to.x, after.x, progress)),
      y: clampCoordinate(catmullRom(before.y, from.y, to.y, after.y, progress)),
      z: clampAltitude(
        catmullRom(
          altitudeOf(before),
          altitudeOf(from),
          altitudeOf(to),
          altitudeOf(after),
          progress,
        ),
      ),
      // The speed and the action belong to the keyframe the segment starts on: the strike is a moment the
      // shooter reached, and the effect it triggers plays out over the movement that follows it.
      speed: from.speed ?? 0,
      action: from.action ?? null,
    };
  }

  return sampleFrom(last);
}

/** One entity's ground position at a moment, which is what the trail and the existing geometry use. */
export function positionAt(
  keyframes: readonly HighlightKeyframe[],
  timeMs: number,
): PitchPoint | null {
  const sample = sampleAt(keyframes, timeMs);

  return sample === null ? null : { x: sample.x, y: sample.y };
}

/**
 * Every entity's state at a moment, ready to draw.
 *
 * An entity with no track holds its anchor, which is what the engine sends for a player who is not involved
 * in the passage — two keyframes rather than a frame per animation tick (`§9.3`).
 */
export function frameAt(
  entities: readonly RendererEntity[],
  tracks: readonly RendererTrack[],
  timeMs: number,
): readonly FrameEntity[] {
  const byId = new Map(tracks.map((track) => [track.entityId, track.keyframes]));

  return entities.map((entity) => {
    const sample = sampleAt(byId.get(entity.id) ?? [], timeMs);

    return sample === null
      ? { entity, position: entity.anchor, z: 0, speed: 0, action: null }
      : {
          entity,
          position: { x: sample.x, y: sample.y },
          z: sample.z,
          speed: sample.speed,
          action: sample.action,
        };
  });
}

function sampleFrom(keyframe: HighlightKeyframe): KeyframeSample {
  return {
    x: keyframe.x,
    y: keyframe.y,
    z: altitudeOf(keyframe),
    speed: keyframe.speed ?? 0,
    action: keyframe.action ?? null,
  };
}

function altitudeOf(keyframe: HighlightKeyframe): number {
  return keyframe.z ?? 0;
}

function clampCoordinate(value: number): number {
  return Math.min(PITCH_SCALE, Math.max(0, value));
}

function clampAltitude(value: number): number {
  return Math.min(ALTITUDE_SCALE, Math.max(0, value));
}
