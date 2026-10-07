/**
 * What the stadium drawing is made of (`STAD-1`).
 *
 * A drawing is an ordered list of shapes, and a shape names its colour rather than choosing it: `primary` and
 * `secondary` are the club's two colours, and the component paints them in. That keeps one drawing per level whatever
 * the club. Pure and framework-free.
 */
import { StadiumStandCode } from './stadium.models';

/** How much of the drawing to build: the thumbnails of the level gallery are small and do not need every row. */
export type SceneDetail = 'full' | 'low';

/** What a shape is, so it can be tested and styled by what it is rather than by where it sits in the list. */
export type PartRole =
  | 'ground'
  | 'road'
  | 'path'
  | 'plaza'
  | 'shadow'
  | 'fence'
  | 'wall'
  | 'trunk'
  | 'canopy'
  | 'pitch'
  | 'mowing'
  | 'marking'
  | 'board'
  | 'goal'
  | 'flag'
  | 'tread'
  | 'riser'
  | 'aisle'
  | 'side'
  | 'seat'
  | 'seat-alt'
  | 'crowd'
  | 'barrier'
  | 'vip-wall'
  | 'vip-glass'
  | 'vip-frame'
  | 'vip-top'
  | 'walkway'
  | 'window'
  | 'roof'
  | 'roof-trim'
  | 'roof-rib'
  | 'pillar'
  | 'mast'
  | 'lamp'
  | 'region';

/** One shape of the drawing. A paint is `primary`, `secondary`, or a plain `#rrggbb`. */
export interface ScenePart {
  readonly role: PartRole;
  readonly d: string;
  readonly fill: string | null;
  readonly stroke: string | null;
  readonly width: number;
  readonly dash: string | null;
  readonly offset: number;
  readonly opacity: number;
  /** The kind of place this shape is part of, when it is one a manager can build. */
  readonly region: StadiumStandCode | null;
}

/** The colours that do not belong to the club. */
export const PALETTE = {
  grass: '#5f9b3c',
  grassDark: '#569236',
  road: '#4d5560',
  roadLine: '#d9dde2',
  path: '#c9a06a',
  plaza: '#9ea6ae',
  pitch: '#74b83f',
  pitchDark: '#69ab39',
  line: '#f3f8ec',
  treadTop: '#b3bbc4',
  lit: '#8e98a3',
  dark: '#727c87',
  aisle: '#cdd3d9',
  walkway: '#bcc4cc',
  crowd: '#d6dce3',
  barrier: '#555f6a',
  boxWall: '#2d3842',
  glass: '#6fa5c9',
  roof: '#d5dbe2',
  roofRib: '#aeb7c1',
  pillar: '#5d6772',
  mast: '#6b7580',
  mastHead: '#2c333b',
  lamp: '#fff3b8',
  fence: '#8d98a4',
  wallLit: '#c4cad1',
  trunk: '#6b4a2f',
  canopy: '#3f7d34',
  canopyLight: '#5c9c43',
  shadow: '#16240f',
  highlight: '#ffe066',
  flagPole: '#e9edf1',
} as const;

/** Describes a shape to add to the drawing. */
export interface Draw {
  readonly role: PartRole;
  readonly d: string;
  readonly fill?: string;
  readonly stroke?: string;
  readonly width?: number;
  readonly dash?: string;
  readonly offset?: number;
  readonly opacity?: number;
  readonly region?: StadiumStandCode;
}

/** The list a drawing is collected in, in the order it is painted. */
export class Canvas {
  readonly parts: ScenePart[] = [];

  add(draw: Draw): void {
    this.parts.push({
      role: draw.role,
      d: draw.d,
      fill: draw.fill ?? null,
      stroke: draw.stroke ?? null,
      width: draw.width ?? 0,
      dash: draw.dash ?? null,
      offset: draw.offset ?? 0,
      opacity: draw.opacity ?? 1,
      region: draw.region ?? null,
    });
  }
}

/** The colour used when the server sends none, or one that is not a plain hex colour. */
export const FALLBACK_COLOUR = '#1f4e79';

/** Accepts only a plain `#rrggbb` colour, so nothing else can reach a drawing attribute. */
export function safeColour(value: string | null | undefined, fallback = FALLBACK_COLOUR): string {
  return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value)
    ? value.toLowerCase()
    : fallback;
}
