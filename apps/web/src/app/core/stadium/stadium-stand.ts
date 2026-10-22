/**
 * One stand of the stadium drawing (`STAD-1`).
 *
 * A stand is drawn from its far rows to its near ones, so a near row covers what it should of the one behind it, and
 * then whatever stands over them: the walkway and the hospitality boxes, the end the camera can see, the roof. A seat
 * is a dash on a line. A row of seats is one stroked line with a dash pattern, and the second colour is a second dash
 * pattern on top of the first, so a ground of thousands of seats is a few hundred elements. The planes a manager can
 * light come last, over the roof, so a kind of place the roof hides can still be found.
 */
import { Vec3, facesCamera, outline, round, spacing, trace, unitLength } from './stadium-camera';
import { Canvas, PALETTE, SceneDetail } from './stadium-canvas';
import {
  AISLE,
  APRON,
  BAND_HEIGHT,
  BOX_DEPTH,
  PITCH,
  ROW_DEPTH,
  ROW_RISE,
  Sector,
  StandPlan,
  StandSide,
  StandSpan,
  TierName,
  TierPlan,
  WALKWAY,
  depthOf,
  heightOf,
  isLong,
  kindOfRow,
  roofFrontOf,
  sectorsOf,
  standFacesCamera,
  tiersOf,
} from './stadium-plan';
import { StadiumStandCode } from './stadium.models';

/** The seat's width and the room one takes along a row, in world units. */
const SEAT_WIDTH = 3.2;
const SEAT_PITCH = 4.4;

/** Where along a tread the seats sit, as a share of its depth from the front. */
const SEAT_LINE = 0.34;

/** How high a roof stands above the highest row under it. */
export const ROOF_CLEARANCE = 6;

/** The roof reaches this far behind the last row. */
const ROOF_OVERHANG_REAR = 2;

/** Maps a place on a stand — along it, out from its front line, and up — to a point of the world. */
export type Frame = (along: number, out: number, up: number) => Vec3;

export function frameOf(side: StandSide): Frame {
  switch (side) {
    case 'main':
      return (a, o, z) => [a, -APRON - o, z];
    case 'opposite':
      return (a, o, z) => [a, PITCH.height + APRON + o, z];
    case 'east':
      return (a, o, z) => [PITCH.width + APRON + o, a, z];
    case 'west':
      return (a, o, z) => [-APRON - o, a, z];
  }
}

/** The end of a stand the camera can see: its start along the stand, or its finish. */
function visibleEnd(side: StandSide): 'from' | 'to' {
  const toward = isLong(side) ? facesCamera(-1, 0, 0) : facesCamera(0, -1, 0);

  return toward ? 'from' : 'to';
}

/** A tier of rows. Grouped rows are taller and deeper so a thumbnail has fewer of them and the same outline. */
interface Tier {
  readonly rows: number;
  readonly depth: number;
  readonly rise: number;
  /** Where the tier's front edge is, out from the stand's front line. */
  readonly out: number;
  /** Where the tier's floor is. */
  readonly base: number;
}

function tierOf(rows: number, out: number, base: number, group: number): Tier {
  const drawn = Math.ceil(rows / group);

  return {
    rows: drawn,
    depth: (ROW_DEPTH * rows) / drawn,
    rise: (ROW_RISE * rows) / drawn,
    out,
    base,
  };
}

/** What every part of one stand's drawing needs to know. */
interface StandContext {
  readonly canvas: Canvas;
  readonly side: StandSide;
  readonly plan: StandPlan;
  readonly at: Frame;
  readonly from: number;
  readonly to: number;
  readonly sectors: readonly Sector[];
  /** How far along the stand its roof runs. */
  readonly roofFrom: number;
  readonly roofTo: number;
  readonly facing: boolean;
  readonly end: 'from' | 'to';
  /** The axis of the ground the rows run along. */
  readonly axis: 'x' | 'y';
  readonly detail: SceneDetail;
  /** The planes a manager can light, drawn after everything else of the stand. */
  readonly planes: Array<{ readonly d: string; readonly region: StadiumStandCode }>;
}

/** The shade of a vertical face by the axis of the ground it looks along: the sun reaches one and not the other. */
function shade(normal: 'x' | 'y'): string {
  return normal === 'y' ? PALETTE.lit : PALETTE.dark;
}

