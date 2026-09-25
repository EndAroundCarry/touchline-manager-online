import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiError } from '../api/api-error';
import { TacticsApi } from './tactics-api';
import {
  PlanDraft,
  assignedCount,
  draftFromFormation,
  draftFromPlan,
  isDirty,
  toRequest,
  withAssignment,
  withFormation,
  withInstruction,
  withMovedSlot,
  withName,
  withRole,
} from './tactics-draft';
import {
  SaveTacticalPlanRequest,
  TacticalPlan,
  TacticalPlanIssue,
  TacticalPlanValidation,
  TeamInstructions,
  Tactics,
} from './tactics.models';

/** The refusal code the server answers a plan that breaks a tactics rule with (master plan §10.4). */
const PLAN_VALIDATION_FAILED = 'PLAN_VALIDATION_FAILED';

/**
 * The tactics module's view state.
 *
 * A feature-scoped store, following `SquadStore` and `OnboardingStore`. It holds two things the squad
 * store does not: the read response and an editable *draft* of one plan. The draft is what every control
 * mutates, and it is swapped for the server's own shape on load, selection, and after a save — so the
 * screen never edits a response object and the "is there anything to save" question is one comparison.
 *
 * The concurrency contract lives here. A save sends the version the client last read in `If-Match`; a
 * `412` means another device won, and rather than overwriting it the store keeps the manager's edits,
 * reloads the server state, and waits for an explicit reapply (§11.2).
 */
@Injectable({ providedIn: 'root' })
export class TacticsStore {
  private readonly api = inject(TacticsApi);

  private readonly tacticsSignal = signal<Tactics | null>(null);
  private readonly draftSignal = signal<PlanDraft | null>(null);
  private readonly selectedPlanIdSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadErrorSignal = signal<string | null>(null);
  private readonly savingSignal = signal(false);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly savedMessageSignal = signal<string | null>(null);
  private readonly validationSignal = signal<TacticalPlanValidation | null>(null);
  private readonly conflictSignal = signal(false);

  /** The tactics screen's read. */
  readonly tactics = this.tacticsSignal.asReadonly();

  /** The plan being edited. */
  readonly draft = this.draftSignal.asReadonly();

  /** Whether the screen is reading the server. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the read failed. */
  readonly loadError = this.loadErrorSignal.asReadonly();

  /** Whether a command is in flight. */
  readonly saving = this.savingSignal.asReadonly();

  /** Why the last command failed. */
  readonly saveError = this.saveErrorSignal.asReadonly();

  /** The last success, so the screen can confirm it. */
  readonly savedMessage = this.savedMessageSignal.asReadonly();

  /** The validation preview of the last refused save, when there was one. */
  readonly validation = this.validationSignal.asReadonly();

  /** Whether the plan changed underneath the client, so an explicit reapply is needed (`CONC-1`). */
  readonly hasConflict = this.conflictSignal.asReadonly();

  /** Every plan the club holds, the default first. */
  readonly plans = computed(() => this.tacticsSignal()?.plans ?? []);

  /** The players who may be assigned to a slot. */
  readonly selectablePlayers = computed(() => this.tacticsSignal()?.selectablePlayers ?? []);

  /** Every formation preset and its default arrangement (`TAC-1`…`TAC-6`). */
  readonly formations = computed(() => this.tacticsSignal()?.formations ?? []);

  /** The saved plan the draft belongs to, or null while a new plan is being built. */
  readonly selectedPlan = computed(() => {
    const id = this.selectedPlanIdSignal();

    return id === null
      ? null
      : (this.tacticsSignal()?.plans.find((plan) => plan.id === id) ?? null);
  });

  /** Whether the draft differs from the server's state, so there is something to save. */
  readonly isDirty = computed(() => {
    const draft = this.draftSignal();

    return draft === null ? false : isDirty(draft, this.selectedPlan());
  });

  /** How many of the eleven slots name a player. */
  readonly assignedCount = computed(() => {
    const draft = this.draftSignal();

    return draft === null ? 0 : assignedCount(draft);
  });

  /** The validation issues keyed by slot, so the board can mark the slots they concern. */
  readonly issuesBySlot = computed(() => {
    const bySlot = new Map<number, TacticalPlanIssue[]>();

    for (const issue of this.validationSignal()?.issues ?? []) {
      if (issue.slotNumber === null) {
        continue;
      }

      bySlot.set(issue.slotNumber, [...(bySlot.get(issue.slotNumber) ?? []), issue]);
    }

    return bySlot;
  });

