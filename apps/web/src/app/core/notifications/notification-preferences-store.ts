import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { NotificationPreferencesApi } from './notification-preferences-api';
import {
  NotificationPreferences,
  UpdateNotificationPreferencesRequest,
} from './notification-preferences.models';

/**
 * A manager's notification preferences (`COM-4`, `CONC-1`).
 *
 * The write is conditional on the version last read. A `412` keeps the manager's draft, records the
 * conflict, and re-reads the server's state so the next save starts from what is actually stored —
 * the same "reload and let the manager decide" reading the tactics and training stores use.
 */
@Injectable({ providedIn: 'root' })
export class NotificationPreferencesStore {
  private readonly api = inject(NotificationPreferencesApi);

  private readonly preferencesSignal = signal<NotificationPreferences | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly savingSignal = signal(false);
  private readonly savedSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly conflictSignal = signal(false);

  readonly preferences = this.preferencesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly saved = this.savedSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly conflict = this.conflictSignal.asReadonly();

  /** Reads the preferences. */
  load(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.savedSignal.set(false);

    this.api.read().subscribe({
      next: (preferences) => {
        this.preferencesSignal.set(preferences);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your notification settings could not be loaded.',
        );
      },
    });
  }

  /** Changes the preferences under the version last read. */
  save(value: UpdateNotificationPreferencesRequest): void {
    const current = this.preferencesSignal();

    if (current === null || this.savingSignal()) {
      return;
    }

    this.savingSignal.set(true);
    this.errorSignal.set(null);
    this.savedSignal.set(false);
    this.conflictSignal.set(false);

    this.api.update(value, current.version).subscribe({
      next: (preferences) => {
        this.preferencesSignal.set(preferences);
        this.savingSignal.set(false);
        this.savedSignal.set(true);
      },
      error: (error: unknown) => {
        this.savingSignal.set(false);

        if (error instanceof ApiError && error.isPreconditionFailed) {
          // Another device won. Re-read so the next save is conditional on the stored version.
          this.conflictSignal.set(true);
          this.load();

          return;
        }

        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your notification settings could not be saved.',
        );
      },
    });
  }

  /** Drops the preferences, on the session ending. */
  clear(): void {
    this.preferencesSignal.set(null);
    this.errorSignal.set(null);
    this.savedSignal.set(false);
    this.conflictSignal.set(false);
  }
}
