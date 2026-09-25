import type { AssignedPlayer, SelectablePlayer } from '../tactics/tactics.models';

/**
 * Transport shapes for the competition module (master plan §10.5, §11.1).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Competition`, like `squad.models.ts` and
 * `tactics.models.ts` and for the same reason: the OpenAPI-generated client is a later stage, and until
 * then the compiler is the check that these stay in step. Everything is `readonly`.
 *
 * A fixture's score is present only once the whole matchday has published (`MAT-7`, `CAL-10`), so a null
 * score means "not played yet" rather than "no result" — the server never sends a half-published round.
 */

/** One club as a fixture calendar names it. */
export interface FixtureClub {
  readonly id: string;
  readonly name: string;
  readonly shortName: string;
}

/** One fixture as a division's calendar shows it. */
export interface FixtureSummary {
  readonly id: string;
  readonly matchdayId: string;
  readonly roundNumber: number;
  readonly homeClubId: string;
  readonly awayClubId: string;
  readonly kickoffAt: string;
  readonly status: string;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly matchId: string | null;
}

/** One round of a division's season (`CAL-10`). */
export interface FixtureMatchday {
  readonly id: string;
  readonly roundNumber: number;
  readonly lockAt: string;
  readonly kickoffAt: string;
  readonly publicationStatus: string;
  readonly fixtures: readonly FixtureSummary[];
}

/** A division-season's whole fixture calendar (§10.5). */
export interface DivisionFixtures {
  readonly divisionId: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly countryId: string;
  readonly countryCode: string;
  readonly countryName: string;
  readonly seasonNumber: number;
  readonly seasonLabel: string;
  readonly clubs: readonly FixtureClub[];
  readonly matchdays: readonly FixtureMatchday[];
  readonly serverTime: string;
}

/** One of the manager's club's fixtures, from that club's point of view. */
export interface ClubFixture {
  readonly id: string;
  readonly roundNumber: number;
  readonly venue: string;
  readonly opponentClubId: string;
  readonly opponentName: string;
  readonly opponentShortName: string;
  readonly kickoffAt: string;
  readonly lockAt: string;
  readonly status: string;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly matchId: string | null;

  /** For a published fixture, `win`, `draw`, or `loss`. Null while there is no result yet. */
  readonly outcome: string | null;
}

/** The manager's club's season fixture list, with the next fixture named (§11.1). */
export interface MyFixtures {
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly divisionId: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly seasonNumber: number;
  readonly seasonLabel: string;
  readonly nextFixtureId: string | null;
  readonly fixtures: readonly ClubFixture[];
  readonly serverTime: string;
}

/** One side of a fixture. */
export interface FixtureSide {
  readonly clubId: string;
  readonly name: string;
  readonly shortName: string;
  readonly city: string;
  readonly region: string;
}

/** A fixture in full, as the prepare-match screen reads it (§11.1). */
export interface FixtureDetail {
  readonly id: string;
  readonly matchdayId: string;
  readonly divisionId: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly countryCode: string;
  readonly roundNumber: number;
  readonly lockAt: string;
  readonly kickoffAt: string;
  readonly status: string;
  readonly isLocked: boolean;
  readonly managedClubId: string | null;
  readonly managedSide: string | null;
  readonly home: FixtureSide;
  readonly away: FixtureSide;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly matchId: string | null;
  readonly serverTime: string;
}

/** One slot of a prepared side, filled or empty (`SQ-4`). */
export interface TeamSheetSlot {
  readonly slotNumber: number;
  readonly designation: string;
  readonly positionFamily: string | null;
  readonly role: string | null;
  readonly player: AssignedPlayer | null;
}

/**
 * Everything the prepare-match screen reads for one fixture and one club (§11.1).
 *
 * When `planId` is null the club has no default plan, so there is nothing to prepare a side from and the
 * screen offers the tactics screen instead. `sheetVersion` is the strong entity tag the next save must
 * carry in `If-Match`; it is null until a side has been saved once (`CONC-1`).
 */
export interface FixtureTeamSheet {
  readonly fixtureId: string;
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly opponentClubId: string;
  readonly opponentName: string;
  readonly opponentShortName: string;
  readonly venue: string;
  readonly divisionId: string;
  readonly divisionName: string;
  readonly roundNumber: number;
  readonly kickoffAt: string;
  readonly lockAt: string;
  readonly fixtureStatus: string;
  readonly isLocked: boolean;
  readonly planId: string | null;
  readonly planName: string | null;
  readonly formationPreset: string | null;
  readonly planVersion: number | null;
  readonly sheetVersion: number | null;
  readonly sheetStatus: string;
  readonly slots: readonly TeamSheetSlot[];
  readonly selectablePlayers: readonly SelectablePlayer[];
  readonly serverTime: string;
}

/** One player's place in a submitted selection. */
export interface TeamSheetSelection {
  readonly slotNumber: number;
  readonly playerId: string;
}

/** The request to prepare or replace a side (§10.4). */
export interface SaveTeamSheetRequest {
  readonly selection: readonly TeamSheetSelection[];
}

/** One reason a selection is not valid, in the validator's stable-code vocabulary. */
export interface TeamSheetIssue {
  readonly code: string;
  readonly slotNumber: number | null;
  readonly playerId: string | null;
}

/** Why a selection was refused, as a validation preview (§10.4). */
export interface TeamSheetValidation {
  readonly isValid: boolean;
  readonly starterCount: number;
  readonly substituteCount: number;
  readonly issues: readonly TeamSheetIssue[];
}
