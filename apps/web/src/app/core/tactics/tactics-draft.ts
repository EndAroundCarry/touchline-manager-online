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

/** One slot as the screen edits it: the layout, plus whoever is picked. */
export interface SlotDraft {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
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
}

/** The neutral instructions a new plan starts from (`INS-1`…`INS-8`). */
export function defaultInstructions(): TeamInstructions {
  return {
    mentality: 'balanced',
    tempo: 'normal',
    passing: 'mixed',
    width: 'normal',
    pressing: 'mid_block',
    defensiveLine: 'normal',
    tackling: 'normal',
    timeWasting: 'off',
  };
}

function bySlot(left: { slotNumber: number }, right: { slotNumber: number }): number {
  return left.slotNumber - right.slotNumber;
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

/** Assigns a player to a slot, or clears it when `playerId` is null. */
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
  };
}

/** Changes the role one slot asks for (`TAC-8`). The family is the preset's and does not move. */
export function withRole(draft: PlanDraft, slotNumber: number, role: string): PlanDraft {
  return {
    ...draft,
    slots: draft.slots.map((slot) => (slot.slotNumber === slotNumber ? { ...slot, role } : slot)),
  };
}

/** Moves a slot to a new validated position (`TAC-7`). */
export function withMovedSlot(
  draft: PlanDraft,
  slotNumber: number,
  normalizedX: number,
  normalizedY: number,
): PlanDraft {
  return {
    ...draft,
    slots: draft.slots.map((slot) =>
      slot.slotNumber === slotNumber ? { ...slot, normalizedX, normalizedY } : slot,
    ),
  };
}

/** Changes one team instruction (`INS-1`…`INS-8`). */
export function withInstruction(
  draft: PlanDraft,
  key: keyof TeamInstructions,
  value: string,
): PlanDraft {
  return { ...draft, instructions: { ...draft.instructions, [key]: value } };
}

/** Renames the plan. */
export function withName(draft: PlanDraft, name: string): PlanDraft {
  return { ...draft, name };
}

/** How many of the eleven slots name a player. */
export function assignedCount(draft: PlanDraft): number {
  return draft.slots.filter((slot) => slot.playerId !== null).length;
}

/** Whether all eleven slots name a player. */
export function isComplete(draft: PlanDraft): boolean {
  return assignedCount(draft) === draft.slots.length;
}

/**
 * Turns a draft into the request the server accepts.
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
