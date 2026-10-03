import { SHADOW_MIN_ALTITUDE } from './pitch-layout';
import { FrameEntity } from './renderer.models';

/**
 * What a frame's action effects are decided from (`replay-v3`).
 *
 * The renderer asks these questions every frame, so the answers live here as pure functions over a frame
 * rather than inside the drawing code: a test can assert that two players five metres apart with the ball
 * between them are a duel, and that a struck ball earns a trail, without a canvas.
 */

/** The action tags the engine writes where the ball is struck at goal. */
const STRIKE_ACTIONS = new Set(['shot', 'penalty', 'free_kick']);

/**
 * The action tags that earn the ball a trail (`replay-v3`).
 *
 * A trail is a projectile effect, so it belongs to a struck or crossed ball rather than to a rolled pass —
 * the plan's rule is that a short ground pass has none.
 */
const TRAIL_ACTIONS = new Set(['shot', 'penalty', 'free_kick', 'cross']);

/** The action tags a duel carries, if a passage ever tags one. */
const TACKLE_ACTIONS = new Set(['tackle', 'duel', 'interception']);

/** The action tags a goalkeeper's lateral save or dive carries. */
const DIVE_ACTIONS = new Set(['save', 'dive']);

/** How close a player must be to the ball to be shown as in possession, in normalized units (~9 m). */
export const POSSESSION_DISTANCE = 900;

/** The speed, in normalized units per second, at or above which an airborne ball earns a trail. */
export const TRAIL_MIN_SPEED = 2_400;

/** Whether an action tag means the ball was struck at goal. */
export function isStrikeAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && STRIKE_ACTIONS.has(action);
}

/** Whether an action tag means a duel. */
export function isTackleAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && TACKLE_ACTIONS.has(action);
}

/** Whether an action tag means a shot or a cross, which is what earns the ball a trail. */
export function isTrailAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && TRAIL_ACTIONS.has(action);
}

/** Whether an action tag means a goalkeeper's dive or save. */
export function isDiveAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && DIVE_ACTIONS.has(action);
}

/** The player the ball is at, which is the one whose name tag is worth showing. */
export function nearestPlayerToBall(
  frame: readonly FrameEntity[],
  maxDistance = POSSESSION_DISTANCE,
): FrameEntity | null {
  const ball = ballOf(frame);

  if (ball === null) {
    return null;
  }

  let nearest: FrameEntity | null = null;
  let nearestDistance = maxDistance;

  for (const item of frame) {
    if (item.entity.isBall || item.entity.side === null) {
      continue;
    }

    const distanceToBall = distance(item.position, ball.position);

    if (distanceToBall <= nearestDistance) {
      nearest = item;
      nearestDistance = distanceToBall;
    }
  }

  return nearest;
}

/**
 * How much of a ball trail a moment deserves, from its altitude and speed (`replay-v3`).
 *
 * Speed alone would miss a lofted cross, which is slow over the ground and fast through the air, so
 * altitude counts too. The value is only a strength: whether a trail is drawn at all is `wantsTrail`'s
 * decision, because a rolled pass shares the grass with a struck ball.
 */
export function trailStrength(z: number, speed: number): number {
  return Math.min(1, Math.max(speed / 3_000, z / 45));
}

/**
 * Whether the ball earns a trail at a moment (`replay-v3`).
 *
 * A trail is only for a shot or a cross, or for a genuinely airborne ball — never for the ground passes
 * that make up most of a passage, which is what the plan asks for.
 */
export function wantsTrail(action: string | null | undefined, z: number, speed: number): boolean {
  if (isTrailAction(action)) {
    return true;
  }

  return z >= SHADOW_MIN_ALTITUDE && trailStrength(z, speed) > 0.4;
}

function ballOf(frame: readonly FrameEntity[]): FrameEntity | null {
  for (const item of frame) {
    if (item.entity.isBall) {
      return item;
    }
  }

  return null;
}

function distance(
  one: { readonly x: number; readonly y: number },
  other: { readonly x: number; readonly y: number },
): number {
  return Math.hypot(one.x - other.x, one.y - other.y);
}
