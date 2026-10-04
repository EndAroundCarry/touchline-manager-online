import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { TrainingApi } from './training-api';
import { PlayerTraining } from './training.models';

/** How many progression days the Training tab asks for: a season and a bit, so "All" is a whole season. */
export const HISTORY_DAYS = 120;

/**
 * One player's training history, for the player page's Training tab (`TRN-17`).
 *
 * Read-only and separate from `TrainingStore`, which owns the club-wide plan and the squad table: the
 * history is per player, can be large, and is only wanted on one tab. It holds the last player read, and a
 * response that arrives for a player the manager has since left is dropped rather than shown on the wrong
 * profile. Cleared at sign-out like the other stores.
 */
@Injectable({ providedIn: 'root' })
export class PlayerTrainingStore {
  private readonly api = inject(TrainingApi);

  private readonly historySignal = signal<PlayerTraining | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  /** The player the last request was for, so a late answer for another player can be told apart. */
  private requestedPlayerId: string | null = null;

  /** The history last read. Check its `playerId` before showing it. */
  readonly history = this.historySignal.asReadonly();

  /** Whether a read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Reads a player's regime and recent days. */
  load(playerId: string): void {
    this.requestedPlayerId = playerId;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    this.api.playerTraining(playerId, HISTORY_DAYS).subscribe({
      next: (history) => {
        if (this.requestedPlayerId !== playerId) {
          return;
        }

        this.historySignal.set(history);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        if (this.requestedPlayerId !== playerId) {
          return;
        }

        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : "This player's training could not be loaded.",
        );
      },
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.requestedPlayerId = null;
    this.historySignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }
}
