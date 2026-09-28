import { PlayerAttributes } from '../squad/squad.models';

/**
 * The scouting module's transport mirror (master plan §10.6; `SCT-1`, `SCT-3`).
 *
 * Hand-written to match `TouchlineManager.Contracts.Market`, like the other core model files. Exact public
 * attributes only: no hidden potential, reputation, or internal valuation crosses the wire (`I-1`).
 */

/** One player as a scouting result reads them. */
export interface PlayerSearchResult {
  readonly playerId: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly nationalityCode: string;
  readonly age: number;
  readonly preferredFoot: string;
  readonly primaryPosition: string;
  readonly secondaryPositions: readonly string[];
  readonly clubId: string | null;
  readonly clubName: string | null;
  readonly tierNumber: number | null;
  readonly divisionId: string | null;
  readonly divisionName: string | null;
  readonly isListed: boolean;
  readonly attributes: PlayerAttributes;
}

/** One page of scouting results. */
export interface PlayerSearchPage {
  readonly players: readonly PlayerSearchResult[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}

/** How a scouting search is ordered (`SCT-1`). */
export type PlayerSort = 'name' | 'age' | 'ability';

/** The filters a scouting search is made with. */
export interface ScoutingFilters {
  readonly name: string | null;
  readonly position: string | null;
  readonly ageMin: number | null;
  readonly ageMax: number | null;
  readonly abilityMin: number | null;
  readonly sort: PlayerSort;
}

/** One shortlisted player (`SCT-3`). */
export interface ShortlistEntry {
  readonly playerId: string;
  readonly playerName: string;
  readonly shortName: string;
  readonly primaryPosition: string;
  readonly age: number;
  readonly notes: string | null;
  readonly isListed: boolean;
}

/** A manager's private shortlist. */
export interface Shortlist {
  readonly entries: readonly ShortlistEntry[];
  readonly serverTime: string;
}
