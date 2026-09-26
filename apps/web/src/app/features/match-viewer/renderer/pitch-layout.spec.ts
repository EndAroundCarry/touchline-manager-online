import { PITCH_ASPECT_RATIO, isOnPitch, pitchRect, toCanvasPoint } from './pitch-layout';

/** The pitch's geometry guarantees (`§9.3`, `§9.4`). */

describe('pitchRect', () => {
  it('keeps the pitch in its real proportions and centres it', () => {
    const rect = pitchRect(1_000, 1_000);

    expect(rect.width / rect.height).toBeCloseTo(PITCH_ASPECT_RATIO, 3);
    expect(rect.x).toBeCloseTo((1_000 - rect.width) / 2, 5);
    expect(rect.y).toBeCloseTo((1_000 - rect.height) / 2, 5);
  });

  it('fits by height when the container is short, so the pitch is never stretched', () => {
    const rect = pitchRect(1_000, 200);

    expect(rect.height).toBeCloseTo(180, 5);
    expect(rect.width / rect.height).toBeCloseTo(PITCH_ASPECT_RATIO, 3);
  });
});

describe('toCanvasPoint', () => {
  const rect = { x: 10, y: 20, width: 100, height: 50 };

  it('maps the normalized corners onto the pitch rectangle', () => {
    expect(toCanvasPoint({ x: 0, y: 0 }, rect)).toEqual({ x: 10, y: 20 });
    expect(toCanvasPoint({ x: 10_000, y: 10_000 }, rect)).toEqual({ x: 110, y: 70 });
  });

  it('maps the centre of the pitch to the centre of the rectangle', () => {
    expect(toCanvasPoint({ x: 5_000, y: 5_000 }, rect)).toEqual({ x: 60, y: 45 });
  });
});

describe('isOnPitch', () => {
  it('bounds a position to the normalized scale', () => {
    expect(isOnPitch({ x: 0, y: 0 })).toBe(true);
    expect(isOnPitch({ x: 10_000, y: 10_000 })).toBe(true);
    expect(isOnPitch({ x: -1, y: 5_000 })).toBe(false);
    expect(isOnPitch({ x: 5_000, y: 10_001 })).toBe(false);
  });
});
