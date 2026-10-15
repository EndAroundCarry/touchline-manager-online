import {
  CREST_PATTERNS,
  CREST_SHAPES,
  DEFAULT_CREST_PATTERN,
  DEFAULT_CREST_SHAPE,
  crestPatternFor,
  crestShapeFor,
} from './crest-designs';

/**
 * The crest catalogue: shapes, and the stripes that go on them.
 *
 * The codes are a contract with the server, which refuses any it does not know: a code added here and not
 * there would let a manager pick a crest that then cannot be saved. Both lists are therefore pinned in full,
 * and `CrestDesignsTests` in the domain tests pins the same lists, so a change to one fails until the other
 * follows.
 */
describe('crest designs', () => {
  const SERVER_SHAPES = [
    'shield',
    'round',
    'pointed',
    'rounded',
    'oval',
    'hexagon',
    'diamond',
    'octagon',
    'square',
    'arch',
    'pentagon',
    'teardrop',
    'flat-base',
    'pennant',
    'gothic',
    'waisted',
    'chamfered',
    'triangle',
    'scooped',
    'wide-oval',
  ];

  const SERVER_PATTERNS = [
    'stripes',
    'hoops',
    'halves',
    'fess',
    'quarters',
    'sash',
    'diagonals',
    'diagonal',
    'cross',
    'saltire',
    'chevron',
    'pale',
    'band',
    'checks',
    'star',
    'plain',
  ];

  it('offers the twenty shapes the server accepts, in the same order', () => {
    expect(CREST_SHAPES.map((shape) => shape.code)).toEqual(SERVER_SHAPES);
  });

  it('offers the kinds of stripes the server accepts, in the same order', () => {
    expect(CREST_PATTERNS.map((pattern) => pattern.code)).toEqual(SERVER_PATTERNS);
  });

  it('gives every shape and every kind of stripes a distinct code and label', () => {
    for (const list of [CREST_SHAPES, CREST_PATTERNS]) {
      expect(new Set(list.map((item) => item.code)).size).toBe(list.length);
      expect(new Set(list.map((item) => item.label)).size).toBe(list.length);
    }
  });

  it('keeps every code within the width of the server column', () => {
    for (const item of [...CREST_SHAPES, ...CREST_PATTERNS]) {
      expect(item.code.length).toBeLessThanOrEqual(24);
    }
  });

  it('draws every shape as a closed outline', () => {
    for (const shape of CREST_SHAPES) {
      expect(shape.path, shape.code).toMatch(/^M.* Z$/);
      expect(shape.path, shape.code).not.toContain('NaN');
    }
  });

  it('gives every kind of stripes something to draw, except the plain one', () => {
    for (const pattern of CREST_PATTERNS) {
      if (pattern.code === 'plain') {
        expect(pattern.pieces).toHaveLength(0);
      } else {
        expect(pattern.pieces.length, pattern.code).toBeGreaterThan(0);
      }

      for (const piece of pattern.pieces) {
        expect(piece.d, pattern.code).not.toContain('NaN');
      }
    }
  });

  it('falls back to the first shape and stripes for a code it does not know', () => {
    expect(crestShapeFor('round').code).toBe('round');
    expect(crestShapeFor('from-a-newer-server')).toBe(DEFAULT_CREST_SHAPE);
    expect(crestShapeFor(null)).toBe(DEFAULT_CREST_SHAPE);
    expect(crestPatternFor('hoops').code).toBe('hoops');
    expect(crestPatternFor('from-a-newer-server')).toBe(DEFAULT_CREST_PATTERN);
    expect(crestPatternFor(undefined)).toBe(DEFAULT_CREST_PATTERN);
  });
});
