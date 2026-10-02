import { Highlight } from '../../../core/match/match.models';
import { ClashPoint, FrameEntity } from './renderer.models';

/**
 * What a frame's action effects are decided from (Stage 6).
 *
 * The renderer asks these questions every frame, so the answers live here as pure functions over a frame
 * rather than inside the drawing code: a test can assert that two players five metres apart with the ball
 * between them are a duel, and that a frame with the ball in the net is a celebration, without a canvas.
 */

/** The action tags the engine writes where the ball is struck at goal. */
const STRIKE_ACTIONS = new Set(['shot', 'penalty', 'free_kick']);

/** The action tags a duel carries, if a passage ever tags one. */
const TACKLE_ACTIONS = new Set(['tackle', 'duel']);

/** The commentary templates that report a goal, which is what pins the celebration to a moment. */
const GOAL_TEMPLATES = new Set(['match.goal', 'match.penalty.goal']);

/** The outcome codes that mean the ball finished in the net. */
const GOAL_OUTCOMES = new Set(['goal', 'penalty_goal']);

/** How close two opposing players must be to read as contesting the ball, in normalized units (~4 m). */
export const DUEL_DISTANCE = 380;

/** How close the duel must be to the ball to matter, in normalized units (~9 m). */
export const DUEL_BALL_DISTANCE = 900;

/** How close a player must be to the ball to be shown as in possession, in normalized units (~9 m). */
export const POSSESSION_DISTANCE = 900;

/** Whether an action tag means the ball was struck at goal. */
export function isStrikeAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && STRIKE_ACTIONS.has(action);
}

/** Whether an action tag means a duel. */
export function isTackleAction(action: string | null | undefined): boolean {
  return action !== null && action !== undefined && TACKLE_ACTIONS.has(action);
}

/**
 * The duels a frame shows, as the point between each pair of contesting players.
 *
 * A duel is read from the geometry the engine already sent rather than from an event: two players of
 * opposite sides inside a few metres, with the ball between them. A tag on either player's keyframe counts
 * too, so a presentation that names its duels is drawn even when the players are a stride apart.
 */
export function duelClashes(
  frame: readonly FrameEntity[],
  maxDistance = DUEL_DISTANCE,
  ballDistance = DUEL_BALL_DISTANCE,
): readonly ClashPoint[] {
  const ball = frame.find((item) => item.entity.isBall);
  const players = frame.filter((item) => !item.entity.isBall && item.entity.side !== null);
  const clashes: ClashPoint[] = [];

  for (let first = 0; first < players.length; first += 1) {
    for (let second = first + 1; second < players.length; second += 1) {
      const one = players[first];
      const other = players[second];

      if (one.entity.side === other.entity.side) {
        continue;
      }

      const midpoint = {
        x: (one.position.x + other.position.x) / 2,
        y: (one.position.y + other.position.y) / 2,
      };
      const tagged = isTackleAction(one.action) || isTackleAction(other.action);
      const separation = Math.hypot(
        one.position.x - other.position.x,
        one.position.y - other.position.y,
      );

      if (!tagged && separation > maxDistance) {
        continue;
      }

      if (ball !== undefined && distance(midpoint, ball.position) > ballDistance) {
        continue;
      }

      clashes.push({
        x: midpoint.x,
        y: midpoint.y,
        // A stable offset per duel, so two contests on opposite flanks are not drawn pulsing in lockstep.
        phase: (Math.round(midpoint.x) + Math.round(midpoint.y)) % 1_000,
      });
    }
  }

  return clashes;
}

/** The player the ball is at, which is the one whose name tag is worth showing. */
export function nearestPlayerToBall(
  frame: readonly FrameEntity[],
  maxDistance = POSSESSION_DISTANCE,
): FrameEntity | null {
  const ball = frame.find((item) => item.entity.isBall);

  if (ball === undefined) {
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
 * How much of a ball trail a moment deserves: nothing for a rolled pass, a streak for a struck ball.
 *
 * Speed alone would miss a lofted cross, which is slow over the ground and fast through the air, so
 * altitude counts too — the plan's trail is for high-velocity shots and aerial balls alike.
 */
export function trailStrength(z: number, speed: number): number {
  return Math.min(1, Math.max(speed / 3_000, z / 45));
}

/**
 * When a goal's celebration begins, in animation milliseconds, or null when the passage is not a goal.
 *
 * The synchronized commentary already pins the outcome to a moment, so the flash is drawn from the beat the
 * narration says the ball went in — which is where the ball is in the net rather than the passage's start.
 */
export function celebrationStartMilliseconds(highlight: Highlight): number | null {
  if (!GOAL_OUTCOMES.has(highlight.outcomeCode)) {
    return null;
  }

  const outcome = (highlight.commentary ?? []).find((line) => GOAL_TEMPLATES.has(line.templateKey));

  return outcome?.timeMilliseconds ?? Math.round(highlight.durationMilliseconds * 0.7);
}

function distance(
  one: { readonly x: number; readonly y: number },
  other: { readonly x: number; readonly y: number },
): number {
  return Math.hypot(one.x - other.x, one.y - other.y);
}
