/**
 * The fifteen stadium levels as a plan (`STAD-1`).
 *
 * A plan says which stands are built, how many rows deep each tier is, which part of a stand is roofed and where the
 * hospitality boxes are. It does not say how any of it is drawn: that is `stadium-scene`. Pure and framework-free.
 *
 * A small ground is open: short roofs over the back of its stands. From level six the ground is closed, as a big ground
 * is: one roof runs round all four sides and the corners, and reaches forward over every row but the front two, so
 * from above a manager sees the roof, the pitch and the rows in front of it.
 *
 * A level is always the previous one with something added, so a manager watches one ground grow. And because places
 * are never removed (`STAD-6`) and every club opens with all four kinds (`STAD-2`), every level has a region of every
 * kind: standing terraces, open seats, covered seats and hospitality boxes. The picture never shows a ground without
 * a kind of place the club owns.
 */
import { facesCamera } from './stadium-camera';
import { StadiumStandCode } from './stadium.models';

/** The pitch, inside its markings, in world units. */
export const PITCH = { width: 160, height: 104 } as const;

/** The clear apron between the pitch and the front row of every stand. */
export const APRON = 10;

/** How deep one row of a stand is, front to back. */
export const ROW_DEPTH = 6.5;

/** How much higher each row is than the one in front of it. */
export const ROW_RISE = 2.4;

/** The walkway between a stand's lower and upper tier, which is also where the hospitality boxes are. */
export const WALKWAY = 5;

/** How tall the walkway and its boxes are. */
export const BAND_HEIGHT = 6;

/** How far the block of boxes behind a one-tier main stand reaches behind its last row. */
export const BOX_DEPTH = 8;

/** The width a sector aims for. A stand has as many as fit, and at least two. */
export const SECTOR_WIDTH = 36;

/** The aisle between two sectors. */
export const AISLE = 2.4;

/** How many front rows the roof of a closed ground leaves open to the sky. */
export const OPEN_ROWS = 2;

/** How far the roof of an end runs on under the roof beside it, so the roofs meet whatever their heights. */
const CORNER_OVERLAP = 10;

/**
 * The roof of an open ground's one-tier stand starts this share of the way back from the front row. A stand seen from
 * behind is roofed further back, so its roof does not hide all of the rows the camera can just see over its wall.
 */
const ROOF_REACH = 0.42;
const ROOF_REACH_FROM_BEHIND = 0.62;

/** The side of the ground a stand stands on. */
export type StandSide = 'main' | 'opposite' | 'east' | 'west';

/**
 * The sides in the order they are drawn, far from the camera first. Fixed, because the camera is: the main stand and
 * the east end face it, the west end and the opposite stand are seen from behind and stand in front of everything.
 */
export const SIDES: readonly StandSide[] = ['main', 'east', 'west', 'opposite'];

/** The long sides run along the pitch, the ends across it. */
export function isLong(side: StandSide): boolean {
  return side === 'main' || side === 'opposite';
}

/** The way the front of a stand — its first row, its risers — looks: toward the pitch. */
const FRONT_NORMAL: Readonly<Record<StandSide, readonly [number, number, number]>> = {
  main: [0, 1, 0],
  opposite: [0, -1, 0],
  east: [-1, 0, 0],
  west: [1, 0, 0],
};

/**
 * Whether a stand's rows face the camera, so its risers show. The main stand and the east end do; the other two are
 * seen from behind. Read from the camera, not written down, so a change of view cannot leave it wrong.
 */
export function standFacesCamera(side: StandSide): boolean {
  return facesCamera(...FRONT_NORMAL[side]);
}

