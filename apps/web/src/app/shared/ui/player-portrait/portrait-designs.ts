import {
  CREST_BORDER,
  CREST_HEIGHT,
  CREST_WIDTH,
  DEFAULT_CREST_PATTERN,
  DEFAULT_CREST_SHAPE,
  crestPatternFor,
  crestShapeFor,
} from '../club-crest/crest-designs';

/**
 * The drawn player portrait: a head-and-shoulders likeness in the club's shirt, drawn as SVG from the player's id.
 *
 * Who the player is (skin tone, face shape, hair, eyes, beard) comes from a generator seeded by the id alone, so a
 * player keeps the same face for life. How old the player is only bends that face: a teenager's jaw is softer and
 * the beard lighter, and from the late twenties lines deepen, the hairline recedes and grey creeps in. Every random
 * draw happens whatever the age, so a birthday never redraws a different person.
 *
 * The drawing is a tree of plain SVG elements rather than markup, so the component builds it with
 * `createElementNS` and nothing is parsed as HTML.
 */

/** One SVG element: its tag, its attributes, and its children. */
export interface SvgNode {
  readonly tag: string;
  readonly attrs: Readonly<Record<string, string | number>>;
  readonly children?: readonly SvgNode[];
}

/** A skin tone: the lit, mid and shaded values the face is painted with. */
export interface SkinTone {
  readonly base: string;
  readonly shadow: string;
  readonly hi: string;
}

/** The hair styles a player can wear. */
export type HairStyle = 'buzz' | 'crop' | 'textured' | 'curly';

/** What the generator decided about one player's face, before anything is drawn. */
export interface PortraitLook {
  readonly seed: number;
  readonly skinIndex: number;
  readonly skin: SkinTone;
  /** The hair colour as worn, after any grey. */
  readonly hair: string;
  readonly hairStyle: HairStyle;
  readonly eye: string;
  /** How heavy the beard is, from 0 (clean-shaven) to 0.9 (a full beard). */
  readonly beard: number;
  /** How far ageing has marked the face, from 0 (27 or younger) to 1 (37 or older). */
  readonly ageing: number;
  /** How young the face looks, from 0 (21 or older) to 1 (17 or younger). */
  readonly youth: number;
  /** How grey the hair has gone, from 0 to 0.6. */
  readonly greying: number;
  readonly geometry: FaceGeometry;
}

/** The measurements of one face, in the 400 by 480 drawing box. */
export interface FaceGeometry {
  readonly top: number;
  readonly fw: number;
  readonly jw: number;
  readonly chinW: number;
  readonly chinY: number;
  readonly jawY: number;
  readonly eyeDx: number;
  readonly browY: number;
  readonly browT: number;
  readonly browArch: number;
  readonly noseY: number;
  readonly nostril: number;
  readonly mouthY: number;
  readonly mouthW: number;
  readonly lipFull: number;
  readonly neckW: number;
  readonly hairline: number;
  readonly recede: number;
}

/** What the player wears: the club's two colours and its crest. */
export interface PortraitKit {
  readonly primary: string;
  readonly secondary: string;
  readonly crestShape: string;
  readonly crestPattern: string;
}

/** The drawing box. */
export const PORTRAIT_WIDTH = 400;
export const PORTRAIT_HEIGHT = 480;

/** The kit a portrait wears when the club's colours are not known. */
export const FALLBACK_KIT: PortraitKit = {
  primary: '#1f4e79',
  secondary: '#f2f2f2',
  crestShape: DEFAULT_CREST_SHAPE.code,
  crestPattern: DEFAULT_CREST_PATTERN.code,
};

const SKINS: readonly (SkinTone & { readonly weight: number })[] = [
  { base: '#efc6a6', shadow: '#bd8564', hi: '#fbe2cf', weight: 3 },
  { base: '#dfa982', shadow: '#a96f4c', hi: '#f1c8a6', weight: 3 },
  { base: '#c68b60', shadow: '#8a5634', hi: '#dfac82', weight: 2 },
  { base: '#a66a42', shadow: '#6a3b1f', hi: '#c38a60', weight: 1.5 },
  { base: '#7c4a2b', shadow: '#472714', hi: '#9d6945', weight: 1.5 },
  { base: '#59341f', shadow: '#311b0d', hi: '#7a4d31', weight: 1 },
];

const HAIR = {
  black: '#15100d',
  darkBrown: '#2a1c13',
  brown: '#4a3120',
  lightBrown: '#6d4b2e',
  blond: '#9b7b4b',
  ginger: '#8d4924',
} as const;

const EYES = {
  darkBrown: '#3a2416',
  brown: '#5b3a21',
  hazel: '#6e5a2c',
  green: '#4d6a43',
  blue: '#4a6d8f',
  grey: '#6a7884',
} as const;

const STYLES: Readonly<
  Record<
    HairStyle,
    {
      readonly vol: number;
      readonly fade: number;
      readonly disp: number;
      readonly freq: number;
      readonly alpha: number;
    }
  >
> = {
  buzz: { vol: 3, fade: 0.55, disp: 2.5, freq: 0.05, alpha: 0.82 },
  crop: { vol: 11, fade: 0.42, disp: 5, freq: 0.045, alpha: 1 },
  textured: { vol: 22, fade: 0.32, disp: 8, freq: 0.04, alpha: 1 },
  curly: { vol: 26, fade: 0.36, disp: 11, freq: 0.11, alpha: 1 },
};

const BEARDS = { clean: 0, stubble: 0.42, heavy: 0.66, beard: 0.9 } as const;

const GREY_HAIR = '#a3a29e';

