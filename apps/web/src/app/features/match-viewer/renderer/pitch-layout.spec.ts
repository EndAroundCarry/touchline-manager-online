import {
  PITCH_ASPECT_RATIO,
  altitudeLift,
  altitudeScale,
  isOnPitch,
  pitchGeometry,
  pitchRect,
  toCanvasPoint,
} from './pitch-layout';

/** The pitch's geometry guarantees (`§9.3`, `§9.4`, master plan Stage 6). */

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

describe('pitchGeometry', () => {
  // One metre is ten pixels on both axes, so every marking can be read in metres straight off the result.
  const rect = { x: 0, y: 0, width: 1_050, height: 680 };

  it('draws the markings at their real sizes', () => {
    const geometry = pitchGeometry(rect);

    expect(geometry.penaltyBoxDepth).toBeCloseTo(165, 5);
    expect(geometry.penaltyBoxHeight).toBeCloseTo(403.2, 5);
    expect(geometry.sixYardBoxDepth).toBeCloseTo(55, 5);
    expect(geometry.sixYardBoxHeight).toBeCloseTo(183.2, 5);
    expect(geometry.penaltySpotInset).toBeCloseTo(110, 5);
    expect(geometry.centreCircleRadius).toBeCloseTo(91.5, 5);
    expect(geometry.goalWidth).toBeCloseTo(73.2, 5);
    expect(geometry.cornerArcRadius).toBeCloseTo(10, 5);
  });

  it('centres the circle and lays the mown stripes across the pitch', () => {
    const geometry = pitchGeometry(rect);

    expect(geometry.centreX).toBeCloseTo(525, 5);
    expect(geometry.centreY).toBeCloseTo(340, 5);
    expect(geometry.stripeCount).toBeGreaterThan(1);
    expect(geometry.stripeWidth * geometry.stripeCount).toBeCloseTo(rect.width, 5);
  });
});

describe('altitude', () => {
  const rect = { x: 0, y: 0, width: 1_050, height: 680 };

  it('lifts the ball by a share of the pitch, growing with altitude', () => {
    expect(altitudeLift(rect, 0)).toBe(0);
    expect(altitudeLift(rect, 50)).toBeCloseTo(rect.height * 0.15, 5);
    expect(altitudeLift(rect, 100)).toBeCloseTo(rect.height * 0.3, 5);
  });

  it("keeps an out-of-band altitude inside the contract's 0…100", () => {
    expect(altitudeLift(rect, -20)).toBe(0);
    expect(altitudeLift(rect, 400)).toBeCloseTo(rect.height * 0.3, 5);
  });

  it('grows the ball with altitude, so height reads as size as well as offset', () => {
    expect(altitudeScale(0)).toBe(1);
    expect(altitudeScale(100)).toBeCloseTo(1.9, 5);
    expect(altitudeScale(200)).toBeCloseTo(1.9, 5);
  });
});
