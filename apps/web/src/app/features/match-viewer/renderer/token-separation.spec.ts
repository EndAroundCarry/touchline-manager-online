import { SEPARATION_PASSES, TOKEN_MIN_GAP, separateTokens } from './token-separation';

/**
 * The de-overlap's guarantees (`replay-v17`, M1): two tokens are never left covering each other, a token that
 * is clear is not moved, the result is the same every time, and it changes smoothly as tokens move through each
 * other, so nothing jitters.
 */

const RADIUS = 8;
const GAP = TOKEN_MIN_GAP * RADIUS;

function separate(
  points: readonly (readonly [number, number])[],
  ids: readonly string[] = points.map((_, index) => `P${index}`),
): { x: number[]; y: number[] } {
  const offsetX = new Float64Array(points.length);
  const offsetY = new Float64Array(points.length);

  separateTokens(
    points.map((point) => point[0]),
    points.map((point) => point[1]),
    ids,
    points.length,
    GAP,
    RADIUS,
    offsetX,
    offsetY,
  );

  return {
    x: points.map((point, index) => point[0] + offsetX[index]),
    y: points.map((point, index) => point[1] + offsetY[index]),
  };
}

describe('separateTokens', () => {
  it('leaves tokens that are already clear of each other where they are', () => {
    const { x, y } = separate([
      [100, 100],
      [100 + GAP + 1, 100],
      [300, 300],
    ]);

    expect(x).toEqual([100, 100 + GAP + 1, 300]);
    expect(y).toEqual([100, 100, 300]);
  });

  it('pushes two overlapping tokens apart along their centre line, half each', () => {
    const { x, y } = separate([
      [100, 100],
      [104, 100],
    ]);

    expect(y).toEqual([100, 100]);
    expect(x[0]).toBeLessThan(100);
    expect(x[1]).toBeGreaterThan(104);
    expect(100 - x[0]).toBeCloseTo(x[1] - 104, 9);
    expect(x[1] - x[0]).toBeGreaterThan(GAP - 1e-6);
  });

  it('puts a token that sits exactly on another beside it, the lower id on the left, every time', () => {
    const first = separate(
      [
        [50, 50],
        [50, 50],
      ],
      ['A', 'B'],
    );
    const second = separate(
      [
        [50, 50],
        [50, 50],
      ],
      ['A', 'B'],
    );

    expect(first.x[0]).toBeLessThan(first.x[1]);
    expect(first).toEqual(second);
  });

  it('never moves a token further than the limit, however many are piled on it', () => {
    const points: [number, number][] = Array.from({ length: 8 }, () => [200, 200]);
    const offsetX = new Float64Array(8);
    const offsetY = new Float64Array(8);

    separateTokens(
      points.map((point) => point[0]),
      points.map((point) => point[1]),
      points.map((_, index) => `P${index}`),
      points.length,
      GAP,
      RADIUS,
      offsetX,
      offsetY,
    );

    for (let index = 0; index < 8; index += 1) {
      expect(Math.hypot(offsetX[index], offsetY[index])).toBeLessThanOrEqual(RADIUS + 1e-9);
    }
  });

  it('runs a fixed number of passes', () => {
    expect(SEPARATION_PASSES).toBe(3);
  });

  it('moves smoothly and boundedly while one token passes through another', () => {
    let previous: { x: number; y: number } | null = null;
    let largestJump = 0;

    // The second token crosses the first at 0.25 px a step, a few pixels off its line so there is a direction.
    for (let step = -80; step <= 80; step += 1) {
      const { x, y } = separate([
        [100, 100],
        [100 + step * 0.25, 101],
      ]);

      if (previous !== null) {
        largestJump = Math.max(largestJump, Math.hypot(x[0] - previous.x, y[0] - previous.y));
      }

      previous = { x: x[0], y: y[0] };

      expect(Math.hypot(x[0] - 100, y[0] - 100)).toBeLessThanOrEqual(RADIUS + 1e-9);
    }

    // A step of a quarter pixel moves the first token by a few tenths at most, apart from the one frame where
    // the two centres are a pixel apart and the line between them turns over.
    expect(largestJump).toBeLessThan(RADIUS * 0.5);
  });
});
