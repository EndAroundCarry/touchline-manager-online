import { VIEW_HEIGHT, VIEW_WIDTH } from './stadium-camera';
import {
  SCENE_LEVELS,
  Scene,
  ScenePart,
  buildScene,
  clampLevel,
  safeColour,
} from './stadium-scene';
import { kindsOf, planOf, spansOf } from './stadium-plan';

/**
 * The ten stadium pictures.
 *
 * What matters is that every level is a distinct ground, that a level is always the one before it with something
 * added — so a manager watches the same stadium grow — that the seats are drawn in the club's two colours from the
 * first level, that every kind of place can be found in the picture, and that nothing is drawn outside the frame.
 */

const LEVELS = Array.from({ length: SCENE_LEVELS }, (_, index) => index + 1);

/** The roles whose paths are plain lines and corners, so every number in them is an `x y` pair. */
const ABSOLUTE_ROLES: readonly ScenePart['role'][] = [
  'pitch',
  'mowing',
  'marking',
  'board',
  'goal',
  'tread',
  'riser',
  'aisle',
  'side',
  'seat',
  'seat-alt',
  'crowd',
  'barrier',
  'vip-wall',
  'vip-glass',
  'vip-frame',
  'vip-top',
  'walkway',
  'window',
  'roof',
  'roof-trim',
  'roof-rib',
  'pillar',
  'region',
];

function parts(scene: Scene, role: ScenePart['role']): ScenePart[] {
  return scene.parts.filter((part) => part.role === role);
}

/** The corners of a path made of absolute moves and lines. */
function corners(d: string): Array<[number, number]> {
  const numbers = (d.match(/-?\d+(?:\.\d+)?/g) ?? []).map(Number);

  return Array.from({ length: numbers.length / 2 }, (_, index) => [
    numbers[2 * index],
    numbers[2 * index + 1],
  ]);
}

