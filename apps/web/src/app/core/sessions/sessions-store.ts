import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { SessionsApi } from './sessions-api';
import { ActiveSession } from './sessions.models';

/**
 * The account's active sessions, for the settings screen (`F-07`).
 *
 * Holds only what the screen renders and the state of an in-flight revoke, so the screen does not have to
 * track which row is busy. A revoke removes the row locally on success rather than re-reading the list:
 * the server has already answered, and a second read would only race the answer.
 */
@Injectable({ providedIn: 'root' })
export class SessionsStore {
  private readonly api = inject(SessionsApi);

  private readonly sessionsSignal = signal<readonly ActiveSession[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly revokingSignal = signal<string | null>(null);

  /** The active sessions, oldest first. */
  readonly sessions = this.sessionsSignal.asReadonly();

  /** Whether the list is being read. */
  readonly loading = this.loadingSignal.asReadonly();

  /** The last read or revoke failure, or null. */
  readonly error = this.errorSignal.asReadonly();

  /** The id of the session currently being revoked, or null. */
  readonly revokingId = this.revokingSignal.asReadonly();

  /** Reads the list. */
  load(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    this.api.list().subscribe({
      next: (response) => {
        this.sessionsSignal.set(response.sessions);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your sessions could not be loaded.',
        );
      },
    });
  }

  /** Revokes one session and removes it from the list. */
  revoke(sessionId: string): void {
    if (this.revokingSignal() !== null) {
      return;
    }

    this.revokingSignal.set(sessionId);
    this.errorSignal.set(null);

    this.api.revoke(sessionId).subscribe({
      next: () => {
        this.revokingSignal.set(null);
        this.sessionsSignal.update((sessions) =>
          sessions.filter((session) => session.id !== sessionId),
        );
      },
      error: (error: unknown) => {
        this.revokingSignal.set(null);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'That session could not be signed out.',
        );
      },
    });
  }

  /** Forgets the list. Called when the session ends. */
  clear(): void {
    this.sessionsSignal.set([]);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
    this.revokingSignal.set(null);
  }
}
