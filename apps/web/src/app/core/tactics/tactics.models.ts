/**
 * Transport shapes for the tactics module (master plan §10.4).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Squad`, like `squad.models.ts` and `world.models.ts`
 * and for the same reason: the OpenAPI-generated client is a later stage, and until then the compiler is
 * the check that these stay in step. Everything is `readonly`.
 *
 * The plan's `version` is its strong entity tag (`CONC-1`): a read returns it and a write must send it
 * back in `If-Match`, so a formation changed on one device cannot be silently overwritten on another.
 */

/** The eight team-level settings, as stable codes (`INS-1`…`INS-8`). */
export interface TeamInstructions {
  readonly mentality: string;
  readonly tempo: string;
  readonly passing: string;
  readonly width: string;
  readonly pressing: string;
  readonly defensiveLine: string;
  readonly tackling: string;
  readonly timeWasting: string;
}

/** The player occupying a slot. */
export interface AssignedPlayer {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly primaryPosition: string;
  readonly isUnavailable: boolean;
}

/** One slot in a saved plan, with whoever occupies it (`TAC-7`…`TAC-9`). */
export interface TacticalSlot {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
  readonly assignedPlayer: AssignedPlayer | null;

  /** Whether the occupant is not at home in the slot's family (`INS-10`): a warning, never a refusal. */
  readonly isOutOfPosition: boolean;
}

/** One saved tactical plan (`INS-11`). */
export interface TacticalPlan {
  readonly id: string;
  readonly name: string;
  readonly formationPreset: string;
  readonly isDefault: boolean;
  readonly instructions: TeamInstructions;
  readonly version: number;
  readonly assignedCount: number;
  readonly isComplete: boolean;
  readonly slots: readonly TacticalSlot[];
}

/** A player who may be assigned to a slot. */
export interface SelectablePlayer {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;
  readonly primaryPosition: string;
  readonly positionFamily: string;
  readonly isUnavailable: boolean;
}

/** One slot of a formation preset's default arrangement. */
export interface FormationSlot {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
}

/** A formation preset and its default arrangement (`TAC-1`…`TAC-6`). */
export interface FormationPreset {
  readonly code: string;
  readonly slots: readonly FormationSlot[];
}

/** The club's plans, the squad they are picked from, and every preset's own arrangement (§10.4). */
export interface Tactics {
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly countryCode: string;
  readonly seasonNumber: number;
  readonly plans: readonly TacticalPlan[];
  readonly selectablePlayers: readonly SelectablePlayer[];
  readonly formations: readonly FormationPreset[];
  readonly serverTime: string;
}

/** One reason a plan is not valid, in the stable-code vocabulary of the validator. */
export interface TacticalPlanIssue {
  readonly code: string;
  readonly slotNumber: number | null;
  readonly playerId: string | null;
}

/** Why a plan was refused, as a validation preview (§10.4). */
export interface TacticalPlanValidation {
  readonly isValid: boolean;
  readonly assignedCount: number;
  readonly isComplete: boolean;
  readonly issues: readonly TacticalPlanIssue[];
}

/** One slot's layout in a submitted plan (`TAC-7`…`TAC-9`). */
export interface TacticalSlotRequest {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
}

/** One player's place in the default lineup (`SQ-4`). */
export interface TacticalLineupEntryRequest {
  readonly slotNumber: number;
  readonly playerId: string;
}

/**
 * The request to create or replace a tactical plan (§10.4).
 *
 * `slots` is the whole layout — the manager's dragged positions — and `lineup` is who occupies which
 * slot. A lineup that names some but not all eleven slots is refused by the server (`SQ-4`), which is the
 * validation preview this screen draws on the pitch.
 */
export interface SaveTacticalPlanRequest extends TeamInstructions {
  readonly name: string;
  readonly formationPreset: string;
  readonly slots: readonly TacticalSlotRequest[];
  readonly lineup: readonly TacticalLineupEntryRequest[] | null;
}
