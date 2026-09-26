import { HighlightKeyframe } from '../../../core/match/match.models';
import { FrameEntity, PitchPoint, RendererEntity, RendererTrack } from './renderer.models';

/**
 * Turns semantic keyframes into a position at an arbitrary moment (`§9.1`, `§9.4`).
 *
 * The client interpolates rather than being sent frames, so the payload is a few kilobytes and the replay
 * is identical on a 60 Hz and a 144 Hz display. Everything here is pure and takes the animation time it is
 * given, so a test can ask for any moment without a canvas or a clock.
 */

/**
 * One entity's position at a moment, interpolated between the two keyframes that bracket it.
 *
 * Before the first keyframe and after the last it holds, rather than extrapolating: a track is the movement
 * the engine decided on, and inventing movement past its ends would show something that never happened.
 */
export function positionAt(
  keyframes: readonly HighlightKeyframe[],
  timeMs: number,
): PitchPoint | null {
  if (keyframes.length === 0) {
    return null;
  }

  const first = keyframes[0];
  const last = keyframes[keyframes.length - 1];

  if (timeMs <= first.timeMilliseconds) {
    return { x: first.x, y: first.y };
  }

  if (timeMs >= last.timeMilliseconds) {
    return { x: last.x, y: last.y };
  }

  for (let index = 1; index < keyframes.length; index += 1) {
    const to = keyframes[index];

    if (timeMs > to.timeMilliseconds) {
      continue;
    }

    const from = keyframes[index - 1];
    const span = to.timeMilliseconds - from.timeMilliseconds;

    if (span <= 0) {
      return { x: to.x, y: to.y };
    }

    const progress = (timeMs - from.timeMilliseconds) / span;

    return {
      x: from.x + (to.x - from.x) * progress,
      y: from.y + (to.y - from.y) * progress,
    };
  }

  return { x: last.x, y: last.y };
}

/**
 * Every entity's position at a moment, ready to draw.
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

  return entities.map((entity) => ({
    entity,
    position: positionAt(byId.get(entity.id) ?? [], timeMs) ?? entity.anchor,
  }));
}
