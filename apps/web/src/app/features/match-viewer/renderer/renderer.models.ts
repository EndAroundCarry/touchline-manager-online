import { HighlightKeyframe } from '../../../core/match/match.models';

/** Which end an entity belongs to. */
export type MatchSide = 'home' | 'away';

/** One position on the pitch, normalized to 0…10,000 in each direction. */
export interface PitchPoint {
  readonly x: number;
  readonly y: number;
}

/** A rectangle in CSS pixels. */
export interface PitchRect {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** One entity the renderer draws: a player or the ball. */
export interface RendererEntity {
  readonly id: string;
  readonly isBall: boolean;
  readonly side: MatchSide | null;
  readonly shirtNumber: number;
  readonly family: string | null;
  readonly anchor: PitchPoint;
}

/** One entity's movement, as the keyframes the renderer interpolates between. */
export interface RendererTrack {
  readonly entityId: string;
  readonly keyframes: readonly HighlightKeyframe[];
}

/** One entity at one moment: where it is now, ready to draw. */
export interface FrameEntity {
  readonly entity: RendererEntity;
  readonly position: PitchPoint;
}

/** The two sides' colours, from the safe generated palettes the engine chose. */
export interface TeamColours {
  readonly home: string;
  readonly away: string;
}

/** How much the renderer drew in the last frame, which the payload and performance budgets read. */
export interface FrameMetrics {
  readonly entities: number;
  readonly interpolated: number;
}
