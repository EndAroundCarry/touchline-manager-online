/**
 * What stands round the stadium's stands (`STAD-1`): the pitch with its markings, the goals and boards, the
 * floodlights, the fence and the concourse, the road, the trees and the flag. Pure and framework-free.
 */
import { Vec3, WORLD_FRAME, nearness, outline, project, round, trace } from './stadium-camera';
import { Canvas, PALETTE } from './stadium-canvas';
import {
  APRON,
  BOX_DEPTH,
  LEVELS,
  PITCH,
  ROW_DEPTH,
  StandPlan,
  StandSide,
  depthOf,
  heightOf,
  isLong,
  planOf,
} from './stadium-plan';
import { ROOF_CLEARANCE } from './stadium-stand';

/** The fence stands this far beyond the stands, and the concourse of a big ground this much further. */
export const FENCE_GAP = 16;
export const RING_GAP = 12;

/** How tall a floodlight mast stands. */
export const MAST_HEIGHT = 62;

/** A flat shape on the ground, from points of the ground. */
export function ground(points: readonly (readonly [number, number])[]): string {
  return outline(points.map(([x, y]) => [x, y, 0] as const));
}

/** The pitch: its grass, its mowing, its markings. */
export function drawPitch(canvas: Canvas): void {
  const { width, height } = PITCH;

  canvas.add({
    role: 'pitch',
    d: ground([
      [0, 0],
      [width, 0],
      [width, height],
      [0, height],
    ]),
    fill: PALETTE.pitch,
  });

  const stripes: string[] = [];
  const count = 10;

  for (let index = 1; index < count; index += 2) {
    const x0 = (index * width) / count;
    const x1 = ((index + 1) * width) / count;

    stripes.push(
      ground([
        [x0, 0],
        [x1, 0],
        [x1, height],
        [x0, height],
      ]),
    );
  }

  canvas.add({ role: 'mowing', d: stripes.join(''), fill: PALETTE.pitchDark });

  const middle = height / 2;
  const box = { deep: 25.1, wide: 61.6 };
  const small = { deep: 8.4, wide: 28 };
  const spot = 16.8;
  const radius = 14;

  const arc = (centre: number, reach: number, sign: 1 | -1): Vec3[] =>
    Array.from({ length: 17 }, (_, step) => {
      const angle = -reach + (2 * reach * step) / 16;

      return [
        centre + sign * radius * Math.cos(angle),
        middle + radius * Math.sin(angle),
        0,
      ] as const;
    });

  const circle = Array.from({ length: 37 }, (_, step) => {
    const angle = (step / 36) * Math.PI * 2;

    return [width / 2 + radius * Math.cos(angle), middle + radius * Math.sin(angle), 0] as const;
  });

  const goalBox = (edge: number, sign: 1 | -1, size: { deep: number; wide: number }): Vec3[] => [
    [edge, middle - size.wide / 2, 0],
    [edge + sign * size.deep, middle - size.wide / 2, 0],
    [edge + sign * size.deep, middle + size.wide / 2, 0],
    [edge, middle + size.wide / 2, 0],
  ];

  const reach = Math.acos((box.deep - spot) / radius);

  const lines = [
    outline([
      [0, 0, 0],
      [width, 0, 0],
      [width, height, 0],
      [0, height, 0],
    ]),
    trace([
      [width / 2, 0, 0],
      [width / 2, height, 0],
    ]),
    trace(circle),
    trace(goalBox(0, 1, box)),
    trace(goalBox(0, 1, small)),
    trace(goalBox(width, -1, box)),
    trace(goalBox(width, -1, small)),
    trace(arc(spot, reach, 1)),
    trace(arc(width - spot, reach, -1)),
  ];

  canvas.add({ role: 'marking', d: lines.join(''), stroke: PALETTE.line, width: 1, opacity: 0.85 });
}

/** A goal on one end line: its posts and bar, and the net behind. */
export function drawGoal(canvas: Canvas, end: 'west' | 'east'): void {
  const x = end === 'east' ? PITCH.width : 0;
  const back = end === 'east' ? x + 4.6 : x - 4.6;
  const left = PITCH.height / 2 - 5.6;
  const right = PITCH.height / 2 + 5.6;
  const high = 3.8;

  canvas.add({
    role: 'goal',
    d: outline([
      [x, left, high],
      [x, right, high],
      [back, right, 0.2],
      [back, left, 0.2],
    ]),
    fill: '#ffffff',
    opacity: 0.28,
  });
  canvas.add({
    role: 'goal',
    d:
      trace([
        [x, left, 0],
        [x, left, high],
        [x, right, high],
        [x, right, 0],
      ]) +
      trace([
        [x, left, high],
        [back, left, 0.2],
        [back, right, 0.2],
        [x, right, high],
      ]),
    stroke: '#ffffff',
    width: 1.4,
  });
}