/** What a stand is built of. */
export interface StandPlan {
  /** Rows in the lower tier. Zero means the side is not built yet. */
  readonly lower: number;
  /** Whether the lower tier is a standing terrace rather than seats. */
  readonly lowerStanding: boolean;
  /** Rows in the upper tier, which is always seats. */
  readonly upper: number;
  /** Rows in a third tier behind the upper one, which is always seats. Only a stand with an upper tier has one. */
  readonly top: number;
  /**
   * The share of the stand's length, from its middle outward, that a roof covers. Seats under it are covered. On an
   * open ground a terrace is never roofed, so a terrace stand is roofed only over the seated tier above it.
   */
  readonly roof: number;
  /** Whether the roof reaches forward to the front rows, as on a closed ground, rather than over the back. */
  readonly forward: boolean;
  /** Whether hospitality boxes stand on the stand. */
  readonly vip: boolean;
}

/** One level of the ground. */
export interface LevelPlan {
  readonly main: StandPlan;
  readonly opposite: StandPlan;
  readonly east: StandPlan;
  readonly west: StandPlan;
  /** How many floodlight masts stand. */
  readonly masts: number;
  /** Whether a paved concourse and a low wall ring the ground (the big grounds). */
  readonly ring: boolean;
  /** Whether the club's flag flies over the main stand (the biggest ground). */
  readonly flag: boolean;
  /** Whether one roof closes the ground: every stand roofed end to end and forward, and the corners closed. */
  readonly closed: boolean;
}

const NONE: StandPlan = {
  lower: 0,
  lowerStanding: false,
  upper: 0,
  top: 0,
  roof: 0,
  forward: false,
  vip: false,
};

function seats(lower: number, upper: number, roof: number, vip = false): StandPlan {
  return { lower, lowerStanding: false, upper, top: 0, roof, forward: false, vip };
}

function terrace(lower: number, upper = 0, roof = 0): StandPlan {
  return { lower, lowerStanding: true, upper, top: 0, roof, forward: false, vip: false };
}

/** A stand with a third tier of `rows` rising behind its upper tier. */
function third(stand: StandPlan, rows: number): StandPlan {
  return { ...stand, top: rows };
}

/** A level whose stands are all roofed by one roof that reaches forward to their front rows. */
function closed(level: Omit<LevelPlan, 'closed'>): LevelPlan {
  const roofed = (stand: StandPlan): StandPlan =>
    stand.lower === 0 ? stand : { ...stand, roof: 1, forward: true };

  return {
    ...level,
    main: roofed(level.main),
    opposite: roofed(level.opposite),
    east: roofed(level.east),
    west: roofed(level.west),
    closed: true,
  };
}

/**
 * The fifteen levels. Each row builds on the one above it: stands deepen and roofs lengthen, then from level six one
 * roof closes the ground, a second tier rises, the ends close the bowl, the ground gets a flag, and a third tier rises
 * on one stand at a time. A terrace stays a terrace, because the standing places it holds are never taken away
 * (`STAD-6`): a stand that wants seats gets a seated tier above its terrace. The main stand is where the club sits, so
 * it has the boxes from the start.
 */