// ---- randomness and colour ---------------------------------------------------------------------------------------

/** A 32-bit FNV-1a hash, so a player's id gives the same seed on every device. */
export function portraitSeed(playerId: string): number {
  let hash = 2166136261;

  for (const ch of playerId) {
    hash ^= ch.codePointAt(0) ?? 0;
    hash = Math.imul(hash, 16777619);
  }

  return hash >>> 0;
}

function mulberry32(seed: number): () => number {
  let a = seed;

  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;

    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function rgb(hex: string): readonly [number, number, number] {
  return [
    parseInt(hex.slice(1, 3), 16),
    parseInt(hex.slice(3, 5), 16),
    parseInt(hex.slice(5, 7), 16),
  ];
}

/** Mixes two `#rrggbb` colours: 0 gives the first, 1 the second. */
export function mixColour(a: string, b: string, t: number): string {
  const from = rgb(a);
  const to = rgb(b);

  return (
    '#' +
    from
      .map((value, index) =>
        Math.round(value + (to[index] - value) * t)
          .toString(16)
          .padStart(2, '0'),
      )
      .join('')
  );
}

/** A colour as the 0–1 channel values a filter's colour matrix takes. */
function channels(hex: string): readonly string[] {
  return rgb(hex).map((value) => (value / 255).toFixed(3));
}

/** The colour if it is `#rrggbb`, otherwise the fallback, so a bad value can never reach an attribute. */
export function kitColour(value: string | null | undefined, fallback: string): string {
  return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value)
    ? value.toLowerCase()
    : fallback;
}

const clamp01 = (value: number): number => Math.min(1, Math.max(0, value));

// ---- the look ----------------------------------------------------------------------------------------------------

/** Decides one player's face from their id, bent by their age. */
export function portraitLook(playerId: string, age: number): PortraitLook {
  const seed = portraitSeed(playerId);
  const r = mulberry32(seed);
  const pick = <T>(list: readonly T[]): T => list[Math.floor(r() * list.length)];

  const totalWeight = SKINS.reduce((sum, skin) => sum + skin.weight, 0);
  let roll = r() * totalWeight;
  let skinIndex = SKINS.length - 1;

  for (let index = 0; index < SKINS.length; index++) {
    roll -= SKINS[index].weight;

    if (roll <= 0) {
      skinIndex = index;
      break;
    }
  }

  const darker = skinIndex >= 3;
  const hairName = pick<keyof typeof HAIR>(
    darker
      ? ['black', 'black', 'darkBrown']
      : skinIndex === 2
        ? ['black', 'darkBrown', 'brown']
        : ['darkBrown', 'brown', 'brown', 'lightBrown', 'blond', 'ginger'],
  );
  const eyeName = pick<keyof typeof EYES>(
    darker ? ['darkBrown', 'brown'] : ['darkBrown', 'brown', 'hazel', 'green', 'blue', 'grey'],
  );
  const hairStyle = pick<HairStyle>(
    darker ? ['buzz', 'crop', 'curly', 'curly'] : ['buzz', 'crop', 'textured', 'textured', 'curly'],
  );
  const beardName = pick<keyof typeof BEARDS>(['clean', 'stubble', 'stubble', 'heavy', 'beard']);
  const greysEarly = r();

  const top = 92 + r() * 6;
  const fw = 72 + r() * 9;
  const jw = 56 + r() * 12;
  const chinW = 15 + r() * 10;
  const chinY = 328 + r() * 8;
  const jawY = 296 + r() * 6;
  const eyeDx = 29 + r() * 3;
  const browY = 224 - 18 - r() * 3;
  const browT = 5.5 + r() * 3;
  const browArch = 1 + r() * 3;
  const noseY = 270 + r() * 4;
  const nostril = 7 + r() * 2.5;
  const mouthY = noseY + 25 + r() * 3;
  const mouthW = 22 + r() * 4;
  const lipFull = 1 + r() * 0.5;
  const neckW = 50 + r() * 8;
  const hairline = 140 + r() * 12;
  const recedes = r();

  // Age only bends what the seed chose; it draws nothing of its own.
  const ageing = clamp01((age - 27) / 10);
  const youth = clamp01((21 - age) / 4);
  const greying = clamp01((age - 29 - greysEarly * 6) / 8) * 0.6;
  const recede = ageing * (6 + recedes * 14);
  const beardCap = 1 - 0.7 * youth;

  return {
    seed,
    skinIndex,
    skin: SKINS[skinIndex],
    hair: mixColour(HAIR[hairName], GREY_HAIR, greying),
    hairStyle,
    eye: EYES[eyeName],
    beard: BEARDS[beardName] * beardCap,
    ageing,
    youth,
    greying,
    geometry: {
      top,
      fw: fw + 2 * youth,
      jw: jw - 5 * youth + 3 * ageing,
      chinW,
      chinY: chinY - 4 * youth,
      jawY,
      eyeDx,
      browY,
      browT,
      browArch,
      noseY,
      nostril,
      mouthY,
      mouthW,
      lipFull: lipFull - 0.15 * ageing,
      neckW: neckW - 4 * youth,
      hairline: hairline + recede,
      recede,
    },
  };
}

// ---- the drawing -------------------------------------------------------------------------------------------------

const el = (
  tag: string,
  attrs: Record<string, string | number>,
  children?: readonly SvgNode[],
): SvgNode => (children === undefined ? { tag, attrs } : { tag, attrs, children });

const BLURS: readonly (readonly [string, number])[] = [
  ['b05', 0.5],
  ['b1', 1],
  ['b2', 2],
  ['b3', 3],
  ['b4', 4],
  ['b5', 5],
  ['b6', 6],
  ['b10', 10],
  ['b16', 16],
];