  /** Reads the club's tactics and starts editing the default plan (or a new one). */
  load(): void {
    this.loadingSignal.set(true);
    this.loadErrorSignal.set(null);

    this.api.get().subscribe({
      next: (tactics) => {
        this.tacticsSignal.set(tactics);
        this.loadingSignal.set(false);
        this.clearMessages();

        const plan =
          tactics.plans.find((candidate) => candidate.isDefault) ?? tactics.plans[0] ?? null;

        this.select(plan);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.loadErrorSignal.set(
          error instanceof ApiError ? error.detail : 'Your tactics could not be loaded.',
        );
      },
    });
  }

  /** Starts editing a saved plan. */
  selectPlan(planId: string): void {
    const tactics = this.tacticsSignal();
    const plan = tactics?.plans.find((candidate) => candidate.id === planId) ?? null;

    if (tactics === null || plan === null) {
      return;
    }

    this.clearMessages();
    this.select(plan);
  }

  /** Starts a new, unassigned plan laid out from the first formation preset (`INS-11`). */
  newPlan(): void {
    const tactics = this.tacticsSignal();

    if (tactics === null) {
      return;
    }

    this.clearMessages();
    this.select(null);
  }

  /** Renames the plan. */
  setName(name: string): void {
    this.edit((draft) => withName(draft, name));
  }

  /** Changes one team instruction (`INS-1`…`INS-8`). */
  setInstruction(key: keyof TeamInstructions, value: string): void {
    this.edit((draft) => withInstruction(draft, key, value));
  }

  /** Re-lays the plan from a new formation, keeping whoever is picked. */
  setFormation(formationCode: string): void {
    const formation = this.formations().find((candidate) => candidate.code === formationCode);

    if (formation === undefined) {
      return;
    }

    this.edit((draft) => withFormation(draft, formation));
  }

  /** Assigns a player to a slot. */
  assignPlayer(slotNumber: number, playerId: string): void {
    this.edit((draft) => withAssignment(draft, slotNumber, playerId));
  }

  /** Empties a slot. */
  clearSlot(slotNumber: number): void {
    this.edit((draft) => withAssignment(draft, slotNumber, null));
  }

  /** Changes the role one slot asks for (`TAC-8`). */
  setRole(slotNumber: number, role: string): void {
    this.edit((draft) => withRole(draft, slotNumber, role));
  }

  /** Moves a slot on the pitch (`TAC-7`). */
  moveSlot(slotNumber: number, normalizedX: number, normalizedY: number): void {
    this.edit((draft) => withMovedSlot(draft, slotNumber, normalizedX, normalizedY));
  }

  /**
   * Saves the draft, creating the plan if it has never been saved.
   *
   * The version sent is always the one the server currently holds, not the draft's remembered copy, so a
   * reapply after a `412` is a plain retry against the state that just arrived.
   */
  save(): void {
    const draft = this.draftSignal();
    const tactics = this.tacticsSignal();

    if (draft === null || tactics === null || this.savingSignal()) {
      return;
    }

    const planId = draft.planId;
    const saved =
      planId === null ? null : (tactics.plans.find((plan) => plan.id === planId) ?? null);

    if (planId !== null && saved === null) {
      this.saveErrorSignal.set('That plan no longer exists. Reload your tactics.');
      this.conflictSignal.set(true);

      return;
    }

    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);

    const request: SaveTacticalPlanRequest = toRequest(draft);
    const created = planId === null;

    const call: Observable<TacticalPlan> =
      planId === null || saved === null
        ? this.api.create(request)
        : this.api.update(planId, request, entityTag(saved.version));

