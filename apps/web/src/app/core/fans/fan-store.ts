import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiError } from '../api/api-error';
import { FanApi } from './fan-api';
import { FanBase, FanHistoryScope } from './fan.models';

/**
 * The fan base's view state (`FAN-1`…`FAN-10`).
 *
 * A feature-scoped store following `StadiumStore`. Three screens read it: the Fan Base page, and the Office and
 * the fixtures screen for the expectation chip. Each asks for a fresh read when it opens, because a match, a
 * weekly review or a takeover may have moved the fans since. A read still in flight when the next starts is
 * dropped, so a slow answer for the history the manager has already left cannot overwrite the one they chose.
 */
@Injectable({ providedIn: 'root' })
export class FanStore {
  private readonly api = inject(FanApi);

  private readonly fansSignal = signal<FanBase | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  private inFlight: Subscription | null = null;

  /** The club's fans, or null before the first read. */
  readonly fans = this.fansSignal.asReadonly();

  /** Whether a read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed, or null. */
  readonly error = this.errorSignal.asReadonly();

  /** The next match's expectation, or null when the season has no match left or nothing has been read. */
  readonly nextFixture = computed(() => this.fansSignal()?.nextFixture ?? null);

  /**
   * Reads the fans.
   *
   * @param history How much history the charts need. The chip on the Office and the fixtures screen does not
   *   draw charts, so it takes the default, which is the shorter read.
   * @param showProgress Whether the screen should swap to its loading state. Switching the history keeps the
   *   page on screen instead.
   */
  load(history: FanHistoryScope = 'tenure', showProgress = true): void {
    this.inFlight?.unsubscribe();
    this.loadingSignal.set(showProgress);
    this.errorSignal.set(null);

    this.inFlight = this.api.get(history).subscribe({
      next: (fans) => {
        this.fansSignal.set(fans);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your fans could not be loaded.',
        );
      },
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.fansSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }
}