/**
 * Draws the portrait. `key` must be unique on the page, because the drawing's gradients, clips and filters are
 * looked up by id and two portraits sharing ids would paint with one another's.
 */
export function portraitScene(look: PortraitLook, kit: PortraitKit, key: string): SvgNode {
  const id = (name: string): string => `${key}-${name}`;
  const url = (name: string): string => `url(#${id(name)})`;
  const { skin, hair, eye, beard, ageing, seed } = look;
  const g = look.geometry;
  const style = STYLES[look.hairStyle];
  const primary = kitColour(kit.primary, FALLBACK_KIT.primary);
  const secondary = kitColour(kit.secondary, FALLBACK_KIT.secondary);

  const cx = 200;
  const templeY = 165;
  const cheekY = 228;
  const eyeY = 224;
  const { top, fw, jw, chinW, chinY, jawY, mouthY: my, mouthW: mw, lipFull, noseY } = g;
  const H = (dx: number, y: number): string => `${(cx + dx).toFixed(1)},${y.toFixed(1)}`;

  const head = `M ${H(0, top)}
    C ${H(fw * 0.78, top)} ${H(fw + 6, top + 28)} ${H(fw + 6, templeY)}
    C ${H(fw + 6, cheekY - 32)} ${H(fw + 2, cheekY - 12)} ${H(fw, cheekY)}
    C ${H(fw - 2, cheekY + 34)} ${H(jw + 12, jawY - 18)} ${H(jw, jawY)}
    C ${H(jw - 10, jawY + 16)} ${H(chinW + 12, chinY - 3)} ${H(chinW, chinY)}
    C ${H(chinW * 0.45, chinY + 5)} ${H(-chinW * 0.45, chinY + 5)} ${H(-chinW, chinY)}
    C ${H(-chinW - 12, chinY - 3)} ${H(-jw + 10, jawY + 16)} ${H(-jw, jawY)}
    C ${H(-jw - 12, jawY - 18)} ${H(-fw + 2, cheekY + 34)} ${H(-fw, cheekY)}
    C ${H(-fw - 2, cheekY - 12)} ${H(-fw - 6, cheekY - 32)} ${H(-fw - 6, templeY)}
    C ${H(-fw - 6, top + 28)} ${H(-fw * 0.78, top)} ${H(0, top)} Z`;

  // The hairline: down the sideburns, across the temples (which go back first as the hair recedes) and the brow.
  const hl = g.hairline;
  const templeIn = 18 + g.recede * 0.6;
  const templeTop = 168 - g.recede * 0.9;
  const vol = style.vol;
  const hairlinePath = `M ${H(-fw + 10, 236)} C ${H(-fw + 9, 212)} ${H(-fw + 10, 186)} ${H(-fw + templeIn, templeTop)}
    C ${H(-50, hl + 8 - g.recede * 0.4)} ${H(-30, hl - 1)} ${H(0, hl)}
    C ${H(30, hl - 1)} ${H(50, hl + 8 - g.recede * 0.4)} ${H(fw - templeIn, templeTop)}
    C ${H(fw - 10, 186)} ${H(fw - 9, 212)} ${H(fw - 10, 236)}`;
  const hairShape = `${hairlinePath}
    L ${H(fw + 1, 236)}
    C ${H(fw + 4, 220)} ${H(fw + 8, 206)} ${H(fw + 9, 196)}
    C ${H(fw + 13, top + 30)} ${H(fw * 0.74, top - vol * 0.8)} ${H(0, top - vol)}
    C ${H(-fw * 0.74, top - vol * 0.8)} ${H(-fw - 13, top + 30)} ${H(-fw - 9, 196)}
    C ${H(-fw - 8, 206)} ${H(-fw - 4, 220)} ${H(-fw - 1, 236)} Z`;

  const neckW = g.neckW;
  const neck = `M ${H(-neckW + 4, 285)} C ${H(-neckW, 330)} ${H(-neckW - 2, 360)} ${H(-neckW - 14, 412)}
    L ${H(neckW + 14, 412)} C ${H(neckW + 2, 360)} ${H(neckW, 330)} ${H(neckW - 4, 285)} Z`;
  const cW = neckW + 10;
  const jersey = `M -20,480 L -20,452 C 0,428 60,410 ${cx - cW - 40},398
    C ${cx - cW - 18},393 ${cx - cW - 6},388 ${cx - cW},384 Q ${cx},420 ${cx + cW},384
    C ${cx + cW + 6},388 ${cx + cW + 18},393 ${cx + cW + 40},398 C 340,410 400,428 420,452 L 420,480 Z`;

  const lidLine = mixColour(skin.shadow, '#120a06', 0.55);
  const lip = mixColour(skin.base, '#a9505a', 0.38);
  const upperLip = mixColour(lip, skin.shadow, 0.5);
  const nostrilFill = mixColour(skin.shadow, '#1a0e08', 0.5);
  const [hr, hg, hb] = channels(mixColour(hair, '#ffffff', 0.1));
  const [dr, dg, db] = channels(mixColour(hair, '#000000', 0.5));
  const [sr, sg, sb] = channels(hair);
  const backdrop = mixColour('#3a4555', primary, 0.22);

  const blur = (name: string, attrs: Record<string, string | number>, tag = 'ellipse'): SvgNode =>
    el(tag, { ...attrs, filter: url(name) });

  // ---- defs ----
  const defs = el('defs', {}, [
    ...BLURS.map(([name, deviation]) =>
      el('filter', { id: id(name), x: '-50%', y: '-50%', width: '200%', height: '200%' }, [
        el('feGaussianBlur', { stdDeviation: deviation }),
      ]),
    ),
    el('filter', { id: id('brow'), x: '-20%', y: '-60%', width: '140%', height: '220%' }, [
      el('feTurbulence', {
        type: 'fractalNoise',
        baseFrequency: 0.6,
        numOctaves: 2,
        seed: seed % 97,
        result: 'n',
      }),
      el('feDisplacementMap', {
        in: 'SourceGraphic',
        in2: 'n',
        scale: 3.5,
        xChannelSelector: 'R',
        yChannelSelector: 'G',
      }),
      el('feGaussianBlur', { stdDeviation: 0.5 }),
    ]),
    el('filter', { id: id('hair'), x: '-20%', y: '-30%', width: '140%', height: '160%' }, [
      el('feTurbulence', {
        type: 'fractalNoise',
        baseFrequency: style.freq,
        numOctaves: 2,
        seed: seed % 89,
        result: 'warp',
      }),
      el('feDisplacementMap', {
        in: 'SourceGraphic',
        in2: 'warp',
        scale: style.disp,
        xChannelSelector: 'R',
        yChannelSelector: 'G',
        result: 'd',
      }),
      el('feGaussianBlur', { in: 'd', stdDeviation: 0.7, result: 'shape' }),
      el('feTurbulence', {
        type: 'fractalNoise',
        baseFrequency: '0.9 0.55',
        numOctaves: 2,
        seed: seed % 83,
        result: 'grain',
      }),
      el('feColorMatrix', {
        in: 'grain',
        type: 'matrix',
        values: `0 0 0 0 ${hr} 0 0 0 0 ${hg} 0 0 0 0 ${hb} 1.5 0 0 0 -0.95`,
        result: 'hi',
      }),
      el('feComposite', { in: 'hi', in2: 'shape', operator: 'in', result: 'hiIn' }),
      el('feColorMatrix', {
        in: 'grain',
        type: 'matrix',
        values: `0 0 0 0 ${dr} 0 0 0 0 ${dg} 0 0 0 0 ${db} -2.4 0 0 0 1.05`,
        result: 'lo',
      }),
      el('feComposite', { in: 'lo', in2: 'shape', operator: 'in', result: 'loIn' }),
      el('feMerge', {}, [
        el('feMergeNode', { in: 'shape' }),
        el('feMergeNode', { in: 'loIn' }),
        el('feMergeNode', { in: 'hiIn' }),
      ]),
    ]),
    el('filter', { id: id('stub'), x: 0, y: 0, width: '100%', height: '100%' }, [
      el('feTurbulence', {
        type: 'fractalNoise',
        baseFrequency: 1.15,
        numOctaves: 1,
        seed: seed % 79,
      }),
      el('feColorMatrix', {
        type: 'matrix',
        values: `0 0 0 0 ${sr} 0 0 0 0 ${sg} 0 0 0 0 ${sb} 3.6 0 0 0 -1.55`,
      }),
    ]),
    el('filter', { id: id('pores'), x: 0, y: 0, width: '100%', height: '100%' }, [
      el('feTurbulence', {
        type: 'fractalNoise',
        baseFrequency: 0.85,
        numOctaves: 2,
        seed: seed % 73,
      }),
      el('feColorMatrix', {
        type: 'matrix',
        values: '0 0 0 0 0.2 0 0 0 0 0.12 0 0 0 0 0.08 1.8 0 0 0 -0.75',
      }),
    ]),

    el(
      'radialGradient',
      { id: id('bg'), gradientUnits: 'userSpaceOnUse', cx: 185, cy: 190, r: 330 },
      [
        el('stop', { offset: 0, 'stop-color': mixColour(backdrop, '#ffffff', 0.1) }),
        el('stop', { offset: 0.55, 'stop-color': mixColour(backdrop, '#0d1117', 0.45) }),
        el('stop', { offset: 1, 'stop-color': '#0b0e13' }),
      ],
    ),
    el(
      'radialGradient',
      { id: id('vig'), gradientUnits: 'userSpaceOnUse', cx: 200, cy: 230, r: 320 },
      [
        el('stop', { offset: 0.55, 'stop-color': '#000000', 'stop-opacity': 0 }),
        el('stop', { offset: 1, 'stop-color': '#000000', 'stop-opacity': 0.5 }),
      ],
    ),
    el(
      'radialGradient',
      {
        id: id('face'),
        gradientUnits: 'userSpaceOnUse',
        cx: cx - 25,
        cy: 200,
        r: 190,
        fx: cx - 38,
        fy: 182,
      },
      [
        el('stop', { offset: 0, 'stop-color': skin.hi }),
        el('stop', { offset: 0.42, 'stop-color': skin.base }),
        el('stop', { offset: 1, 'stop-color': skin.shadow }),
      ],
    ),
    el(
      'linearGradient',
      { id: id('neck'), gradientUnits: 'userSpaceOnUse', x1: 0, y1: 290, x2: 0, y2: 400 },
      [
        el('stop', { offset: 0, 'stop-color': mixColour(skin.shadow, '#000000', 0.25) }),
        el('stop', { offset: 1, 'stop-color': mixColour(skin.base, skin.shadow, 0.35) }),
      ],
    ),
    el(
      'linearGradient',
      { id: id('shade'), gradientUnits: 'userSpaceOnUse', x1: 0, y1: 0, x2: 400, y2: 0 },
      [
        el('stop', { offset: 0, 'stop-color': '#ffffff', 'stop-opacity': 0.12 }),
        el('stop', { offset: 0.45, 'stop-color': '#ffffff', 'stop-opacity': 0 }),
        el('stop', { offset: 1, 'stop-color': '#000000', 'stop-opacity': 0.5 }),
      ],
    ),
    el(
      'linearGradient',
      {
        id: id('hairFill'),
        gradientUnits: 'userSpaceOnUse',
        x1: cx - 90,
        y1: top - 20,
        x2: cx + 90,
        y2: 240,
      },
      [
        el('stop', { offset: 0, 'stop-color': mixColour(hair, '#ffffff', 0.14) }),
        el('stop', { offset: 1, 'stop-color': mixColour(hair, '#000000', 0.35) }),
      ],
    ),
    el(
      'linearGradient',
      { id: id('fade'), gradientUnits: 'userSpaceOnUse', x1: 0, y1: 172, x2: 0, y2: 236 },
      [
        el('stop', { offset: 0, 'stop-color': '#ffffff' }),
        el('stop', { offset: 1, 'stop-color': '#ffffff', 'stop-opacity': style.fade }),
      ],
    ),
    el(
      'linearGradient',
      { id: id('beardGrad'), gradientUnits: 'userSpaceOnUse', x1: 0, y1: 236, x2: 0, y2: 268 },
      [
        el('stop', { offset: 0, 'stop-color': '#ffffff', 'stop-opacity': 0 }),
        el('stop', { offset: 1, 'stop-color': '#ffffff' }),
      ],
    ),
    el('radialGradient', { id: id('iris') }, [
      el('stop', { offset: 0, 'stop-color': mixColour(eye, '#ffffff', 0.2) }),
      el('stop', { offset: 0.7, 'stop-color': eye }),
      el('stop', { offset: 1, 'stop-color': mixColour(eye, '#000000', 0.6) }),
    ]),
    el('radialGradient', { id: id('white') }, [
      el('stop', { offset: 0.35, 'stop-color': '#ece5dd' }),
      el('stop', { offset: 1, 'stop-color': mixColour('#ece5dd', skin.shadow, 0.55) }),
    ]),
    el(
      'linearGradient',
      {
        id: id('rimSide'),
        gradientUnits: 'userSpaceOnUse',
        x1: cx + 20,
        y1: 0,
        x2: cx + 90,
        y2: 0,
      },
      [
        el('stop', { offset: 0, 'stop-color': '#ffffff', 'stop-opacity': 0 }),
        el('stop', { offset: 1, 'stop-color': '#ffffff' }),
      ],
    ),

    el('clipPath', { id: id('head') }, [el('path', { d: head })]),
    el('clipPath', { id: id('neckClip') }, [el('path', { d: neck })]),
    el('clipPath', { id: id('jerseyClip') }, [el('path', { d: jersey })]),
    el(
      'mask',
      { id: id('fadeMask'), maskUnits: 'userSpaceOnUse', x: 0, y: 0, width: 400, height: 480 },
      [el('rect', { width: 400, height: 480, fill: url('fade') })],
    ),
    el(
      'mask',
      { id: id('rimMask'), maskUnits: 'userSpaceOnUse', x: 0, y: 0, width: 400, height: 480 },
      [el('rect', { width: 400, height: 480, fill: url('rimSide') })],
    ),
    el(
      'mask',
      { id: id('beard'), maskUnits: 'userSpaceOnUse', x: 0, y: 0, width: 400, height: 480 },
      [
        el('rect', { width: 400, height: 480, fill: '#000000' }),
        el('path', { d: head, fill: url('beardGrad') }),
        blur('b1', { cx, cy: my + 1.5, rx: mw + 1, ry: 7 * lipFull, fill: '#000000' }),
        blur('b6', { cx: cx - 42, cy: 254, rx: 30, ry: 20, fill: '#000000' }),
        blur('b6', { cx: cx + 42, cy: 254, rx: 30, ry: 20, fill: '#000000' }),
        blur('b2', { cx, cy: noseY, rx: 20, ry: 11, fill: '#000000' }),
      ],
    ),
  ]);

  // ---- ears ----
  const ear = (side: 1 | -1): SvgNode[] => {
    const ex = cx + side * (fw - 2);
    const X = (dx: number): string => (ex + side * dx).toFixed(1);

    return [
      el('path', {
        d: `M ${X(0)},206 C ${X(12)},196 ${X(22)},206 ${X(21)},226 C ${X(20)},246 ${X(14)},264 ${X(2)},270 Z`,
        fill: mixColour(skin.base, skin.shadow, side > 0 ? 0.45 : 0.15),
      }),
      el('path', {
        d: `M ${X(5)},214 C ${X(12)},210 ${X(16)},220 ${X(14)},236 C ${X(12)},250 ${X(9)},256 ${X(5)},258`,
        fill: 'none',
        stroke: skin.shadow,
        'stroke-width': 2.6,
        opacity: 0.6,
        filter: url('b1'),
      }),
      el('path', {
        d: `M ${X(3)},250 C ${X(8)},252 ${X(11)},258 ${X(6)},266`,
        fill: 'none',
        stroke: skin.hi,
        'stroke-width': 1.5,
        opacity: side > 0 ? 0.15 : 0.35,
        filter: url('b1'),
      }),
    ];
  };

  // ---- eyes ----
  const eyeAt = (side: 1 | -1): SvgNode[] => {
    const ex = cx + side * g.eyeDx;
    const E = (dx: number, dy: number): string =>
      `${(ex + dx).toFixed(1)},${(eyeY + dy).toFixed(1)}`;
    const O = (dx: number, dy: number): string => E(side * dx, dy);
    const white = `M ${E(-14, 0)} C ${E(-8, -7)} ${E(8, -7)} ${E(14, 0)} C ${E(8, 5)} ${E(-8, 5)} ${E(-14, 0)} Z`;
    const clip = id(side > 0 ? 'eyeR' : 'eyeL');
    const nodes: SvgNode[] = [
      blur('b6', { cx: ex, cy: eyeY + 1, rx: 27, ry: 15, fill: skin.shadow, opacity: 0.32 }),
      blur('b3', {
        cx: ex,
        cy: eyeY + 10,
        rx: 15,
        ry: 4,
        fill: skin.shadow,
        opacity: 0.28 + 0.25 * ageing,
      }),
      el('clipPath', { id: clip }, [el('path', { d: white })]),
      el('path', { d: white, fill: url('white') }),
      el('g', { 'clip-path': `url(#${clip})` }, [
        el('circle', { cx: ex + 0.4, cy: eyeY - 0.6, r: 6.3, fill: url('iris') }),
        el('circle', { cx: ex + 0.4, cy: eyeY - 0.6, r: 2.6, fill: '#0b0705' }),
        el('path', {
          d: `M ${E(-15, -1)} C ${E(-8, -8)} ${E(8, -8)} ${E(15, -1)}`,
          fill: 'none',
          stroke: lidLine,
          'stroke-width': 4,
          opacity: 0.45,
          filter: url('b1'),
        }),
      ]),
      el('circle', { cx: ex - 1.8, cy: eyeY - 2.8, r: 1.3, fill: '#ffffff', opacity: 0.9 }),
      el('path', {
        d: `M ${E(-15, 0.6)} C ${E(-8, -8)} ${E(8, -8)} ${E(15, -0.4)}`,
        fill: 'none',
        stroke: lidLine,
        'stroke-width': 1.9,
        'stroke-linecap': 'round',
      }),
      el('path', {
        d: `M ${E(-13, -6)} C ${E(-6, -12.5)} ${E(7, -12.5)} ${E(13, -5)}`,
        fill: 'none',
        stroke: skin.shadow,
        'stroke-width': 1.3,
        opacity: 0.6,
        filter: url('b05'),
      }),
      el('path', {
        d: `M ${E(-12, 2.5)} C ${E(-6, 5.5)} ${E(7, 5.5)} ${E(13, 1)}`,
        fill: 'none',
        stroke: skin.shadow,
        'stroke-width': 1,
        opacity: 0.45,
      }),
    ];

    if (ageing > 0) {
      // Crow's feet at the outer corner.
      nodes.push(
        el('path', {
          d: `M ${O(17, -2)} L ${O(24, -5)} M ${O(17, 1)} L ${O(25, 1)} M ${O(16, 4)} L ${O(23, 7)}`,
          fill: 'none',
          stroke: skin.shadow,
          'stroke-width': 1,
          'stroke-linecap': 'round',
          opacity: 0.55 * ageing,
          filter: url('b05'),
        }),
      );
    }

    return nodes;
  };

  const brow = (side: 1 | -1): SvgNode => {
    const B = (dx: number, dy: number): string =>
      `${(cx + side * dx).toFixed(1)},${(g.browY + dy).toFixed(1)}`;

    return el('path', {
      d: `M ${B(11, 0)} C ${B(22, -4 - g.browArch)} ${B(40, -6 - g.browArch)} ${B(55, 2)}
        C ${B(42, -2 - g.browArch + g.browT * 0.35)} ${B(24, g.browT - 3 - g.browArch * 0.4)} ${B(12, g.browT)} Z`,
      fill: hair,
      opacity: 0.8,
      filter: url('brow'),
    });
  };

  // ---- nose and mouth ----
  const n = g.nostril;
  const nose: SvgNode[] = [
    blur('b5', { cx: cx + 9, cy: noseY - 20, rx: 6, ry: 27, fill: skin.shadow, opacity: 0.42 }),
    blur('b4', { cx: cx - 3, cy: noseY - 26, rx: 5, ry: 26, fill: skin.hi, opacity: 0.5 }),
    blur('b3', { cx, cy: noseY + 9, rx: 19, ry: 5, fill: skin.shadow, opacity: 0.5 }),
    el('path', {
      d: `M ${H(-n - 9, noseY + 2)} C ${H(-n - 12, noseY - 6)} ${H(-n - 5, noseY - 11)} ${H(-n - 1, noseY - 7)}`,
      fill: 'none',
      stroke: skin.shadow,
      'stroke-width': 1.8,
      opacity: 0.5,
      filter: url('b05'),
    }),
    el('path', {
      d: `M ${H(n + 9, noseY + 2)} C ${H(n + 12, noseY - 6)} ${H(n + 5, noseY - 11)} ${H(n + 1, noseY - 7)}`,
      fill: 'none',
      stroke: skin.shadow,
      'stroke-width': 2,
      opacity: 0.65,
      filter: url('b05'),
    }),
    blur('b1', { cx: cx - n, cy: noseY + 4.5, rx: 3.6, ry: 1.6, fill: nostrilFill, opacity: 0.6 }),
    blur('b1', { cx: cx + n, cy: noseY + 4.5, rx: 3.6, ry: 1.6, fill: nostrilFill, opacity: 0.7 }),
    blur('b2', { cx: cx - 2, cy: noseY - 2, rx: 6, ry: 4.5, fill: skin.hi, opacity: 0.5 }),
  ];

  const M = (dx: number, dy: number): string => `${(cx + dx).toFixed(1)},${(my + dy).toFixed(1)}`;
  const mouth: SvgNode[] = [
    blur('b3', { cx, cy: my + 15, rx: 15, ry: 5, fill: skin.shadow, opacity: 0.5 }),
    el('path', {
      d: `M ${M(-mw, 0)} C ${M(-12, -6 * lipFull)} ${M(-4, -5 * lipFull)} ${M(0, -3 * lipFull)}
        C ${M(4, -5 * lipFull)} ${M(12, -6 * lipFull)} ${M(mw, 0)} C ${M(10, 1)} ${M(-10, 1)} ${M(-mw, 0)} Z`,
      fill: upperLip,
    }),
    el('path', {
      d: `M ${M(-mw + 2, 0.5)} C ${M(-10, 10 * lipFull)} ${M(10, 10 * lipFull)} ${M(mw - 2, 0.5)}
        C ${M(10, 2)} ${M(-10, 2)} ${M(-mw + 2, 0.5)} Z`,
      fill: lip,
    }),
    blur('b1', { cx: cx - 3, cy: my + 4.5 * lipFull, rx: 7, ry: 1.8, fill: skin.hi, opacity: 0.4 }),
    el('path', {
      d: `M ${M(-mw, 0)} C ${M(-8, 2)} ${M(8, 2)} ${M(mw, 0)}`,
      fill: 'none',
      stroke: mixColour(upperLip, '#140906', 0.6),
      'stroke-width': 1.4,
      'stroke-linecap': 'round',
    }),
    blur('b1', { cx: cx - mw, cy: my, r: 2, fill: skin.shadow, opacity: 0.5 }, 'circle'),
    blur('b1', { cx: cx + mw, cy: my, r: 2, fill: skin.shadow, opacity: 0.6 }, 'circle'),
    el('path', {
      d: `M ${H(-4, noseY + 9)} L ${H(-3, my - 4)} M ${H(4, noseY + 9)} L ${H(3, my - 4)}`,
      stroke: skin.shadow,
      'stroke-width': 1.6,
      opacity: 0.25,
      filter: url('b1'),
    }),
  ];

  // ---- the lines age draws: brow furrows and the folds from nose to mouth ----
  const lines: SvgNode[] =
    ageing > 0
      ? [
          el('path', {
            d: `M ${H(-n - 11, noseY + 1)} C ${H(-n - 20, noseY + 14)} ${H(-mw - 8, my - 6)} ${H(-mw - 3, my + 8)}
              M ${H(n + 11, noseY + 1)} C ${H(n + 20, noseY + 14)} ${H(mw + 8, my - 6)} ${H(mw + 3, my + 8)}`,
            fill: 'none',
            stroke: skin.shadow,
            'stroke-width': 2.4,
            opacity: 0.12 + 0.4 * ageing,
            filter: url('b2'),
          }),
          el('path', {
            d: [0, 1, 2]
              .map((row) => {
                const y = g.browY - 16 - row * 9;
                const w = 34 - row * 4;

                return `M ${H(-w, y + 2)} C ${H(-w / 2, y - 3)} ${H(w / 2, y - 3)} ${H(w, y + 2)}`;
              })
              .join(' '),
            fill: 'none',
            stroke: skin.shadow,
            'stroke-width': 1.3,
            opacity: 0.45 * ageing,
            filter: url('b1'),
          }),
        ]
      : [];

  // ---- the club crest on the chest ----
  const crestScale = 0.3;
  const crestShape = crestShapeFor(kit.crestShape);
  const crestClip = id('crest');
  const crest = el(
    'g',
    {
      transform: `translate(${cx + 72 - (CREST_WIDTH * crestScale) / 2} ${446 - (CREST_HEIGHT * crestScale) / 2}) scale(${crestScale})`,
    },
    [
      el('clipPath', { id: crestClip }, [el('path', { d: crestShape.path })]),
      // A rim in the second colour, so the crest stands out from a shirt of its own main colour.
      el('path', {
        d: crestShape.path,
        fill: secondary,
        stroke: secondary,
        'stroke-width': 12,
        'stroke-linejoin': 'round',
      }),
      el('g', { 'clip-path': `url(#${crestClip})` }, [
        el('rect', { width: CREST_WIDTH, height: CREST_HEIGHT, fill: primary }),
        ...crestPatternFor(kit.crestPattern).pieces.map((piece) =>
          piece.kind === 'fill'
            ? el('path', { d: piece.d, fill: secondary })
            : el('path', {
                d: piece.d,
                fill: 'none',
                stroke: secondary,
                'stroke-width': piece.width,
              }),
        ),
        el('path', {
          d: crestShape.path,
          fill: 'none',
          stroke: secondary,
          'stroke-width': CREST_BORDER,
          'stroke-linejoin': 'round',
        }),
      ]),
      el('path', {
        d: crestShape.path,
        fill: 'none',
        stroke: 'rgba(0,0,0,0.45)',
        'stroke-width': 3,
        'stroke-linejoin': 'round',
      }),
    ],
  );

  return el(
    'svg',
    {
      xmlns: 'http://www.w3.org/2000/svg',
      viewBox: `0 0 ${PORTRAIT_WIDTH} ${PORTRAIT_HEIGHT}`,
      preserveAspectRatio: 'xMidYMid slice',
    },
    [
      defs,
      el('rect', { width: 400, height: 480, fill: url('bg') }),
      el('path', {
        d: head,
        transform: 'translate(12 8)',
        fill: '#000000',
        opacity: 0.4,
        filter: url('b16'),
      }),
      el('path', {
        d: jersey,
        transform: 'translate(10 6)',
        fill: '#000000',
        opacity: 0.4,
        filter: url('b16'),
      }),

      el('path', { d: neck, fill: url('neck') }),
      el('g', { 'clip-path': url('neckClip') }, [
        blur('b10', {
          cx,
          cy: jawY + 8,
          rx: jw + 14,
          ry: 34,
          fill: mixColour(skin.shadow, '#000000', 0.35),
          opacity: 0.85,
        }),
        blur('b10', { cx: cx + neckW, cy: 350, rx: 18, ry: 60, fill: skin.shadow, opacity: 0.6 }),
        blur('b10', { cx: cx - neckW + 10, cy: 360, rx: 12, ry: 40, fill: skin.hi, opacity: 0.2 }),
        blur('b5', { cx, cy: 398, rx: cW, ry: 10, fill: '#000000', opacity: 0.35 }),
      ]),

      ...ear(1),
      ...ear(-1),

      el('path', { d: head, fill: url('face') }),
      el('g', { 'clip-path': url('head') }, [
        blur('b16', { cx: cx + fw, cy: 240, rx: 40, ry: 115, fill: skin.shadow, opacity: 0.55 }),
        blur('b10', { cx: cx - fw - 8, cy: 250, rx: 22, ry: 90, fill: skin.shadow, opacity: 0.3 }),
        blur('b10', {
          cx: cx + 50,
          cy: 268,
          rx: 22,
          ry: 12,
          fill: skin.shadow,
          opacity: 0.32 + 0.1 * ageing - 0.12 * look.youth,
        }),
        blur('b10', {
          cx: cx - 50,
          cy: 268,
          rx: 22,
          ry: 12,
          fill: skin.shadow,
          opacity: 0.18 + 0.08 * ageing - 0.08 * look.youth,
        }),
        blur('b16', {
          cx: cx - 12,
          cy: g.hairline + 25,
          rx: 46,
          ry: 22,
          fill: skin.hi,
          opacity: 0.5,
        }),
        blur('b10', {
          cx: cx - 44,
          cy: 248,
          rx: 18,
          ry: 13,
          fill: skin.hi,
          opacity: 0.35 + 0.1 * look.youth,
        }),
        blur('b5', {
          cx: cx + 2,
          cy: chinY - 4,
          rx: chinW + 6,
          ry: 10,
          fill: skin.shadow,
          opacity: 0.3,
        }),
        blur('b4', { cx: cx - 4, cy: chinY - 12, rx: 11, ry: 6, fill: skin.hi, opacity: 0.35 }),
        blur('b10', { cx: cx - 40, cy: 262, rx: 16, ry: 10, fill: '#c0505a', opacity: 0.07 }),
        blur('b10', { cx: cx + 40, cy: 262, rx: 16, ry: 10, fill: '#c0505a', opacity: 0.07 }),
        el('rect', { width: 400, height: 480, filter: url('pores'), opacity: 0.1 + 0.05 * ageing }),
        ...lines,
        el('path', {
          d: hairlinePath,
          fill: 'none',
          stroke: skin.shadow,
          'stroke-width': 7,
          opacity: style.vol > 5 ? 0.4 : 0.2,
          filter: url('b4'),
        }),
        ...(beard > 0.8
          ? [
              el('path', {
                d: head,
                fill: hair,
                opacity: 0.5,
                mask: url('beard'),
                filter: url('brow'),
              }),
            ]
          : []),
        ...(beard > 0
          ? [
              el('rect', {
                width: 400,
                height: 480,
                filter: url('stub'),
                opacity: beard,
                mask: url('beard'),
              }),
            ]
          : []),
      ]),

      ...eyeAt(-1),
      ...eyeAt(1),
      brow(-1),
      brow(1),
      ...nose,
      ...mouth,

      el('g', { mask: url('fadeMask'), opacity: style.alpha }, [
        el('path', { d: hairShape, fill: url('hairFill'), filter: url('hair') }),
      ]),

      el('g', { 'clip-path': url('head'), mask: url('rimMask') }, [
        el('path', {
          d: head,
          fill: 'none',
          stroke: '#e4ecff',
          'stroke-width': 6,
          opacity: 0.35,
          filter: url('b2'),
        }),
      ]),

      el('path', { d: jersey, fill: primary }),
      el('g', { 'clip-path': url('jerseyClip') }, [
        el('rect', { width: 400, height: 480, fill: url('shade') }),
        blur('b5', { cx, cy: 402, rx: cW + 26, ry: 12, fill: '#000000', opacity: 0.35 }),
        blur('b10', { cx: cx - 75, cy: 448, rx: 60, ry: 28, fill: '#ffffff', opacity: 0.07 }),
        el('path', {
          d: `M ${cx - 4},420 C ${cx - 2},440 ${cx + 2},460 ${cx},480`,
          stroke: '#000000',
          'stroke-width': 6,
          opacity: 0.15,
          fill: 'none',
          filter: url('b4'),
        }),
        el('path', {
          d: 'M 330,420 C 360,430 385,440 410,460',
          stroke: '#ffffff',
          'stroke-width': 5,
          opacity: 0.18,
          fill: 'none',
          filter: url('b3'),
        }),
      ]),
      el('path', {
        d: `M ${cx - cW},384 Q ${cx},420 ${cx + cW},384`,
        fill: 'none',
        stroke: secondary,
        'stroke-width': 7,
        'stroke-linecap': 'round',
      }),
      el('path', {
        d: `M ${cx - cW},388 Q ${cx},424 ${cx + cW},388`,
        fill: 'none',
        stroke: '#000000',
        'stroke-width': 2,
        opacity: 0.25,
        filter: url('b1'),
      }),
      crest,

      el('rect', { width: 400, height: 480, fill: url('vig') }),
    ],
  );
}
