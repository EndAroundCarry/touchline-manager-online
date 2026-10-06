/**
 * The geometry of the stadium pictures, one per level (`STAD-1`).
 *
 * Pure and framework-free so the ten levels can be unit tested without a DOM: the picture is a top-down
 * drawing of the pitch with its stands around it, and a level decides how many sides are built, how deep each
 * stand is, which are roofed, and how many floodlight masts stand. A bigger level is always the previous
 * level plus something — never a different ground — so a manager watches the same stadium grow.
 *
 * Coordinates live in a 520 x 340 box with the pitch in the middle. The deepest stand (level 10) is 77 units
 * deep, which leaves a margin on every side.
 */

/** The drawing's width. */
export const SCENE_WIDTH = 520;

/** The drawing's height. */
export const SCENE_HEIGHT = 340;

/** The pitch, inside its markings. */
export const PITCH = { x: 180, y: 122, width: 160, height: 96 } as const;

/** The clear apron between the pitch and the front row of every stand. */
const APRON = 10;

/** How tall one row of seats is. */
const ROW = 6;

/** The walkway between a stand's lower and upper tier, which is also where the hospitality boxes sit. */
const GAP = 5;

/** What a rectangle of the drawing is. */
export type SceneKind = 'terrace' | 'seats' | 'vip' | 'roof';