const PLANS: readonly LevelPlan[] = [
  // 1 — a village ground: a main stand with a short roof, and a terrace along the far touchline.
  {
    main: seats(3, 0, 0.5, true),
    opposite: terrace(2),
    east: NONE,
    west: NONE,
    masts: 0,
    ring: false,
    flag: false,
    closed: false,
  },
  // 2 — the main stand deepens and a terrace goes up behind one goal.
  {
    main: seats(4, 0, 0.5, true),
    opposite: terrace(3),
    east: terrace(2),
    west: NONE,
    masts: 0,
    ring: false,
    flag: false,
    closed: false,
  },
  // 3 — both ends are terraced, the roof lengthens and the floodlights go up.
  {
    main: seats(4, 0, 0.7, true),
    opposite: terrace(3),
    east: terrace(2),
    west: terrace(2),
    masts: 2,
    ring: false,
    flag: false,
    closed: false,
  },
  // 4 — seats rise over the far terrace, partly roofed.
  {
    main: seats(5, 0, 0.7, true),
    opposite: terrace(3, 2, 0.5),
    east: terrace(3),
    west: terrace(3),
    masts: 4,
    ring: false,
    flag: false,
    closed: false,
  },
  // 5 — every stand deepens.
  {
    main: seats(6, 0, 0.8, true),
    opposite: terrace(4, 2, 0.6),
    east: terrace(4),
    west: terrace(4),
    masts: 4,
    ring: false,
    flag: false,
    closed: false,
  },
  // 6 — the main stand rises a second tier, and one roof closes the ground.
  closed({
    main: seats(6, 3, 0.8, true),
    opposite: terrace(4, 3, 0.6),
    east: terrace(4),
    west: terrace(4),
    masts: 4,
    ring: false,
    flag: false,
  }),
  // 7 — both long stands have two tiers.
  closed({
    main: seats(6, 4, 1, true),
    opposite: terrace(5, 3, 0.8),
    east: terrace(5),
    west: terrace(5),
    masts: 4,
    ring: false,
    flag: false,
  }),
  // 8 — the ends rise a second tier of seats, a concourse rings the ground.
  closed({
    main: seats(7, 4, 1, true),
    opposite: terrace(5, 4, 1),
    east: terrace(5, 3),
    west: terrace(5, 3),
    masts: 6,
    ring: true,
    flag: false,
  }),
  // 9 — nearly a full bowl.
  closed({
    main: seats(7, 5, 1, true),
    opposite: terrace(6, 4, 1),
    east: terrace(6, 4),
    west: terrace(6, 4),
    masts: 6,
    ring: true,
    flag: false,
  }),
  // 10 — the complete two-tier bowl, and the club's flag.
  closed({
    main: seats(7, 5, 1, true),
    opposite: terrace(7, 5, 1),
    east: terrace(7, 5),
    west: terrace(7, 5),
    masts: 6,
    ring: true,
    flag: true,
  }),
  // 11 — the ends' upper tiers deepen.
  closed({
    main: seats(7, 5, 1, true),
    opposite: terrace(7, 5, 1),
    east: terrace(7, 6),
    west: terrace(7, 6),
    masts: 6,
    ring: true,
    flag: true,
  }),
  // 12 — the main stand rises a third tier, with a second row of boxes under it.
  closed({
    main: third(seats(7, 5, 1, true), 4),
    opposite: terrace(7, 5, 1),
    east: terrace(7, 6),
    west: terrace(7, 6),
    masts: 6,
    ring: true,
    flag: true,
  }),
  // 13 — the east end rises a third tier.
  closed({
    main: third(seats(7, 5, 1, true), 4),
    opposite: terrace(7, 5, 1),
    east: third(terrace(7, 6), 4),
    west: terrace(7, 6),
    masts: 6,
    ring: true,
    flag: true,
  }),
  // 14 — the opposite stand rises a third tier.
  closed({
    main: third(seats(7, 5, 1, true), 4),
    opposite: third(terrace(7, 5, 1), 3),
    east: third(terrace(7, 6), 4),
    west: terrace(7, 6),
    masts: 6,
    ring: true,
    flag: true,
  }),
  // 15 — the west end rises a third tier: the complete three-tier bowl.
  closed({
    main: third(seats(7, 5, 1, true), 4),
    opposite: third(terrace(7, 5, 1), 3),
    east: third(terrace(7, 6), 4),
    west: third(terrace(7, 6), 3),
    masts: 6,
    ring: true,
    flag: true,
  }),
];

/** How many levels there are to draw. */
export const LEVELS = PLANS.length;

/** Clamps a level into the range the drawing has a plan for. */
export function clampLevel(level: number): number {
  if (!Number.isFinite(level)) {
    return 1;
  }

  return Math.min(Math.max(Math.round(level), 1), LEVELS);
}