/** The axis the risers and the back wall of a stand look along. */
function frontAxis(context: StandContext): 'x' | 'y' {
  return context.axis === 'x' ? 'y' : 'x';
}

/**
 * The sloped plane over a run of rows of a tier in one sector, a little above the steps, for the highlight a manager
 * can switch on.
 */
function surface(
  context: StandContext,
  tier: Tier,
  sector: Sector,
  first: number,
  last: number,
): string {
  const { at } = context;
  const front = tier.base + (first + 1) * tier.rise;
  const back = tier.base + (last + 1) * tier.rise + 0.6;
  const near = tier.out + first * tier.depth;
  const far = tier.out + (last + 1) * tier.depth;

  return outline([
    at(sector.from, near, front),
    at(sector.to, near, front),
    at(sector.to, far, back),
    at(sector.from, far, back),
  ]);
}

/** Draws one row of a tier: its tread, its riser, the aisles, and the seats or the standing crowd on it. */
function drawRow(context: StandContext, tier: Tier, tierName: TierName, row: number): void {
  const { canvas, at, from, to, sectors, axis, plan } = context;

  const near = tier.out + row * tier.depth;
  const far = near + tier.depth;
  const low = tier.base + row * tier.rise;
  const top = low + tier.rise;

  canvas.add({
    role: 'tread',
    d: outline([at(from, near, top), at(to, near, top), at(to, far, top), at(from, far, top)]),
    fill: PALETTE.treadTop,
  });

  if (context.facing) {
    canvas.add({
      role: 'riser',
      d: outline([at(from, near, low), at(to, near, low), at(to, near, top), at(from, near, top)]),
      fill: shade(frontAxis(context)),
    });
  }

  // The aisles are the lighter steps between sectors.
  const aisles = sectors.slice(1).map((sector, index) => {
    const start = sectors[index].to;
    const finish = sector.from;

    return (
      outline([
        at(start, near, top),
        at(finish, near, top),
        at(finish, far, top),
        at(start, far, top),
      ]) +
      (context.facing
        ? outline([
            at(start, near, low),
            at(finish, near, low),
            at(finish, near, top),
            at(start, near, top),
          ])
        : '')
    );
  });

  if (aisles.length > 0) {
    canvas.add({ role: 'aisle', d: aisles.join(''), fill: PALETTE.aisle });
  }

  const line = near + tier.depth * SEAT_LINE;
  const along = unitLength(axis);

  const primary: string[] = [];
  const secondary: string[] = [];
  const standing: string[] = [];
  let seatsInARow = 0;

  for (const sector of sectors) {
    const space = sector.to - sector.from;

    if (tierName === 'lower' && plan.lowerStanding) {
      standing.push(trace([at(sector.from + 1, line, top), at(sector.to - 1, line, top)]));
      continue;
    }

    const count = Math.max(1, Math.floor(space / SEAT_PITCH));
    const start = sector.from + (space - count * SEAT_PITCH) / 2;

    seatsInARow = count;
    primary.push(
      trace([at(start, line, top + 0.02), at(start + count * SEAT_PITCH, line, top + 0.02)]),
    );
    secondary.push(
      trace([at(start, line, top + 0.03), at(start + count * SEAT_PITCH, line, top + 0.03)]),
    );
  }

  const thickness = spacing(axis) * tier.depth * 0.5;

  if (primary.length > 0) {
    const seat = SEAT_WIDTH * along;
    const gap = (SEAT_PITCH - SEAT_WIDTH) * along;
    const pitch = SEAT_PITCH * along;
    const period = Math.max(3, seatsInARow);

    canvas.add({
      role: 'seat',
      d: primary.join(''),
      stroke: 'primary',
      width: round(thickness),
      dash: `${round(seat)} ${round(gap)}`,
    });

    // A band of the second colour runs across the sector, a seat further along on every row.
    canvas.add({
      role: 'seat-alt',
      d: secondary.join(''),
      stroke: 'secondary',
      width: round(thickness),
      dash: `${round(seat)} ${round(gap)} ${round(seat)} ${round(period * pitch - 2 * seat - gap)}`,
      offset: round((period - (row % period)) * pitch),
    });
  }

  if (standing.length > 0) {
    canvas.add({
      role: 'crowd',
      d: standing.join(''),
      stroke: PALETTE.crowd,
      width: round(thickness * 0.8),
      dash: '1.1 3.3',
    });

    if (row % 3 === 2) {
      const rail = near + tier.depth * 0.9;

      canvas.add({
        role: 'barrier',
        d: sectors
          .map((sector) =>
            trace([at(sector.from, rail, top + 1.6), at(sector.to, rail, top + 1.6)]),
          )
          .join(''),
        stroke: PALETTE.barrier,
        width: 1,
      });
    }
  }
}

