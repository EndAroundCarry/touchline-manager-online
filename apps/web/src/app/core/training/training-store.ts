import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { TrainingApi } from './training-api';
import { Training, TrainingDraft } from './training.models';
import {
  focusOptions as buildFocusOptions,
  intensityOptions as buildIntensityOptions,
  teamFocusOptions as buildTeamFocusOptions,
} from './training-presentation';

/**
 * The training module's view state (master plan §11.1, F-20).
 *
 * A feature-scoped store following `TacticsStore`. It holds the read response and an editable *draft* of the
 * plan, so the screen never edits a response object and "is there anything to save" is one comparison. The
 * roster's individual focuses are written straight into the read model, because each is a single value on a
 * single player rather than a document to draft.
 *
 * The concurrency contract: a revise sends the plan's `version` in `If-Match` (a first plan sends none,
 * because there is nothing to be conditional against). A `412` keeps the manager's edits, reloads the
 * server's state, and offers an explicit reapply (§11.2). A player's focus carries the same contract on its
 * own version.
 */
@Injectable({ providedIn: 'root' })
export class TrainingStore {
  private readonly api = inject(TrainingApi);

  private readonly trainingSignal = signal<Training | null>(null);
  private readonly draftSignal = signal<TrainingDraft | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadErrorSignal = signal<string | null>(null);
  private readonly savingSignal = signal(false);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly savedMessageSignal = signal<string | null>(null);
  private readonly conflictSignal = signal(false);
  private readonly focusErrorSignal = signal<string | null>(null);
  private readonly savingFocusPlayerIdSignal = signal<string | null>(null);

  /** The training screen's read. */
  readonly training = this.trainingSignal.asReadonly();

  /** The plan being edited. */
  readonly draft = this.draftSignal.asReadonly();

  /** Whether the screen is reading the server. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the read failed. */
  readonly loadError = this.loadErrorSignal.asReadonly();

  /** Whether the plan is being saved. */
  readonly saving = this.savingSignal.asReadonly();

  /** Why the last plan save failed. */
  readonly saveError = this.saveErrorSignal.asReadonly();

  /** The last success, so the screen can confirm it. */
  readonly savedMessage = this.savedMessageSignal.asReadonly();

  /** Whether the plan changed underneath the client, so an explicit reapply is needed (`CONC-1`). */
  readonly hasConflict = this.conflictSignal.asReadonly();

  /** Why the last individual-focus write failed, announced beside the roster. */
  readonly focusError = this.focusErrorSignal.asReadonly();

  /** The player whose focus is mid-flight, so only that row's control is disabled. */
  readonly savingFocusPlayerId = this.savingFocusPlayerIdSignal.asReadonly();

  /** The club-wide focus choices, labelled and in the server's order (`TRN-1`). */
  readonly teamFocusOptions = computed(() =>
    buildTeamFocusOptions(this.trainingSignal()?.teamFocusOptions ?? []),
  );

  /** The intensity choices, labelled. */
  readonly intensityOptions = computed(() =>
    buildIntensityOptions(this.trainingSignal()?.intensityOptions ?? []),
  );

  /** The individual-focus choices, with the team plan first (`TRN-2`). */
  readonly focusOptions = computed(() =>
    buildFocusOptions(this.trainingSignal()?.focusFamilyOptions ?? []),
  );

  /** Whether the draft differs from the plan in force, so there is something to save. */
  readonly isDirty = computed(() => {
    const draft = this.draftSignal();
    const training = this.trainingSignal();

    if (draft === null || training === null) {
      return false;
    }

    return draft.teamFocus !== training.teamFocus || draft.intensity !== training.intensity;
  });

  /** Reads the club's training state and starts editing the plan. */
  load(): void {
    this.loadingSignal.set(true);
    this.loadErrorSignal.set(null);

    this.api.get().subscribe({
      next: (training) => {
        this.trainingSignal.set(training);
        this.draftSignal.set(draftOf(training));
        this.loadingSignal.set(false);
        this.clearMessages();
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.loadErrorSignal.set(
          error instanceof ApiError ? error.detail : 'Your training could not be loaded.',
        );
      },
    });
  }

  /** Changes the club-wide focus (`TRN-1`). */
  setTeamFocus(code: string): void {
    this.edit((draft) => ({ ...draft, teamFocus: code }));
  }

  /** Changes how hard the club trains. */
  setIntensity(code: string): void {
    this.edit((draft) => ({ ...draft, intensity: code }));
  }

  /**
   * Saves the plan, creating it when the club has never set one.
   *
   * The version sent is always the one the server currently holds, not a remembered copy, so a reapply after
   * a `412` is a plain retry against the state that just arrived.
   */
  save(): void {
    const draft = this.draftSignal();
    const training = this.trainingSignal();

    if (draft === null || training === null || this.savingSignal()) {
      return;
    }

    const created = !training.isConfigured;
    const etag = created ? undefined : entityTag(training.version);

    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);

