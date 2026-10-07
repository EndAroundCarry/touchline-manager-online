/**
 * The drawing of the stadium at one level (`STAD-1`).
 *
 * {@link buildScene} turns a level's plan into an ordered list of shapes, far to near, which a component paints as it
 * is. The list is the whole picture: the grass, the pitch, every stand with its rows, sectors and seats, the roofs,
 * the hospitality boxes, the floodlights and the trees. Nothing is a bitmap, so the seats can take the club's own two
 * colours, and nothing here knows about the DOM, so each of the ten levels can be tested as plain data.
 *
 * The parts live beside this file: `stadium-camera` projects the world, `stadium-plan` is the table of levels,
 * `stadium-stand` draws a stand, `stadium-grounds` draws everything round the stands and `stadium-canvas` is what a
 * drawing is made of.
 */
import { VIEW_HEIGHT, VIEW_WIDTH, WORLD_FRAME, nearness, project } from './stadium-camera';
import { Canvas, PALETTE, SceneDetail, ScenePart } from './stadium-canvas';
import {
  Extent,
  FENCE_GAP,
  MAST_HEIGHT,
  RING_GAP,
  ROAD,
  drawBoards,
  drawCornerFlags,
  drawEnclosure,
  drawFlag,
  drawGoal,
  drawMast,
  drawPitch,
  drawRoads,
  drawTrees,
  extentOf,
  ground,
  grow,
  mastSites,
  treeSites,
} from './stadium-grounds';
import {
  BAND_HEIGHT,
  LEVELS,
  PITCH,
  StandSide,
  clampLevel,
  depthOf,
  heightOf,
  kindsOf,
  planOf,
  spansOf,
} from './stadium-plan';
import { ROOF_CLEARANCE, drawStand, frameOf } from './stadium-stand';
import { StadiumStandCode } from './stadium.models';

export { FALLBACK_COLOUR, safeColour } from './stadium-canvas';
export type { PartRole, SceneDetail, ScenePart } from './stadium-canvas';
export { clampLevel } from './stadium-plan';