/** Draws the hospitality boxes over a range of a stand: a dark wall, a band of glass and the frames between boxes. */
function drawBoxes(
  context: StandContext,
  from: number,
  to: number,
  out: number,
  deep: number,
  floor: number,
): void {
  const { canvas, at } = context;
  const top = floor + BAND_HEIGHT;

  if (context.facing) {
    canvas.add({
      role: 'vip-wall',
      d: outline([at(from, out, floor), at(to, out, floor), at(to, out, top), at(from, out, top)]),
      fill: PALETTE.boxWall,
      region: 'vip',
    });
    canvas.add({
      role: 'vip-glass',
      d: outline([
        at(from + 1.2, out, floor + 1.4),
        at(to - 1.2, out, floor + 1.4),
        at(to - 1.2, out, top - 1),
        at(from + 1.2, out, top - 1),
      ]),
      fill: PALETTE.glass,
      opacity: 0.9,
      region: 'vip',
    });

    const frames: string[] = [
      trace([at(from + 1.2, out, floor + 1.4), at(to - 1.2, out, floor + 1.4)]),
    ];

    for (let a = from + 1.2; a <= to - 1.2 + 0.01; a += 9) {
      frames.push(trace([at(a, out, floor + 1.4), at(a, out, top - 1)]));
    }

    canvas.add({
      role: 'vip-frame',
      d: frames.join(''),
      stroke: 'secondary',
      width: 1,
      region: 'vip',
    });
  }

  canvas.add({
    role: 'vip-top',
    d: outline([
      at(from, out, top),
      at(to, out, top),
      at(to, out + deep, top),
      at(from, out + deep, top),
    ]),
    fill: PALETTE.walkway,
    region: 'vip',
  });

  // The whole block, for the highlight a manager can switch on.
  if (context.detail === 'full') {
    context.planes.push({
      d: outline([
        at(from, out, floor),
        at(to, out, floor),
        at(to, out, top),
        at(to, out + deep, top),
        at(from, out + deep, top),
        at(from, out, top),
      ]),
      region: 'vip',
    });
  }

  // The end of the block the camera can see.
  if (context.facing) {
    const edge = context.end === 'from' ? from : to;

    canvas.add({
      role: 'side',
      d: outline([
        at(edge, out, floor),
        at(edge, out + deep, floor),
        at(edge, out + deep, top),
        at(edge, out, top),
      ]),
      fill: shade(context.axis),
    });
  }
}

/** The profile of a stand, the stepped outline of its rows, for the end the camera can see. */
function drawEnd(context: StandContext, tiers: readonly Tier[]): void {
  const { canvas, at } = context;
  const edge = context.end === 'from' ? context.from : context.to;

  const profile: [number, number][] = [[0, 0]];

  const climb = (tier: Tier): void => {
    for (let row = 0; row < tier.rows; row++) {
      profile.push([tier.out + row * tier.depth, tier.base + (row + 1) * tier.rise]);
      profile.push([tier.out + (row + 1) * tier.depth, tier.base + (row + 1) * tier.rise]);
    }
  };

  tiers.forEach((tier, index) => {
    // The walkway behind the tier below, at this tier's floor.
    if (index > 0) {
      const below = tiers[index - 1];

      profile.push([below.out + below.rows * below.depth, tier.base]);
      profile.push([tier.out, tier.base]);
    }

    climb(tier);
  });

  const last = profile[profile.length - 1];

  profile.push([last[0], 0]);

  canvas.add({
    role: 'side',
    d: outline(profile.map(([out, up]) => at(edge, out, up))),
    fill: shade(context.axis),
  });
}

