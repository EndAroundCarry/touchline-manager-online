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

/** The team-level settings, as stable codes (`INS-1`…`INS-8`, the pass focus, and the counter-attack switch). */
export interface TeamInstructions {
  readonly mentality: string;
  readonly tempo: string;
  readonly passing: string;
  readonly width: string;
  readonly passFocus: string;
  readonly pressing: string;
  readonly defensiveLine: string;
  readonly tackling: string;
  readonly timeWasting: string;

  /** Whether the team plays on the counter-attack: a switch, not a choice of codes. */
  readonly counterAttack: boolean;
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

/** One substitute on a saved plan's bench (`SQ-4`). */
export interface TacticalBenchSlot {
  /** The bench slot number, 12–18: the places that follow the eleven starters. */
  readonly slotNumber: number;
  readonly assignedPlayer: AssignedPlayer;
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

  /** The substitutes the plan names, in slot order. Empty when it names none. */
  readonly bench: readonly TacticalBenchSlot[];
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

/** A formation preset and its default arrangement (`TAC-1`…`TAC-6`, `TAC-11`…`TAC-17`). */
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

  /**
   * The default bench, slots 12–18, or null for none. When any substitute is named all seven must be, and
   * one of them a goalkeeper (`SQ-4`): the server answers `BENCH_INCOMPLETE` or `BENCH_NEEDS_GOALKEEPER`.
   */
  readonly bench: readonly TacticalLineupEntryRequest[] | null;
}
