import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { TrainingApi } from './training-api';
import { PlayerTrainingProgramme, Training, TrainingDraft } from './training.models';
import { intensityOptions as buildIntensityOptions, trainingRows } from './training-presentation';

/**
 * The training module's view state (master plan §11.1, F-20).
 *
 * A feature-scoped store following `TacticsStore`. It holds the read response and an editable *draft* of the
 * plan, so the screen never edits a response object and "is there anything to save" is one comparison. The
 * plan is the club's intensity alone; what each player trains is a programme, written straight into the read
 * model, because each is a single value on a single player rather than a document to draft.
 *
 * The store holds every player's attributes, so it is cleared when the session ends (`signOut`), like the
 * squad store.
 *
 * The concurrency contract: a revise sends the plan's `version` in `If-Match` (a first plan sends none,
 * because there is nothing to be conditional against). A `412` keeps the manager's edits, reloads the
 * server's state, and offers an explicit reapply (§11.2). A player's programme override carries the same
 * contract on its own version.
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
  private readonly programmeErrorSignal = signal<string | null>(null);
  private readonly savingProgrammePlayerIdSignal = signal<string | null>(null);

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

  /** Why the last programme write failed, announced beside the roster. */
  readonly programmeError = this.programmeErrorSignal.asReadonly();

  /** The player whose programme is mid-flight, so only that row's control is disabled. */
  readonly savingProgrammePlayerId = this.savingProgrammePlayerIdSignal.asReadonly();

  /** The programme catalogue the server sent: what each programme trains and how strongly (`TRN-1`). */
  readonly programmes = computed(() => this.trainingSignal()?.programmes ?? []);

  /** The intensity choices, labelled. */
  readonly intensityOptions = computed(() =>
    buildIntensityOptions(this.trainingSignal()?.intensityOptions ?? []),
  );

  /** The squad as table rows: each player's attributes, marked by what their programme trains (`TRN-4`). */
  readonly rows = computed(() => {
    const training = this.trainingSignal();

    return training === null ? [] : trainingRows(training.players, training.programmes);
  });

  /** Whether the draft differs from the plan in force, so there is something to save. */
  readonly isDirty = computed(() => {
    const draft = this.draftSignal();
    const training = this.trainingSignal();

    if (draft === null || training === null) {
      return false;
    }

    return draft.intensity !== training.intensity;
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
   * Sets or clears one player's training programme (`TRN-1`, `TRN-2`).
   *
   * A null programme clears the override and returns the player to the programme for their position. When an
   * override already exists its version is sent in `If-Match`; a stale one is refused rather than
   * overwriting a change made elsewhere.
   */
  setPlayerProgramme(playerId: string, programme: string | null): void {
    const training = this.trainingSignal();
    const player = training?.players.find((candidate) => candidate.id === playerId);

    if (
      training === null ||
      player === undefined ||
      this.savingProgrammePlayerIdSignal() !== null
    ) {
      return;
    }

    // Choosing what the player already trains changes nothing. Naming their default programme while they
    // hold no override is still a change: it pins the choice rather than following the position.
    const unchanged =
      programme === null
        ? player.isDefaultProgramme
        : !player.isDefaultProgramme && player.programme === programme;

    if (unchanged) {
      return;
    }

    const etag = player.focusVersion === null ? undefined : entityTag(player.focusVersion);

    this.savingProgrammePlayerIdSignal.set(playerId);
    this.programmeErrorSignal.set(null);
    this.savedMessageSignal.set(null);

    this.api.setProgramme(playerId, programme, etag).subscribe({
      next: (saved) => {
        this.savingProgrammePlayerIdSignal.set(null);
        this.applyProgramme(playerId, saved);
        this.savedMessageSignal.set(
          saved.isDefaultProgramme
            ? `${player.fullName} now trains the position default.`
            : `${player.fullName}'s programme was updated.`,
        );
      },
      error: (error: unknown) => {
        this.savingProgrammePlayerIdSignal.set(null);

        if (error instanceof ApiError && error.isPreconditionFailed) {
          this.programmeErrorSignal.set(
            'That player changed on another device. The list was refreshed — choose the programme again.',
          );
          this.reload();

          return;
        }

        this.programmeErrorSignal.set(
          error instanceof ApiError ? error.detail : "That player's programme could not be saved.",
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
    this.savingProgrammePlayerIdSignal.set(null);
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

  /** Re-reads the training state without touching the draft, for a conflict or a stale override. */
  private reload(): void {
    this.api.get().subscribe({
      next: (training) => this.trainingSignal.set(training),
      error: () =>
        this.saveErrorSignal.set(
          'Your training could not be reloaded. Reload the page to reapply.',
        ),
    });
  }

  private applyProgramme(playerId: string, saved: PlayerTrainingProgramme): void {
    const training = this.trainingSignal();

    if (training === null) {
      return;
    }

    this.trainingSignal.set({
      ...training,
      players: training.players.map((player) =>
        player.id === playerId
          ? {
              ...player,
              programme: saved.programme,
              isDefaultProgramme: saved.isDefaultProgramme,
              defaultProgramme: saved.defaultProgramme,
              focusVersion: saved.version === 0 ? null : saved.version,
            }
          : player,
      ),
    });
  }

  private clearMessages(): void {
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.programmeErrorSignal.set(null);
    this.conflictSignal.set(false);
  }
}

/** The editable draft of a read response's plan. */
function draftOf(training: Training): TrainingDraft {
  return { intensity: training.intensity };
}

/** Formats a version as the strong entity tag the precondition expects (`CONC-1`). */
function entityTag(version: number): string {
  return `"${version}"`;
}
