/**
 * Transport shapes for the squad module (master plan §10.3).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Squad`, like `world.models.ts` and for the same
 * reason: the OpenAPI-generated client is a later stage, and until then the compiler is the check that
 * these stay in step. Everything is `readonly`, and money is integer minor units.
 *
 * Note what is absent: the server never sends a player's hidden potential or reputation, so no shape here
 * has a field for one. `DataClassificationTests` on the API side fails if a DTO ever grows one.
 */

/** The technical attribute family. */
export interface TechnicalAttributes {
  readonly finishing: number;
  readonly passing: number;
  readonly crossing: number;
  readonly dribbling: number;
  readonly firstTouch: number;
  readonly tackling: number;
  readonly marking: number;
  readonly heading: number;
  readonly technique: number;
  readonly setPieces: number;
}

/** The mental attribute family. */
export interface MentalAttributes {
  readonly decisions: number;
  readonly vision: number;
  readonly positioning: number;
  readonly composure: number;
  readonly anticipation: number;
  readonly workRate: number;
  readonly aggression: number;
  readonly leadership: number;
}

/** The physical attribute family. */
export interface PhysicalAttributes {
  readonly pace: number;
  readonly acceleration: number;
  readonly stamina: number;
  readonly strength: number;
  readonly agility: number;
  readonly jumpingReach: number;
}

/** The goalkeeping attribute family. */
export interface GoalkeepingAttributes {
  readonly handling: number;
  readonly reflexes: number;
  readonly oneOnOnes: number;
  readonly aerialAbility: number;
}

/** A player's displayed attributes, grouped into their four families (`TRN-4`). */
export interface PlayerAttributes {
  readonly technical: TechnicalAttributes;
  readonly mental: MentalAttributes;
  readonly physical: PhysicalAttributes;
  readonly goalkeeping: GoalkeepingAttributes;
}

/** Condition, fatigue, morale, and match sharpness, as user-facing 0–100 values (`TRN-8`). */
export interface PlayerState {
  readonly condition: number;
  readonly fatigue: number;
  readonly morale: number;
  readonly matchSharpness: number;
}

/** An open injury or suspension, measured in the fixtures it costs (`TRN-12`). */
export interface PlayerAvailability {
  readonly id: string;
  readonly type: string;
  readonly severity: string;
  readonly remainingFixtures: number;
  readonly startedAt: string;
}

/** A contract as the squad and contract screens show it. */
export interface PlayerContractSummary {
  readonly id: string;
  readonly startSeasonNumber: number;
  readonly endSeasonNumber: number;
  readonly seasonsRemaining: number;
  readonly weeklyWageMinor: number;
  readonly squadStatus: string;
  readonly status: string;
}

/** One player as the squad table shows them. */
export interface SquadPlayer {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly nationalityCode: string;
  readonly age: number;
  readonly preferredFoot: string;
  readonly primaryPosition: string;
  readonly secondaryPositions: readonly string[];
  readonly state: PlayerState;
  readonly contract: PlayerContractSummary | null;
  readonly availability: readonly PlayerAvailability[];
}

/** The squad's size and legality, so the screen can warn without recomputing (`SQ-2`, `SQ-9`). */
export interface SquadSummary {
  readonly playerCount: number;
  readonly goalkeepers: number;
  readonly meetsMinimum: boolean;
  readonly hasMinimumGoalkeepers: boolean;
  readonly weeklyWageTotalMinor: number;
}

/** A club's squad. */
export interface Squad {
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly countryCode: string;
  readonly seasonNumber: number;
  readonly summary: SquadSummary;
  readonly players: readonly SquadPlayer[];
  readonly serverTime: string;
}

/** A player's registration, which is what makes them selectable (`SQ-6`). */
export interface PlayerRegistrationSummary {
  readonly id: string;
  readonly clubId: string;
  readonly status: string;
  readonly effectiveFixtureBoundaryRound: number;
}

/**
 * A player's own season statistics for the club they play for (`STA-2`).
 *
 * Read from the publication's projection, so it agrees with the division leaderboard. Null on the profile
 * before the player has taken the pitch; there is no line of zeros.
 */
export interface PlayerSeasonStats {
  readonly appearances: number;
  readonly starts: number;
  readonly minutesPlayed: number;
  readonly goals: number;
  readonly assists: number;
  readonly shots: number;
  readonly shotsOnTarget: number;
  readonly saves: number;
  readonly yellowCards: number;
  readonly redCards: number;

