/**
 * The editable plan: the shape the screen mutates, and the pure transforms that change it.
 *
 * A saved plan is immutable transport; the draft is what the manager is editing. Keeping the transforms
 * here as pure functions means the screen holds no layout logic, the same formation change produces the
 * same slots every time, and the request the save sends is built in one place rather than assembled from
 * bindings at the call site.
 */

import type {
  FormationPreset,
  SaveTacticalPlanRequest,
  TacticalLineupEntryRequest,
  TacticalPlan,
  TeamInstructions,
} from './tactics.models';

/** The name a new plan starts with, until the manager renames it. */
export const NEW_PLAN_NAME = 'New plan';

/** The first bench slot number, which follows the eleven starters (`SQ-4`). */
export const BENCH_FIRST_SLOT = 12;

/** How many substitutes a bench holds (`SQ-4`). */
export const BENCH_SIZE = 7;

/**
 * The bench place kept for a goalkeeper. The server asks only that the bench holds one somewhere; the
 * screen reserves the first place for it so the rule is something a manager can see rather than discover.
 */
export const KEEPER_BENCH_SLOT = BENCH_FIRST_SLOT;

/** One slot as the screen edits it: the layout, plus whoever is picked. */
export interface SlotDraft {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
  readonly playerId: string | null;
}

/** One bench place as the screen edits it. */
export interface BenchDraft {
  readonly slotNumber: number;
  readonly playerId: string | null;
}

/** A plan being edited. `planId` and `version` are null until the plan has been saved once. */
export interface PlanDraft {
  readonly planId: string | null;
  readonly version: number | null;
  readonly name: string;
  readonly formationPreset: string;
  readonly instructions: TeamInstructions;
  readonly slots: readonly SlotDraft[];

  /** Always seven places, slots 12–18, filled or not. */
  readonly bench: readonly BenchDraft[];
}

/** The neutral instructions a new plan starts from (`INS-1`…`INS-8`). */
export function defaultInstructions(): TeamInstructions {
  return {
    mentality: 'balanced',
    tempo: 'normal',
    passing: 'mixed',
    width: 'normal',
    passFocus: 'balanced',
    pressing: 'mid_block',
    defensiveLine: 'normal',
    tackling: 'normal',
    timeWasting: 'off',
    counterAttack: false,
  };
}

function bySlot(left: { slotNumber: number }, right: { slotNumber: number }): number {
  return left.slotNumber - right.slotNumber;
}

/** The seven bench places, with whoever is named in each. */
function benchOf(picked: ReadonlyMap<number, string>): readonly BenchDraft[] {
  return Array.from({ length: BENCH_SIZE }, (_, index) => {
    const slotNumber = BENCH_FIRST_SLOT + index;

    return { slotNumber, playerId: picked.get(slotNumber) ?? null };
  });
}

/** Builds a draft from a saved plan, so the screen edits what the server holds. */
export function draftFromPlan(plan: TacticalPlan): PlanDraft {
  return {
    planId: plan.id,
    version: plan.version,
    name: plan.name,
    formationPreset: plan.formationPreset,
    instructions: { ...plan.instructions },
    slots: [...plan.slots].sort(bySlot).map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
      playerId: slot.assignedPlayer?.id ?? null,
    })),
    bench: benchOf(new Map(plan.bench.map((place) => [place.slotNumber, place.assignedPlayer.id]))),
  };
}

/** Builds a new, unassigned draft laid out from a formation preset. */
export function draftFromFormation(formation: FormationPreset, name = NEW_PLAN_NAME): PlanDraft {
  return {
    planId: null,
    version: null,
    name,
    formationPreset: formation.code,
    instructions: defaultInstructions(),
    slots: [...formation.slots].sort(bySlot).map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
      playerId: null,
    })),
    bench: benchOf(new Map()),
  };
}

/**
 * Re-lays a plan's slots from a new formation, keeping whoever is already picked.
 *
 * Slot numbers are stable across a formation change (`TacticalSlot.Reshape`), so a manager keeps their
 * lineup: the right back picked in a 4-4-2 is still slot 2 in a 4-3-3, only standing somewhere else.
 */
export function withFormation(draft: PlanDraft, formation: FormationPreset): PlanDraft {
  const picked = new Map(draft.slots.map((slot) => [slot.slotNumber, slot.playerId]));

  return {
    ...draft,
    formationPreset: formation.code,
    slots: [...formation.slots].sort(bySlot).map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
      playerId: picked.get(slot.slotNumber) ?? null,
    })),
  };
}

/**
 * Assigns a player to a slot, or clears it when `playerId` is null.
 *
 * A player picked to start leaves the bench: one player cannot be in both, and moving them is what the
 * manager means by picking them.
 */