    call.subscribe({
      next: (plan) => {
        this.savingSignal.set(false);
        this.validationSignal.set(null);
        this.conflictSignal.set(false);
        this.acceptSaved(plan);
        this.savedMessageSignal.set(created ? 'Your plan was created.' : 'Your plan was saved.');
      },
      error: (error: unknown) => this.onWriteError(error),
    });
  }

  /** Re-applies the draft against the state that arrived after a conflict (`§11.2`). */
  reapply(): void {
    this.conflictSignal.set(false);
    this.save();
  }

  /** Abandons the draft and adopts the server's current plan. */
  discardConflict(): void {
    const plan = this.selectedPlan();

    this.clearMessages();
    this.draftSignal.set(plan === null ? this.freshDraft() : draftFromPlan(plan));
  }

  /** Makes a plan the club's default (`INS-11`). */
  makeDefault(planId: string): void {
    const tactics = this.tacticsSignal();
    const plan = tactics?.plans.find((candidate) => candidate.id === planId) ?? null;

    if (tactics === null || plan === null || this.savingSignal()) {
      return;
    }

    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);

    this.api.makeDefault(planId, entityTag(plan.version)).subscribe({
      next: (updated) => {
        this.savingSignal.set(false);
        this.conflictSignal.set(false);
        this.applyPromotion(updated);
        this.savedMessageSignal.set(`${updated.name} is now your default plan.`);
      },
      error: (error: unknown) => this.onWriteError(error),
    });
  }

  /** Forgets everything read and edited. Called when the session ends. */
  clear(): void {
    this.tacticsSignal.set(null);
    this.draftSignal.set(null);
    this.selectedPlanIdSignal.set(null);
    this.loadingSignal.set(false);
    this.savingSignal.set(false);
    this.clearMessages();
  }

  private edit(transform: (draft: PlanDraft) => PlanDraft): void {
    const draft = this.draftSignal();

    if (draft === null) {
      return;
    }

    this.draftSignal.set(transform(draft));

    // An edit invalidates the last refusal's highlights and any stale success note, so the manager sees
    // the effect of what they just changed rather than a message about what they changed before.
    this.validationSignal.set(null);
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
  }

  private select(plan: TacticalPlan | null): void {
    this.selectedPlanIdSignal.set(plan?.id ?? null);
    this.draftSignal.set(plan === null ? this.freshDraft() : draftFromPlan(plan));
  }

  private freshDraft(): PlanDraft | null {
    const formation = this.formations()[0] ?? null;

    return formation === null ? null : draftFromFormation(formation);
  }

  private acceptSaved(plan: TacticalPlan): void {
    const tactics = this.tacticsSignal();

    if (tactics === null) {
      return;
    }

    const exists = tactics.plans.some((candidate) => candidate.id === plan.id);
    const plans = (
      exists
        ? tactics.plans.map((candidate) => (candidate.id === plan.id ? plan : candidate))
        : [plan, ...tactics.plans]
    ).map((candidate) =>
      plan.isDefault && candidate.id !== plan.id ? { ...candidate, isDefault: false } : candidate,
    );

    this.tacticsSignal.set({ ...tactics, plans });
    this.selectedPlanIdSignal.set(plan.id);
    this.draftSignal.set(draftFromPlan(plan));
  }

  private applyPromotion(updated: TacticalPlan): void {
    const tactics = this.tacticsSignal();

    if (tactics === null) {
      return;
    }

    // Only the default flags change, so the draft and the selection are left alone: promoting one plan
    // must not throw away unsaved edits on another.
    this.tacticsSignal.set({
      ...tactics,
      plans: tactics.plans.map((plan) =>
        plan.id === updated.id ? updated : { ...plan, isDefault: false },
      ),
    });
  }

  private onWriteError(error: unknown): void {
    this.savingSignal.set(false);

    if (!(error instanceof ApiError)) {
      this.saveErrorSignal.set('Your plan could not be saved.');

      return;
    }

    if (error.code === PLAN_VALIDATION_FAILED) {
      const validation = error.extension<TacticalPlanValidation>('validation');

      this.validationSignal.set(validation);
      this.saveErrorSignal.set(validation === null ? error.detail : null);

      return;
    }

    if (error.isPreconditionFailed) {
      // Another device won. Keep the draft, pull the server's state, and let the manager reapply (§11.2).
      this.conflictSignal.set(true);
      this.saveErrorSignal.set(null);
      this.reloadPlans();

      return;
    }

    this.saveErrorSignal.set(error.detail);
  }

  private reloadPlans(): void {
    this.api.get().subscribe({
      next: (tactics) => this.tacticsSignal.set(tactics),
      error: () =>
        this.saveErrorSignal.set('Your tactics could not be reloaded. Reload the page to reapply.'),
    });
  }

  private clearMessages(): void {
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.validationSignal.set(null);
    this.conflictSignal.set(false);
  }
}

/** Formats a version as the strong entity tag the precondition expects (`CONC-1`). */
function entityTag(version: number): string {
  return `"${version}"`;
}
