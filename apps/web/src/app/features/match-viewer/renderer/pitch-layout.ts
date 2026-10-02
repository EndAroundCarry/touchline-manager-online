import { PitchPoint, PitchRect } from './renderer.models';

/**
 * Where the pitch is inside a canvas, how a normalized position maps onto it, and where its markings go.
 *
 * The pitch keeps its real proportions rather than filling the container, because a pitch stretched to a
 * phone's shape would put a shot from the byline in the wrong place relative to the goal — and the whole
 * point of normalized coordinates is that the geometry means the same thing on every screen (`§9.3`, `§9.4`).
 *
 * Every marking is derived from the laws of the game in metres (a 105 × 68 m pitch, a 16.5 m penalty area,
 * a 9.15 m circle) and scaled onto the drawn rectangle here, so the renderer draws a football pitch rather
 * than a set of boxes that happened to look right at one size.
 */

/** A pitch's real proportions — 105 metres by 68 — so the drawn shape is a football pitch. */
export const PITCH_ASPECT_RATIO = 105 / 68;

/** The normalized coordinate scale the engine uses. */
export const PITCH_COORDINATE_SCALE = 10_000;

/** The pitch's real dimensions, in metres. */
export const PITCH_LENGTH_METRES = 105;
export const PITCH_WIDTH_METRES = 68;

/** How much of the shorter axis is left as margin, so the goal lines are not on the canvas edge. */
const PITCH_INSET = 0.05;

/** Fits the largest pitch of the right proportions inside a canvas. */
export function pitchRect(width: number, height: number): PitchRect {
  const availableWidth = Math.max(0, width * (1 - PITCH_INSET * 2));
  const availableHeight = Math.max(0, height * (1 - PITCH_INSET * 2));

  let pitchWidth = availableWidth;
  let pitchHeight = pitchWidth / PITCH_ASPECT_RATIO;

  if (pitchHeight > availableHeight) {
    pitchHeight = availableHeight;
    pitchWidth = pitchHeight * PITCH_ASPECT_RATIO;
  }

  return {
    x: (width - pitchWidth) / 2,
    y: (height - pitchHeight) / 2,
    width: pitchWidth,
    height: pitchHeight,
  };
}

/** Maps a normalized position onto canvas pixels. */
export function toCanvasPoint(point: PitchPoint, rect: PitchRect): PitchPoint {
  return {
    x: rect.x + (point.x / PITCH_COORDINATE_SCALE) * rect.width,
    y: rect.y + (point.y / PITCH_COORDINATE_SCALE) * rect.height,
  };
}

/** Whether a normalized position is inside the pitch, which a stored track always should be. */
export function isOnPitch(point: PitchPoint): boolean {
  return (
    point.x >= 0 &&
    point.x <= PITCH_COORDINATE_SCALE &&
    point.y >= 0 &&
    point.y <= PITCH_COORDINATE_SCALE
  );
}

/** Every marking the pitch is drawn with, measured from the rectangle it is drawn in. */
export interface PitchGeometry {
  readonly centreX: number;
  readonly centreY: number;
  readonly centreCircleRadius: number;
  readonly centreSpotRadius: number;
  readonly penaltyBoxDepth: number;
  readonly penaltyBoxHeight: number;
  readonly sixYardBoxDepth: number;
  readonly sixYardBoxHeight: number;
  readonly penaltySpotInset: number;
  readonly penaltySpotRadius: number;
  readonly goalWidth: number;
  readonly goalDepth: number;
  readonly cornerArcRadius: number;
  readonly stripeWidth: number;
  readonly stripeCount: number;
  readonly lineWidth: number;
}

/** The markings of a real pitch, in metres, as the laws of the game give them. */
const PITCH_METRES = {
  centreCircleRadius: 9.15,
  centreSpotRadius: 0.2,
  penaltyBoxDepth: 16.5,
  penaltyBoxHeight: 40.32,
  sixYardBoxDepth: 5.5,
  sixYardBoxHeight: 18.32,
  penaltySpotInset: 11,
  penaltySpotRadius: 0.2,
  goalWidth: 7.32,
  goalDepth: 2,
  cornerArcRadius: 1,
} as const;

/** How many cut stripes the grass is mown in. */
const STRIPE_COUNT = 10;

/** Scales a distance along the pitch's length onto the drawn rectangle. */
export function metresAcrossPitch(rect: PitchRect, metres: number): number {
  return (metres / PITCH_LENGTH_METRES) * rect.width;
}

/** Scales a distance along the pitch's width onto the drawn rectangle. */
export function metresDownPitch(rect: PitchRect, metres: number): number {
  return (metres / PITCH_WIDTH_METRES) * rect.height;
}

/** Every marking's size for a drawn pitch rectangle. */
export function pitchGeometry(rect: PitchRect): PitchGeometry {
  return {
    centreX: rect.x + rect.width / 2,
    centreY: rect.y + rect.height / 2,
    centreCircleRadius: metresDownPitch(rect, PITCH_METRES.centreCircleRadius),
    centreSpotRadius: Math.max(1.5, metresDownPitch(rect, PITCH_METRES.centreSpotRadius)),
    penaltyBoxDepth: metresAcrossPitch(rect, PITCH_METRES.penaltyBoxDepth),
    penaltyBoxHeight: metresDownPitch(rect, PITCH_METRES.penaltyBoxHeight),
    sixYardBoxDepth: metresAcrossPitch(rect, PITCH_METRES.sixYardBoxDepth),
    sixYardBoxHeight: metresDownPitch(rect, PITCH_METRES.sixYardBoxHeight),
    penaltySpotInset: metresAcrossPitch(rect, PITCH_METRES.penaltySpotInset),
    penaltySpotRadius: Math.max(1.5, metresDownPitch(rect, PITCH_METRES.penaltySpotRadius)),
    goalWidth: metresDownPitch(rect, PITCH_METRES.goalWidth),
    goalDepth: metresAcrossPitch(rect, PITCH_METRES.goalDepth),
    cornerArcRadius: metresDownPitch(rect, PITCH_METRES.cornerArcRadius),
    stripeWidth: rect.width / STRIPE_COUNT,
    stripeCount: STRIPE_COUNT,
    lineWidth: Math.max(1, rect.height / 220),
  };
}

/**
 * How far above its shadow a ball at an altitude is drawn, in CSS pixels (Stage 6).
 *
 * The plan's altitude band is 0…100, and the lift is a share of the pitch's own height rather than a fixed
 * number of pixels, so a cross climbs the same fraction of the pitch on a phone as on a desktop.
 */
export function altitudeLift(rect: PitchRect, z: number): number {
  return (clampAltitude(z) / 100) * rect.height * 0.3;
}

/** How much a ball's token grows as it climbs, so height reads as size as well as offset. */
export function altitudeScale(z: number): number {
  return 1 + (clampAltitude(z) / 100) * 0.9;
}

function clampAltitude(z: number): number {
  return Number.isFinite(z) ? Math.min(100, Math.max(0, z)) : 0;
}