export function withAssignment(
  draft: PlanDraft,
  slotNumber: number,
  playerId: string | null,
): PlanDraft {
  return {
    ...draft,
    slots: draft.slots.map((slot) =>
      slot.slotNumber === slotNumber ? { ...slot, playerId } : slot,
    ),
    bench:
      playerId === null
        ? draft.bench
        : draft.bench.map((place) =>
            place.playerId === playerId ? { ...place, playerId: null } : place,
          ),
  };
}

/**
 * Names a substitute for a bench place, or empties the place when `playerId` is null.
 *
 * A player already starting, or already on the bench, is moved rather than copied: the place they held is
 * left empty, so the manager sees the gap rather than a refusal about a double booking.
 */
export function withBenchAssignment(
  draft: PlanDraft,
  slotNumber: number,
  playerId: string | null,
): PlanDraft {
  return {
    ...draft,
    slots:
      playerId === null
        ? draft.slots
        : draft.slots.map((slot) =>
            slot.playerId === playerId ? { ...slot, playerId: null } : slot,
          ),
    bench: draft.bench.map((place) => {
      if (place.slotNumber === slotNumber) {
        return { ...place, playerId };
      }

      return playerId !== null && place.playerId === playerId
        ? { ...place, playerId: null }
        : place;
    }),
  };
}

/** Changes the role one slot asks for (`TAC-8`). The family is the preset's and does not move. */
export function withRole(draft: PlanDraft, slotNumber: number, role: string): PlanDraft {
  return {
    ...draft,
    slots: draft.slots.map((slot) => (slot.slotNumber === slotNumber ? { ...slot, role } : slot)),
  };
}

/** The instructions that are a choice of codes, which are every one but the counter-attack switch. */
export type CodedInstructionKey = Exclude<keyof TeamInstructions, 'counterAttack'>;

/** Changes one team instruction (`INS-1`…`INS-8`). */
export function withInstruction(
  draft: PlanDraft,
  key: CodedInstructionKey,
  value: string,
): PlanDraft {
  return { ...draft, instructions: { ...draft.instructions, [key]: value } };
}

/** Switches the counter-attack on or off. */
export function withCounterAttack(draft: PlanDraft, on: boolean): PlanDraft {
  return { ...draft, instructions: { ...draft.instructions, counterAttack: on } };
}

/** Renames the plan. */
export function withName(draft: PlanDraft, name: string): PlanDraft {
  return { ...draft, name };
}

/** How many of the eleven slots name a player. */
export function assignedCount(draft: PlanDraft): number {
  return draft.slots.filter((slot) => slot.playerId !== null).length;
}

/** How many of the seven bench places name a player. */
export function benchCount(draft: PlanDraft): number {
  return draft.bench.filter((place) => place.playerId !== null).length;
}

/** Whether all eleven slots name a player. */
export function isComplete(draft: PlanDraft): boolean {
  return assignedCount(draft) === draft.slots.length;
}

/**
 * Turns a draft into the request the server accepts.
 *
 * `bench` follows the same rule as `lineup`: any substitute at all is sent, so a half-filled bench is
 * refused with `BENCH_INCOMPLETE` rather than quietly dropped, and an empty bench is omitted.
 *
 * `lineup` is emitted whenever anybody is picked, including a partial selection: the server refuses a
 * half-filled side with `SELECTION_INCOMPLETE` (`SQ-4`), and omitting it would silently save a template
 * with nobody in it — the manager would think their picks were kept. A draft with no picks omits the
 * lineup, which is how a legal, empty template is saved.
 */
export function toRequest(draft: PlanDraft): SaveTacticalPlanRequest {
  const slots = [...draft.slots].sort(bySlot);
  const lineup: TacticalLineupEntryRequest[] = slots
    .filter((slot): slot is SlotDraft & { playerId: string } => slot.playerId !== null)
    .map((slot) => ({ slotNumber: slot.slotNumber, playerId: slot.playerId }));
  const bench: TacticalLineupEntryRequest[] = [...draft.bench]
    .sort(bySlot)
    .filter((place): place is BenchDraft & { playerId: string } => place.playerId !== null)
    .map((place) => ({ slotNumber: place.slotNumber, playerId: place.playerId }));

  return {
    name: draft.name,
    formationPreset: draft.formationPreset,
    ...draft.instructions,
    slots: slots.map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
    })),
    lineup: lineup.length === 0 ? null : lineup,
    bench: bench.length === 0 ? null : bench,
  };
}

/**
 * Whether the draft differs from what the server holds.
 *
 * The comparison is over the request the save would send, not the object graph: that is exactly what
 * "there is something to save" means, and it keeps a re-ordered or re-built draft from reading as a
 * change. A brand-new plan is always dirty, because there is nothing to compare it to.
 */
export function isDirty(draft: PlanDraft, saved: TacticalPlan | null): boolean {
  if (saved === null) {
    return true;
  }

  return JSON.stringify(toRequest(draft)) !== JSON.stringify(toRequest(draftFromPlan(saved)));
}
