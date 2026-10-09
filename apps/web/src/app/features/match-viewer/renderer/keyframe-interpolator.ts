import { FilmTrack } from '../../../core/match/film-timeline';
import { PITCH_COORDINATE_SCALE, PITCH_LENGTH_METRES, PITCH_WIDTH_METRES } from './pitch-layout';

/**
 * Turns a film track into a position at an arbitrary moment (`§9.1`, `§9.4`, `replay-v4`).
 *
 * The client interpolates rather than being sent frames, so the payload is a couple of megabytes instead of
 * hundreds and the replay is identical on a 60 Hz and a 144 Hz display. Everything here is pure and takes the
 * animation time it is given, so a test can ask for any moment without a canvas or a clock.
 *
 * **Players** move on a cubic Hermite spline whose tangents are *planar* and aware of *time*: the server
 * keeps only the samples where a run changed, so they are not evenly spaced, and a spline that assumed they
 * were — the Catmull-Rom this replaces — overshot a corner and wobbled after a slow stretch. The tangent at a
 * keyframe is the time-weighted mean of the two segment velocity vectors, so a player who turns keeps most of
 * his speed through the bend (a per-axis monotone spline zeroed an axis at every turn and slowed him on it),
 * and it is zero only at a real stop, a cut or a track end. It is limited to 1.5 times the slower segment and
 * the curve is kept within 0.3 m of the line between its keyframes, so a player cannot swing past where they
 * were going. It is still an *interpolating* spline: it passes through every keyframe, so a strike or a dive
 * lands when it was authored. Altitude keeps the monotone spline, because a header must not dip below the
 * ground.
 *
 * **The ball** is linear. The server samples a flight every hundred milliseconds already, and a curve through
 * those samples would invent a bend in a pass that is a straight line.
 *
 * A **step** — two keyframes at one instant, which is what a cut is — is never interpolated across: the entity
 * is at the first until the instant and at the second from it. Each track keeps a cursor, so a frame does not
 * scan the keyframes or allocate anything; it moves the cursor on by a segment, or searches after a seek.
 */

/** One entity's state at one moment: where it is, how high it is, and what it is doing. */
export interface KeyframeSample {
  x: number;
  y: number;
  z: number;
  speed: number;
  action: string | null;
}

/** Creates a sample to be filled in, so a caller can keep one and reuse it every frame. */
export function emptySample(): KeyframeSample {
  return { x: 0, y: 0, z: 0, speed: 0, action: null };
}

/**
 * The tangents of a monotone cubic Hermite spline through `(times[i], values[i])`, in value units per
 * millisecond (Fritsch–Carlson, for spacing that is not even).
 *
 * Two keyframes at one instant are a break: the spline on either side of it is built from its own side only,
 * and the tangents at the break come from the segment next to them.
 */
export function monotoneTangents(
  times: ArrayLike<number>,
  values: ArrayLike<number>,
): Float64Array {
  const count = times.length;
  const tangents = new Float64Array(count);

  if (count < 2) {
    return tangents;
  }

  const spans = new Float64Array(count - 1);
  const secants = new Float64Array(count - 1);

  for (let index = 0; index < count - 1; index += 1) {
    spans[index] = times[index + 1] - times[index];
    secants[index] = spans[index] > 0 ? (values[index + 1] - values[index]) / spans[index] : 0;
  }

  for (let index = 0; index < count; index += 1) {
    const hasLeft = index > 0 && spans[index - 1] > 0;
    const hasRight = index < count - 1 && spans[index] > 0;

    if (hasLeft && hasRight) {
      const left = secants[index - 1];
      const right = secants[index];

      // A turning point or a stop: the velocity is zero there, which is what keeps the curve inside its
      // keyframes. Otherwise the three-point slope for uneven spacing.
      tangents[index] =
        left * right <= 0
          ? 0
          : (spans[index] * left + spans[index - 1] * right) / (spans[index - 1] + spans[index]);
    } else if (hasLeft) {
      tangents[index] = secants[index - 1];
    } else if (hasRight) {
      tangents[index] = secants[index];
    }
  }

  for (let index = 0; index < count - 1; index += 1) {
    if (spans[index] <= 0) {
      continue;
    }

    if (secants[index] === 0) {
      tangents[index] = 0;
      tangents[index + 1] = 0;

      continue;
    }

    const alpha = tangents[index] / secants[index];
    const beta = tangents[index + 1] / secants[index];
    const size = alpha * alpha + beta * beta;

    // Outside the circle of radius three the spline would overshoot its keyframes, so the tangents are
    // brought back onto it.
    if (size > 9) {
      const scale = 3 / Math.sqrt(size);

      tangents[index] = scale * alpha * secants[index];
      tangents[index + 1] = scale * beta * secants[index];
    }
  }

  return tangents;
}

