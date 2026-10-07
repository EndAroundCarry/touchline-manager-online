/**
 * The camera of the stadium picture (`STAD-1`).
 *
 * The picture is a three-dimensional ground drawn from one fixed corner, the way a management game shows it: the
 * pitch lies on the ground plane, stands rise from it, and every point is carried to the screen by {@link project}.
 * Pure and framework-free, so the whole drawing can be tested without a DOM.
 *
 * World axes: `x` runs along the pitch from the west goal to the east goal, `y` across it from the main-stand
 * touchline to the opposite one, `z` is height. One world unit is about two thirds of a metre. The camera stands
 * beyond the west goal and the opposite touchline, so the main stand and the east end face it and the other two are
 * seen from behind.
 */

/** A point of the world: `[x, y, z]`. */
export type Vec3 = readonly [number, number, number];

/** The drawing's width, in the units of its `viewBox`. */
export const VIEW_WIDTH = 960;

/** The drawing's height. */
export const VIEW_HEIGHT = 620;

/** The camera is turned this far about the vertical axis. */
const YAW = (33 * Math.PI) / 180;

/** The camera looks down at the ground at this angle, 90 degrees being straight down. */
const ELEVATION = (50 * Math.PI) / 180;

const COS_YAW = Math.cos(YAW);
const SIN_YAW = Math.sin(YAW);
const COS_ELEVATION = Math.cos(ELEVATION);
const SIN_ELEVATION = Math.sin(ELEVATION);

/** The part of the world the picture frames: the biggest ground and the grass around it. */
export const WORLD_FRAME = {
  minX: -140,
  maxX: 300,
  minY: -134,
  maxY: 248,
  maxZ: 70,
} as const;

/** The clear border kept inside the drawing, in its own units. */
const MARGIN = 14;

/** Where a world point lands before the picture is scaled to fit. */
function flat(x: number, y: number, z: number): [number, number] {
  const across = x * COS_YAW + y * SIN_YAW;
  const toward = -x * SIN_YAW + y * COS_YAW;

  return [across, toward * SIN_ELEVATION - z * COS_ELEVATION];
}

/** Fits the framed world into the drawing: one scale, and where the origin lands. */
function fit(): { readonly scale: number; readonly originX: number; readonly originY: number } {
  const corners: [number, number][] = [];

  for (const x of [WORLD_FRAME.minX, WORLD_FRAME.maxX]) {
    for (const y of [WORLD_FRAME.minY, WORLD_FRAME.maxY]) {
      corners.push(flat(x, y, 0), flat(x, y, WORLD_FRAME.maxZ));
    }
  }

  const xs = corners.map(([x]) => x);
  const ys = corners.map(([, y]) => y);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);

  const scale = Math.min(
    (VIEW_WIDTH - 2 * MARGIN) / (maxX - minX),
    (VIEW_HEIGHT - 2 * MARGIN) / (maxY - minY),
  );

  return {
    scale,
    originX: (VIEW_WIDTH - (maxX - minX) * scale) / 2 - minX * scale,
    originY: (VIEW_HEIGHT - (maxY - minY) * scale) / 2 - minY * scale,
  };
}

const FIT = fit();

/** How many drawing units one world unit is, before foreshortening. */
export const SCALE = FIT.scale;

/** Carries a world point to the drawing, to a tenth of a unit. */
export function project(x: number, y: number, z: number): [number, number] {
  const [screenX, screenY] = flat(x, y, z);

  return [round(FIT.originX + screenX * SCALE), round(FIT.originY + screenY * SCALE)];
}

/** How long one world unit looks along an axis of the ground, so a dash can be sized on the screen. */
export function unitLength(axis: 'x' | 'y'): number {
  const [a, b] = axis === 'x' ? flat(1, 0, 0) : flat(0, 1, 0);

  return Math.hypot(a, b) * SCALE;
}

/**
 * How far apart two lines one world unit apart look, when both run along `axis` of the ground: the thickness a row of
 * seats can have on the screen before it touches the next.
 */
export function spacing(axis: 'x' | 'y'): number {
  const [ax, ay] = axis === 'x' ? flat(1, 0, 0) : flat(0, 1, 0);
  const [bx, by] = axis === 'x' ? flat(0, 1, 0) : flat(1, 0, 0);

  return (Math.abs(ax * by - ay * bx) / Math.hypot(ax, ay)) * SCALE;
}

/**
 * Whether a face whose outward normal is `(nx, ny, nz)` is turned toward the camera, so a face that is not is never
 * drawn.
 */
export function facesCamera(nx: number, ny: number, nz: number): boolean {
  const towardX = -SIN_YAW * COS_ELEVATION;
  const towardY = COS_YAW * COS_ELEVATION;

  return nx * towardX + ny * towardY + nz * SIN_ELEVATION > 1e-9;
}

/** How near to the camera a ground point is: larger is nearer, and is the order things are drawn in. */
export function nearness(x: number, y: number): number {
  return -x * SIN_YAW + y * COS_YAW;
}

/** A closed outline through world points, as an SVG path. */
export function outline(points: readonly Vec3[]): string {
  return `${trace(points)}Z`;
}

/** An open line through world points, as an SVG path. */
export function trace(points: readonly Vec3[]): string {
  return points
    .map(([x, y, z], index) => {
      const [screenX, screenY] = project(x, y, z);

      return `${index === 0 ? 'M' : 'L'}${screenX} ${screenY}`;
    })
    .join('');
}

/** Rounds to a tenth, which is finer than a screen can show and keeps the paths short. */
export function round(value: number): number {
  return Math.round(value * 10) / 10;
}
