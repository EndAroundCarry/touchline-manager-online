import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { appEnvironment } from '../config/app-environment';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { MaintenanceStore } from '../maintenance/maintenance-store';
import { SessionStore } from '../auth/session-store';
import { SyncApi } from './sync-api';

/**
 * The shell's synchronization poll (master plan §11.2, ADR-0007).
 *
 * It repeats one lightweight read while the tab is visible and the browser is online, and exposes the unread
 * inbox count the navigation badge shows. The poll is suspended rather than queued in a hidden or offline
 * tab: freshness is worth a request a minute, not a burst of them when a laptop wakes up. It also reads
 * again the moment the tab becomes visible or the connection returns, so a manager who comes back to a
 * backgrounded tab does not stare at a minute-old badge.
 *
 * A failed poll is swallowed on purpose — the next tick retries — but it is remembered: `refreshFailed`
 * is what lets the shell label the screen as showing the last data it could load rather than pretend the
 * read is current.
 */
@Injectable({ providedIn: 'root' })
export class SyncStore implements OnDestroy {
  private readonly api = inject(SyncApi);
  private readonly connectivity = inject(ConnectivityStore);
  private readonly maintenance = inject(MaintenanceStore);
  private readonly session = inject(SessionStore);

  private readonly unreadSignal = signal(0);
  private readonly lastRefreshedSignal = signal<number | null>(null);
  private readonly refreshFailedSignal = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  /** How many inbox messages are unread, for the navigation badge. */
  readonly unreadInboxCount = this.unreadSignal.asReadonly();

  /** When the last successful poll landed, in epoch milliseconds, or null before the first. */
  readonly lastRefreshedAt = this.lastRefreshedSignal.asReadonly();

  /** Whether the most recent poll could not reach the server. */
  readonly refreshFailed = this.refreshFailedSignal.asReadonly();

  /** Whether what is on screen may be out of date — offline, or the last refresh did not reach the server. */
  readonly isStale = computed(() => !this.connectivity.isOnline() || this.refreshFailedSignal());

  /** Begins polling, with one immediate read so the badge is right on arrival. */
  start(): void {
    if (this.timer !== null) {
      return;
    }

    this.refresh();

    this.timer = setInterval(() => this.refresh(), appEnvironment.syncIntervalMs);

    // Read again as soon as freshness can be regained, rather than waiting out the tick.
    globalThis.document?.addEventListener('visibilitychange', this.onVisibilityChange);
    globalThis.addEventListener?.('online', this.onOnline);
  }

  /** Stops polling, so no timer or listener outlives the shell. */
  stop(): void {
    if (this.timer === null) {
      return;
    }

    clearInterval(this.timer);
    this.timer = null;

    globalThis.document?.removeEventListener('visibilitychange', this.onVisibilityChange);
    globalThis.removeEventListener?.('online', this.onOnline);
  }

  /** Drops the count and freshness, so a signed-out device keeps none of the previous manager's state. */
  clear(): void {
    this.unreadSignal.set(0);
    this.lastRefreshedSignal.set(null);
    this.refreshFailedSignal.set(false);
    this.maintenance.clear();
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
      next: (summary) => {
        this.unreadSignal.set(summary.unreadInboxCount);
        this.maintenance.set(summary.readOnly, summary.readOnlyMessage);
        this.lastRefreshedSignal.set(Date.now());
        this.refreshFailedSignal.set(false);
      },
      error: () => {
        // The next tick retries; a poll that has not happened yet is not worth a message.
        this.refreshFailedSignal.set(true);
      },
    });
  }

  /** Reads again on the manager's request, from the stale banner. */
  retry(): void {
    this.refresh();
  }

  private readonly onVisibilityChange = (): void => {
    if (this.isVisible()) {
      this.refresh();
    }
  };

  private readonly onOnline = (): void => {
    this.refresh();
  };

  private isVisible(): boolean {
    return globalThis.document?.visibilityState !== 'hidden';
  }
}
