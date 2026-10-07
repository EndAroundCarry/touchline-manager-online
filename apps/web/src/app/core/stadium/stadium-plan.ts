/**
 * The ten stadium levels as a plan (`STAD-1`).
 *
 * A plan says which stands are built, how many rows deep each tier is, which part of a stand is roofed and where the
 * hospitality boxes are. It does not say how any of it is drawn: that is `stadium-scene`. Pure and framework-free.
 *
 * A level is always the previous one with something added, so a manager watches one ground grow. And because places
 * are never removed (`STAD-6`) and every club opens with all four kinds (`STAD-2`), every level has a region of every
 * kind: standing terraces, open seats, covered seats and hospitality boxes. The picture never shows a ground without
 * a kind of place the club owns.
 */
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

/** What a stand is built of. */
export interface StandPlan {
  /** Rows in the lower tier. Zero means the side is not built yet. */
  readonly lower: number;
  /** Whether the lower tier is a standing terrace rather than seats. */
  readonly lowerStanding: boolean;
  /** Rows in the upper tier, which is always seats. */
  readonly upper: number;
  /**
   * The share of the stand's length, from its middle outward, that a roof covers. Seats under it are covered. A
   * terrace is never roofed, so a terrace stand is roofed only over the seated tier above it.
   */
  readonly roof: number;
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
}

const NONE: StandPlan = { lower: 0, lowerStanding: false, upper: 0, roof: 0, vip: false };

function seats(lower: number, upper: number, roof: number, vip = false): StandPlan {
  return { lower, lowerStanding: false, upper, roof, vip };
}

function terrace(lower: number, upper = 0, roof = 0): StandPlan {
  return { lower, lowerStanding: true, upper, roof, vip: false };
}

/**
 * The ten levels. Each row builds on the one above it: stands deepen, roofs lengthen, a second tier rises, the ends
 * close the bowl and the ground gets a flag. A terrace stays a terrace, because the standing places it holds are never
 * taken away (`STAD-6`): a stand that wants seats gets a seated tier above its terrace. The main stand is where the
 * club sits, so it has the boxes from the start.
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
  },
  // 6 — the main stand rises a second tier.
  {
    main: seats(6, 3, 0.8, true),
    opposite: terrace(4, 3, 0.6),
    east: terrace(4),
    west: terrace(4),
    masts: 4,
    ring: false,
    flag: false,
  },
  // 7 — both long stands have two tiers, the main stand is roofed end to end.
  {
    main: seats(6, 4, 1, true),
    opposite: terrace(5, 3, 0.8),
    east: terrace(5),
    west: terrace(5),
    masts: 4,
    ring: false,
    flag: false,
  },
  // 8 — the ends rise a second tier of seats, a concourse rings the ground.
  {
    main: seats(7, 4, 1, true),
    opposite: terrace(5, 4, 1),
    east: terrace(5, 3),
    west: terrace(5, 3),
    masts: 6,
    ring: true,
    flag: false,
  },
  // 9 — nearly a full bowl.
  {
    main: seats(7, 5, 1, true),
    opposite: terrace(6, 4, 1),
    east: terrace(6, 4),
    west: terrace(6, 4),
    masts: 6,
    ring: true,
    flag: false,
  },
  // 10 — the complete two-tier bowl, and the club's flag.
  {
    main: seats(7, 5, 1, true),
    opposite: terrace(7, 5, 1),
    east: terrace(7, 5),
    west: terrace(7, 5),
    masts: 6,
    ring: true,
    flag: true,
  },
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

  return stand.lower * ROW_DEPTH + (stand.upper > 0 ? WALKWAY + stand.upper * ROW_DEPTH : 0);
}

/** How tall a stand is at its highest row. */
export function heightOf(stand: StandPlan): number {
  if (stand.lower === 0) {
    return 0;
  }

  return stand.lower * ROW_RISE + (stand.upper > 0 ? BAND_HEIGHT + stand.upper * ROW_RISE : 0);
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
}

/**
 * Lays the stands of a level along their sides.
 *
 * The long stands run on past the pitch to close the corners wherever an end stand stands beside them, so a stand is
 * as long as the pitch plus the apron plus the depth of the end stands.
 */
export function spansOf(plan: LevelPlan): readonly StandSpan[] {
  const west = depthOf(plan.west);
  const east = depthOf(plan.east);

  const spans: StandSpan[] = [];

  for (const side of SIDES) {
    const stand = plan[side];

    if (stand.lower === 0) {
      continue;
    }

    spans.push(
      isLong(side)
        ? {
            side,
            plan: stand,
            from: -APRON - west,
            to: PITCH.width + APRON + east,
          }
        : { side, plan: stand, from: -APRON, to: PITCH.height + APRON },
    );
  }

  return spans;
}

/** Which kinds of place a level draws, in the order the screen lists them. */
export function kindsOf(level: number): readonly StadiumStandCode[] {
  const found = new Set<StadiumStandCode>();

  for (const span of spansOf(planOf(level))) {
    const sectors = sectorsOf(span.from, span.to, span.plan.roof);

    if (span.plan.lowerStanding) {
      found.add('standing');
    }

    for (const sector of sectors) {
      if (!span.plan.lowerStanding) {
        found.add(sector.covered ? 'covered_seating' : 'seating');
      }

      if (span.plan.upper > 0) {
        found.add(sector.covered ? 'covered_seating' : 'seating');
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
