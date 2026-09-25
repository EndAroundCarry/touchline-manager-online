import type { PlayerState } from '../squad/squad.models';

/**
 * Transport shapes for the training module (master plan §10.4).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Squad`, like `squad.models.ts` and
 * `tactics.models.ts` and for the same reason: the OpenAPI-generated client is a later stage, and until
 * then the compiler is the check that these stay in step. Everything is `readonly`.
 *
 * The plan's `version` is its strong entity tag (`CONC-1`): a read returns it and a write must send it
 * back in `If-Match` once a plan exists, so two devices editing training cannot silently overwrite each
 * other.
 */

/** One player as the training screen shows them, with the individual focus that steers their development. */
export interface TrainingPlayer {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly primaryPosition: string;
  readonly positionFamily: string;
  readonly age: number;
  readonly state: PlayerState;

  /** The attribute family code the player focuses on, or null when they train with the team (`TRN-2`). */
  readonly focusFamily: string | null;

  /** The focus's version, or null when there is none. Required in `If-Match` to change a set focus. */
  readonly focusVersion: number | null;
}

/**
 * The club's training plan and the squad it applies to (master plan §10.4, §11.1).
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
  readonly teamFocus: string;
  readonly intensity: string;
  readonly effectiveDate: string;
  readonly version: number;
  readonly isConfigured: boolean;
  readonly teamFocusOptions: readonly string[];
  readonly intensityOptions: readonly string[];
  readonly focusFamilyOptions: readonly string[];
  readonly players: readonly TrainingPlayer[];
  readonly serverTime: string;
}

/** The editable part of the plan: the two choices a manager makes (`TRN-1`). */
export interface TrainingDraft {
  readonly teamFocus: string;
  readonly intensity: string;
}

/** The request to set or replace the club's plan (§10.4). */
export interface SaveTrainingRequest {
  readonly teamFocus: string;
  readonly intensity: string;
}

/** The result of setting or clearing one player's individual focus (`TRN-2`). */
export interface PlayerTrainingFocus {
  readonly playerId: string;
  readonly focusFamily: string | null;
  readonly version: number;
  readonly serverTime: string;
}

/** The request to set a player's focus, or to clear it with a null family. */
export interface SetPlayerTrainingFocusRequest {
  readonly focusFamily: string | null;
}
