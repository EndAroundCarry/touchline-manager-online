import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { appEnvironment } from '../config/app-environment';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { SessionStore } from '../auth/session-store';
import { SyncApi } from './sync-api';

/**
 * The shell's synchronization poll (master plan §11.2, ADR-0007).
 *
 * It repeats one lightweight read while the tab is visible and the browser is online, and exposes the unread
 * inbox count the navigation badge shows. The poll is suspended rather than queued in a hidden or offline
 * tab: freshness is worth a request a minute, not a burst of them when a laptop wakes up.
 *
 * A failed poll is swallowed on purpose. The next tick retries, and a background refresh that has not
 * happened yet is not something to interrupt a manager about.
 */
@Injectable({ providedIn: 'root' })
export class SyncStore implements OnDestroy {
  private readonly api = inject(SyncApi);
  private readonly connectivity = inject(ConnectivityStore);
  private readonly session = inject(SessionStore);

  private readonly unreadSignal = signal(0);
  private timer: ReturnType<typeof setInterval> | null = null;

  /** How many inbox messages are unread, for the navigation badge. */
  readonly unreadInboxCount = this.unreadSignal.asReadonly();

  /** Begins polling, with one immediate read so the badge is right on arrival. */
  start(): void {
    if (this.timer !== null) {
      return;
    }

    this.refresh();

    this.timer = setInterval(() => this.refresh(), appEnvironment.syncIntervalMs);
  }

  /** Stops polling, so no timer outlives the shell. */
  stop(): void {
    if (this.timer === null) {
      return;
    }

    clearInterval(this.timer);
    this.timer = null;
  }

  /** Drops the count, so a signed-out device does not keep the previous manager's badge. */
  clear(): void {
    this.unreadSignal.set(0);
  }

  /** Stops the interval when the injector is torn down. */
  ngOnDestroy(): void {
    this.stop();
  }

  /**
   * Reads the summary now, rather than waiting for the next tick.
   *
   * The inbox asks for this after a mark, so the navigation badge agrees with the screen the manager is
   * looking at instead of lagging a poll behind it.
   */
  refresh(): void {
    if (!this.session.isAuthenticated() || !this.connectivity.isOnline() || !this.isVisible()) {
      return;
    }

    this.api.summary().subscribe({
      next: (summary) => this.unreadSignal.set(summary.unreadInboxCount),
      error: () => {
        // The next tick retries; a poll that has not happened yet is not worth a message.
      },
    });
  }

  private isVisible(): boolean {
    return globalThis.document?.visibilityState !== 'hidden';
  }
}