/** The back wall of a stand, which the camera sees from the other side of the ground. */
function drawBackWall(context: StandContext, deep: number, top: number): void {
  const { canvas, at, from, to, sectors } = context;

  canvas.add({
    role: 'side',
    d: outline([at(from, deep, 0), at(to, deep, 0), at(to, deep, top), at(from, deep, top)]),
    fill: shade(frontAxis(context)),
  });

  // The concourse windows of each sector: a low dark band, so the wall reads as a building and not as a slab.
  const windows = sectors.map((sector) =>
    outline([
      at(sector.from + 3, deep, top * 0.18),
      at(sector.to - 3, deep, top * 0.18),
      at(sector.to - 3, deep, top * 0.18 + Math.min(4, top * 0.2)),
      at(sector.from + 3, deep, top * 0.18 + Math.min(4, top * 0.2)),
    ]),
  );

  canvas.add({ role: 'window', d: windows.join(''), fill: PALETTE.boxWall, opacity: 0.55 });
}

/**
 * Draws the roof over the covered sectors of a stand, with its trim in the club's second colour.
 *
 * The roof reaches from `front`, out from the stand's front line, to a little behind the last row: it covers the back
 * of the stand and leaves the front rows open to the sky, which is how a stand is roofed and what lets the camera see
 * the seats. On a closed ground the roof of an end runs on over the corners to meet the roofs beside it.
 */
function drawRoof(context: StandContext, front: number, deep: number, top: number): void {
  const { canvas, at, sectors } = context;
  const covered = sectors.filter((sector) => sector.covered);

  if (covered.length === 0) {
    return;
  }

  const start = context.roofFrom < context.from ? context.roofFrom : covered[0].from - 1;
  const finish = context.roofTo > context.to ? context.roofTo : covered[covered.length - 1].to + 1;
  const rear = deep + ROOF_OVERHANG_REAR;
  const height = top + ROOF_CLEARANCE;

  // The columns stand at the back, in the aisles, so the roof reads as one slab over the seats.
  if (!context.facing) {
    const columns = covered.map((sector) =>
      trace([at(sector.to + AISLE / 2, rear - 1, 0), at(sector.to + AISLE / 2, rear - 1, height)]),
    );

    columns.push(trace([at(start + 1, rear - 1, 0), at(start + 1, rear - 1, height)]));
    canvas.add({
      role: 'pillar',
      d: columns.join(''),
      stroke: PALETTE.pillar,
      width: 1.8,
      region: 'covered_seating',
    });
  }

  canvas.add({
    role: 'roof',
    d: outline([
      at(start, front, height),
      at(finish, front, height),
      at(finish, rear, height),
      at(start, rear, height),
    ]),
    fill: PALETTE.roof,
    region: 'covered_seating',
  });

  const ribs: string[] = [];

  for (let a = start + 6; a < finish; a += 9) {
    ribs.push(trace([at(a, front, height), at(a, rear, height)]));
  }

  canvas.add({
    role: 'roof-rib',
    d: ribs.join(''),
    stroke: PALETTE.roofRib,
    width: 0.8,
    region: 'covered_seating',
  });

  // The edge of the roof the camera sees, in the club's second colour.
  const edge = context.facing ? front : rear;

  canvas.add({
    role: 'roof-trim',
    d: outline([
      at(start, edge, height - 2),
      at(finish, edge, height - 2),
      at(finish, edge, height),
      at(start, edge, height),
    ]),
    fill: 'secondary',
    region: 'covered_seating',
  });

  const endAt = context.end === 'from' ? start : finish;

  canvas.add({
    role: 'side',
    d: outline([
      at(endAt, front, height - 2),
      at(endAt, rear, height - 2),
      at(endAt, rear, height),
      at(endAt, front, height),
    ]),
    fill: shade(context.axis),
  });
}

/** The sectors the hospitality boxes of a one-tier main stand take: the middle two or three. */
function boxRange(sectors: readonly Sector[]): { readonly from: number; readonly to: number } {
  const size = sectors.length <= 4 ? 2 : 3;
  const first = Math.floor((sectors.length - size) / 2);

  return { from: sectors[first].from, to: sectors[first + size - 1].to };
}