/** The part of the drawing that is shown, in the drawing's own units. */
export interface SceneView {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** A name written beside a stand. */
export interface SceneLabel {
  readonly text: string;
  readonly x: number;
  readonly y: number;
}

/** The drawing of one level. */
export interface Scene {
  readonly level: number;
  readonly detail: SceneDetail;
  /** The shapes, far to near. */
  readonly parts: readonly ScenePart[];
  /** The part of the drawing that holds this level's ground, so a small ground is not lost in a big frame. */
  readonly view: SceneView;
  /** The names of the stands that are built. */
  readonly labels: readonly SceneLabel[];
  /** Which kinds of place the ground has, in the order the screen lists them. */
  readonly regions: readonly StadiumStandCode[];
}

/** How many levels there are to draw. */
export const SCENE_LEVELS = LEVELS;

/** What each stand is called, written beside it. */
const STAND_NAMES: Readonly<Record<StandSide, string>> = {
  main: 'Main stand',
  opposite: 'Opposite stand',
  east: 'East end',
  west: 'West end',
};

/** The room kept round a ground in its frame, in the drawing's units. */
const VIEW_PADDING = 30;

const TREES = treeSites();
const CACHE = new Map<string, Scene>();

/**
 * Frames a level: the smallest part of the drawing, in the drawing's own proportions, that holds its fenced ground
 * and everything that stands over it. The biggest ground fills the whole drawing; a smaller one fills more of its
 * frame, which is what keeps the first level from being a small ground lost in a big field.
 */
function viewOf(level: number, extent: Extent): SceneView {
  const plan = planOf(level);
  const compound = grow(extent, FENCE_GAP + (plan.ring ? RING_GAP : 0));

  const stands = [plan.main, plan.opposite, plan.east, plan.west];
  const tallest = Math.max(...stands.map((stand) => heightOf(stand) + BAND_HEIGHT));
  const high = Math.max(
    tallest + ROOF_CLEARANCE + (plan.flag ? 28 : 6),
    plan.masts > 0 ? MAST_HEIGHT + 6 : 0,
  );

  const points = [compound.minX, compound.maxX].flatMap((x) =>
    [compound.minY, compound.maxY].flatMap((y) => [project(x, y, 0), project(x, y, high)]),
  );

  const xs = points.map(([x]) => x);
  const ys = points.map(([, y]) => y);
  const minX = Math.min(...xs) - VIEW_PADDING;
  const maxX = Math.max(...xs) + VIEW_PADDING;
  const minY = Math.min(...ys) - VIEW_PADDING;
  const maxY = Math.max(...ys) + VIEW_PADDING;

  const ratio = VIEW_WIDTH / VIEW_HEIGHT;
  const width = Math.min(Math.max(maxX - minX, (maxY - minY) * ratio), VIEW_WIDTH);
  const height = width / ratio;

  return {
    x:
      Math.round(Math.min(Math.max((minX + maxX) / 2 - width / 2, 0), VIEW_WIDTH - width) * 10) /
      10,
    y:
      Math.round(Math.min(Math.max((minY + maxY) / 2 - height / 2, 0), VIEW_HEIGHT - height) * 10) /
      10,
    width: Math.round(width * 10) / 10,
    height: Math.round(height * 10) / 10,
  };
}

/** Where the names of the built stands go: on the outside of each, clear of its roof. */
function labelsOf(level: number): SceneLabel[] {
  return spansOf(planOf(level)).map((span) => {
    const at = frameOf(span.side);
    const deep = depthOf(span.plan);
    // A floodlight stands in the middle of each long side, so its name goes a little along from it.
    const middle =
      span.side === 'main' || span.side === 'opposite'
        ? span.from + (span.to - span.from) * 0.3
        : (span.from + span.to) / 2;

    // The two stands the camera sees from the front are named above their roofs, the two it sees from behind in
    // front of their walls.
    const [x, y] =
      span.side === 'main' || span.side === 'east'
        ? project(...at(middle, deep + 6, heightOf(span.plan) + ROOF_CLEARANCE + 6))
        : project(...at(middle, deep + 12, 0));

    return { text: STAND_NAMES[span.side], x, y };
  });
}

/**
 * Builds the drawing of one level.
 *
 * @param level The stadium level, 1 to 10. Anything else is clamped, so a response the screen does not yet know about
 *   still draws something rather than nothing.
 * @param detail `full` draws every row; `low` groups rows in threes, for a thumbnail.
 */
export function buildScene(level: number, detail: SceneDetail = 'full'): Scene {
  const clamped = clampLevel(level);
  const key = `${clamped}:${detail}`;
  const cached = CACHE.get(key);

  if (cached !== undefined) {
    return cached;
  }

  const plan = planOf(clamped);
  const extent = extentOf(clamped);
  const compound = grow(extent, plan.ring ? FENCE_GAP + RING_GAP : FENCE_GAP);
  const canvas = new Canvas();
  const spans = spansOf(plan);

  canvas.add({ role: 'ground', d: `M0 0H${VIEW_WIDTH}V${VIEW_HEIGHT}H0Z`, fill: PALETTE.grass });
  canvas.add({
    role: 'ground',
    d: ground([
      [WORLD_FRAME.minX, ROAD.y + 18],
      [WORLD_FRAME.maxX, ROAD.y + 18],
      [WORLD_FRAME.maxX, WORLD_FRAME.maxY],
      [WORLD_FRAME.minX, WORLD_FRAME.maxY],
    ]),
    fill: PALETTE.grassDark,
    opacity: 0.55,
  });

  drawRoads(canvas, extent);
  drawTrees(canvas, TREES.far);

  if (plan.ring) {
    const paved = grow(extent, RING_GAP);

    canvas.add({
      role: 'plaza',
      d: ground([
        [paved.minX, paved.minY],
        [paved.maxX, paved.minY],
        [paved.maxX, paved.maxY],
        [paved.minX, paved.maxY],
      ]),
      fill: PALETTE.plaza,
    });
  }

  drawEnclosure(canvas, compound, false, false);

  // A mast is drawn with the far stands or the near ones, by where it stands.
  const masts = mastSites(extent, plan.masts);
  const middle = nearness(PITCH.width / 2, PITCH.height / 2);

  for (const [x, y] of masts.filter(([mx, my]) => nearness(mx, my) < middle - 40)) {
    drawMast(canvas, x, y);
  }

  drawPitch(canvas);

  const stand = (side: StandSide): void => {
    const span = spans.find((candidate) => candidate.side === side);

    if (span !== undefined) {
      drawStand(canvas, span, detail);
    }
  };

  // Far to near: what the camera sees past is drawn before what it sees over.
  stand('main');
  stand('east');
  drawBoards(canvas, 'main');
  drawBoards(canvas, 'east');
  drawGoal(canvas, 'east');
  drawGoal(canvas, 'west');
  drawCornerFlags(canvas);
  drawBoards(canvas, 'west');
  drawBoards(canvas, 'opposite');
  stand('west');
  stand('opposite');

  for (const [x, y] of masts.filter(([mx, my]) => nearness(mx, my) >= middle - 40)) {
    drawMast(canvas, x, y);
  }

  if (plan.flag) {
    drawFlag(canvas, extent);
  }

  if (plan.ring) {
    drawEnclosure(canvas, grow(extent, RING_GAP), true, true);
  }

  drawEnclosure(canvas, compound, true, false);
  drawTrees(canvas, TREES.near);

  const scene: Scene = {
    level: clamped,
    detail,
    parts: canvas.parts,
    view: viewOf(clamped, extent),
    labels: labelsOf(clamped),
    regions: kindsOf(clamped),
  };

  CACHE.set(key, scene);

  return scene;
}
