import {
  PITCH,
  SCENE_HEIGHT,
  SCENE_LEVELS,
  SCENE_WIDTH,
  Scene,
  buildScene,
  clampLevel,
  safeColour,
} from './stadium-scene';

/**
 * The ten stadium pictures.
 *
 * What matters is that every level is a distinct ground, that a level is always the one before it with
 * something added — so a manager watches the same stadium grow — and that nothing is ever drawn outside the
 * picture.
 */

const LEVELS = Array.from({ length: SCENE_LEVELS }, (_, index) => index + 1);

/** The area of stand a scene builds, counting every seat, terrace and box. */
function built(scene: Scene): number {
  return scene.rects
    .filter((rect) => rect.kind !== 'roof')
    .reduce((total, rect) => total + rect.width * rect.height, 0);
}

describe('stadium scenes', () => {
  it('has a picture for each of the ten levels', () => {
    expect(SCENE_LEVELS).toBe(10);
  });

  it('draws every level differently', () => {
    const drawings = LEVELS.map((level) => JSON.stringify(buildScene(level)));

    expect(new Set(drawings).size).toBe(10);
  });

  it('builds more at every level than at the one before', () => {
    const areas = LEVELS.map((level) => built(buildScene(level)));

    for (let index = 1; index < areas.length; index++) {
      expect(areas[index]).toBeGreaterThan(areas[index - 1]);
    }
  });

  it('keeps everything the earlier level had, in the same place or deeper', () => {
    // A stand never moves or shrinks as the ground grows: its front edge stays where it was.
    for (const level of LEVELS.slice(1)) {
      const before = buildScene(level - 1);
      const after = buildScene(level);

      expect(after.rects.length).toBeGreaterThanOrEqual(before.rects.length);
      expect(after.masts.length).toBeGreaterThanOrEqual(before.masts.length);
      expect(after.bounds.width).toBeGreaterThanOrEqual(before.bounds.width);
      expect(after.bounds.height).toBeGreaterThanOrEqual(before.bounds.height);
    }
  });

  it('never draws outside the picture', () => {
    for (const level of LEVELS) {
      const scene = buildScene(level);

      for (const rect of scene.rects) {
        expect(rect.x).toBeGreaterThanOrEqual(0);
        expect(rect.y).toBeGreaterThanOrEqual(0);
        expect(rect.x + rect.width).toBeLessThanOrEqual(SCENE_WIDTH);
        expect(rect.y + rect.height).toBeLessThanOrEqual(SCENE_HEIGHT);
      }

      for (const mast of scene.masts) {
        expect(mast.x).toBeGreaterThan(0);
        expect(mast.y).toBeGreaterThan(0);
        expect(mast.x).toBeLessThan(SCENE_WIDTH);
        expect(mast.y).toBeLessThan(SCENE_HEIGHT);
      }
    }
  });

  it('never builds over the pitch', () => {
    for (const level of LEVELS) {
      for (const rect of buildScene(level).rects.filter((r) => r.kind !== 'roof')) {
        const overlapsX = rect.x < PITCH.x + PITCH.width && rect.x + rect.width > PITCH.x;
        const overlapsY = rect.y < PITCH.y + PITCH.height && rect.y + rect.height > PITCH.y;

        expect(overlapsX && overlapsY).toBe(false);
      }
    }
  });

  it('starts as a village ground of terraces and ends as a closed two-tier bowl', () => {
    const first = buildScene(1);
    const last = buildScene(10);

    expect(
      first.rects.filter((rect) => rect.kind !== 'vip').every((rect) => rect.kind === 'terrace'),
    ).toBe(true);
    expect(first.masts).toHaveLength(0);
    expect(first.ring).toBe(false);
    expect(first.banner).toBe(false);

    expect(last.rects.filter((rect) => rect.kind === 'roof')).toHaveLength(4);
    expect(last.rects.filter((rect) => rect.kind === 'seats')).toHaveLength(8);
    expect(last.masts).toHaveLength(6);
    expect(last.ring).toBe(true);
    expect(last.banner).toBe(true);
  });

  it('draws seats from the second level on, and a hospitality block from the first', () => {
    expect(buildScene(1).rects.some((rect) => rect.kind === 'seats')).toBe(false);
    expect(buildScene(2).rects.some((rect) => rect.kind === 'seats')).toBe(true);
    expect(buildScene(1).rects.some((rect) => rect.kind === 'vip')).toBe(true);
  });

  it('raises the floodlights at level three', () => {
    expect(buildScene(2).masts).toHaveLength(0);
    expect(buildScene(3).masts).toHaveLength(2);
  });

  it('clamps a level it has no plan for rather than drawing nothing', () => {
    expect(buildScene(0).level).toBe(1);
    expect(buildScene(99).level).toBe(10);
    expect(buildScene(Number.NaN).level).toBe(1);
    expect(clampLevel(3.6)).toBe(4);
  });
});

describe('safeColour', () => {
  it('accepts a plain hex colour and normalises its case', () => {
    expect(safeColour('#8C2F39')).toBe('#8c2f39');
  });

  it('refuses anything else, so nothing but a colour reaches a drawing attribute', () => {
    expect(safeColour('red')).toBe('#1f4e79');
    expect(safeColour('#fff')).toBe('#1f4e79');
    expect(safeColour('url(javascript:alert(1))')).toBe('#1f4e79');
    expect(safeColour('#8c2f39" onload="x')).toBe('#1f4e79');
    expect(safeColour(null)).toBe('#1f4e79');
    expect(safeColour(undefined, '#112233')).toBe('#112233');
  });
});