/** Draws one stand, from its far rows to its near ones, and then whatever stands over them. */
export function drawStand(canvas: Canvas, span: StandSpan, detail: SceneDetail): void {
  const { side, plan, from, to } = span;
  const group = detail === 'low' ? 3 : 1;
  const sectors = sectorsOf(from, to, plan.roof);

  const context: StandContext = {
    canvas,
    side,
    plan,
    at: frameOf(side),
    from,
    to,
    sectors,
    roofFrom: span.roofFrom,
    roofTo: span.roofTo,
    facing: standFacesCamera(side),
    end: visibleEnd(side),
    axis: isLong(side) ? 'x' : 'y',
    detail,
    planes: [],
  };

  const { at } = context;

  // Each tier drawn, with the walkway in front of it: a tier above the first stands behind a walkway.
  const planned = tiersOf(plan);
  const tiers: Array<[Tier, TierName, TierPlan]> = planned.map((tier) => [
    tierOf(tier.rows, tier.out, tier.base, group),
    tier.name,
    tier,
  ]);
  const tier = (name: TierName): Tier | null =>
    tiers.find(([, candidate]) => candidate === name)?.[0] ?? null;

  const lower = tier('lower')!;
  const upper = tier('upper');
  const third = tier('top');
  const floor = plan.lower * ROW_RISE;
  const bandOut = plan.lower * ROW_DEPTH;
  const topFloor = third === null ? 0 : third.base - BAND_HEIGHT;
  const topBandOut = third === null ? 0 : third.out - WALKWAY;

  // The boxes of a one-tier main stand are a block behind its last row.
  const block = plan.vip && upper === null;
  const deep = depthOf(plan) + (block ? BOX_DEPTH : 0);
  const top = heightOf(plan) + (block ? BAND_HEIGHT : 0);

  // Rows are drawn from the far one to the near one, so a near row covers what it should of the one behind it.
  const drawTier = (tier: Tier, name: TierName): void => {
    const rows = Array.from({ length: tier.rows }, (_, index) => index);

    for (const row of context.facing ? rows.reverse() : rows) {
      drawRow(context, tier, name, row);
    }
  };

  // The walkway in front of a tier above the first, or on the main stand the boxes along it.
  const drawBand = (out: number, base: number): void => {
    if (plan.vip) {
      drawBoxes(context, from, to, out, WALKWAY, base);
      return;
    }

    canvas.add({
      role: 'walkway',
      d: outline([
        at(from, out, base + BAND_HEIGHT),
        at(to, out, base + BAND_HEIGHT),
        at(to, out + WALKWAY, base + BAND_HEIGHT),
        at(from, out + WALKWAY, base + BAND_HEIGHT),
      ]),
      fill: PALETTE.walkway,
    });

    if (context.facing) {
      canvas.add({
        role: 'side',
        d: outline([
          at(from, out, base),
          at(to, out, base),
          at(to, out, base + BAND_HEIGHT),
          at(from, out, base + BAND_HEIGHT),
        ]),
        fill: shade(frontAxis(context)),
      });
    }
  };

  if (context.facing) {
    if (third !== null) {
      drawTier(third, 'top');
      drawBand(topBandOut, topFloor);
    }

    if (upper !== null) {
      drawTier(upper, 'upper');
      drawBand(bandOut, floor);
    } else if (block) {
      const range = boxRange(sectors);

      drawBoxes(context, range.from, range.to, bandOut, BOX_DEPTH, floor);
    }

    drawTier(lower, 'lower');
  } else {
    drawTier(lower, 'lower');

    if (upper !== null) {
      drawBand(bandOut, floor);
      drawTier(upper, 'upper');
    }

    if (third !== null) {
      drawBand(topBandOut, topFloor);
      drawTier(third, 'top');
    }

    drawBackWall(context, deep, top);
  }

  drawEnd(
    context,
    tiers.map(([tier]) => tier),
  );

  drawRoof(context, roofFrontOf(side, plan), deep, top);

  // One invisible plane over each run of rows of one kind, in every sector of every tier, which the screen lights to
  // show where a kind of place is: the open rows in front of a roof and the covered ones under it are lit apart.
  if (detail === 'full') {
    for (const [drawn, , planTier] of tiers) {
      for (const sector of sectors) {
        let first = 0;

        for (let row = 1; row <= planTier.rows; row++) {
          const kind = kindOfRow(side, plan, planTier, first, sector);

          if (row < planTier.rows && kindOfRow(side, plan, planTier, row, sector) === kind) {
            continue;
          }

          context.planes.push({ d: surface(context, drawn, sector, first, row - 1), region: kind });
          first = row;
        }
      }
    }
  }

  for (const plane of context.planes) {
    canvas.add({
      role: 'region',
      d: plane.d,
      fill: PALETTE.highlight,
      opacity: 0,
      region: plane.region,
    });
  }
}
