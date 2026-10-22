/**
 * The drawing of a club structure at one level (`STRC-1`).
 *
 * The snack bar and the merchandise shop are drawn the way the stadium is (ADR-0065): seen from the same corner, as an
 * ordered list of shapes that name the club's two colours rather than choose them, so the awnings, parasols, signs,
 * flags and shirts take the club's own colours. Each of the ten levels is the one before with something added — a
 * kiosk grows into a food hall with a covered terrace, a market stall into a megastore — so a manager watches the
 * building grow. Pure and framework-free, so every level can be tested as plain data.
 */
import { Vec3, nearness, outline, project, round, trace } from '../stadium/stadium-camera';
import { Canvas, PALETTE, ScenePart } from '../stadium/stadium-canvas';
import { StructureKind } from './structures.models';

export type { ScenePart } from '../stadium/stadium-canvas';

/** How many levels a structure has (`StructureRuleSet.MaxLevel`). */
export const STRUCTURE_LEVELS = 10;

/** The part of the drawing that is shown, in the drawing's own units. */
export interface StructureView {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** The drawing of one structure at one level. */
export interface StructureScene {
  readonly kind: StructureKind;
  readonly level: number;
  /** The shapes, far to near. */
  readonly parts: readonly ScenePart[];
  /** The part of the drawing that holds the site, in the picture's proportions. */
  readonly view: StructureView;
}

/** The picture's proportions, width over height. */
export const STRUCTURE_RATIO = 16 / 9;

/**
 * World units per metre. The stadium's world is coarse, about two thirds of a metre a unit; a building needs finer
 * steps than the camera's rounding allows, so a structure's metres are drawn larger.
 */
const UNIT = 20;

/** The narrowest frame, in drawing units, so a kiosk is shown on its plot rather than blown up to fill the picture. */
const MIN_WIDTH = 380;

/** The room kept round a site in its frame, in drawing units. */
const PADDING = 22;

/** Where a shadow falls, per metre of height: away from the sun, as in the stadium. */
const SUN: readonly [number, number] = [0.55, 0.35];

/** The colours that do not belong to the club. */
const COLOURS = {
  paving: '#cfc8b9',
  joint: '#b6ae9d',
  render: '#ece5d8',
  brick: '#b4613d',
  mortar: '#8e4527',
  cabin: '#d9dfe4',
  rib: '#97a2ad',
  cladding: '#3d4650',
  roof: '#69727c',
  wood: '#a8744a',
  hatch: '#3a2e26',
  glass: '#8fc0de',
  glassDark: '#3f6680',
  shine: '#e3f1fa',
  frame: '#2c3640',
  steel: '#5d6772',
  board: '#2a2f35',
  chalk: '#eef1f3',
  cloth: '#f4f1ea',
  trousers: '#2e3540',
  mannequin: '#e9ecef',
  planter: '#6c757d',
  bun: '#e0a24a',
  lettuce: '#6fb043',
  patty: '#6a3a22',
  white: '#ffffff',
  black: '#000000',
} as const;

const SKIN = ['#f1c9a8', '#d8a179', '#a9724e', '#6f4a33'] as const;

/** What the people wear: mostly the club's colours, since they are its fans. */
const SHIRTS = [
  'primary',
  'primary',
  'secondary',
  'primary',
  '#e9ecef',
  'secondary',
  'primary',
  '#c8553d',
  'primary',
  '#3f6fb0',
] as const;

/** A football shirt, in a unit square with `v` up: body, sleeves and a V at the neck. */
const SHIRT: readonly (readonly [number, number])[] = [
  [0.32, 1],
  [0.42, 0.92],
  [0.58, 0.92],
  [0.68, 1],
  [1, 0.8],
  [0.9, 0.58],
  [0.8, 0.63],
  [0.8, 0],
  [0.2, 0],
  [0.2, 0.63],
  [0.1, 0.58],
  [0, 0.8],
];

/** The snack bar at one level. Every level keeps what the one before had, and adds to it. */
interface SnackPlan {
  readonly width: number;
  readonly depth: number;
  readonly height: number;
  readonly hatches: number;
  /** A built snack bar with rendered walls, rather than a kiosk painted in the club's colour. */
  readonly built: boolean;
  readonly sign: boolean;
  readonly tables: number;
  /** A drinks kiosk beside the bar. */
  readonly kiosk: boolean;
  /** A covered terrace over the first row of tables. */
  readonly terrace: boolean;
  readonly lights: boolean;
  /** A glazed upper floor with a balcony. */
  readonly upper: boolean;
  readonly roofSign: boolean;
  readonly flags: number;
  /** How many people stand about, queueing or passing; the seated ones come with the tables. */
  readonly crowd: number;
}

const SNACK_PLANS: readonly SnackPlan[] = [
  snack({ width: 3, depth: 2.2, height: 2.6, hatches: 1, crowd: 2 }),
  snack({ width: 3, depth: 2.2, height: 2.6, hatches: 1, tables: 2, crowd: 3 }),
  snack({ width: 5, depth: 2.4, height: 2.7, hatches: 2, sign: true, tables: 3, crowd: 4 }),
  snack({
    width: 5,
    depth: 2.4,
    height: 2.7,
    hatches: 2,
    sign: true,
    tables: 4,
    kiosk: true,
    crowd: 5,
  }),
  snack({
    width: 8,
    depth: 4.5,
    height: 3.2,
    hatches: 3,
    built: true,
    sign: true,
    tables: 5,
    kiosk: true,
    crowd: 6,
  }),
  snack({
    width: 8,
    depth: 4.5,
    height: 3.2,
    hatches: 3,
    built: true,
    sign: true,
    tables: 6,
    kiosk: true,
    terrace: true,
    crowd: 7,
  }),
  snack({
    width: 11,
    depth: 4.5,
    height: 3.2,
    hatches: 4,
    built: true,
    sign: true,
    tables: 8,
    kiosk: true,
    terrace: true,
    lights: true,
    crowd: 9,
  }),
  snack({
    width: 11,
    depth: 5,
    height: 3.2,
    hatches: 4,
    built: true,
    sign: true,
    tables: 9,
    kiosk: true,
    terrace: true,
    lights: true,
    upper: true,
    crowd: 10,
  }),
  snack({
    width: 11,
    depth: 5,
    height: 3.2,
    hatches: 4,
    built: true,
    sign: true,
    tables: 10,
    kiosk: true,
    terrace: true,
    lights: true,
    upper: true,
    roofSign: true,
    crowd: 11,
  }),
  snack({
    width: 14,
    depth: 5.5,
    height: 3.4,
    hatches: 5,
    built: true,
    sign: true,
    tables: 12,
    kiosk: true,
    terrace: true,
    lights: true,
    upper: true,
    roofSign: true,
    flags: 2,
    crowd: 14,
  }),
];

/** The shop's own building, from the third level on. */
interface ShopBuilding {
  readonly width: number;
  readonly depth: number;
  readonly height: number;
  readonly look: 'cabin' | 'brick' | 'glass';
  readonly windows: number;
  readonly sign: boolean;
  readonly awning: boolean;
  readonly upper: boolean;
  readonly banners: boolean;
  /** A giant shirt standing on the roof. */
  readonly roofShirt: boolean;
  readonly planters: boolean;
}

/** The merchandise shop at one level. Every level keeps what the one before had, and adds to it. */
interface ShopPlan {
  /** A market stall of scarves and folded shirts. */
  readonly stall: boolean;
  /** A rail of shirts beside the stall. */
  readonly rail: boolean;
  readonly shop: ShopBuilding | null;
  readonly flags: number;
  readonly crowd: number;
}

const SHOP_PLANS: readonly ShopPlan[] = [
  { stall: true, rail: false, shop: null, flags: 0, crowd: 2 },
  { stall: true, rail: true, shop: null, flags: 1, crowd: 3 },
  {
    stall: true,
    rail: true,
    shop: shop({ width: 5, depth: 3, height: 2.7, look: 'cabin', windows: 1 }),
    flags: 1,
    crowd: 4,
  },
  {
    stall: true,
    rail: true,
    shop: shop({ width: 7, depth: 3.5, height: 3, look: 'brick', windows: 2, sign: true }),
    flags: 1,
    crowd: 5,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 7,
      depth: 3.5,
      height: 3,
      look: 'brick',
      windows: 2,
      sign: true,
      awning: true,
    }),
    flags: 2,
    crowd: 6,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 10,
      depth: 4,
      height: 3.2,
      look: 'brick',
      windows: 3,
      sign: true,
      awning: true,
      roofShirt: true,
    }),
    flags: 2,
    crowd: 7,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 10,
      depth: 4.5,
      height: 3.2,
      look: 'brick',
      windows: 3,
      sign: true,
      awning: true,
      upper: true,
      roofShirt: true,
    }),
    flags: 2,
    crowd: 8,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 13,
      depth: 5,
      height: 3.4,
      look: 'glass',
      windows: 0,
      sign: true,
      upper: true,
      roofShirt: true,
    }),
    flags: 3,
    crowd: 9,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 13,
      depth: 5,
      height: 3.4,
      look: 'glass',
      windows: 0,
      sign: true,
      upper: true,
      banners: true,
      roofShirt: true,
    }),
    flags: 4,
    crowd: 11,
  },
  {
    stall: true,
    rail: true,
    shop: shop({
      width: 16,
      depth: 5.5,
      height: 3.6,
      look: 'glass',
      windows: 0,
      sign: true,
      upper: true,
      banners: true,
      roofShirt: true,
      planters: true,
    }),
    flags: 6,
    crowd: 14,
  },
];

