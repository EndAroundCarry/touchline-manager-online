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
  readonly z?: number;
  readonly speed?: number;
  readonly action?: string | null;
}

/** One entity's movement through a highlight. */
export interface HighlightTrack {
  readonly entityId: string;
  readonly keyframes: readonly HighlightKeyframe[];
}

/** One line of commentary pinned to a moment inside a highlight (`replay-v2`). */
export interface HighlightCommentary {
  readonly timeMilliseconds: number;
  readonly templateKey: string;
  readonly variantKey: string;
  readonly parameters: readonly CommentaryParameter[];
  readonly text: string;
}

/** One segment of the film playback clock: a passage (`replay-v3`). */
export interface PlaybackSegment {
  readonly kind: string;
  readonly sourceEventSequence: number;
  readonly startMilliseconds: number;
  readonly durationMilliseconds: number;
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
  readonly name?: string | null;
  readonly position?: string | null;
}

/** One player's performance and state for the match center lineup. */
export interface MatchLineupPlayer {
  readonly participantId: string;
  readonly playerId: string;
  readonly shirtNumber: number;
  readonly name: string;
  readonly position: string;
  readonly family: string;
  readonly isStarter: boolean;
  readonly slotNumber: number;
  readonly kickoffCondition: number;
  readonly finalCondition: number;
  readonly finalRating: number;
  readonly goals: number;
  readonly assists: number;
  readonly yellowCards: number;
  readonly sentOff: boolean;
  readonly subbedOutMinute: number | null;
  readonly subbedInMinute: number | null;
  readonly isInjured: boolean;
}

/** One team's tactical lineup setup for the match center. */
export interface MatchLineup {
  readonly clubName: string;
  readonly shortName: string;
  readonly primaryColour: string;
  readonly secondaryColour: string;
  readonly formation: string;
  readonly starters: readonly MatchLineupPlayer[];
  readonly bench: readonly MatchLineupPlayer[];
}

/** A player's live condition and rating at a specific minute in the match. */
export interface PlayerLiveMetric {
  readonly participantId: string;
  readonly minute: number;
  readonly conditionBasisPoints: number;
  readonly ratingBasisPoints: number;
}

/**
 * One immutable, replayable film passage: a slice of a continuous match (`replay-v3`).
 *
 * Carries no frames and no video: the client interpolates between keyframes at its own refresh rate. The
 * match window (`startMatchSecond`…`endMatchSecond`) is what the continuous clock reads, and the event
 * sequences are what the report's "watch" seeks by.
 */
export interface Passage {
  readonly sourceEventSequence: number;
  readonly minute: number;
  readonly stoppageMinute: number;
  readonly startMatchSecond: number;
  readonly endMatchSecond: number;
  readonly durationMilliseconds: number;
  readonly outcomeCode: string;
  readonly narration: string;
  readonly homeColour: string;
  readonly awayColour: string;
  readonly eventSequences: readonly number[];
  readonly entities: readonly HighlightEntity[];
  readonly tracks: readonly HighlightTrack[];
  readonly commentary?: readonly HighlightCommentary[] | null;
}

/** One clip of the highlights reel: a window of the film around a chance (`replay-v3`). */
export interface ReelClip {
  readonly sourceEventSequence: number;
  readonly outcomeCode: string;
  readonly minute: number;
  readonly stoppageMinute: number;
  readonly startMilliseconds: number;
  readonly endMilliseconds: number;
}

/** A played match's whole replay: one continuous film, a highlights reel, and the commentary log. */
export interface MatchPresentation {
  readonly matchId: string;
  readonly presentationVersion: string;
  readonly engineVersion: string;
  readonly homeGoals: number;
  readonly awayGoals: number;
  readonly commentary: readonly CommentaryLine[];
  readonly passages: readonly Passage[];
  readonly reel: readonly ReelClip[];
  readonly estimatedPayloadBytes: number;
  readonly homeLineup?: MatchLineup | null;
  readonly awayLineup?: MatchLineup | null;
  readonly liveMetrics?: readonly PlayerLiveMetric[] | null;
  readonly playback?: readonly PlaybackSegment[] | null;
  readonly totalPlaybackMilliseconds?: number;
}
