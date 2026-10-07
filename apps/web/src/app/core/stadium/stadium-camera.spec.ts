import {
  VIEW_HEIGHT,
  VIEW_WIDTH,
  WORLD_FRAME,
  facesCamera,
  nearness,
  outline,
  project,
  spacing,
  trace,
  unitLength,
} from './stadium-camera';

/**
 * The camera of the stadium picture.
 *
 * What matters is that the ground is seen from the corner the drawing assumes (so the order the stands are drawn in
 * is right), that the whole framed world lands inside the drawing, and that the faces it calls hidden are the ones
 * that look away.
 */

describe('stadium camera', () => {
  it('puts the whole framed world inside the drawing', () => {
    for (const x of [WORLD_FRAME.minX, WORLD_FRAME.maxX]) {
      for (const y of [WORLD_FRAME.minY, WORLD_FRAME.maxY]) {
        for (const z of [0, WORLD_FRAME.maxZ]) {
          const [screenX, screenY] = project(x, y, z);

          expect(screenX).toBeGreaterThanOrEqual(0);
          expect(screenX).toBeLessThanOrEqual(VIEW_WIDTH);
          expect(screenY).toBeGreaterThanOrEqual(0);
          expect(screenY).toBeLessThanOrEqual(VIEW_HEIGHT);
        }
      }
    }
  });

  it('sees the pitch from a corner: along it goes right and up, across it right and down, up is up', () => {
    const [originX, originY] = project(0, 0, 0);
    const [alongX, alongY] = project(10, 0, 0);
    const [acrossX, acrossY] = project(0, 10, 0);
    const [upX, upY] = project(0, 0, 10);

    expect(alongX).toBeGreaterThan(originX);
    expect(alongY).toBeLessThan(originY);
    expect(acrossX).toBeGreaterThan(originX);
    expect(acrossY).toBeGreaterThan(originY);
    expect(upX).toBe(originX);
    expect(upY).toBeLessThan(originY);
  });

  it('is nearer the camera toward the opposite touchline and the west goal', () => {
    expect(nearness(0, 100)).toBeGreaterThan(nearness(0, 0));
    expect(nearness(0, 50)).toBeGreaterThan(nearness(160, 50));
  });

  it('turns a face toward the camera only when it looks toward it', () => {
    // The roofs and treads look up, the main stand's risers look toward the opposite touchline.
    expect(facesCamera(0, 0, 1)).toBe(true);
    expect(facesCamera(0, 0, -1)).toBe(false);
    expect(facesCamera(0, 1, 0)).toBe(true);
    expect(facesCamera(0, -1, 0)).toBe(false);
    expect(facesCamera(-1, 0, 0)).toBe(true);
    expect(facesCamera(1, 0, 0)).toBe(false);
  });

  it('measures a unit of the ground on the screen, so a dash can be sized', () => {
    expect(unitLength('x')).toBeGreaterThan(0);
    expect(unitLength('y')).toBeGreaterThan(0);

    // A row 1 unit deep is thinner on the screen than it is long, because the ground is foreshortened.
    expect(spacing('x')).toBeGreaterThan(0);
    expect(spacing('x')).toBeLessThan(unitLength('y'));
    expect(spacing('y')).toBeLessThan(unitLength('x'));
  });

  it('writes outlines and lines as paths a drawing can use', () => {
    const square = outline([
      [0, 0, 0],
      [10, 0, 0],
      [10, 10, 0],
    ]);

    expect(square.startsWith('M')).toBe(true);
    expect(square.endsWith('Z')).toBe(true);
    expect(square.match(/L/g)).toHaveLength(2);
    expect(
      trace([
        [0, 0, 0],
        [1, 1, 1],
      ]).endsWith('Z'),
    ).toBe(false);
    expect(square).not.toMatch(/NaN|Infinity/);
  });

  it('rounds to a tenth of a unit', () => {
    const [x, y] = project(12.3456, 7.891, 3.21);

    expect(Math.round(x * 10) / 10).toBe(x);
    expect(Math.round(y * 10) / 10).toBe(y);
  });
});