  /** The average match rating on a 0.0–10.0 scale, or null before the player is rated (`TRN-8`). */
  readonly averageRating: number | null;
}

/** One season of a player's career (`STA-2`). */
export interface PlayerCareerSeason {
  readonly seasonNumber: number;
  readonly seasonLabel: string;
  readonly clubId: string;
  readonly clubName: string;
  readonly stats: PlayerSeasonStats;
}

/**
 * A player's whole career across seasons (`STA-2`).
 *
 * A read-only aggregation over the season statistics, which survive rollover, so it is derived rather than
 * stored. Null on the profile before the player has ever appeared.
 */
export interface PlayerCareerStats {
  readonly totals: PlayerSeasonStats;
  readonly seasonsPlayed: number;
  readonly seasons: readonly PlayerCareerSeason[];
}

/** A player's full profile: the attribute grid plus the state and contract around it. */
export interface Player {
  readonly id: string;
  readonly clubId: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly nationalityCode: string;
  readonly age: number;
  readonly birthGameYear: number;
  readonly preferredFoot: string;
  readonly heightCm: number;
  readonly weightKg: number;
  readonly primaryPosition: string;
  readonly secondaryPositions: readonly string[];
  readonly status: string;
  readonly attributes: PlayerAttributes;
  readonly state: PlayerState;
  readonly contract: PlayerContractSummary | null;
  readonly registration: PlayerRegistrationSummary | null;
  readonly availability: readonly PlayerAvailability[];
  readonly seasonStats: PlayerSeasonStats | null;
  readonly careerStats: PlayerCareerStats | null;
  readonly serverTime: string;
}

/** One contract in the club's contract list. */
export interface PlayerContractRow {
  readonly id: string;
  readonly playerId: string;
  readonly playerName: string;
  readonly playerShortName: string;
  readonly primaryPosition: string;
  readonly age: number;
  readonly startSeasonNumber: number;
  readonly endSeasonNumber: number;
  readonly seasonsRemaining: number;
  readonly weeklyWageMinor: number;
  readonly squadStatus: string;
  readonly status: string;
}

/** The deterministic terms the server offers for a renewal (`CON-3`). */
export interface RenewalQuote {
  readonly contractId: string;
  readonly playerId: string;
  readonly seasons: number;
  readonly startSeasonNumber: number;
  readonly endSeasonNumber: number;
  readonly weeklyWageMinor: number;

  /** The contract's version at the moment it was quoted, sent back as `If-Match` when signing. */
  readonly contractVersion: number;
  readonly serverTime: string;
}

/** The contract a renewal signed a player to (`CON-4`). */
export interface ContractRenewal {
  readonly contractId: string;
  readonly playerId: string;
  readonly seasons: number;
  readonly startSeasonNumber: number;
  readonly endSeasonNumber: number;
  readonly weeklyWageMinor: number;
  readonly version: number;
  readonly serverTime: string;
}

/** A club's contract list. */
export interface ContractList {
  readonly clubId: string;
  readonly clubName: string;
  readonly seasonNumber: number;
  readonly weeklyWageTotalMinor: number;
  readonly contracts: readonly PlayerContractRow[];
  readonly serverTime: string;
}

/**
 * One match of a player's own history (`STA-2`).
 *
 * Read from the stored match, so the rows add up to the season line. There is no passes field: the match
 * engine does not count passes yet, and the screen says so rather than inventing a number.
 */
export interface PlayerMatchStat {
  readonly fixtureId: string;
  readonly seasonNumber: number;
  readonly seasonLabel: string;
  readonly round: number;
  readonly playedAt: string;
  readonly opponentClubId: string;
  readonly opponentName: string;
  readonly home: boolean;
  readonly goalsFor: number;
  readonly goalsAgainst: number;
  readonly started: boolean;
  readonly minutesPlayed: number;
  readonly goals: number;
  readonly assists: number;
  readonly shots: number;
  readonly shotsOnTarget: number;
  readonly saves: number;
  readonly yellowCards: number;
  readonly redCards: number;

  /** The match rating on a 0.0–10.0 scale, or null when the player was not rated (`TRN-8`). */
  readonly rating: number | null;
}

/** A player's matches, most recent first, across every season they have appeared in. */
export interface PlayerMatches {
  readonly playerId: string;
  readonly matches: readonly PlayerMatchStat[];
  readonly serverTime: string;
}