/** Metres per film unit along each axis: the pitch is 105 m by 68 m on a 10 000 by 10 000 grid, so a unit is not square. */
const METRES_PER_UNIT_X = PITCH_LENGTH_METRES / PITCH_COORDINATE_SCALE;
const METRES_PER_UNIT_Y = PITCH_WIDTH_METRES / PITCH_COORDINATE_SCALE;

/** Below this, in metres per millisecond (0.3 m/s), both neighbouring segments mean the player is standing. */
const STOP_SPEED = 0.0003;

/** A tangent is never faster than this multiple of the slower neighbouring segment, so a corner cannot overshoot. */
const TANGENT_SPEED_LIMIT = 1.5;

/** How far, in metres, a drawn position may stray from the straight line between its two keyframes. */
const CHORD_GUARD_METRES = 0.3;

/**
 * The tangents of a planar path through `(times[i], xs[i], ys[i])`, in film units per millisecond, so a player who
 * changes direction at a keyframe carries his speed round the corner instead of stopping on it.
 *
 * Each axis used to be made monotone on its own, which sets that axis's velocity to zero wherever it changes sign:
 * every bend slowed the token down. Here the tangent is the time-aware three-point slope of the two segment
 * *velocity vectors*, worked out in metres (the grid is not square), limited to 1.5 times the slower of the two
 * segments, and zero only at a real stop, at a cut, or at either end of the track.
 */
export function planarTangents(
  times: ArrayLike<number>,
  xs: ArrayLike<number>,
  ys: ArrayLike<number>,
): { readonly x: Float64Array; readonly y: Float64Array } {
  const count = times.length;
  const tangentX = new Float64Array(count);
  const tangentY = new Float64Array(count);

  for (let index = 1; index < count - 1; index += 1) {
    const before = times[index] - times[index - 1];
    const after = times[index + 1] - times[index];

    // Next to a cut, or at an end, there is no run to carry on: the entity starts or stops there.
    if (before <= 0 || after <= 0) {
      continue;
    }

    const leftX = ((xs[index] - xs[index - 1]) * METRES_PER_UNIT_X) / before;
    const leftY = ((ys[index] - ys[index - 1]) * METRES_PER_UNIT_Y) / before;
    const rightX = ((xs[index + 1] - xs[index]) * METRES_PER_UNIT_X) / after;
    const rightY = ((ys[index + 1] - ys[index]) * METRES_PER_UNIT_Y) / after;
    const leftSpeed = Math.hypot(leftX, leftY);
    const rightSpeed = Math.hypot(rightX, rightY);

    if (leftSpeed < STOP_SPEED && rightSpeed < STOP_SPEED) {
      continue;
    }

    let slopeX = (after * leftX + before * rightX) / (before + after);
    let slopeY = (after * leftY + before * rightY) / (before + after);
    const limit = TANGENT_SPEED_LIMIT * Math.min(leftSpeed, rightSpeed);
    const slope = Math.hypot(slopeX, slopeY);

    if (slope > limit) {
      const scale = limit / slope;

      slopeX *= scale;
      slopeY *= scale;
    }

    tangentX[index] = slopeX / METRES_PER_UNIT_X;
    tangentY[index] = slopeY / METRES_PER_UNIT_Y;
  }

  return { x: tangentX, y: tangentY };
}

/** The cubic Hermite basis at `fraction` (0…1) between two values with their tangents and the span between them. */
export function hermite(
  from: number,
  to: number,
  fromTangent: number,
  toTangent: number,
  span: number,
  fraction: number,
): number {
  const squared = fraction * fraction;
  const cubed = squared * fraction;

  return (
    (2 * cubed - 3 * squared + 1) * from +
    (cubed - 2 * squared + fraction) * span * fromTangent +
    (-2 * cubed + 3 * squared) * to +
    (cubed - squared) * span * toTangent
  );
}

/** Samples one film track, with a cursor so that playing forward costs a comparison a frame. */
export class TrackInterpolator {
  private readonly xTangents: Float64Array;
  private readonly yTangents: Float64Array;
  private readonly zTangents: Float64Array;
  private cursor = 0;

  /**
   * Initializes the interpolator over a track.
   *
   * @param track The track.
   * @param smooth Whether to curve between keyframes (a player) or join them with straight lines (the ball).
   */
  constructor(
    private readonly track: FilmTrack,
    private readonly smooth: boolean,
  ) {
    const empty = new Float64Array(0);

    const planar = smooth ? planarTangents(track.times, track.xs, track.ys) : null;

    this.xTangents = planar?.x ?? empty;
    this.yTangents = planar?.y ?? empty;
    this.zTangents = smooth ? monotoneTangents(track.times, track.zs) : empty;
  }

  /** How many keyframes the track has. */
  get length(): number {
    return this.track.length;
  }

