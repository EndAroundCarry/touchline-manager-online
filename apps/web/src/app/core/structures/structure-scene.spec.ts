import { ScenePart } from '../stadium/stadium-canvas';
import {
  STRUCTURE_LEVELS,
  STRUCTURE_RATIO,
  buildStructureScene,
  clampStructureLevel,
} from './structure-scene';
import { StructureKind } from './structures.models';

/**
 * The drawing of a structure.
 *
 * What it must get right: every level of both buildings draws, in the picture's proportions; each level differs from
 * the one before and draws at least as much of the building; a shape names only the club's two colours or a plain
 * hex colour, and both club colours are used; and a level out of range is clamped rather than drawing nothing.
 */

const KINDS: readonly StructureKind[] = ['snack_bar', 'merchandise'];
const LEVELS = Array.from({ length: STRUCTURE_LEVELS }, (_, index) => index + 1);

function count(parts: readonly ScenePart[], role: ScenePart['role']): number {
  return parts.filter((part) => part.role === role).length;
}

describe('Structure scene', () => {
  for (const kind of KINDS) {
    describe(kind, () => {
      it('draws every level in the picture proportions', () => {
        for (const level of LEVELS) {
          const scene = buildStructureScene(kind, level);

          expect(scene.level).toBe(level);
          expect(scene.parts.length).toBeGreaterThan(0);
          expect(scene.view.width / scene.view.height).toBeCloseTo(STRUCTURE_RATIO, 1);
        }
      });

      it('changes the picture at every level, and never draws less of the building', () => {
        for (const level of LEVELS.slice(1)) {
          const before = buildStructureScene(kind, level - 1).parts;
          const after = buildStructureScene(kind, level).parts;

          expect(after.map((part) => part.d).join()).not.toBe(before.map((part) => part.d).join());
          expect(after.length).toBeGreaterThanOrEqual(before.length);
          expect(count(after, 'person')).toBeGreaterThanOrEqual(count(before, 'person'));
        }
      });

      it('paints only the club colours or plain hex colours, and uses both club colours', () => {
        for (const level of LEVELS) {
          const parts = buildStructureScene(kind, level).parts;
          const paints = parts
            .flatMap((part) => [part.fill, part.stroke])
            .filter((paint) => paint !== null);

          for (const paint of paints) {
            expect(paint).toMatch(/^(primary|secondary|#[0-9a-f]{6})$/);
          }

          expect(paints).toContain('primary');
          expect(paints).toContain('secondary');
        }
      });

      it('writes only drawing commands into a path', () => {
        for (const part of buildStructureScene(kind, STRUCTURE_LEVELS).parts) {
          expect(part.d).toMatch(/^[MLHVZQAhvaz0-9 .\-]+$/);
          expect(part.d).not.toContain('NaN');
        }
      });
    });
  }

  it('grows the snack bar from a kiosk to a bar with tables, a terrace and an upper floor', () => {
    const kiosk = buildStructureScene('snack_bar', 1).parts;
    const hall = buildStructureScene('snack_bar', STRUCTURE_LEVELS).parts;

    expect(count(kiosk, 'parasol')).toBe(0);
    expect(count(hall, 'parasol')).toBeGreaterThan(0);
    expect(count(hall, 'window')).toBeGreaterThan(count(kiosk, 'window'));
  });

  it('grows the shop from a stall to a building with windows', () => {
    expect(count(buildStructureScene('merchandise', 1).parts, 'window')).toBe(0);
    expect(count(buildStructureScene('merchandise', 3).parts, 'window')).toBeGreaterThan(0);
  });

  it('clamps a level it does not know', () => {
    expect(clampStructureLevel(0)).toBe(1);
    expect(clampStructureLevel(14)).toBe(STRUCTURE_LEVELS);
    expect(clampStructureLevel(Number.NaN)).toBe(1);
    expect(buildStructureScene('snack_bar', 99).level).toBe(STRUCTURE_LEVELS);
  });
});
