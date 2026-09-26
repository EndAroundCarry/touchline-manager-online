/**
 * Transport shapes for the match center (master plan §9.5, §11.1).
 *
 * Hand-written mirrors of `TouchlineManager.Contracts.Match`, as the rest of the app's shapes are. Every
 * field is `readonly`: a response is a fact the server produced, and the screen never edits one — the
 * replay is played, not changed.
 */

/** One side's statistics, as the engine derived them from the event stream (`MAT-5`). */
export interface MatchStatistics {
  readonly possessionBasisPoints: number;
  readonly goals: number;
  readonly shots: number;
  readonly shotsOnTarget: number;
  readonly shotsOffTarget: number;
  readonly shotsBlocked: number;
  readonly woodworkHits: number;
  readonly saves: number;
  readonly corners: number;
  readonly offsides: number;
  readonly fouls: number;
  readonly yellowCards: number;
  readonly redCards: number;
  readonly penaltiesAwarded: number;
  readonly penaltiesScored: number;
  readonly injuries: number;
  readonly substitutions: number;
}

/** One side of a played match: who it was, the score, and what it did. */
export interface MatchTeam {
  readonly clubId: string;
  readonly name: string;
  readonly shortName: string;
  readonly goals: number;
  readonly statistics: MatchStatistics;
}

/** A played match's summary. */
export interface Match {
  readonly matchId: string;
  readonly fixtureId: string;
  readonly divisionId: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly countryId: string;
  readonly countryCode: string;
  readonly countryName: string;
  readonly seasonNumber: number;
  readonly seasonLabel: string;
  readonly roundNumber: number;
  readonly kickoffAt: string;
  readonly status: string;
  readonly home: MatchTeam;
  readonly away: MatchTeam;
  readonly engineVersion: string;
  readonly presentationVersion: string;
  readonly serverTime: string;
}

/** One named fact a commentary line was built from. */
export interface CommentaryParameter {
  readonly name: string;
  readonly value: string;
}

/** One line of commentary: where it came from, the facts, and the text. */
export interface CommentaryLine {
  readonly sequence: number;
  readonly minute: number;
  readonly stoppageMinute: number;
  readonly side: string;
  readonly templateKey: string;
  readonly variantKey: string;
  readonly parameters: readonly CommentaryParameter[];
  readonly text: string;
}

/** One position at one moment, normalized to the pitch. */
export interface HighlightKeyframe {
  readonly timeMilliseconds: number;
  readonly x: number;
  readonly y: number;
}

/** One entity's movement through a highlight. */
export interface HighlightTrack {
  readonly entityId: string;
  readonly keyframes: readonly HighlightKeyframe[];
}

/** One entity in a highlight: a player or the ball. */
export interface HighlightEntity {
  readonly entityId: string;
  readonly isBall: boolean;
  readonly side: string | null;
  readonly participantId: string | null;
  readonly shirtNumber: number;
  readonly family: string | null;
  readonly x: number;
  readonly y: number;
}

/** One immutable, replayable highlight. */
export interface Highlight {
  readonly sourceEventSequence: number;
  readonly minute: number;
  readonly stoppageMinute: number;
  readonly durationMilliseconds: number;
  readonly outcomeCode: string;
  readonly narration: string;
  readonly homeColour: string;
  readonly awayColour: string;
  readonly entities: readonly HighlightEntity[];
  readonly tracks: readonly HighlightTrack[];
}

/** A played match's whole replay: commentary and highlights, in event order. */
export interface MatchPresentation {
  readonly matchId: string;
  readonly presentationVersion: string;
  readonly engineVersion: string;
  readonly homeGoals: number;
  readonly awayGoals: number;
  readonly commentary: readonly CommentaryLine[];
  readonly highlights: readonly Highlight[];
  readonly estimatedPayloadBytes: number;
}