/** The plan of one level. Anything out of range is clamped, so a level this build does not know still draws. */
export function planOf(level: number): LevelPlan {
  return PLANS[clampLevel(level) - 1];
}

/** How deep a stand is, front row to back wall, not counting boxes behind a one-tier stand. */
export function depthOf(stand: StandPlan): number {
  if (stand.lower === 0) {
    return 0;
  }

  return (
    stand.lower * ROW_DEPTH +
    (stand.upper > 0 ? WALKWAY + stand.upper * ROW_DEPTH : 0) +
    (stand.upper > 0 && stand.top > 0 ? WALKWAY + stand.top * ROW_DEPTH : 0)
  );
}

/** A tier of a stand, front to back. */
export type TierName = 'lower' | 'upper' | 'top';

/** Where a tier of a stand stands: its rows, its front edge out from the stand's front line, and its floor. */
export interface TierPlan {
  readonly name: TierName;
  readonly rows: number;
  readonly out: number;
  readonly base: number;
}

/** The tiers of a stand, front to back. A tier above the first stands behind a walkway at the back of the one below. */
export function tiersOf(stand: StandPlan): readonly TierPlan[] {
  if (stand.lower === 0) {
    return [];
  }

  const floor = stand.lower * ROW_RISE;
  const bandOut = stand.lower * ROW_DEPTH;
  const tiers: TierPlan[] = [{ name: 'lower', rows: stand.lower, out: 0, base: 0 }];

  if (stand.upper > 0) {
    tiers.push({
      name: 'upper',
      rows: stand.upper,
      out: bandOut + WALKWAY,
      base: floor + BAND_HEIGHT,
    });

    if (stand.top > 0) {
      tiers.push({
        name: 'top',
        rows: stand.top,
        out: bandOut + 2 * WALKWAY + stand.upper * ROW_DEPTH,
        base: floor + 2 * BAND_HEIGHT + stand.upper * ROW_RISE,
      });
    }
  }

  return tiers;
}

/**
 * How far out from its front line a stand's roof starts. A roof that reaches forward leaves only the front rows open.
 * Otherwise a one-tier stand is roofed over its back half, and a stand with more tiers over its highest one.
 */
export function roofFrontOf(side: StandSide, stand: StandPlan): number {
  if (stand.forward) {
    return Math.min(OPEN_ROWS, stand.lower) * ROW_DEPTH;
  }

  if (stand.upper === 0) {
    const deep = depthOf(stand) + (stand.vip ? BOX_DEPTH : 0);

    return deep * (standFacesCamera(side) ? ROOF_REACH : ROOF_REACH_FROM_BEHIND);
  }

  return tiersOf(stand)[tiersOf(stand).length - 1].out - WALKWAY;
}

/**
 * The kind of place one row of a tier is: a terrace is standing, and a seat is covered when it is in a sector the roof
 * covers and starts behind the roof's front edge.
 */
export function kindOfRow(
  side: StandSide,
  stand: StandPlan,
  tier: TierPlan,
  row: number,
  sector: Sector,
): StadiumStandCode {
  if (tier.name === 'lower' && stand.lowerStanding) {
    return 'standing';
  }

  const near = tier.out + row * ROW_DEPTH;

  return sector.covered && near >= roofFrontOf(side, stand) - 1e-6 ? 'covered_seating' : 'seating';
}

/** How tall a stand is at its highest row. */
export function heightOf(stand: StandPlan): number {
  if (stand.lower === 0) {
    return 0;
  }

  return (
    stand.lower * ROW_RISE +
    (stand.upper > 0 ? BAND_HEIGHT + stand.upper * ROW_RISE : 0) +
    (stand.upper > 0 && stand.top > 0 ? BAND_HEIGHT + stand.top * ROW_RISE : 0)
  );
}

/** One section of a stand, between two aisles. */
export interface Sector {
  readonly index: number;
  /** Where the sector starts along the stand. */
  readonly from: number;
  /** Where it ends. */
  readonly to: number;
  /** Whether the roof covers it. */
  readonly covered: boolean;
}