/** The advertising boards round the pitch, in the club's two colours. */
export function drawBoards(canvas: Canvas, side: StandSide): void {
  const gap = 5;
  const high = 2.4;
  const length = isLong(side) ? PITCH.width + 2 * gap : PITCH.height + 2 * gap;
  const section = 20;
  const count = Math.ceil(length / section);

  const first: string[] = [];
  const second: string[] = [];

  for (let index = 0; index < count; index++) {
    const start = -gap + index * section;
    const finish = Math.min(start + section, length - gap);

    const quad = (fixed: number, long: boolean): Vec3[] =>
      long
        ? [
            [start, fixed, 0],
            [finish, fixed, 0],
            [finish, fixed, high],
            [start, fixed, high],
          ]
        : [
            [fixed, start, 0],
            [fixed, finish, 0],
            [fixed, finish, high],
            [fixed, start, high],
          ];

    const board =
      side === 'main'
        ? quad(-gap, true)
        : side === 'opposite'
          ? quad(PITCH.height + gap, true)
          : side === 'east'
            ? quad(PITCH.width + gap, false)
            : quad(-gap, false);

    (index % 2 === 0 ? first : second).push(outline(board));
  }

  canvas.add({ role: 'board', d: first.join(''), fill: 'primary' });
  canvas.add({ role: 'board', d: second.join(''), fill: 'secondary' });
}

/** The four corner flags. */
export function drawCornerFlags(canvas: Canvas): void {
  const corners: [number, number][] = [
    [0, 0],
    [PITCH.width, 0],
    [PITCH.width, PITCH.height],
    [0, PITCH.height],
  ];

  canvas.add({
    role: 'flag',
    d: corners
      .map(([x, y]) =>
        trace([
          [x, y, 0],
          [x, y, 5],
        ]),
      )
      .join(''),
    stroke: '#f2f5f8',
    width: 1,
  });
  canvas.add({
    role: 'flag',
    d: corners
      .map(([x, y]) =>
        outline([
          [x, y, 5],
          [x + 3, y + 1, 4.2],
          [x, y, 3.4],
        ]),
      )
      .join(''),
    fill: '#ffd84d',
  });
}

/** A floodlight mast: a tapering lattice with a bank of lamps, and its shadow on the grass. */
export function drawMast(canvas: Canvas, x: number, y: number): void {
  const [baseX, baseY] = project(x, y, 0);
  const [topX, topY] = project(x, y, MAST_HEIGHT);
  const [shadowX, shadowY] = project(x + 34, y + 22, 0);

  canvas.add({
    role: 'shadow',
    d: `M${round(baseX - 1.6)} ${baseY}L${shadowX} ${shadowY}L${round(shadowX + 2)} ${shadowY}L${round(baseX + 1.6)} ${baseY}Z`,
    fill: PALETTE.shadow,
    opacity: 0.22,
  });

  const legs: string[] = [
    `M${round(baseX - 3.4)} ${baseY}L${round(topX - 1.1)} ${round(topY + 6)}`,
    `M${round(baseX + 3.4)} ${baseY}L${round(topX + 1.1)} ${round(topY + 6)}`,
  ];

  const braces = 7;
  let previous: [number, number] = [baseX - 3.4, baseY];

  for (let step = 1; step <= braces; step++) {
    const share = step / braces;
    const half = 3.4 - 2.3 * share;
    const height = baseY + (topY + 6 - baseY) * share;
    const next: [number, number] = [baseX + (step % 2 === 0 ? -half : half), height];

    legs.push(`M${round(previous[0])} ${round(previous[1])}L${round(next[0])} ${round(next[1])}`);
    previous = next;
  }

  canvas.add({ role: 'mast', d: legs.join(''), stroke: PALETTE.mast, width: 1.1 });
  canvas.add({
    role: 'mast',
    d: `M${round(topX - 8)} ${round(topY - 1)}H${round(topX + 8)}V${round(topY + 7)}H${round(topX - 8)}Z`,
    fill: PALETTE.mastHead,
  });

  const lamps: string[] = [];

  for (let column = 0; column < 4; column++) {
    for (let row = 0; row < 2; row++) {
      lamps.push(`M${round(topX - 6.6 + column * 3.6)} ${round(topY + row * 3.4)}h2.2v2h-2.2z`);
    }
  }

  canvas.add({ role: 'lamp', d: lamps.join(''), fill: PALETTE.lamp });
}

/** The ground a level has built on, in world units. */
export interface Extent {
  readonly minX: number;
  readonly maxX: number;
  readonly minY: number;
  readonly maxY: number;
}