function snack(
  plan: Partial<SnackPlan> & Pick<SnackPlan, 'width' | 'depth' | 'height' | 'hatches' | 'crowd'>,
): SnackPlan {
  return {
    built: false,
    sign: false,
    tables: 0,
    kiosk: false,
    terrace: false,
    lights: false,
    upper: false,
    roofSign: false,
    flags: 0,
    ...plan,
  };
}

function shop(
  building: Partial<ShopBuilding> &
    Pick<ShopBuilding, 'width' | 'depth' | 'height' | 'look' | 'windows'>,
): ShopBuilding {
  return {
    sign: false,
    awning: false,
    upper: false,
    banners: false,
    roofShirt: false,
    planters: false,
    ...building,
  };
}

/** Keeps a level in range, so a response the screen does not yet know about still draws something. */
export function clampStructureLevel(level: number): number {
  return Number.isFinite(level) ? Math.min(Math.max(Math.round(level), 1), STRUCTURE_LEVELS) : 1;
}

// ---------------------------------------------------------------------------------------------------------------
// Geometry. Every length below is in metres; the world `x` runs along the front of a building, `y` toward the
// camera's side, `z` up. A building's front faces `+y` and its west side faces `-x`: the two walls the camera sees.

type Point = readonly [number, number, number];

/** A piece of ground, in metres. */
interface Area {
  readonly minX: number;
  readonly maxX: number;
  readonly minY: number;
  readonly maxY: number;
}

function area(minX: number, maxX: number, minY: number, maxY: number): Area {
  return { minX, maxX, minY, maxY };
}

function around(x: number, y: number, half: number): Area {
  return area(x - half, x + half, y - half, y + half);
}

function grow(box: Area, by: number): Area {
  return area(box.minX - by, box.maxX + by, box.minY - by, box.maxY + by);
}

function world(points: readonly Point[]): Vec3[] {
  return points.map(([x, y, z]) => [x * UNIT, y * UNIT, z * UNIT] as const);
}

/** A point of the world, in metres, on the drawing. */
function at(x: number, y: number, z: number): [number, number] {
  return project(x * UNIT, y * UNIT, z * UNIT);
}

/** A closed outline through points in metres. */
function shape(points: readonly Point[]): string {
  return outline(world(points));
}

/** An open line through points in metres. */
function line(points: readonly Point[]): string {
  return trace(world(points));
}

/** A rectangle upright on a front, at `y`. */
function onFront(y: number, x0: number, x1: number, z0: number, z1: number): string {
  return shape([
    [x0, y, z0],
    [x1, y, z0],
    [x1, y, z1],
    [x0, y, z1],
  ]);
}

/** A rectangle upright on a west side, at `x`. */
function onSide(x: number, y0: number, y1: number, z0: number, z1: number): string {
  return shape([
    [x, y1, z0],
    [x, y0, z0],
    [x, y0, z1],
    [x, y1, z1],
  ]);
}

/** Points round a level circle, anticlockwise, so every shadow winds the same way and they merge. */
function ring(x: number, y: number, z: number, radius: number, steps = 12): Point[] {
  return Array.from({ length: steps }, (_, step) => {
    const angle = (step / steps) * Math.PI * 2;

    return [x + radius * Math.cos(angle), y + radius * Math.sin(angle), z] as const;
  });
}

/** A circle on the drawing itself, for the round things that face the camera: heads, bulbs, leaves. */
function circle(x: number, y: number, radius: number): string {
  const r = round(radius);

  return `M${round(x - radius)} ${round(y)}a${r} ${r} 0 1 0 ${round(2 * radius)} 0a${r} ${r} 0 1 0 ${round(-2 * radius)} 0Z`;
}

/** How tall one metre stands on the drawing at a point. */
function metreAt(x: number, y: number, z: number): number {
  return at(x, y, z)[1] - at(x, y, z + 1)[1];
}

