import type { PlayerAttributes, PlayerState } from '../squad/squad.models';

/**
 * Transport shapes for the training module (master plan §10.4).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Squad`, like `squad.models.ts` and
 * `tactics.models.ts` and for the same reason: the OpenAPI-generated client is a later stage, and until
 * then the compiler is the check that these stay in step. Everything is `readonly`.
 *
 * Note what is absent: the server never sends a player's hidden potential or training aptitude, so no shape
 * here has a field for one (`TRN-9`).
 *
 * The plan's `version` is its strong entity tag (`CONC-1`): a read returns it and a write must send it
 * back in `If-Match` once a plan exists, so two devices editing training cannot silently overwrite each
 * other. A player's programme override carries the same contract on its own `focusVersion`.
 */

/** One attribute a programme trains, and how much it matters to it. */
export interface TrainingProgrammeAttribute {
  /** The attribute code, which is its property name in the attribute grid, e.g. `firstTouch`. */
  readonly name: string;

  /** The code of the attribute's family, e.g. `technical`. */
  readonly family: string;

  /** 3 for a core attribute, 2 for an important one, 1 for a supporting one. */
  readonly weight: number;
}

/** One training programme: a weighted set of attributes. The server owns the catalogue (`TRN-1`). */
export interface TrainingProgramme {
  readonly code: string;
  readonly label: string;
  readonly description: string;

  /** Heaviest first; empty for recovery. */
  readonly attributes: readonly TrainingProgrammeAttribute[];
}

/** One player as the training screen shows them, with the programme they train and every attribute. */
export interface TrainingPlayer {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly primaryPosition: string;
  readonly positionFamily: string;
  readonly age: number;
  readonly state: PlayerState;
  readonly attributes: PlayerAttributes;

  /** The programme the player trains: the override, else the position default. */
  readonly programme: string;

  /** Whether `programme` is the position default rather than a choice. */
  readonly isDefaultProgramme: boolean;

  /** The programme matching the player's position. */
  readonly defaultProgramme: string;

  /** The override's version, or null when there is none. Required in `If-Match` to change a set override. */
  readonly focusVersion: number | null;
}

/**
 * The club's training plan, the programme catalogue, and the squad it applies to (master plan §10.4, §11.1).
 *
 * When a club has not set a plan yet, `isConfigured` is false and the values are the implicit defaults
 * the progression job would use (`TRN-3`), so the screen can show what is actually in force.
 */
export interface Training {
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly countryCode: string;
  readonly seasonNumber: string | number;
  readonly intensity: string;
  readonly effectiveDate: string;
  readonly version: number;
  readonly isConfigured: boolean;
  readonly intensityOptions: readonly string[];
  readonly programmes: readonly TrainingProgramme[];
  readonly players: readonly TrainingPlayer[];
  readonly serverTime: string;
}

/** The editable part of the plan: the one club-wide choice a manager makes (`TRN-1`). */
export interface TrainingDraft {
  readonly intensity: string;
}

/** The request to set or replace the club's plan (§10.4). */
export interface SaveTrainingRequest {
  readonly intensity: string;
}

/** The result of setting or clearing one player's programme (`TRN-1`, `TRN-2`). */
export interface PlayerTrainingProgramme {
  readonly playerId: string;
  readonly programme: string;
  readonly isDefaultProgramme: boolean;
  readonly defaultProgramme: string;

  /** The override's version; zero when it was cleared. */
  readonly version: number;
  readonly serverTime: string;
}

/** The request to set a player's programme, or to clear it with a null programme. */
export interface SetPlayerTrainingProgrammeRequest {
  readonly programme: string | null;
}