/** One rectangle of the drawing. */
export interface SceneRect {
  readonly kind: SceneKind;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** A floodlight mast. */
export interface SceneMast {
  readonly x: number;
  readonly y: number;
}

/** The drawing of one level. */
export interface Scene {
  readonly level: number;
  readonly rects: readonly SceneRect[];
  readonly masts: readonly SceneMast[];
  /** Whether the ground is ringed by an outer concourse wall (the big grounds). */
  readonly ring: boolean;
  /** Whether the club's banner flies over the main stand (the biggest ground). */
  readonly banner: boolean;
  /** The extent of everything built, for the outer wall and the banner. */
  readonly bounds: {
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
  };
}

type Side = 'south' | 'north' | 'east' | 'west';

interface Stand {
  /** Rows in the lower tier. Zero means the side is not built yet. */
  readonly lower: number;
  /** Rows in the upper tier. */
  readonly upper: number;
  /** Whether a roof covers the stand. */
  readonly roof: boolean;
  /** Whether the lower tier is a standing terrace rather than seats. */
  readonly terrace: boolean;
}

interface LevelPlan {
  readonly south: Stand;
  readonly north: Stand;
  readonly east: Stand;
  readonly west: Stand;
  readonly masts: number;
  readonly ring: boolean;
  readonly banner: boolean;
}

const NONE: Stand = { lower: 0, upper: 0, roof: false, terrace: false };

function stand(lower: number, upper: number, roof: boolean, terrace: boolean): Stand {
  return { lower, upper, roof, terrace };
}

/**
 * The ten levels. Each row of the table builds on the one above it: stands deepen, terraces become seats, roofs
 * appear, an upper tier rises, and the last levels close the bowl.
 */
const PLANS: readonly LevelPlan[] = [
  // 1 — a village ground: a terrace along each touchline.
  {
    south: stand(3, 0, false, true),
    north: stand(2, 0, false, true),
    east: NONE,
    west: NONE,
    masts: 0,
    ring: false,
    banner: false,
  },
  // 2 — the main stand gets seats, a terrace goes up behind one goal.
  {
    south: stand(4, 0, false, false),
    north: stand(3, 0, false, true),
    east: stand(2, 0, false, true),
    west: NONE,
    masts: 0,
    ring: false,
    banner: false,
  },
  // 3 — the main stand is roofed, both ends are terraced, and the floodlights go up.
  {
    south: stand(4, 0, true, false),
    north: stand(3, 0, false, true),
    east: stand(2, 0, false, true),
    west: stand(2, 0, false, true),
    masts: 2,
    ring: false,
    banner: false,
  },
  // 4 — the far side is seated and roofed.
  {
    south: stand(5, 0, true, false),
    north: stand(4, 0, true, false),
    east: stand(3, 0, false, true),
    west: stand(3, 0, false, true),
    masts: 4,
    ring: false,
    banner: false,
  },
  // 5 — every stand deepens.
  {
    south: stand(6, 0, true, false),
    north: stand(5, 0, true, false),
    east: stand(4, 0, false, true),
    west: stand(4, 0, false, true),
    masts: 4,
    ring: false,
    banner: false,
  },
  // 6 — the main stand rises a second tier, one end is roofed.
  {
    south: stand(6, 3, true, false),
    north: stand(5, 0, true, false),
    east: stand(4, 0, true, true),
    west: stand(4, 0, false, true),
    masts: 4,
    ring: false,
    banner: false,
  },
  // 7 — both long stands have two tiers, the ends are seated and roofed.
  {
    south: stand(6, 4, true, false),
    north: stand(6, 3, true, false),
    east: stand(5, 0, true, false),
    west: stand(5, 0, true, false),
    masts: 4,
    ring: false,
    banner: false,
  },
  // 8 — the ends rise a second tier, a wall rings the ground.
  {
    south: stand(7, 4, true, false),
    north: stand(6, 4, true, false),
    east: stand(5, 3, true, false),
    west: stand(5, 3, true, false),
    masts: 6,
    ring: true,
    banner: false,
  },
  // 9 — nearly a full bowl.
  {
    south: stand(7, 5, true, false),
    north: stand(7, 4, true, false),
    east: stand(6, 4, true, false),
    west: stand(6, 4, true, false),
    masts: 6,
    ring: true,
    banner: false,
  },
  // 10 — the complete two-tier bowl, and the club's banner.
  {
    south: stand(7, 5, true, false),
    north: stand(7, 5, true, false),
    east: stand(7, 5, true, false),
    west: stand(7, 5, true, false),
    masts: 6,
    ring: true,
    banner: true,
  },
];

/** How many levels there are to draw. */
export const SCENE_LEVELS = PLANS.length;

/** The depth of a stand, front row to back wall. */
function depthOf(side: Stand): number {
  if (side.lower === 0) {
    return 0;
  }

  return side.lower * ROW + (side.upper > 0 ? GAP + side.upper * ROW : 0);
}

/** Clamps a level into the range the drawing has a plan for. */
export function clampLevel(level: number): number {
  if (!Number.isFinite(level)) {
    return 1;
  }

  return Math.min(Math.max(Math.round(level), 1), PLANS.length);
}

/**
 * Builds the drawing of one level.
 *
 * @param level The stadium level, 1 to 10. Anything else is clamped, so a response the screen does not yet
 *   know about still draws something rather than nothing.
 */
export function buildScene(level: number): Scene {
  const clamped = clampLevel(level);
  const plan = PLANS[clamped - 1];

  const left = PITCH.x - APRON;
  const right = PITCH.x + PITCH.width + APRON;
  const top = PITCH.y - APRON;
  const bottom = PITCH.y + PITCH.height + APRON;

  const depth: Record<Side, number> = {
    south: depthOf(plan.south),
    north: depthOf(plan.north),
    east: depthOf(plan.east),
    west: depthOf(plan.west),
  };

  // The long stands run on past the pitch to close the corners wherever an end stand stands beside them.
  const spanLeft = left - depth.west;
  const spanRight = right + depth.east;

  const rects: SceneRect[] = [];

  addLongStand(rects, plan.south, 'south', spanLeft, spanRight, bottom);
  addLongStand(rects, plan.north, 'north', spanLeft, spanRight, top);
  addEndStand(rects, plan.east, 'east', top, bottom, right);
  addEndStand(rects, plan.west, 'west', top, bottom, left);

  const bounds = {
    x: left - depth.west,
    y: top - depth.north,
    width: right - left + depth.west + depth.east,
    height: bottom - top + depth.north + depth.south,
  };

  return {
    level: clamped,
    rects,
    masts: mastsFor(plan.masts, bounds),
    ring: plan.ring,
    banner: plan.banner,
    bounds,
  };
}

function addLongStand(
  rects: SceneRect[],
  stand: Stand,
  side: 'south' | 'north',
  spanLeft: number,
  spanRight: number,
  edge: number,
): void {
  if (stand.lower === 0) {
    return;
  }

  const width = spanRight - spanLeft;
  const lowerDepth = stand.lower * ROW;
  const upperDepth = stand.upper * ROW;
  const total = depthOf(stand);

  // South stands grow downwards from the front edge, north stands upwards.
  const at = (offset: number, height: number): number =>
    side === 'south' ? edge + offset : edge - offset - height;

  rects.push({
    kind: stand.terrace ? 'terrace' : 'seats',
    x: spanLeft,
    y: at(0, lowerDepth),
    width,
    height: lowerDepth,
  });

  if (stand.upper > 0) {
    // The walkway between the tiers is where the hospitality boxes are.
    rects.push({ kind: 'vip', x: spanLeft, y: at(lowerDepth, GAP), width, height: GAP });
    rects.push({
      kind: 'seats',
      x: spanLeft,
      y: at(lowerDepth + GAP, upperDepth),
      width,
      height: upperDepth,
    });
  } else if (side === 'south') {
    // A small block of boxes behind the main stand, so even a young ground has somewhere to host.
    const centre = spanLeft + width / 2;

    rects.push({ kind: 'vip', x: centre - 34, y: at(lowerDepth, GAP), width: 68, height: GAP });
  }

  if (stand.roof) {
    rects.push({
      kind: 'roof',
      x: spanLeft - 2,
      y: at(-2, total + 6),
      width: width + 4,
      height: total + 6,
    });
  }
}

function addEndStand(
  rects: SceneRect[],
  stand: Stand,
  side: 'east' | 'west',
  top: number,
  bottom: number,
  edge: number,
): void {
  if (stand.lower === 0) {
    return;
  }

  const height = bottom - top;
  const lowerDepth = stand.lower * ROW;
  const upperDepth = stand.upper * ROW;
  const total = depthOf(stand);

  // East stands grow to the right of the front edge, west stands to the left.
  const at = (offset: number, width: number): number =>
    side === 'east' ? edge + offset : edge - offset - width;

  rects.push({
    kind: stand.terrace ? 'terrace' : 'seats',
    x: at(0, lowerDepth),
    y: top,
    width: lowerDepth,
    height,
  });

  if (stand.upper > 0) {
    rects.push({ kind: 'vip', x: at(lowerDepth, GAP), y: top, width: GAP, height });
    rects.push({
      kind: 'seats',
      x: at(lowerDepth + GAP, upperDepth),
      y: top,
      width: upperDepth,
      height,
    });
  }

  if (stand.roof) {
    rects.push({
      kind: 'roof',
      x: at(-2, total + 6),
      y: top - 2,
      width: total + 6,
      height: height + 4,
    });
  }
}

/** Places the floodlight masts at the corners, then along the touchlines for the biggest grounds. */
function mastsFor(count: number, bounds: Scene['bounds']): SceneMast[] {
  if (count === 0) {
    return [];
  }

  const inset = 6;
  const left = bounds.x - inset;
  const right = bounds.x + bounds.width + inset;
  const top = bounds.y - inset;
  const bottom = bounds.y + bounds.height + inset;
  const middle = bounds.x + bounds.width / 2;

  const corners: SceneMast[] = [
    { x: left, y: top },
    { x: right, y: bottom },
    { x: right, y: top },
    { x: left, y: bottom },
  ];

  const all = [...corners, { x: middle, y: top }, { x: middle, y: bottom }];

  // Two masts light a village ground from opposite corners; four light it evenly; six ring the biggest.
  return all.slice(0, count);
}

/** The colour used when the server sends none, or one that is not a plain hex colour. */
export const FALLBACK_COLOUR = '#1f4e79';

/** Accepts only a plain `#rrggbb` colour, so nothing else can reach a drawing attribute. */
export function safeColour(value: string | null | undefined, fallback = FALLBACK_COLOUR): string {
  return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value)
    ? value.toLowerCase()
    : fallback;
}
