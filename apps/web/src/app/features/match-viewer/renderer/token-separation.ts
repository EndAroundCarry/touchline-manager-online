/**
 * Keeps player tokens from sitting on top of each other (`replay-v17`, M1).
 *
 * This is display only: the film's positions are untouched and the push is worked out afresh from them on every
 * frame, so it cannot drift or accumulate. A token is about 2 m across, so two players challenging for the ball
 * really are inside each other; this shows them side by side instead, which is what a manager needs to read a
 * duel. Some overlap is still fine (tokens may overlap by about a quarter).
 */

/** The nearest two token centres may come, as a multiple of a token's radius: 1.45 leaves about a quarter overlapping. */
export const TOKEN_MIN_GAP = 1.45;

/** How many relaxation passes are run over the tokens in a frame. */
export const SEPARATION_PASSES = 3;

/**
 * Works out how far each token is pushed so that no two centres are closer than `minGap`.
 *
 * Two tokens that are too close are moved apart along the line between their centres, half each, for a few
 * passes. The push depends continuously on the distance, so a token sliding past another is pushed smoothly
 * aside and back; the one exception is two tokens at exactly the same point, which have no line between them,
 * and those are split along x by entity id so the result is still deterministic. No token ever moves more
 * than `maxPush` in total.
 *
 * @param xs Token centres, in pixels.
 * @param ys Token centres, in pixels.
 * @param ids The entity ids, which break exact ties.
 * @param count How many tokens to look at.
 * @param minGap The smallest allowed distance between two centres, in pixels.
 * @param maxPush The most a token may be moved, in pixels.
 * @param offsetX Receives each token's push, in pixels.
 * @param offsetY Receives each token's push, in pixels.
 */
export function separateTokens(
  xs: ArrayLike<number>,
  ys: ArrayLike<number>,
  ids: readonly string[],
  count: number,
  minGap: number,
  maxPush: number,
  offsetX: Float64Array,
  offsetY: Float64Array,
): void {
  for (let index = 0; index < count; index += 1) {
    offsetX[index] = 0;
    offsetY[index] = 0;
  }

  for (let pass = 0; pass < SEPARATION_PASSES; pass += 1) {
    for (let first = 0; first < count - 1; first += 1) {
      for (let second = first + 1; second < count; second += 1) {
        const deltaX = xs[second] + offsetX[second] - (xs[first] + offsetX[first]);
        const deltaY = ys[second] + offsetY[second] - (ys[first] + offsetY[first]);
        let distance = Math.hypot(deltaX, deltaY);

        if (distance >= minGap) {
          continue;
        }

        // The direction is the line between the centres; with no line, the lower id goes to the left.
        let directionX = 0;
        let directionY = 0;

        if (distance < 1e-6) {
          directionX = ids[first] <= ids[second] ? 1 : -1;
          distance = 0;
        } else {
          directionX = deltaX / distance;
          directionY = deltaY / distance;
        }

        const push = (minGap - distance) / 2;
        const pushX = directionX * push;
        const pushY = directionY * push;

        offsetX[first] -= pushX;
        offsetY[first] -= pushY;
        offsetX[second] += pushX;
        offsetY[second] += pushY;
      }
    }
  }

  for (let index = 0; index < count; index += 1) {
    const length = Math.hypot(offsetX[index], offsetY[index]);

    if (length > maxPush) {
      offsetX[index] = (offsetX[index] / length) * maxPush;
      offsetY[index] = (offsetY[index] / length) * maxPush;
    }
  }
}
