import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { appEnvironment } from '../config/app-environment';
import { StatusApi } from './status-api';
import { PublicStatus } from './status.models';

/**
 * The public service status (`F-55`, ADR-0050).
 *
 * The status page keeps it live: `start()` reads once and then on an interval while the page is open, and
 * reads again the moment the tab becomes visible, so a visitor who comes back to a backgrounded tab is not
 * looking at a stale "Operational". The interval is stopped when the page goes away, so nothing polls from a
 * screen that is not on show.
 *
 * A failed read is remembered rather than swallowed, because on this page the failure *is* the message: it
 * says the client could not reach the server rather than pretending the service is down.
 */
@Injectable({ providedIn: 'root' })
export class StatusStore implements OnDestroy {
  private readonly api = inject(StatusApi);

  private readonly statusSignal = signal<PublicStatus | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly failedSignal = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  /** The last status read, or null before the first successful read. */
  readonly status = this.statusSignal.asReadonly();

  /** Whether a read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Whether the most recent read could not reach the server. */
  readonly failed = this.failedSignal.asReadonly();

  /** Begins polling, with one immediate read. */
  start(): void {
    if (this.timer !== null) {
      return;
    }

    this.refresh();

    this.timer = setInterval(() => this.refresh(), appEnvironment.statusIntervalMs);

    globalThis.document?.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  /** Stops polling, so no timer or listener outlives the page. */
  stop(): void {
    if (this.timer === null) {
      return;
    }

    clearInterval(this.timer);
    this.timer = null;

    globalThis.document?.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  /** Stops the interval when the injector is torn down. */
  ngOnDestroy(): void {
    this.stop();
  }

  /** Reads the status once; the status page's interval and the legal pages both call this. */
  refresh(): void {
    this.loadingSignal.set(true);

    this.api.summary().subscribe({
      next: (status) => {
        this.statusSignal.set(status);
        this.failedSignal.set(false);
        this.loadingSignal.set(false);
      },
      error: () => {
        // The last status is kept: it is more useful to show a moment-old "Operational" with a failure note
        // than to blank the page.
        this.failedSignal.set(true);
        this.loadingSignal.set(false);
      },
    });
  }

  private readonly onVisibilityChange = (): void => {
    if (globalThis.document?.visibilityState !== 'hidden') {
      this.refresh();
    }
  };
}