  /**
   * Writes the state at a moment into `out`, moving the cursor to the segment the moment falls in.
   *
   * Before the first keyframe and after the last it holds, rather than extrapolating: a track is the movement
   * the film decided on, and inventing movement past its ends would show something that never happened.
   *
   * @returns Whether the track has anything to sample.
   */
  sample(timeMs: number, out: KeyframeSample): boolean {
    if (this.track.length === 0) {
      return false;
    }

    this.cursor = this.locate(timeMs, this.cursor);
    this.write(this.cursor, timeMs, out);

    return true;
  }

  /**
   * Writes the state at a moment into `out` without moving the cursor, for looking *back* along a track — a
   * ball's trail, a strike's streak — while the cursor follows the playhead.
   */
  sampleAt(timeMs: number, out: KeyframeSample): boolean {
    if (this.track.length === 0) {
      return false;
    }

    this.write(this.locate(timeMs, -1), timeMs, out);

    return true;
  }

  /**
   * The index of the keyframe the segment containing a moment starts on, or the last keyframe at or after the
   * track's end. Two keyframes at one instant are skipped past, so a moment at a step is on the far side of it.
   */
  private locate(timeMs: number, hint: number): number {
    const { times, length } = this.track;
    const last = length - 1;

    if (timeMs >= times[last]) {
      return last;
    }

    if (timeMs < times[0]) {
      return 0;
    }

    if (hint >= 0 && hint < last) {
      if (times[hint] <= timeMs && timeMs < times[hint + 1]) {
        return hint;
      }

      // Playing forward moves on by one segment, so that is the next thing worth trying.
      if (hint + 1 < last && times[hint + 1] <= timeMs && timeMs < times[hint + 2]) {
        return hint + 1;
      }
    }

    let low = 0;
    let high = length;

    while (low < high) {
      const middle = (low + high) >>> 1;

      if (times[middle] <= timeMs) {
        low = middle + 1;
      } else {
        high = middle;
      }
    }

    return Math.min(last, low - 1);
  }

  private write(index: number, timeMs: number, out: KeyframeSample): void {
    const { times, xs, ys, zs, speeds, actions, length } = this.track;

    // The speed and the action belong to the keyframe the segment starts on: a strike is a moment the shooter
    // reached, and the effect it triggers plays out over the movement that follows it.
    out.speed = speeds[index];
    out.action = actions[index];

    if (index >= length - 1 || timeMs <= times[index]) {
      out.x = xs[index];
      out.y = ys[index];
      out.z = zs[index];

      return;
    }

    const span = times[index + 1] - times[index];
    const fraction = (timeMs - times[index]) / span;

    if (!this.smooth) {
      out.x = xs[index] + (xs[index + 1] - xs[index]) * fraction;
      out.y = ys[index] + (ys[index + 1] - ys[index]) * fraction;
      out.z = zs[index] + (zs[index + 1] - zs[index]) * fraction;

      return;
    }

    const fromX = xs[index];
    const fromY = ys[index];
    const toX = xs[index + 1];
    const toY = ys[index + 1];
    let x = hermite(fromX, toX, this.xTangents[index], this.xTangents[index + 1], span, fraction);
    let y = hermite(fromY, toY, this.yTangents[index], this.yTangents[index + 1], span, fraction);

    // The curve may bow a little off the straight line between its keyframes, but not past a small guard: the
    // nearest point of the chord is found in metres, and a point further than the guard is pulled back to it.
    const chordX = (toX - fromX) * METRES_PER_UNIT_X;
    const chordY = (toY - fromY) * METRES_PER_UNIT_Y;
    const chordLength = chordX * chordX + chordY * chordY;
    const offsetX = (x - fromX) * METRES_PER_UNIT_X;
    const offsetY = (y - fromY) * METRES_PER_UNIT_Y;
    const along =
      chordLength > 0
        ? Math.min(1, Math.max(0, (offsetX * chordX + offsetY * chordY) / chordLength))
        : 0;
    const strayX = offsetX - along * chordX;
    const strayY = offsetY - along * chordY;
    const stray = Math.hypot(strayX, strayY);

    if (stray > CHORD_GUARD_METRES) {
      const scale = CHORD_GUARD_METRES / stray;

      x = fromX + (along * chordX + strayX * scale) / METRES_PER_UNIT_X;
      y = fromY + (along * chordY + strayY * scale) / METRES_PER_UNIT_Y;
    }

    out.x = x;
    out.y = y;
    out.z = within(
      hermite(
        zs[index],
        zs[index + 1],
        this.zTangents[index],
        this.zTangents[index + 1],
        span,
        fraction,
      ),
      zs[index],
      zs[index + 1],
    );
  }
}

/** Keeps a value between two others, which a monotone spline already guarantees up to rounding. */
function within(value: number, one: number, other: number): number {
  return value < Math.min(one, other)
    ? Math.min(one, other)
    : value > Math.max(one, other)
      ? Math.max(one, other)
      : value;
}