export function extentOf(level: number): Extent {
  const plan = planOf(level);
  const reach = (stand: StandPlan): number =>
    Math.max(depthOf(stand) + (stand.vip && stand.upper === 0 ? BOX_DEPTH : 0), 4);

  return {
    minX: -APRON - reach(plan.west),
    maxX: PITCH.width + APRON + reach(plan.east),
    minY: -APRON - reach(plan.main),
    maxY: PITCH.height + APRON + reach(plan.opposite),
  };
}

/** The box round a ground, grown by a margin. */
export function grow(extent: Extent, by: number): Extent {
  return {
    minX: extent.minX - by,
    maxX: extent.maxX + by,
    minY: extent.minY - by,
    maxY: extent.maxY + by,
  };
}

/** Where the floodlights stand: the corners of the compound first, then the middle of the touchlines. */
export function mastSites(extent: Extent, count: number): [number, number][] {
  const box = grow(extent, 8);
  const middle = (box.minX + box.maxX) / 2;

  const all: [number, number][] = [
    [box.minX, box.minY],
    [box.maxX, box.maxY],
    [box.maxX, box.minY],
    [box.minX, box.maxY],
    [middle, box.minY],
    [middle, box.maxY],
  ];

  return all.slice(0, count);
}

/** The sides of a rectangle, and whether the camera sees each from the inside (far) or from the outside (near). */
function sidesOf(box: Extent): Array<{
  readonly from: [number, number];
  readonly to: [number, number];
  readonly near: boolean;
  readonly normal: 'x' | 'y';
}> {
  return [
    { from: [box.minX, box.minY], to: [box.maxX, box.minY], near: false, normal: 'y' },
    { from: [box.maxX, box.minY], to: [box.maxX, box.maxY], near: false, normal: 'x' },
    { from: [box.minX, box.minY], to: [box.minX, box.maxY], near: true, normal: 'x' },
    { from: [box.minX, box.maxY], to: [box.maxX, box.maxY], near: true, normal: 'y' },
  ];
}

/** A fence, or the low wall of a big ground, on the far or the near sides of a rectangle. */
export function drawEnclosure(canvas: Canvas, box: Extent, near: boolean, wall: boolean): void {
  const lines: string[] = [];

  for (const side of sidesOf(box).filter((candidate) => candidate.near === near)) {
    const [x0, y0] = side.from;
    const [x1, y1] = side.to;

    if (wall) {
      canvas.add({
        role: 'wall',
        d: outline([
          [x0, y0, 0],
          [x1, y1, 0],
          [x1, y1, 4],
          [x0, y0, 4],
        ]),
        fill: side.normal === 'y' ? PALETTE.wallLit : PALETTE.dark,
      });
      continue;
    }

    const length = Math.hypot(x1 - x0, y1 - y0);
    const posts = Math.max(2, Math.round(length / 14));

    for (let post = 0; post <= posts; post++) {
      const x = x0 + ((x1 - x0) * post) / posts;
      const y = y0 + ((y1 - y0) * post) / posts;

      lines.push(
        trace([
          [x, y, 0],
          [x, y, 4.2],
        ]),
      );
    }

    lines.push(
      trace([
        [x0, y0, 4.2],
        [x1, y1, 4.2],
      ]),
      trace([
        [x0, y0, 2.2],
        [x1, y1, 2.2],
      ]),
    );
  }

  if (lines.length > 0) {
    canvas.add({ role: 'fence', d: lines.join(''), stroke: PALETTE.fence, width: 0.8 });
  }
}

/** A circle, as a path. */
function circle(x: number, y: number, radius: number): string {
  return ellipse(x, y, radius, radius);
}

/** An ellipse, as a path. */
function ellipse(x: number, y: number, radiusX: number, radiusY: number): string {
  return `M${round(x - radiusX)} ${round(y)}a${round(radiusX)} ${round(radiusY)} 0 1 0 ${round(radiusX * 2)} 0a${round(radiusX)} ${round(radiusY)} 0 1 0 ${round(-radiusX * 2)} 0Z`;
}

/** A number from 0 to 1 that is the same every time for the same pair, so the trees stand where they stood. */
function scatter(a: number, b: number): number {
  const value = Math.sin(a * 12.9898 + b * 78.233) * 43758.5453;

  return value - Math.floor(value);
}

/** The road runs along the far touchline of the picture. */
export const ROAD = { y: WORLD_FRAME.minY + 10, half: 8 } as const;