    this.api.save(draft, etag).subscribe({
      next: (saved) => {
        this.savingSignal.set(false);
        this.conflictSignal.set(false);
        this.trainingSignal.set(saved);
        this.draftSignal.set(draftOf(saved));
        this.savedMessageSignal.set(
          created ? 'Your training plan was created.' : 'Your training plan was saved.',
        );
      },
      error: (error: unknown) => this.onSaveError(error),
    });
  }

  /** Re-applies the draft against the state that arrived after a conflict (`§11.2`). */
  reapply(): void {
    this.conflictSignal.set(false);
    this.save();
  }

  /** Abandons the draft and adopts the server's current plan. */
  discardConflict(): void {
    const training = this.trainingSignal();

    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.conflictSignal.set(false);
    this.draftSignal.set(training === null ? null : draftOf(training));
  }

  /**
   * Sets or clears one player's individual focus (`TRN-2`).
   *
   * A null family clears the focus and returns the player to the team plan. When a focus already exists its
   * version is sent in `If-Match`; a stale one is refused rather than overwriting a change made elsewhere.
   */
  setPlayerFocus(playerId: string, focusFamily: string | null): void {
    const training = this.trainingSignal();
    const player = training?.players.find((candidate) => candidate.id === playerId);

    if (training === null || player === undefined || this.savingFocusPlayerIdSignal() !== null) {
      return;
    }

    if (player.focusFamily === focusFamily) {
      return;
    }

    const etag =
      player.focusFamily !== null && player.focusVersion !== null
        ? entityTag(player.focusVersion)
        : undefined;

    this.savingFocusPlayerIdSignal.set(playerId);
    this.focusErrorSignal.set(null);
    this.savedMessageSignal.set(null);

    this.api.setFocus(playerId, focusFamily, etag).subscribe({
      next: (focus) => {
        this.savingFocusPlayerIdSignal.set(null);
        this.applyFocus(playerId, focus.focusFamily, focus.version);
        this.savedMessageSignal.set(
          focusFamily === null
            ? `${player.fullName} now trains with the team.`
            : `${player.fullName}'s focus was updated.`,
        );
      },
      error: (error: unknown) => {
        this.savingFocusPlayerIdSignal.set(null);

        if (error instanceof ApiError && error.isPreconditionFailed) {
          this.focusErrorSignal.set(
            'That player changed on another device. The list was refreshed — set the focus again.',
          );
          this.reload();

          return;
        }

        this.focusErrorSignal.set(
          error instanceof ApiError ? error.detail : "That player's focus could not be saved.",
        );
      },
    });
  }

  /** Forgets everything read and edited. Called when the session ends. */
  clear(): void {
    this.trainingSignal.set(null);
    this.draftSignal.set(null);
    this.loadingSignal.set(false);
    this.savingSignal.set(false);
    this.savingFocusPlayerIdSignal.set(null);
    this.clearMessages();
  }

  private edit(transform: (draft: TrainingDraft) => TrainingDraft): void {
    const draft = this.draftSignal();

    if (draft === null) {
      return;
    }

    this.draftSignal.set(transform(draft));

    // An edit invalidates the last refusal and any stale success note, so the manager sees the effect of
    // what they just changed rather than a message about what they changed before.
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.conflictSignal.set(false);
  }

  private onSaveError(error: unknown): void {
    this.savingSignal.set(false);

    if (!(error instanceof ApiError)) {
      this.saveErrorSignal.set('Your training plan could not be saved.');

      return;
    }

    if (error.isPreconditionFailed) {
      // Another device won. Keep the draft, pull the server's state, and let the manager reapply (§11.2).
      this.conflictSignal.set(true);
      this.saveErrorSignal.set(null);
      this.reload();

      return;
    }

    this.saveErrorSignal.set(error.detail);
  }

  /** Re-reads the training state without touching the draft, for a conflict or a stale focus. */
  private reload(): void {
    this.api.get().subscribe({
      next: (training) => this.trainingSignal.set(training),
      error: () =>
        this.saveErrorSignal.set(
          'Your training could not be reloaded. Reload the page to reapply.',
        ),
    });
  }

  private applyFocus(playerId: string, focusFamily: string | null, version: number): void {
    const training = this.trainingSignal();

    if (training === null) {
      return;
    }

    this.trainingSignal.set({
      ...training,
      players: training.players.map((player) =>
        player.id === playerId
          ? { ...player, focusFamily, focusVersion: version === 0 ? null : version }
          : player,
      ),
    });
  }

  private clearMessages(): void {
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.focusErrorSignal.set(null);
    this.conflictSignal.set(false);
  }
}

/** The editable draft of a read response's plan. */
function draftOf(training: Training): TrainingDraft {
  return { teamFocus: training.teamFocus, intensity: training.intensity };
}

/** Formats a version as the strong entity tag the precondition expects (`CONC-1`). */
function entityTag(version: number): string {
  return `"${version}"`;
}
