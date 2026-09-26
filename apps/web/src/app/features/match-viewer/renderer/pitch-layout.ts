import { PitchPoint, PitchRect } from './renderer.models';

/**
 * Where the pitch is inside a canvas, and how a normalized position maps onto it.
 *
 * The pitch keeps its real proportions rather than filling the container, because a pitch stretched to a
 * phone's shape would put a shot from the byline in the wrong place relative to the goal — and the whole
 * point of normalized coordinates is that the geometry means the same thing on every screen (`§9.3`, `§9.4`).
 */

/** A pitch's real proportions — 105 metres by 68 — so the drawn shape is a football pitch. */
export const PITCH_ASPECT_RATIO = 105 / 68;

/** The normalized coordinate scale the engine uses. */
export const PITCH_COORDINATE_SCALE = 10_000;

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