/** Where the trees grow: round the biggest ground, clear of the road and of any stand, far or near the camera. */
export function treeSites(): {
  readonly near: [number, number][];
  readonly far: [number, number][];
} {
  const keep = grow(extentOf(LEVELS), FENCE_GAP + RING_GAP + 6);
  const near: [number, number][] = [];
  const far: [number, number][] = [];

  for (let x = WORLD_FRAME.minX + 10; x <= WORLD_FRAME.maxX - 6; x += 24) {
    for (let y = WORLD_FRAME.minY + 10; y <= WORLD_FRAME.maxY - 6; y += 24) {
      const px = x + (scatter(x, y) - 0.5) * 14;
      const py = y + (scatter(y, x) - 0.5) * 14;

      const inside = px > keep.minX && px < keep.maxX && py > keep.minY && py < keep.maxY;
      const onRoad = Math.abs(py - ROAD.y) < ROAD.half + 14;

      if (inside || onRoad || scatter(x * 3, y * 7) > 0.62) {
        continue;
      }

      (nearness(px, py) < 0 ? far : near).push([px, py]);
    }
  }

  return { near, far };
}

/** Draws a set of trees, the far ones first. */
export function drawTrees(canvas: Canvas, sites: readonly [number, number][]): void {
  const ordered = [...sites].sort((a, b) => nearness(a[0], a[1]) - nearness(b[0], b[1]));

  const shadows: string[] = [];
  const trunks: string[] = [];
  const dark: string[] = [];
  const light: string[] = [];

  for (const [x, y] of ordered) {
    const [sx, sy] = project(x, y, 0);
    const size = 8 + scatter(x, y) * 4;

    shadows.push(ellipse(sx + size * 0.6, sy + 1, size * 1.4, size * 0.5));
    trunks.push(`M${round(sx - 1.3)} ${sy}h2.6v-6h-2.6z`);
    dark.push(
      circle(sx, sy - 12, size),
      circle(sx - size * 0.5, sy - 8, size * 0.72),
      circle(sx + size * 0.55, sy - 8, size * 0.7),
    );
    light.push(
      circle(sx - size * 0.2, sy - 14, size * 0.62),
      circle(sx + size * 0.3, sy - 10.5, size * 0.42),
    );
  }

  canvas.add({ role: 'shadow', d: shadows.join(''), fill: PALETTE.shadow, opacity: 0.2 });
  canvas.add({ role: 'trunk', d: trunks.join(''), fill: PALETTE.trunk });
  canvas.add({ role: 'canopy', d: dark.join(''), fill: PALETTE.canopy });
  canvas.add({ role: 'canopy', d: light.join(''), fill: PALETTE.canopyLight });
}

/** The road along the top of the picture, and the track that leads from it to the gate of the ground. */
export function drawRoads(canvas: Canvas, extent: Extent): void {
  const left = WORLD_FRAME.minX - 20;
  const right = WORLD_FRAME.maxX + 20;

  canvas.add({
    role: 'road',
    d: ground([
      [left, ROAD.y - ROAD.half],
      [right, ROAD.y - ROAD.half],
      [right, ROAD.y + ROAD.half],
      [left, ROAD.y + ROAD.half],
    ]),
    fill: PALETTE.road,
  });

  const dashes: string[] = [];

  for (let x = WORLD_FRAME.minX; x < WORLD_FRAME.maxX; x += 22) {
    dashes.push(
      ground([
        [x, ROAD.y - 0.6],
        [x + 11, ROAD.y - 0.6],
        [x + 11, ROAD.y + 0.6],
        [x, ROAD.y + 0.6],
      ]),
    );
  }

  canvas.add({ role: 'road', d: dashes.join(''), fill: PALETTE.roadLine });

  const gateX = extent.minX - 8;
  const gateY = extent.maxY + 10;

  canvas.add({
    role: 'path',
    d: ground([
      [WORLD_FRAME.minX + 24, ROAD.y + ROAD.half],
      [WORLD_FRAME.minX + 34, ROAD.y + ROAD.half],
      [gateX + 6, gateY],
      [gateX - 4, gateY],
    ]),
    fill: PALETTE.path,
    opacity: 0.9,
  });
}

/** The club's flag over the main stand's roof, for the biggest ground. */
export function drawFlag(canvas: Canvas, extent: Extent): void {
  const x = (extent.minX + extent.maxX) / 2;
  const y = -APRON - ROW_DEPTH * 3;
  const base = heightOf(planOf(LEVELS).main) + ROOF_CLEARANCE;

  canvas.add({
    role: 'flag',
    d: trace([
      [x, y, base],
      [x, y, base + 24],
    ]),
    stroke: PALETTE.flagPole,
    width: 1.4,
  });
  canvas.add({
    role: 'flag',
    d: outline([
      [x, y, base + 24],
      [x + 16, y, base + 24],
      [x + 16, y, base + 15],
      [x, y, base + 15],
    ]),
    fill: 'primary',
  });
  canvas.add({
    role: 'flag',
    d: outline([
      [x, y, base + 21],
      [x + 16, y, base + 21],
      [x + 16, y, base + 18],
      [x, y, base + 18],
    ]),
    fill: 'secondary',
  });
}
