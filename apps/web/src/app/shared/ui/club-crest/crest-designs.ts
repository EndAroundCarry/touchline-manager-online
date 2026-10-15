/**
 * What a club's crest is made of: a shape, and the stripes laid on it.
 *
 * The two are chosen independently, so any shape can wear any stripes. The shapes are the silhouettes real
 * clubs' crests are cut to (the shield, the roundel, the pointed and the Spanish-style rounded shield, the
 * oval and so on); the stripes are the divisions of the field they carry (vertical or horizontal stripes,
 * halves, quarters, a sash, a cross, checks). They are drawn in the club's two colours: the first colour is
 * the field and the second is everything laid on it, including the border. Each is drawn from scratch, with
 * no real mark, name, letter or animal in it.
 *
 * Every shape and every stripe pattern lives in one 100 x 120 box, which is what lets any pattern be laid
 * over any shape: the shape is a clip, and what falls outside it is simply not drawn. The codes are the ones
 * the server stores and accepts (`CrestDesigns` in the domain); the lists must stay in step.
 */

/** One piece of a stripe pattern, drawn in the second colour. */
export type CrestPiece =
  | { readonly kind: 'fill'; readonly d: string }
  | { readonly kind: 'stroke'; readonly d: string; readonly width: number };

/** A crest shape: the outline every pattern is clipped to. */
export interface CrestShape {
  /** The code the server stores. */
  readonly code: string;
  /** What the dropdown calls it. */
  readonly label: string;
  readonly path: string;
}

/** A kind of stripes. */
export interface CrestPattern {
  /** The code the server stores. */
  readonly code: string;
  /** What the dropdown calls it. */
  readonly label: string;
  /** What is laid over the field, in the second colour. */
  readonly pieces: readonly CrestPiece[];
}

/** The size of the box every shape and pattern is drawn in. */
export const CREST_WIDTH = 100;
export const CREST_HEIGHT = 120;

/** The width of the border, half of which falls inside the shape where it can be seen. */
export const CREST_BORDER = 8;

/** Every shape, in the order the dropdown shows them. The codes match `CrestDesigns.Shapes` on the server. */
export const CREST_SHAPES: readonly CrestShape[] = [
  {
    code: 'shield',
    label: 'Classic shield',
    path: 'M6 6 H94 V58 C94 90 70 108 50 116 C30 108 6 90 6 58 Z',
  },
  {
    code: 'round',
    label: 'Roundel',
    path: 'M4 60 A46 46 0 1 0 96 60 A46 46 0 1 0 4 60 Z',
  },
  {
    code: 'pointed',
    label: 'Pointed shield',
    path: 'M8 6 H92 V50 Q92 64 50 116 Q8 64 8 50 Z',
  },
  {
    code: 'rounded',
    label: 'Spanish shield',
    path: 'M6 8 H94 V68 A44 44 0 0 1 6 68 Z',
  },
  {
    code: 'oval',
    label: 'Oval',
    path: 'M10 60 A40 54 0 1 0 90 60 A40 54 0 1 0 10 60 Z',
  },
  {
    code: 'hexagon',
    label: 'Hexagon',
    path: 'M50 4 L92 30 V90 L50 116 L8 90 V30 Z',
  },
  {
    code: 'diamond',
    label: 'Diamond',
    path: 'M50 4 L94 60 L50 116 L6 60 Z',
  },
  {
    code: 'octagon',
    label: 'Octagon',
    path: 'M30 4 H70 L96 30 V90 L70 116 H30 L4 90 V30 Z',
  },
  {
    code: 'square',
    label: 'Rounded square',
    path: 'M20 6 H80 Q94 6 94 20 V100 Q94 114 80 114 H20 Q6 114 6 100 V20 Q6 6 20 6 Z',
  },
  {
    code: 'arch',
    label: 'Arch',
    path: 'M6 114 V50 A44 44 0 0 1 94 50 V114 Z',
  },
  {
    code: 'pentagon',
    label: 'Pentagon',
    path: 'M6 6 H94 V70 L50 116 L6 70 Z',
  },
  {
    code: 'teardrop',
    label: 'Teardrop shield',
    path: 'M50 4 C75 4 94 16 94 40 C94 72 70 100 50 116 C30 100 6 72 6 40 C6 16 25 4 50 4 Z',
  },
  {
    code: 'flat-base',
    label: 'Flat-base shield',
    path: 'M6 6 H94 V88 Q94 100 50 114 Q6 100 6 88 Z',
  },
  {
    code: 'pennant',
    label: 'Swallowtail pennant',
    path: 'M6 6 H94 V116 L50 92 L6 116 Z',
  },
  {
    code: 'gothic',
    label: 'Gothic shield',
    path: 'M6 114 V46 Q6 26 50 4 Q94 26 94 46 V114 Z',
  },
  {
    code: 'waisted',
    label: 'Waisted shield',
    path: 'M6 6 H94 Q80 36 84 64 Q86 92 50 116 Q14 92 16 64 Q20 36 6 6 Z',
  },
  {
    code: 'chamfered',
    label: 'Chamfered shield',
    path: 'M22 6 H78 L94 22 V62 C94 92 70 108 50 116 C30 108 6 92 6 62 V22 Z',
  },
  {
    code: 'triangle',
    label: 'Triangle',
    path: 'M12 8 H88 Q98 8 92 18 L56 108 Q50 118 44 108 L8 18 Q2 8 12 8 Z',
  },
  {
    code: 'scooped',
    label: 'Scooped shield',
    path: 'M6 6 Q50 30 94 6 V58 C94 90 70 108 50 116 C30 108 6 90 6 58 Z',
  },
  {
    code: 'wide-oval',
    label: 'Wide oval',
    path: 'M4 60 A46 34 0 1 0 96 60 A46 34 0 1 0 4 60 Z',
  },
];

