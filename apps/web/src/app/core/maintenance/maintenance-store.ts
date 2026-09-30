import { Injectable, computed, inject, signal } from '@angular/core';
import { ConnectivityStore } from '../connectivity/connectivity-store';

/**
 * Tracks the game's read-only incident mode (master plan §13, `F-51`).
 *
 * An operator can put the whole game in read-only mode, which refuses manager commands while reads keep
 * working. The shell learns it from the sync poll and shows a maintenance banner; the same state disables
 * commands so a manager is told why instead of meeting a `503`. The server stays the backstop: a client that
 * has not caught up still cannot write.
 *
 * It folds connectivity and the incident switch into one `canMutate`, so a command, a board, and a form all
 * answer the same question in one place rather than each re-deriving it.
 */
@Injectable({ providedIn: 'root' })
export class MaintenanceStore {
  private readonly connectivity = inject(ConnectivityStore);

  private readonly readOnlySignal = signal(false);
  private readonly messageSignal = signal<string | null>(null);

  /** Whether the game is read-only. */
  readonly readOnly = this.readOnlySignal.asReadonly();

  /** The operator's stated reason while read-only, or null. */
  readonly message = this.messageSignal.asReadonly();

  /** Whether a write is allowed: online and not in read-only mode. */
  readonly canMutate = computed(() => this.connectivity.isOnline() && !this.readOnlySignal());

  /** Records the state the sync poll reported. */
  set(readOnly: boolean, message: string | null): void {
    this.readOnlySignal.set(readOnly);
    this.messageSignal.set(message);
  }

  /** Drops the state, so a signed-out device keeps none of the previous manager's incident state. */
  clear(): void {
    this.set(false, null);
  }
}