/**
 * Cuts a stand's length into sectors and says which the roof covers.
 *
 * The roof is centred, so the covered sectors are one run. A roof is never lost to rounding: any roof covers at
 * least the middle sector, and a roof that is not complete leaves at least the outermost sectors open.
 */
export function sectorsOf(from: number, to: number, roof: number): readonly Sector[] {
  const length = to - from;
  const count = Math.max(2, Math.round(length / SECTOR_WIDTH));
  const width = (length - (count - 1) * AISLE) / count;
  const middle = (from + to) / 2;

  const sectors = Array.from({ length: count }, (_, index) => {
    const start = from + index * (width + AISLE);
    const centre = start + width / 2;

    return {
      index,
      from: start,
      to: start + width,
      covered: roof >= 1 || (roof > 0 && Math.abs(centre - middle) <= (roof * length) / 2),
    };
  });

  if (roof > 0 && roof < 1 && !sectors.some((sector) => sector.covered)) {
    const nearest = sectors.reduce((best, sector) =>
      Math.abs((sector.from + sector.to) / 2 - middle) <
      Math.abs((best.from + best.to) / 2 - middle)
        ? sector
        : best,
    );

    return sectors.map((sector) => ({ ...sector, covered: sector === nearest }));
  }

  return sectors;
}

/** Where one stand lies along its side, with the corners closed by the long stands. */
export interface StandSpan {
  readonly side: StandSide;
  readonly plan: StandPlan;
  /** The span along the stand, in the world axis the stand runs on. */
  readonly from: number;
  readonly to: number;
  /** How far along the stand its roof may run, past its ends where it meets the roofs beside it. */
  readonly roofFrom: number;
  readonly roofTo: number;
}

/**
 * Lays the stands of a level along their sides.
 *
 * The long stands run on past the pitch to close the corners wherever an end stand stands beside them, so a stand is
 * as long as the pitch plus the apron plus the depth of the end stands. On a closed ground the roof of an end runs on
 * over the corners to the front of the long stands' roofs, so the roof is one ring.
 */
export function spansOf(plan: LevelPlan): readonly StandSpan[] {
  const west = depthOf(plan.west);
  const east = depthOf(plan.east);
  const reach = (side: 'main' | 'opposite'): number =>
    plan.closed && plan[side].lower > 0 ? roofFrontOf(side, plan[side]) + CORNER_OVERLAP : 0;

  const spans: StandSpan[] = [];

  for (const side of SIDES) {
    const stand = plan[side];

    if (stand.lower === 0) {
      continue;
    }

    if (isLong(side)) {
      const from = -APRON - west;
      const to = PITCH.width + APRON + east;

      spans.push({ side, plan: stand, from, to, roofFrom: from, roofTo: to });
    } else {
      const from = -APRON;
      const to = PITCH.height + APRON;

      spans.push({
        side,
        plan: stand,
        from,
        to,
        roofFrom: from - reach('main'),
        roofTo: to + reach('opposite'),
      });
    }
  }

  return spans;
}

/** Which kinds of place a level draws, in the order the screen lists them. */
export function kindsOf(level: number): readonly StadiumStandCode[] {
  const found = new Set<StadiumStandCode>();

  for (const span of spansOf(planOf(level))) {
    for (const sector of sectorsOf(span.from, span.to, span.plan.roof)) {
      for (const tier of tiersOf(span.plan)) {
        for (let row = 0; row < tier.rows; row++) {
          found.add(kindOfRow(span.side, span.plan, tier, row, sector));
        }
      }
    }

    if (span.plan.vip) {
      found.add('vip');
    }
  }

  return ['standing', 'seating', 'covered_seating', 'vip'].filter((kind) =>
    found.has(kind as StadiumStandCode),
  ) as StadiumStandCode[];
}