/** A rectangle as a path, so every piece is one kind of thing. */
function rect(x: number, y: number, width: number, height: number): string {
  return `M${x} ${y} h${width} v${height} h${-width} Z`;
}

/**
 * A diagonal band running from the upper left to the lower right, `offset` units from the centre and
 * `half` units either side of its own middle line, measured across the page.
 */
function bend(offset: number, half: number): string {
  const slope = 1.2;
  const x = (y: number, edge: number): number =>
    Math.round((50 + offset + (y - 60) / slope + edge) * 10) / 10;

  return `M${x(-10, -half)} -10 L${x(-10, half)} -10 L${x(130, half)} 130 L${x(130, -half)} 130 Z`;
}

/** A five-by-five checkerboard whose top-left square is the second colour, as on a chequered shield. */
function checks(): string {
  const columns = 5;
  const rows = 5;
  const cells: string[] = [];

  for (let row = 0; row < rows; row++) {
    for (let column = 0; column < columns; column++) {
      if ((row + column) % 2 === 0) {
        cells.push(
          rect(
            (column * CREST_WIDTH) / columns,
            (row * CREST_HEIGHT) / rows,
            CREST_WIDTH / columns,
            CREST_HEIGHT / rows,
          ),
        );
      }
    }
  }

  return cells.join(' ');
}

/** Every kind of stripes, in the order the dropdown shows them. The codes match `CrestDesigns.Patterns`. */
export const CREST_PATTERNS: readonly CrestPattern[] = [
  {
    code: 'stripes',
    label: 'Vertical stripes',
    pieces: [
      { kind: 'fill', d: rect(20, -5, 20, 130) },
      { kind: 'fill', d: rect(60, -5, 20, 130) },
    ],
  },
  {
    code: 'hoops',
    label: 'Horizontal stripes',
    pieces: [
      { kind: 'fill', d: rect(-5, 24, 110, 24) },
      { kind: 'fill', d: rect(-5, 72, 110, 24) },
    ],
  },
  {
    code: 'halves',
    label: 'Left and right halves',
    pieces: [{ kind: 'fill', d: rect(50, -5, 55, 130) }],
  },
  {
    code: 'fess',
    label: 'Top and bottom halves',
    pieces: [{ kind: 'fill', d: rect(-5, 60, 110, 65) }],
  },
  {
    code: 'quarters',
    label: 'Quarters',
    pieces: [
      { kind: 'fill', d: rect(50, -5, 55, 65) },
      { kind: 'fill', d: rect(-5, 60, 55, 65) },
    ],
  },
  {
    code: 'sash',
    label: 'Diagonal sash',
    pieces: [{ kind: 'fill', d: bend(0, 18) }],
  },
  {
    code: 'diagonals',
    label: 'Diagonal stripes',
    pieces: [
      { kind: 'fill', d: bend(-48, 12) },
      { kind: 'fill', d: bend(0, 12) },
      { kind: 'fill', d: bend(48, 12) },
    ],
  },
  {
    code: 'diagonal',
    label: 'Diagonal split',
    pieces: [{ kind: 'fill', d: 'M-10 -10 L110 -10 L110 130 Z' }],
  },
  {
    code: 'cross',
    label: 'Cross',
    pieces: [
      { kind: 'fill', d: rect(40, -5, 20, 130) },
      { kind: 'fill', d: rect(-5, 36, 110, 20) },
    ],
  },
  {
    code: 'saltire',
    label: 'Saltire (X)',
    pieces: [
      { kind: 'stroke', d: 'M0 0 L100 120', width: 18 },
      { kind: 'stroke', d: 'M100 0 L0 120', width: 18 },
    ],
  },
  {
    code: 'chevron',
    label: 'Chevron',
    pieces: [{ kind: 'fill', d: 'M-10 74 L50 34 L110 74 L110 102 L50 62 L-10 102 Z' }],
  },
  {
    code: 'pale',
    label: 'Central stripe',
    pieces: [{ kind: 'fill', d: rect(32, -5, 36, 130) }],
  },
  {
    code: 'band',
    label: 'Horizontal band',
    pieces: [{ kind: 'fill', d: rect(-5, 42, 110, 30) }],
  },
  {
    code: 'checks',
    label: 'Chequered',
    pieces: [{ kind: 'fill', d: checks() }],
  },
  {
    code: 'star',
    label: 'Star',
    pieces: [
      {
        kind: 'fill',
        d: 'M50 28 L56.6 46.9 L76.6 47.4 L60.7 59.5 L66.5 78.7 L50 67.2 L33.5 78.7 L39.3 59.5 L23.4 47.4 L43.4 46.9 Z',
      },
    ],
  },
  {
    code: 'plain',
    label: 'Plain (no stripes)',
    pieces: [],
  },
];

/** The shape shown when a code is not one this client knows, such as one added by a newer server. */
export const DEFAULT_CREST_SHAPE: CrestShape = CREST_SHAPES[0];

/** The stripes shown when a code is not one this client knows. */
export const DEFAULT_CREST_PATTERN: CrestPattern = CREST_PATTERNS[0];

/** Finds a shape by its code, or the default when the code is unknown. */
export function crestShapeFor(code: string | null | undefined): CrestShape {
  return CREST_SHAPES.find((shape) => shape.code === code) ?? DEFAULT_CREST_SHAPE;
}

/** Finds a kind of stripes by its code, or the default when the code is unknown. */
export function crestPatternFor(code: string | null | undefined): CrestPattern {
  return CREST_PATTERNS.find((pattern) => pattern.code === code) ?? DEFAULT_CREST_PATTERN;
}