describe('stadium scenes', () => {
  it('has a picture for each of the ten levels', () => {
    expect(SCENE_LEVELS).toBe(10);
  });

  it('draws every level differently', () => {
    const drawings = LEVELS.map((level) => JSON.stringify(buildScene(level).parts));

    expect(new Set(drawings).size).toBe(10);
  });

  it('builds the ground first and the trees nearest the camera last', () => {
    for (const level of LEVELS) {
      const { parts: all } = buildScene(level);

      expect(all[0].role).toBe('ground');
      expect(all[all.length - 1].role).toBe('canopy');
    }
  });

  it('draws the pitch before the stands, so a stand covers what it should', () => {
    for (const level of LEVELS) {
      const { parts: all } = buildScene(level);
      const pitch = all.findIndex((part) => part.role === 'pitch');
      const firstTread = all.findIndex((part) => part.role === 'tread');

      expect(pitch).toBeGreaterThan(-1);
      expect(firstTread).toBeGreaterThan(pitch);
    }
  });

  it('builds more rows at every level than at the one before', () => {
    const rows = LEVELS.map((level) => parts(buildScene(level), 'tread').length);

    for (let index = 1; index < rows.length; index++) {
      expect(rows[index]).toBeGreaterThan(rows[index - 1]);
    }
  });

  it('keeps everything the earlier level had', () => {
    for (const level of LEVELS.slice(1)) {
      const before = buildScene(level - 1);
      const after = buildScene(level);

      for (const role of ['tread', 'seat', 'mast', 'roof', 'vip-glass'] as const) {
        expect(parts(after, role).length, `${role} at level ${level}`).toBeGreaterThanOrEqual(
          parts(before, role).length,
        );
      }
    }
  });

  it('puts seats in the club colour on the first level and in the second colour beside them', () => {
    for (const level of LEVELS) {
      const scene = buildScene(level);
      const seats = parts(scene, 'seat');
      const alternate = parts(scene, 'seat-alt');

      expect(seats.length, `seats at level ${level}`).toBeGreaterThan(0);
      expect(alternate).toHaveLength(seats.length);
      expect(seats.every((part) => part.stroke === 'primary')).toBe(true);
      expect(alternate.every((part) => part.stroke === 'secondary')).toBe(true);
    }
  });

  it('draws a seat as a dash shorter than the room it takes, and the second colour as a pattern over it', () => {
    const scene = buildScene(10);

    for (const part of parts(scene, 'seat')) {
      const [seat, gap] = part.dash!.split(' ').map(Number);

      expect(seat).toBeGreaterThan(0);
      expect(gap).toBeGreaterThan(0);
      expect(part.width).toBeGreaterThan(0);
    }

    for (const part of parts(scene, 'seat-alt')) {
      const lengths = part.dash!.split(' ').map(Number);

      expect(lengths).toHaveLength(4);
      expect(lengths.every((length) => length > 0)).toBe(true);
    }
  });

  it('uses the second colour on the roofs, the boards and the frames of the boxes as well', () => {
    const scene = buildScene(10);

    expect(parts(scene, 'roof-trim').every((part) => part.fill === 'secondary')).toBe(true);
    expect(parts(scene, 'vip-frame').every((part) => part.stroke === 'secondary')).toBe(true);
    expect(parts(scene, 'board').map((part) => part.fill)).toEqual(
      expect.arrayContaining(['primary', 'secondary']),
    );
    expect(parts(scene, 'flag').map((part) => part.fill)).toEqual(
      expect.arrayContaining(['primary', 'secondary']),
    );
  });

  it('names a colour only as primary, secondary or a plain hex colour', () => {
    const allowed = /^(primary|secondary|#[0-9a-f]{6})$/;

    for (const level of LEVELS) {
      for (const part of buildScene(level).parts) {
        for (const paint of [part.fill, part.stroke]) {
          expect(paint === null || allowed.test(paint), `${part.role}: ${paint}`).toBe(true);
        }
      }
    }
  });

  it('draws every shape from finite numbers', () => {
    for (const level of LEVELS) {
      for (const part of buildScene(level).parts) {
        expect(part.d).not.toMatch(/NaN|Infinity|undefined/);
        expect(Number.isFinite(part.width)).toBe(true);
        expect(Number.isFinite(part.offset)).toBe(true);
        expect(part.opacity).toBeGreaterThanOrEqual(0);
        expect(part.opacity).toBeLessThanOrEqual(1);
      }
    }
  });

  it('never draws a stand outside the part of the picture that holds the ground', () => {
    for (const level of LEVELS) {
      const scene = buildScene(level);
      const { x, y, width, height } = scene.view;

      for (const part of scene.parts.filter((p) => ABSOLUTE_ROLES.includes(p.role))) {
        for (const [px, py] of corners(part.d)) {
          expect(px, `${part.role} x at level ${level}`).toBeGreaterThanOrEqual(x - 0.5);
          expect(px, `${part.role} x at level ${level}`).toBeLessThanOrEqual(x + width + 0.5);
          expect(py, `${part.role} y at level ${level}`).toBeGreaterThanOrEqual(y - 0.5);
          expect(py, `${part.role} y at level ${level}`).toBeLessThanOrEqual(y + height + 0.5);
        }
      }
    }
  });

  it('frames a small ground close and the biggest one in the whole picture', () => {
    const widths = LEVELS.map((level) => buildScene(level).view.width);

    for (let index = 1; index < widths.length; index++) {
      expect(widths[index]).toBeGreaterThanOrEqual(widths[index - 1]);
    }

    expect(widths[0]).toBeLessThan(widths[widths.length - 1] * 0.7);
    expect(widths[widths.length - 1]).toBeGreaterThan(VIEW_WIDTH * 0.9);

    for (const level of LEVELS) {
      const { x, y, width, height } = buildScene(level).view;

      expect(width / height).toBeCloseTo(VIEW_WIDTH / VIEW_HEIGHT, 1);
      expect(x).toBeGreaterThanOrEqual(0);
      expect(y).toBeGreaterThanOrEqual(0);
      expect(x + width).toBeLessThanOrEqual(VIEW_WIDTH + 0.1);
      expect(y + height).toBeLessThanOrEqual(VIEW_HEIGHT + 0.1);
    }
  });

  it('starts with a roofed main stand with boxes and a terrace, and ends with the whole bowl', () => {
    const first = buildScene(1);
    const last = buildScene(10);

    expect(parts(first, 'vip-glass')).toHaveLength(1);
    expect(parts(first, 'roof')).toHaveLength(1);
    expect(parts(first, 'crowd').length).toBeGreaterThan(0);
    expect(parts(first, 'mast')).toHaveLength(0);
    expect(parts(first, 'wall')).toHaveLength(0);
    expect(parts(first, 'flag').some((part) => part.fill === 'primary')).toBe(false);

    expect(parts(last, 'roof')).toHaveLength(2);
    expect(parts(last, 'mast').length).toBeGreaterThan(0);
    expect(parts(last, 'wall').length).toBeGreaterThan(0);
    expect(parts(last, 'plaza')).toHaveLength(1);
    expect(parts(last, 'flag').some((part) => part.fill === 'primary' && part.d.length > 0)).toBe(
      true,
    );
  });

  it('raises the floodlights at level three', () => {
    expect(parts(buildScene(2), 'lamp')).toHaveLength(0);
    expect(parts(buildScene(3), 'lamp').length).toBeGreaterThan(0);
  });

  it('lets a manager find every kind of place the club owns, at every level', () => {
    for (const level of LEVELS) {
      const scene = buildScene(level);
      const found = new Set(parts(scene, 'region').map((part) => part.region));

      expect(scene.regions, `level ${level}`).toEqual(kindsOf(level));

      for (const kind of scene.regions) {
        expect(found.has(kind), `${kind} at level ${level}`).toBe(true);
      }
    }
  });

  it('keeps the planes that light a region invisible until the screen lights them', () => {
    for (const level of LEVELS) {
      for (const part of parts(buildScene(level), 'region')) {
        expect(part.opacity).toBe(0);
        expect(part.region).not.toBeNull();
      }
    }
  });

  it('builds a lighter drawing for a thumbnail, with the same frame and the same regions', () => {
    for (const level of LEVELS.slice(4)) {
      const full = buildScene(level, 'full');
      const low = buildScene(level, 'low');

      expect(low.parts.length, `level ${level}`).toBeLessThan(full.parts.length);
      expect(parts(low, 'region')).toHaveLength(0);
      expect(low.view).toEqual(full.view);
      expect(low.regions).toEqual(full.regions);
      expect(parts(low, 'seat').length).toBeGreaterThan(0);
    }
  });

  it('names each stand that is built, inside the picture', () => {
    for (const level of LEVELS) {
      const scene = buildScene(level);
      const { x, y, width, height } = scene.view;

      expect(scene.labels).toHaveLength(spansOf(planOf(level)).length);

      for (const label of scene.labels) {
        expect(label.x).toBeGreaterThan(x);
        expect(label.x).toBeLessThan(x + width);
        expect(label.y).toBeGreaterThan(y);
        expect(label.y).toBeLessThan(y + height);
      }
    }

    expect(buildScene(1).labels.map((label) => label.text)).toEqual([
      'Main stand',
      'Opposite stand',
    ]);
    expect(
      buildScene(10)
        .labels.map((label) => label.text)
        .sort(),
    ).toEqual(['East end', 'Main stand', 'Opposite stand', 'West end']);
  });

  it('builds a level once', () => {
    expect(buildScene(4)).toBe(buildScene(4));
    expect(buildScene(4, 'low')).not.toBe(buildScene(4, 'full'));
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