/** The convex hull of ground points, anticlockwise. */
function hull(points: readonly (readonly [number, number])[]): [number, number][] {
  const sorted = [...points].sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const cross = (o: readonly number[], a: readonly number[], b: readonly number[]): number =>
    (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
  const half = (list: readonly (readonly [number, number])[]): [number, number][] => {
    const result: [number, number][] = [];

    for (const point of list) {
      while (
        result.length >= 2 &&
        cross(result[result.length - 2], result[result.length - 1], point) <= 0
      ) {
        result.pop();
      }

      result.push([point[0], point[1]]);
    }

    return result.slice(0, -1);
  };

  return [...half(sorted), ...half([...sorted].reverse())];
}

/** The shadow a block standing from `from` to `height` over a piece of ground throws on the ground. */
function shadowOf(box: Area, height: number, from = 0): string {
  const corners: [number, number][] = [
    [box.minX, box.minY],
    [box.maxX, box.minY],
    [box.maxX, box.maxY],
    [box.minX, box.maxY],
  ];
  const cast = (z: number) => corners.map(([x, y]) => [x + SUN[0] * z, y + SUN[1] * z] as const);

  return shape(hull([...cast(from), ...cast(height)]).map(([x, y]) => [x, y, 0] as const));
}

/** The round shadow of something slim: a person, a parasol, a tree. */
function roundShadow(x: number, y: number, height: number, radius: number): string {
  return shape(ring(x + SUN[0] * height, y + SUN[1] * height, 0, radius, 10));
}

/** A fixed, even scatter in `[0, 1)`, so a drawing is the same every time it is built. */
function scatter(seed: number): number {
  const value = Math.sin(seed * 12.9898 + 78.233) * 43758.5453;

  return value - Math.floor(value);
}

// ---------------------------------------------------------------------------------------------------------------
// Things on the site. An item is drawn whole, and the items are painted far to near by where they stand.

interface Item {
  readonly area: Area;
  /** How high it stands, in metres, so the frame holds it. */
  readonly top: number;
  /** 0 for what stands on the ground; 1 for what is held over it, like a terrace roof over its tables. */
  readonly layer: number;
  readonly shadow: string | null;
  readonly draw: (canvas: Canvas) => void;
}

/**
 * A block: the west side and the front, which the camera sees, and the top. One paint for the walls, the side shaded
 * and the top lit, so it reads as solid in any club colour; `top` paints the top apart, as a roof is.
 */
function block(
  canvas: Canvas,
  box: Area,
  z0: number,
  z1: number,
  paint: string,
  role: ScenePart['role'],
  top?: string,
): void {
  const { minX, maxX, minY, maxY } = box;
  const side = onSide(minX, minY, maxY, z0, z1);
  const front = onFront(maxY, minX, maxX, z0, z1);
  const lid = shape([
    [minX, minY, z1],
    [maxX, minY, z1],
    [maxX, maxY, z1],
    [minX, maxY, z1],
  ]);

  if (top === undefined) {
    canvas.add({ role, d: side + front + lid, fill: paint });
    canvas.add({ role: 'tone', d: lid, fill: COLOURS.white, opacity: 0.2 });
  } else {
    canvas.add({ role, d: side + front, fill: paint });
    canvas.add({ role, d: lid, fill: top });
  }

  canvas.add({ role: 'tone', d: side, fill: COLOURS.black, opacity: 0.2 });
}

/** A person standing, or sitting when short, seen from the corner: legs, a shirt, a head. */
function person(
  canvas: Canvas,
  x: number,
  y: number,
  shirt: string,
  skin: string,
  tall = 1.75,
): void {
  const [fx, fy] = at(x, y, 0);
  const h = fy - at(x, y, tall)[1];
  const w = h * 0.32;
  const shoulder = fy - h * 0.78;
  const waist = fy - h * 0.44;

  canvas.add({
    role: 'person',
    d: `M${round(fx - w * 0.38)} ${fy}H${round(fx + w * 0.38)}V${round(fy - h * 0.47)}H${round(fx - w * 0.38)}Z`,
    fill: COLOURS.trousers,
  });
  canvas.add({
    role: 'person',
    d:
      `M${round(fx - w / 2)} ${round(waist)}V${round(shoulder + h * 0.07)}` +
      `Q${round(fx - w / 2)} ${round(shoulder)} ${round(fx - w * 0.22)} ${round(shoulder)}` +
      `H${round(fx + w * 0.22)}Q${round(fx + w / 2)} ${round(shoulder)} ${round(fx + w / 2)} ${round(shoulder + h * 0.07)}` +
      `V${round(waist)}Z`,
    fill: shirt,
  });
  canvas.add({ role: 'person', d: circle(fx, fy - h * 0.88, h * 0.1), fill: skin });
}

/** A fan, dressed by the seed. */
function fan(canvas: Canvas, x: number, y: number, seed: number, tall = 1.75): void {
  person(
    canvas,
    x,
    y,
    SHIRTS[seed % SHIRTS.length],
    SKIN[Math.floor(scatter(seed) * SKIN.length)],
    tall - scatter(seed + 7) * 0.15,
  );
}

function standing(x: number, y: number, seed: number): Item {
  return {
    area: around(x, y, 0.25),
    top: 1.8,
    layer: 0,
    shadow: roundShadow(x, y, 0.9, 0.28),
    draw: (canvas) => fan(canvas, x, y, seed),
  };
}

function tree(x: number, y: number, seed: number): Item {
  const size = 1.5 + scatter(seed) * 0.8;
  const trunk = 2.2 + scatter(seed + 3) * 0.6;

  return {
    area: around(x, y, 0.4),
    top: trunk + size * 2,
    layer: 0,
    shadow: roundShadow(x, y, trunk * 0.9, size * 0.85),
    draw: (canvas) => {
      block(canvas, around(x, y, 0.14), 0, trunk + size * 0.5, PALETTE.trunk, 'trunk');

      const [cx, cy] = at(x, y, trunk + size * 0.7);
      const r = size * metreAt(x, y, trunk) * 1.15;

      canvas.add({
        role: 'canopy',
        d:
          circle(cx, cy - r * 0.2, r) +
          circle(cx - r * 0.55, cy + r * 0.25, r * 0.7) +
          circle(cx + r * 0.6, cy + r * 0.2, r * 0.68),
        fill: PALETTE.canopy,
      });
      canvas.add({
        role: 'canopy',
        d:
          circle(cx - r * 0.25, cy - r * 0.4, r * 0.58) +
          circle(cx + r * 0.3, cy - r * 0.05, r * 0.4),
        fill: PALETTE.canopyLight,
      });
    },
  };
}

/** A parasol in the club's colours, its panels alternating. */
function parasol(canvas: Canvas, x: number, y: number, high = 2.3, radius = 1.15): void {
  canvas.add({
    role: 'furniture',
    d: line([
      [x, y, 0.75],
      [x, y, high + 0.35],
    ]),
    stroke: COLOURS.steel,
    width: 1.2,
  });

  const apex: Point = [x, y, high + 0.4];
  const rim = ring(x, y, high, radius, 8);
  const panels: [string[], string[]] = [[], []];

  rim.forEach((point, index) =>
    panels[index % 2].push(shape([apex, point, rim[(index + 1) % rim.length]])),
  );

  canvas.add({ role: 'parasol', d: panels[0].join(''), fill: 'primary' });
  canvas.add({ role: 'parasol', d: panels[1].join(''), fill: 'secondary' });
}

/** A round table with a stool each side, its diners, and a parasol unless it stands under a roof. */
function table(x: number, y: number, seed: number, covered: boolean): Item {
  const diners = [scatter(seed + 11) < 0.7, scatter(seed + 13) < 0.55];

  return {
    area: area(x - 0.95, x + 0.95, y - 0.6, y + 0.6),
    top: covered ? 1.4 : 2.8,
    layer: 0,
    shadow: covered ? roundShadow(x, y, 0.5, 0.6) : roundShadow(x, y, 2.4, 1.15),
    draw: (canvas) => {
      const stool = (sx: number, sitting: boolean, who: number): void => {
        canvas.add({
          role: 'furniture',
          d: line([
            [sx, y, 0],
            [sx, y, 0.4],
          ]),
          stroke: COLOURS.steel,
          width: 1.2,
        });
        block(canvas, around(sx, y, 0.17), 0.4, 0.47, COLOURS.wood, 'furniture');

        if (sitting) {
          fan(canvas, sx, y, who, 1.3);
        }
      };

      // The far stool first, and the near one after the table, so the table stands between them.
      stool(x + 0.75, diners[0], seed);
      canvas.add({
        role: 'furniture',
        d: line([
          [x, y, 0],
          [x, y, 0.75],
        ]),
        stroke: COLOURS.steel,
        width: 1.4,
      });
      canvas.add({ role: 'furniture', d: shape(ring(x, y, 0.75, 0.45)), fill: COLOURS.white });
      stool(x - 0.75, diners[1], seed + 5);

      if (!covered) {
        parasol(canvas, x, y);
      }
    },
  };
}

/** A striped awning on a front: it leaves the wall at `high` and slopes out and down, with a hanging edge. */
function awning(
  canvas: Canvas,
  y: number,
  x0: number,
  x1: number,
  high: number,
  reach: number,
  drop: number,
): void {
  const count = Math.max(2, Math.round((x1 - x0) / 0.5));
  const step = (x1 - x0) / count;
  const stripes: [string[], string[]] = [[], []];

  for (let index = 0; index < count; index++) {
    const a = x0 + index * step;
    const b = a + step;

    stripes[index % 2].push(
      shape([
        [a, y, high],
        [b, y, high],
        [b, y + reach, high - drop],
        [a, y + reach, high - drop],
      ]),
      onFront(y + reach, a, b, high - drop - 0.22, high - drop),
    );
  }

  canvas.add({ role: 'awning', d: stripes[0].join(''), fill: 'primary' });
  canvas.add({ role: 'awning', d: stripes[1].join(''), fill: 'secondary' });
  canvas.add({
    role: 'tone',
    d: onFront(y + reach, x0, x1, high - drop - 0.22, high - drop),
    fill: COLOURS.black,
    opacity: 0.12,
  });
}

/** Bars of differing widths that read as a name, from a distance. */
function lettering(
  y: number,
  x0: number,
  x1: number,
  z0: number,
  z1: number,
  seed: number,
): string {
  const bars: string[] = [];
  let x = x0;
  let index = 0;

  while (true) {
    const wide = 0.1 + scatter(seed + index) * 0.14;

    if (x + wide > x1) {
      break;
    }

    bars.push(onFront(y, x, x + wide, z0, z1));
    x += wide + (scatter(seed + index + 50) < 0.18 ? 0.26 : 0.06);
    index++;
  }

  return bars.join('');
}

/** A football shirt upright on a front, in one colour with its collar, cuffs and a hoop in the other. */
function shirt(
  canvas: Canvas,
  y: number,
  x0: number,
  z0: number,
  size: number,
  body: string,
  trim: string,
  outlined = false,
): void {
  canvas.add({
    role: 'goods',
    d: shape(SHIRT.map(([u, v]) => [x0 + u * size, y, z0 + v * size] as const)),
    fill: body,
    ...(outlined ? { stroke: COLOURS.white, width: 2 } : {}),
  });
  canvas.add({
    role: 'goods',
    d:
      onFront(y, x0 + 0.2 * size, x0 + 0.8 * size, z0 + 0.4 * size, z0 + 0.52 * size) +
      shape(
        [
          [0.32, 1],
          [0.42, 0.92],
          [0.58, 0.92],
          [0.68, 1],
          [0.61, 1],
          [0.55, 0.97],
          [0.45, 0.97],
          [0.39, 1],
        ].map(([u, v]) => [x0 + u * size, y, z0 + v * size] as const),
      ) +
      shape(
        [
          [1, 0.8],
          [0.9, 0.58],
          [0.86, 0.6],
          [0.96, 0.82],
        ].map(([u, v]) => [x0 + u * size, y, z0 + v * size] as const),
      ) +
      shape(
        [
          [0, 0.8],
          [0.04, 0.82],
          [0.14, 0.6],
          [0.1, 0.58],
        ].map(([u, v]) => [x0 + u * size, y, z0 + v * size] as const),
      ),
    fill: trim,
  });
}

/** A burger on a front, the snack bar's sign. */
function burger(canvas: Canvas, y: number, x: number, z: number, size: number): void {
  const dome = Array.from({ length: 11 }, (_, step) => {
    const angle = (step / 10) * Math.PI;

    return [
      x + size * Math.cos(angle),
      y,
      z + 0.12 * size + 0.75 * size * Math.sin(angle),
    ] as const;
  });

  canvas.add({
    role: 'sign',
    d: shape(dome) + onFront(y, x - 0.92 * size, x + 0.92 * size, z - 0.62 * size, z - 0.3 * size),
    fill: COLOURS.bun,
  });
  canvas.add({
    role: 'sign',
    d: onFront(y, x - 1.04 * size, x + 1.04 * size, z - 0.04 * size, z + 0.12 * size),
    fill: COLOURS.lettuce,
  });
  canvas.add({
    role: 'sign',
    d: onFront(y, x - size, x + size, z - 0.3 * size, z - 0.04 * size),
    fill: COLOURS.patty,
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Buildings.

/** What is set into a building's front, left to right. */
type Opening = 'hatch' | 'window' | 'door' | 'pane';

interface Building {
  readonly area: Area;
  readonly height: number;
  readonly wall: string;
  readonly trim: string;
  readonly texture: 'none' | 'planks' | 'ribs' | 'brick';
  readonly openings: readonly Opening[];
  readonly sign: 'none' | 'plain' | 'shirt';
  readonly awning: boolean;
  readonly menu: boolean;
  readonly upper: boolean;
  readonly banners: boolean;
  readonly roof: 'none' | 'burger' | 'shirt';
  readonly seed: number;
}

/** The lines that give a wall its make: planks, metal ribs or brick courses. */
function texture(canvas: Canvas, spec: Building): void {
  const { minX, maxX, minY, maxY } = spec.area;
  const h = spec.height;
  const lines: string[] = [];

  if (spec.texture === 'brick') {
    for (let z = 0.3; z < h; z += 0.3) {
      lines.push(
        line([
          [minX, minY, z],
          [minX, maxY, z],
          [maxX, maxY, z],
        ]),
      );
    }
  } else if (spec.texture !== 'none') {
    const gap = spec.texture === 'planks' ? 0.25 : 0.3;

    for (let x = minX + gap; x < maxX; x += gap) {
      lines.push(
        line([
          [x, maxY, 0],
          [x, maxY, h],
        ]),
      );
    }

    for (let y = minY + gap; y < maxY; y += gap) {
      lines.push(
        line([
          [minX, y, 0],
          [minX, y, h],
        ]),
      );
    }
  }

  if (lines.length > 0) {
    canvas.add({
      role: 'texture',
      d: lines.join(''),
      stroke:
        spec.texture === 'brick'
          ? COLOURS.mortar
          : spec.texture === 'ribs'
            ? COLOURS.rib
            : COLOURS.black,
      width: 0.6,
      opacity: spec.texture === 'planks' ? 0.14 : 0.55,
    });
  }
}

/** A serving hatch: the dark opening, someone serving, and a counter in front. */
function hatch(canvas: Canvas, y: number, x0: number, x1: number): void {
  canvas.add({ role: 'hatch', d: onFront(y, x0, x1, 1, 2.05), fill: COLOURS.hatch });

  const middle = (x0 + x1) / 2;
  const [cx, base] = at(middle, y, 1);
  const metre = metreAt(middle, y, 1);
  const w = metre * 0.42;

  canvas.add({
    role: 'person',
    d: `M${round(cx - w / 2)} ${base}V${round(base - metre * 0.48)}H${round(cx + w / 2)}V${base}Z`,
    fill: COLOURS.cloth,
  });
  canvas.add({ role: 'person', d: circle(cx, base - metre * 0.66, metre * 0.12), fill: SKIN[1] });
  canvas.add({
    role: 'person',
    d: `M${round(cx - metre * 0.14)} ${round(base - metre * 0.7)}h${round(metre * 0.28)}v${round(-metre * 0.11)}h${round(-metre * 0.28)}Z`,
    fill: 'primary',
  });

  block(canvas, area(x0 - 0.1, x1 + 0.1, y, y + 0.35), 0.95, 1.05, COLOURS.wood, 'furniture');
}

/** A shop window with two shirts on show. */
function shopWindow(canvas: Canvas, y: number, x0: number, x1: number, high: number): void {
  const z0 = 0.8;
  const width = x1 - x0;

  canvas.add({ role: 'window', d: onFront(y, x0, x1, z0, high), fill: COLOURS.glassDark });

  const size = Math.min(width / 2.6, (high - z0) * 0.62);

  shirt(canvas, y, x0 + width * 0.27 - size / 2, z0 + 0.2, size, 'primary', 'secondary');
  shirt(canvas, y, x0 + width * 0.73 - size / 2, z0 + 0.2, size, 'secondary', 'primary');
  glaze(canvas, y, x0, x1, z0, high);
}

/** The light on a pane of glass, and its frame. */
function glaze(canvas: Canvas, y: number, x0: number, x1: number, z0: number, z1: number): void {
  const width = x1 - x0;

  canvas.add({ role: 'window', d: onFront(y, x0, x1, z0, z1), fill: COLOURS.glass, opacity: 0.22 });
  canvas.add({
    role: 'window',
    d: shape([
      [x0 + width * 0.12, y, z1],
      [x0 + width * 0.3, y, z1],
      [x0 + width * 0.08, y, z0],
      [x0, y, z0],
      [x0, y, z0 + (z1 - z0) * 0.3],
    ]),
    fill: COLOURS.shine,
    opacity: 0.22,
  });
  canvas.add({ role: 'window', d: onFront(y, x0, x1, z0, z1), stroke: COLOURS.frame, width: 1.2 });
}

/** A glazed front pane of a big shop, with a figure in a club shirt behind it. */
function pane(
  canvas: Canvas,
  y: number,
  x0: number,
  x1: number,
  high: number,
  index: number,
): void {
  canvas.add({ role: 'window', d: onFront(y, x0, x1, 0.1, high), fill: COLOURS.glassDark });
  person(
    canvas,
    (x0 + x1) / 2,
    y - 0.45,
    index % 2 === 0 ? 'primary' : 'secondary',
    COLOURS.mannequin,
    1.8,
  );
  glaze(canvas, y, x0, x1, 0.1, high);
}

function door(canvas: Canvas, y: number, x0: number, x1: number, high: number): void {
  canvas.add({ role: 'window', d: onFront(y, x0, x1, 0, high), fill: COLOURS.frame });
  canvas.add({
    role: 'window',
    d: onFront(y, x0 + 0.1, x1 - 0.1, 0.1, high - 0.12),
    fill: COLOURS.glassDark,
  });
  glaze(canvas, y, x0 + 0.1, x1 - 0.1, 0.1, high - 0.12);
}

/** A board on a west side with the day's menu chalked on it. */
function menuBoard(canvas: Canvas, x: number, y0: number, y1: number): void {
  canvas.add({ role: 'sign', d: onSide(x, y0, y1, 1, 2), fill: COLOURS.board });

  const lines: string[] = [];

  for (let row = 0; row < 4; row++) {
    const z = 1.75 - row * 0.2;

    lines.push(
      line([
        [x, y1 - 0.12, z],
        [x, y0 + 0.12 + ((row * 0.37) % 0.4), z],
      ]),
    );
  }

  canvas.add({ role: 'sign', d: lines.join(''), stroke: COLOURS.chalk, width: 0.8, opacity: 0.85 });
}

/** A building whole: walls, what is set in its front, sign, awning, roof, and what stands on the roof. */
function building(spec: Building): Item {
  const { area: box, height: h } = spec;
  const roofTop = h + 0.26;
  const upperArea = area(box.minX + 0.5, box.maxX - 0.5, box.minY + 0.3, box.maxY - 1.3);
  const upperTop = roofTop + 2.5 + 0.22;
  const top = (spec.upper ? upperTop : roofTop) + (spec.roof === 'none' ? 0 : 2.1);

  return {
    area: box,
    top,
    layer: 0,
    shadow: shadowOf(box, spec.upper ? upperTop : roofTop),
    draw: (canvas) => {
      const y = box.maxY;

      block(canvas, box, 0, h, spec.wall, 'wall');
      texture(canvas, spec);

      if (spec.menu && box.maxY - box.minY >= 1.6) {
        menuBoard(canvas, box.minX - 0.01, box.minY + 0.35, box.maxY - 0.35);
      }

      const slot = (box.maxX - box.minX) / spec.openings.length;
      const high = spec.sign === 'none' ? h - 0.35 : h - 0.8;

      spec.openings.forEach((opening, index) => {
        const middle = box.minX + slot * (index + 0.5);
        const half = (wanted: number): number => Math.min(wanted, slot - 0.3) / 2;

        if (opening === 'hatch') {
          hatch(canvas, y, middle - half(1.4), middle + half(1.4));
        } else if (opening === 'window') {
          shopWindow(canvas, y, middle - half(1.9), middle + half(1.9), Math.min(2.4, high));
        } else if (opening === 'door') {
          const wide = spec.openings.includes('pane') ? 1.6 : 1.1;

          door(canvas, y, middle - half(wide), middle + half(wide), Math.min(2.3, high));
        } else {
          pane(canvas, y, middle - slot / 2 + 0.06, middle + slot / 2 - 0.06, high, index);
        }
      });

      if (spec.sign !== 'none') {
        canvas.add({
          role: 'sign',
          d: onFront(y, box.minX + 0.15, box.maxX - 0.15, h - 0.65, h - 0.12),
          fill: 'primary',
        });

        const inset = spec.sign === 'shirt' ? 0.9 : 0.4;
        const span = Math.min(box.maxX - box.minX - inset - 0.4, 5.5);
        const from = box.minX + inset + (box.maxX - box.minX - inset - span) / 2;

        canvas.add({
          role: 'sign',
          d: lettering(y, from, from + span, h - 0.5, h - 0.27, spec.seed),
          fill: 'secondary',
        });

        if (spec.sign === 'shirt') {
          shirt(canvas, y, box.minX + 0.3, h - 0.62, 0.48, 'secondary', 'primary');
        }
      }

      if (spec.awning) {
        // A hatch wants a deep awning over the queue; a shop window a shallow one, so the window still shows.
        const deep = spec.openings.includes('hatch');

        awning(
          canvas,
          y,
          box.minX + 0.12,
          box.maxX - 0.12,
          spec.sign === 'none' ? h - 0.12 : h - 0.72,
          deep ? 1 : 0.55,
          deep ? 0.4 : 0.22,
        );
      }

      block(canvas, grow(box, 0.22), h, roofTop, spec.trim, 'roof-trim', COLOURS.roof);

      let roofArea = box;
      let level = roofTop;

      if (spec.upper) {
        block(canvas, upperArea, roofTop, roofTop + 2.5, spec.wall, 'wall');

        const panes = Math.max(2, Math.round((upperArea.maxX - upperArea.minX) / 1.6));
        const step = (upperArea.maxX - upperArea.minX - 0.4) / panes;
        const mullions: string[] = [];

        canvas.add({
          role: 'window',
          d: onFront(
            upperArea.maxY,
            upperArea.minX + 0.2,
            upperArea.maxX - 0.2,
            roofTop + 0.3,
            roofTop + 2.2,
          ),
          fill: COLOURS.glassDark,
        });
        glaze(
          canvas,
          upperArea.maxY,
          upperArea.minX + 0.2,
          upperArea.maxX - 0.2,
          roofTop + 0.3,
          roofTop + 2.2,
        );

        for (let index = 1; index < panes; index++) {
          const x = upperArea.minX + 0.2 + index * step;

          mullions.push(
            line([
              [x, upperArea.maxY, roofTop + 0.3],
              [x, upperArea.maxY, roofTop + 2.2],
            ]),
          );
        }

        canvas.add({ role: 'window', d: mullions.join(''), stroke: COLOURS.frame, width: 1.2 });

        if (spec.banners) {
          const count = Math.max(2, Math.floor((upperArea.maxX - upperArea.minX) / 3));

          for (let index = 0; index < count; index++) {
            const middle =
              upperArea.minX + ((upperArea.maxX - upperArea.minX) * (index + 0.5)) / count;
            const [paint, band] =
              index % 2 === 0 ? ['primary', 'secondary'] : ['secondary', 'primary'];
            const at0 = upperArea.maxY + 0.02;

            canvas.add({
              role: 'flag',
              d: onFront(at0, middle - 0.38, middle + 0.38, roofTop + 0.5, roofTop + 2.35),
              fill: paint,
            });
            canvas.add({
              role: 'flag',
              d: onFront(at0, middle - 0.38, middle + 0.38, roofTop + 1.35, roofTop + 1.55),
              fill: band,
            });
          }
        }

        block(
          canvas,
          grow(upperArea, 0.18),
          roofTop + 2.5,
          upperTop,
          spec.trim,
          'roof-trim',
          COLOURS.roof,
        );

        // The balcony's glass rail, along the edge of the lower roof.
        const rail = grow(box, 0.2);

        canvas.add({
          role: 'window',
          d:
            onSide(rail.minX, rail.minY, rail.maxY, roofTop, roofTop + 1) +
            onFront(rail.maxY, rail.minX, rail.maxX, roofTop, roofTop + 1),
          fill: COLOURS.glass,
          opacity: 0.35,
        });
        canvas.add({
          role: 'window',
          d: line([
            [rail.minX, rail.minY, roofTop + 1],
            [rail.minX, rail.maxY, roofTop + 1],
            [rail.maxX, rail.maxY, roofTop + 1],
          ]),
          stroke: COLOURS.steel,
          width: 1.2,
        });

        roofArea = upperArea;
        level = upperTop;
      }

      const middle = (roofArea.minX + roofArea.maxX) / 2;
      const front = roofArea.maxY - 0.6;

      if (spec.roof === 'burger') {
        const wide = Math.min(6, (roofArea.maxX - roofArea.minX) * 0.6);
        const x0 = middle - wide / 2;
        const x1 = middle + wide / 2;

        canvas.add({
          role: 'sign',
          d:
            line([
              [x0 + 0.3, front, level],
              [x0 + 0.3, front, level + 0.45],
            ]) +
            line([
              [x1 - 0.3, front, level],
              [x1 - 0.3, front, level + 0.45],
            ]),
          stroke: COLOURS.steel,
          width: 1.6,
        });
        canvas.add({
          role: 'sign',
          d: onFront(front, x0, x1, level + 0.45, level + 1.95),
          fill: 'primary',
          stroke: 'secondary',
          width: 2,
        });
        burger(canvas, front, x0 + 0.8, level + 1.2, 0.5);
        canvas.add({
          role: 'sign',
          d: lettering(front, x0 + 1.6, x1 - 0.35, level + 1.02, level + 1.38, spec.seed + 9),
          fill: 'secondary',
        });
      } else if (spec.roof === 'shirt') {
        const size = Math.min(2.4, (roofArea.maxX - roofArea.minX) * 0.3);

        canvas.add({
          role: 'goods',
          d:
            line([
              [middle - size * 0.2, front, level],
              [middle - size * 0.2, front, level + 0.3],
            ]) +
            line([
              [middle + size * 0.2, front, level],
              [middle + size * 0.2, front, level + 0.3],
            ]),
          stroke: COLOURS.steel,
          width: 1.6,
        });
        shirt(canvas, front, middle - size / 2, level + 0.3, size, 'primary', 'secondary', true);
      }
    },
  };
}

/** A covered terrace over the front row of tables: posts, a roof in the club's colours, and its lights. */
function terrace(box: Area, lights: boolean): Item[] {
  const posts = (y: number): [number, number][] => {
    const count = Math.max(1, Math.round((box.maxX - box.minX) / 3));

    return Array.from({ length: count + 1 }, (_, index) => [
      box.minX + 0.1 + ((box.maxX - box.minX - 0.2) * index) / count,
      y,
    ]);
  };
  const post = (canvas: Canvas, [x, y]: [number, number]): void =>
    block(canvas, around(x, y, 0.08), 0, 2.6, 'secondary', 'pillar');

  const back: Item = {
    area: area(box.minX, box.maxX, box.minY, box.minY + 0.2),
    top: 2.6,
    layer: 0,
    shadow: null,
    draw: (canvas) => posts(box.minY + 0.1).forEach((at0) => post(canvas, at0)),
  };

  const roof: Item = {
    area: box,
    top: 2.8,
    layer: 1,
    shadow: shadowOf(box, 2.8, 2.6),
    draw: (canvas) => {
      posts(box.maxY - 0.1).forEach((at0) => post(canvas, at0));
      block(canvas, box, 2.6, 2.78, 'secondary', 'roof-trim', 'primary');

      const slats: string[] = [];

      for (let x = box.minX + 0.5; x < box.maxX; x += 0.5) {
        slats.push(
          line([
            [x, box.minY, 2.78],
            [x, box.maxY, 2.78],
          ]),
        );
      }

      canvas.add({
        role: 'tone',
        d: slats.join(''),
        stroke: COLOURS.black,
        width: 0.6,
        opacity: 0.15,
      });

      if (lights) {
        const wire: Point[] = [];
        const bulbs: string[] = [];
        const span = (box.maxX - box.minX) / Math.max(1, Math.round((box.maxX - box.minX) / 3));
        const y = box.maxY + 0.03;

        for (let x = box.minX + 0.1; x <= box.maxX - 0.1; x += 0.1) {
          const share = ((x - box.minX) % span) / span;

          wire.push([x, y, 2.55 - 0.35 * Math.sin(Math.PI * share)]);
        }

        wire.forEach(([x, wy, z], index) => {
          if (index % 5 === 2) {
            const [bx, by] = at(x, wy, z - 0.06);

            bulbs.push(circle(bx, by, metreAt(x, wy, z) * 0.07));
          }
        });

        canvas.add({ role: 'lamp', d: line(wire), stroke: COLOURS.frame, width: 0.7 });
        canvas.add({ role: 'lamp', d: bulbs.join(''), fill: PALETTE.lamp });
      }
    },
  };

  return [back, roof];
}

/** A flag on a pole, in the club's colours. */
function flag(x: number, y: number, swap: boolean): Item {
  return {
    area: area(x - 0.1, x + 1.5, y - 0.1, y + 0.1),
    top: 5.1,
    layer: 0,
    shadow: shadowOf(around(x, y, 0.05), 5),
    draw: (canvas) => {
      canvas.add({
        role: 'flag',
        d: line([
          [x, y, 0],
          [x, y, 5.1],
        ]),
        stroke: PALETTE.flagPole,
        width: 1.6,
      });
      canvas.add({
        role: 'flag',
        d: onFront(y, x, x + 1.5, 4, 4.9),
        fill: swap ? 'secondary' : 'primary',
      });
      canvas.add({
        role: 'flag',
        d: onFront(y, x, x + 1.5, 4.3, 4.6),
        fill: swap ? 'primary' : 'secondary',
      });
    },
  };
}

/** A planter with a shrub. */
function planter(x: number, y: number): Item {
  return {
    area: area(x - 0.5, x + 0.5, y - 0.35, y + 0.35),
    top: 1.3,
    layer: 0,
    shadow: shadowOf(area(x - 0.5, x + 0.5, y - 0.35, y + 0.35), 1),
    draw: (canvas) => {
      block(
        canvas,
        area(x - 0.5, x + 0.5, y - 0.35, y + 0.35),
        0,
        0.5,
        COLOURS.planter,
        'furniture',
      );

      const [cx, cy] = at(x, y, 0.85);
      const r = metreAt(x, y, 0.5) * 0.42;

      canvas.add({
        role: 'canopy',
        d: circle(cx - r * 0.5, cy, r) + circle(cx + r * 0.5, cy + r * 0.1, r * 0.9),
        fill: PALETTE.canopy,
      });
      canvas.add({
        role: 'canopy',
        d: circle(cx - r * 0.3, cy - r * 0.35, r * 0.55),
        fill: PALETTE.canopyLight,
      });
    },
  };
}

/** A market stall: a table of folded shirts, scarves hung along the front, a striped canopy, and the seller. */
function stall(x0: number, y0: number): Item {
  const box = area(x0, x0 + 3, y0, y0 + 1.2);

  return {
    area: area(x0, x0 + 3, y0 - 0.5, y0 + 1.45),
    top: 2.4,
    layer: 0,
    shadow: shadowOf(box, 2.35),
    draw: (canvas) => {
      const pole = (x: number, y: number, high: number): string =>
        line([
          [x, y, 0],
          [x, y, high],
        ]);

      canvas.add({
        role: 'pillar',
        d: pole(x0 + 0.05, y0 + 0.05, 2.35) + pole(x0 + 2.95, y0 + 0.05, 2.35),
        stroke: COLOURS.steel,
        width: 1.4,
      });
      fan(canvas, x0 + 1.5, y0 - 0.25, 4);
      block(
        canvas,
        area(x0 + 0.1, x0 + 2.9, y0 + 0.15, y0 + 1.05),
        0,
        0.85,
        'secondary',
        'furniture',
        COLOURS.cloth,
      );

      for (let index = 0; index < 4; index++) {
        const x = x0 + 0.3 + index * 0.62;

        block(
          canvas,
          area(x, x + 0.45, y0 + 0.4, y0 + 0.75),
          0.85,
          0.93,
          index % 2 === 0 ? 'primary' : 'secondary',
          'goods',
        );
      }

      canvas.add({
        role: 'pillar',
        d: pole(x0 + 0.05, y0 + 1.15, 2.05) + pole(x0 + 2.95, y0 + 1.15, 2.05),
        stroke: COLOURS.steel,
        width: 1.4,
      });

      // Scarves hang from the front bar, each with two bands of the other colour.
      for (let index = 0; index < 7; index++) {
        const x = x0 + 0.25 + index * 0.38;
        const [paint, band] = index % 2 === 0 ? ['primary', 'secondary'] : ['secondary', 'primary'];
        const y = y0 + 1.2;

        canvas.add({ role: 'goods', d: onFront(y, x, x + 0.2, 1.2, 1.95), fill: paint });
        canvas.add({
          role: 'goods',
          d: onFront(y, x, x + 0.2, 1.4, 1.48) + onFront(y, x, x + 0.2, 1.64, 1.72),
          fill: band,
        });
      }

      awning(canvas, y0, x0, x0 + 3, 2.35, 1.45, 0.35);
    },
  };
}

/** A rail of shirts on hangers. */
function rail(x0: number, y: number): Item {
  return {
    area: area(x0 - 0.1, x0 + 2.5, y - 0.2, y + 0.2),
    top: 1.8,
    layer: 0,
    shadow: shadowOf(area(x0, x0 + 2.4, y - 0.05, y + 0.05), 1.7),
    draw: (canvas) => {
      canvas.add({
        role: 'furniture',
        d: line([
          [x0, y, 0],
          [x0, y, 1.75],
          [x0 + 2.4, y, 1.75],
          [x0 + 2.4, y, 0],
        ]),
        stroke: COLOURS.steel,
        width: 1.4,
      });

      for (let index = 0; index < 3; index++) {
        const x = x0 + 0.25 + index * 0.68;

        shirt(
          canvas,
          y,
          x,
          1.05,
          0.62,
          index % 2 === 0 ? 'primary' : 'secondary',
          index % 2 === 0 ? 'secondary' : 'primary',
        );
      }
    },
  };
}

// ---------------------------------------------------------------------------------------------------------------
// The two sites.

function snackItems(plan: SnackPlan): Item[] {
  const items: Item[] = [];
  const box = area(0, plan.width, -plan.depth, 0);

  items.push(
    building({
      area: box,
      height: plan.height,
      wall: plan.built ? COLOURS.render : 'primary',
      trim: 'secondary',
      texture: plan.built ? 'none' : 'planks',
      openings: Array.from({ length: plan.hatches }, () => 'hatch' as const),
      sign: plan.sign ? 'plain' : 'none',
      awning: true,
      menu: true,
      upper: plan.upper,
      banners: false,
      roof: plan.roofSign ? 'burger' : 'none',
      seed: 3,
    }),
  );

  const hatches = Array.from(
    { length: plan.hatches },
    (_, index) => (plan.width * (index + 0.5)) / plan.hatches,
  );

  if (plan.kiosk) {
    items.push(
      building({
        area: area(-3.6, -1.3, -1.9, 0),
        height: 2.5,
        wall: 'secondary',
        trim: 'primary',
        texture: 'planks',
        openings: ['hatch'],
        sign: 'none',
        awning: true,
        menu: false,
        upper: false,
        banners: false,
        roof: 'none',
        seed: 21,
      }),
    );
    hatches.unshift(-2.45);
  }

  // Tables in rows in front of the bar, the first row under the terrace once there is one.
  const columns = Math.max(2, Math.floor(Math.max(plan.width, 6) / 2.6));
  const firstRow = 3.7;

  for (let index = 0; index < plan.tables; index++) {
    const row = Math.floor(index / columns);
    const column = index % columns;

    items.push(
      table(0.9 + column * 2.6, firstRow + row * 2.6, 40 + index * 3, plan.terrace && row === 0),
    );
  }

  if (plan.terrace) {
    const across = Math.min(columns, plan.tables) * 2.6 - 0.8;

    items.push(...terrace(area(-0.2, across + 0.4, firstRow - 1.05, firstRow + 1.05), plan.lights));
  }

  const rows = Math.ceil(plan.tables / columns);
  const front = plan.tables === 0 ? 3.2 : firstRow + rows * 2.6 - 0.9;

  for (let index = 0; index < plan.flags; index++) {
    items.push(flag(index === 0 ? -1.4 : plan.width + 0.6, 1.9, index % 2 === 1));
  }

  // People queue at the hatches, two deep, and the rest pass along the front of the yard.
  const spots: [number, number][] = [
    ...hatches.flatMap((x): [number, number][] => [
      [x - 0.1, 1.8],
      [x + 0.18, 2.6],
    ]),
    ...Array.from({ length: 12 }, (_, index): [number, number] => [
      -1.8 + index * 1.7 + scatter(index + 90) * 0.6,
      front + 0.4 + scatter(index + 70) * 0.8,
    ]),
  ];

  spots.slice(0, plan.crowd).forEach(([x, y], index) => items.push(standing(x, y, index * 3 + 1)));

  return items;
}

function shopItems(plan: ShopPlan): Item[] {
  const items: Item[] = [];
  const stallAt: [number, number] = [-4.8, 0.6];
  const spots: [number, number][] = [];

  if (plan.stall) {
    items.push(stall(...stallAt));
    spots.push([stallAt[0] + 0.8, stallAt[1] + 2.1], [stallAt[0] + 2.3, stallAt[1] + 2.3]);
  }

  if (plan.rail) {
    items.push(rail(-4.6, 3.9));
    spots.push([-3.1, 4.6]);
  }

  const shopping = plan.shop;

  if (shopping !== null) {
    const box = area(0, shopping.width, -shopping.depth, 0);
    const panes = Math.round(shopping.width / 2.4);
    const openings: Opening[] =
      shopping.look === 'glass'
        ? Array.from({ length: panes }, (_, index) =>
            index === Math.floor(panes / 2) ? 'door' : 'pane',
          )
        : [
            ...Array.from({ length: Math.ceil(shopping.windows / 2) }, () => 'window' as const),
            'door' as const,
            ...Array.from({ length: Math.floor(shopping.windows / 2) }, () => 'window' as const),
          ];

    items.push(
      building({
        area: box,
        height: shopping.height,
        wall:
          shopping.look === 'cabin'
            ? COLOURS.cabin
            : shopping.look === 'brick'
              ? COLOURS.brick
              : COLOURS.cladding,
        trim: shopping.look === 'glass' ? 'primary' : 'secondary',
        texture: shopping.look === 'cabin' ? 'ribs' : shopping.look === 'brick' ? 'brick' : 'none',
        openings,
        sign: shopping.sign ? 'shirt' : 'none',
        awning: shopping.awning,
        menu: false,
        upper: shopping.upper,
        banners: shopping.banners,
        roof: shopping.roofShirt ? 'shirt' : 'none',
        seed: 7,
      }),
    );

    const slot = shopping.width / openings.length;

    openings.forEach((opening, index) => {
      if (opening !== 'door') {
        spots.push([slot * (index + 0.5) + 0.3, 1.5 + (index % 2) * 0.5]);
      }
    });

    if (shopping.planters) {
      items.push(planter(0.6, 1.2), planter(shopping.width - 0.6, 1.2));
    }
  }

  const across = shopping === null ? 0 : shopping.width;

  for (let index = 0; index < plan.flags; index++) {
    items.push(flag(-1.2 + index * 3, 5.4, index % 2 === 1));
  }

  spots.push(
    ...Array.from({ length: 12 }, (_, index): [number, number] => [
      -1 + ((index * 1.9) % Math.max(4, across + 1)) + scatter(index + 30) * 0.6,
      6.4 + (index % 3) * 0.7 + scatter(index + 60) * 0.4,
    ]),
  );

  spots.slice(0, plan.crowd).forEach(([x, y], index) => items.push(standing(x, y, index * 3 + 2)));

  return items;
}

/** The trees round a site: behind it and down its sides, never in front, so they frame it rather than hide it. */
function treesAround(plaza: Area): Item[] {
  const items: Item[] = [];
  let seed = 1;

  for (let x = plaza.minX - 1; x <= plaza.maxX + 2; x += 4.5) {
    items.push(tree(x + scatter(seed) * 1.5, plaza.minY - 2 - scatter(seed + 1), seed++));
  }

  // The west side is the near one, so its trees stop halfway down, clear of the front of the site.
  for (let y = plaza.minY + 2; y <= plaza.maxY - 1; y += 4.5) {
    if (y <= (plaza.minY + plaza.maxY) / 2) {
      items.push(tree(plaza.minX - 2.4 - scatter(seed), y + scatter(seed + 1), seed));
    }

    items.push(tree(plaza.maxX + 2.4 + scatter(seed + 2), y + scatter(seed + 3), seed + 1));
    seed += 2;
  }

  return items;
}

// ---------------------------------------------------------------------------------------------------------------
// Painting order and framing.

function overlaps(a: Area, b: Area): boolean {
  return a.minX < b.maxX && b.minX < a.maxX && a.minY < b.maxY && b.minY < a.maxY;
}

/** Whether one piece of ground lies wholly beyond another from the camera: further along `x`, or further back. */
function beyond(a: Area, b: Area): boolean {
  return a.maxY <= b.minY || a.minX >= b.maxX;
}

function paintsFirst(a: Item, b: Item): boolean {
  if (overlaps(a.area, b.area)) {
    return a.layer < b.layer;
  }

  return beyond(a.area, b.area) && !beyond(b.area, a.area);
}

function nearnessOf(item: Item): number {
  const { minX, maxX, minY, maxY } = item.area;

  return nearness((minX + maxX) / 2, (minY + maxY) / 2) + item.layer * 1000;
}

/** Puts the items far to near: whatever must be painted first, and among those the farthest. */
function painted(items: readonly Item[]): Item[] {
  const left = [...items];
  const ordered: Item[] = [];

  while (left.length > 0) {
    const free = left.filter(
      (item) => !left.some((other) => other !== item && paintsFirst(other, item)),
    );
    const next = (free.length > 0 ? free : left).reduce((best, item) =>
      nearnessOf(item) < nearnessOf(best) ? item : best,
    );

    ordered.push(next);
    left.splice(left.indexOf(next), 1);
  }

  return ordered;
}

function union(areas: readonly Area[]): Area {
  return area(
    Math.min(...areas.map((each) => each.minX)),
    Math.max(...areas.map((each) => each.maxX)),
    Math.min(...areas.map((each) => each.minY)),
    Math.max(...areas.map((each) => each.maxY)),
  );
}

/** Frames a site: its paving and everything standing on it, in the picture's proportions. */
function frame(plaza: Area, items: readonly Item[]): StructureView {
  const corners = (box: Area, z: number): [number, number][] => [
    at(box.minX, box.minY, z),
    at(box.maxX, box.minY, z),
    at(box.maxX, box.maxY, z),
    at(box.minX, box.maxY, z),
  ];
  const points = [...corners(plaza, 0), ...items.flatMap((item) => corners(item.area, item.top))];
  const xs = points.map(([x]) => x);
  const ys = points.map(([, y]) => y);
  const minX = Math.min(...xs) - PADDING;
  const maxX = Math.max(...xs) + PADDING;
  const minY = Math.min(...ys) - PADDING;
  const maxY = Math.max(...ys) + PADDING;
  const width = Math.max(maxX - minX, (maxY - minY) * STRUCTURE_RATIO, MIN_WIDTH);
  const height = width / STRUCTURE_RATIO;

  return {
    x: round((minX + maxX) / 2 - width / 2),
    y: round((minY + maxY) / 2 - height / 2),
    width: round(width),
    height: round(height),
  };
}

const CACHE = new Map<string, StructureScene>();

/**
 * Builds the drawing of a structure at one level.
 *
 * @param kind The snack bar or the merchandise shop.
 * @param level 1 to 10. Anything else is clamped, so a response the screen does not yet know about still draws
 *   something rather than nothing.
 */
export function buildStructureScene(kind: StructureKind, level: number): StructureScene {
  const clamped = clampStructureLevel(level);
  const key = `${kind}:${clamped}`;
  const cached = CACHE.get(key);

  if (cached !== undefined) {
    return cached;
  }

  const site =
    kind === 'snack_bar'
      ? snackItems(SNACK_PLANS[clamped - 1])
      : shopItems(SHOP_PLANS[clamped - 1]);
  const plaza = grow(union(site.map((item) => item.area)), 1.4);
  const view = frame(plaza, site);
  const items = [...site, ...treesAround(plaza)];
  const canvas = new Canvas();

  canvas.add({
    role: 'ground',
    d: `M${view.x} ${view.y}h${view.width}v${view.height}h${-view.width}Z`,
    fill: PALETTE.grass,
  });

  const middle = (plaza.minX + plaza.maxX) / 2;

  canvas.add({
    role: 'path',
    d: shape([
      [middle - 1.3, plaza.maxY, 0],
      [middle + 1.3, plaza.maxY, 0],
      [middle + 1.3, plaza.maxY + 40, 0],
      [middle - 1.3, plaza.maxY + 40, 0],
    ]),
    fill: PALETTE.path,
    opacity: 0.9,
  });
  canvas.add({
    role: 'paving',
    d: shape([
      [plaza.minX, plaza.minY, 0],
      [plaza.maxX, plaza.minY, 0],
      [plaza.maxX, plaza.maxY, 0],
      [plaza.minX, plaza.maxY, 0],
    ]),
    fill: COLOURS.paving,
  });

  const joints: string[] = [];

  for (let x = plaza.minX + 1.5; x < plaza.maxX; x += 1.5) {
    joints.push(
      line([
        [x, plaza.minY, 0],
        [x, plaza.maxY, 0],
      ]),
    );
  }

  for (let y = plaza.minY + 1.5; y < plaza.maxY; y += 1.5) {
    joints.push(
      line([
        [plaza.minX, y, 0],
        [plaza.maxX, y, 0],
      ]),
    );
  }

  canvas.add({
    role: 'paving',
    d: joints.join(''),
    stroke: COLOURS.joint,
    width: 0.6,
    opacity: 0.8,
  });
  canvas.add({
    role: 'shadow',
    d: items.flatMap((item) => (item.shadow === null ? [] : [item.shadow])).join(''),
    fill: PALETTE.shadow,
    opacity: 0.22,
  });

  for (const item of painted(items)) {
    item.draw(canvas);
  }

  const scene: StructureScene = { kind, level: clamped, parts: canvas.parts, view };

  CACHE.set(key, scene);

  return scene;
}
