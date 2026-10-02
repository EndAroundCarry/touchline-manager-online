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
  readonly participantId: string | null;
  readonly shirtNumber: number;
  readonly family: string | null;
  readonly name: string | null;
  readonly anchor: PitchPoint;
}

/** One entity's movement, as the keyframes the renderer interpolates between. */
export interface RendererTrack {
  readonly entityId: string;
  readonly keyframes: readonly HighlightKeyframe[];
}

/**
 * One entity at one moment: where it is now, how high the ball is, and what it is doing.
 *
 * The position is the entity's *ground* position — for the ball that is where its shadow sits — so the
 * renderer can lift the ball off the shadow by the altitude without the interpolator knowing anything about
 * pixels or perspective.
 */
export interface FrameEntity {
  readonly entity: RendererEntity;
  readonly position: PitchPoint;
  readonly z: number;
  readonly speed: number;
  readonly action: string | null;
}

/** A kit's two colours, from the safe generated palettes the engine chose. */
export interface TeamKit {
  readonly primary: string;
  readonly secondary: string;
}

/** Both sides' kits. */
export interface TeamKits {
  readonly home: TeamKit;
  readonly away: TeamKit;
}

/** A card a player carries into the passage being drawn. */
export type CardKind = 'yellow' | 'red';

/** How much the renderer drew in the last frame, which the payload and performance budgets read. */
export interface FrameMetrics {
  readonly entities: number;
  readonly interpolated: number;
  readonly effects: number;
  readonly milliseconds: number;
}

/** One duel the renderer marks, at the point between the two players contesting it. */
export interface ClashPoint {
  readonly x: number;
  readonly y: number;
  /** A per-duel offset so two duels do not pulse in lockstep. */
  readonly phase: number;
}

/** What the renderer needs beyond the highlight itself. */
export interface RendererOptions {
  readonly kits?: Partial<TeamKits>;
  /** Cards by participant: what the player carries at the minute being drawn. */
  readonly cards?: ReadonlyMap<string, CardKind>;
  /** When set, a paused frame is drawn without its time-based pulses, for `prefers-reduced-motion`. */
  readonly reducedMotion?: boolean;
}
