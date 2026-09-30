import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { GameClockApi } from './game-clock-api';
import { GameClockStatus, GameClockTarget } from './game-clock.models';

/**
 * The non-production season-stepping toolbar's state (ADR-0049).
 *
 * A step is asynchronous: the endpoint enqueues a real job, the worker sets the stored instant and
 * materialises the day, and the day's jobs then run. So the store asks for a step, waits until the clock
 * has actually reached the target, gives the day's jobs a moment to finish, and only then tells the screen
 * to refresh — otherwise a reload would show yesterday's table with today's date on it.
 *
 * It is deliberately server-driven: the toolbar appears only when the API reports a stepped clock, so a
 * manager on a real-time or production host never sees it and the endpoints do not exist to call.
 */
@Injectable({ providedIn: 'root' })
export class GameClockStore implements OnDestroy {
  /** How often the store checks whether a step has landed. */
  private static readonly PollIntervalMs = 1_000;

  /** How long to let a day's materialised jobs run before refreshing. */
  private static readonly SettleMs = 1_500;

  /** How many polls before a step is abandoned, so a stuck worker cannot spin the toolbar forever. */
  private static readonly MaxPolls = 60;

  private readonly api = inject(GameClockApi);

  private readonly availableSignal = signal(false);
  private readonly gameNowSignal = signal<string | null>(null);
  private readonly nextMatchdaySignal = signal<string | null>(null);
  private readonly busySignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly completedSignal = signal(0);

  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Whether the API reports a stepped clock, so the toolbar should show. */
  readonly available = this.availableSignal.asReadonly();

  /** The current game instant, or null before the first read. */
  readonly gameNow = this.gameNowSignal.asReadonly();

  /** The next round's kickoff, or null when the season has no round left to play. */
  readonly nextMatchdayAt = this.nextMatchdaySignal.asReadonly();

  /** Whether a step is in flight. */
  readonly busy = this.busySignal.asReadonly();

  /** The last step's failure, or null. */
  readonly error = this.errorSignal.asReadonly();

  /**
   * Increments each time a step has landed and the day has been given time to finish, so the screen can
   * refresh exactly once per completed step.
   */
  readonly completedTick = this.completedSignal.asReadonly();

  /** Reads the clock once, so the toolbar knows whether it is enabled and what date to show. */
  probe(): void {
    this.api.status().subscribe({
      next: (status) => {
        this.availableSignal.set(true);
        this.apply(status);
      },
      error: () => {
        // A host without the stepped clock (real time, compressed, or production) has no such route.
        this.availableSignal.set(false);
        this.gameNowSignal.set(null);
        this.nextMatchdaySignal.set(null);
      },
    });
  }

  /** Asks for one step and, once it has landed, signals the screen to refresh. */
  advance(target: GameClockTarget): void {
    if (this.busySignal()) {
      return;
    }

    this.busySignal.set(true);
    this.errorSignal.set(null);

    this.api.advance(target).subscribe({
      next: (result) => this.poll(Date.parse(result.targetInstantUtc), 0),
      error: (error: unknown) => {
        this.busySignal.set(false);
        this.errorSignal.set(this.message(error));
      },
    });
  }

  /** Forgets everything. Called when the session ends. */
  clear(): void {
    this.cancelTimer();
    this.availableSignal.set(false);
    this.gameNowSignal.set(null);
    this.nextMatchdaySignal.set(null);
    this.busySignal.set(false);
    this.errorSignal.set(null);
    this.completedSignal.set(0);
  }

  /** Stops any pending poll when the injector is torn down. */
  ngOnDestroy(): void {
    this.cancelTimer();
  }

  private poll(target: number, attempts: number): void {
    if (attempts >= GameClockStore.MaxPolls) {
      this.busySignal.set(false);
      this.errorSignal.set('The step is taking longer than expected; check the worker.');

      return;
    }

    this.timer = setTimeout(() => {
      this.api.status().subscribe({
        next: (status) => {
          this.apply(status);

          if (Date.parse(status.gameNow) >= target) {
            // The clock has reached the target; let the day's jobs finish, then refresh the screen.
            this.timer = setTimeout(() => this.finish(), GameClockStore.SettleMs);

            return;
          }

          this.poll(target, attempts + 1);
        },
        error: () => this.poll(target, attempts + 1),
      });
    }, GameClockStore.PollIntervalMs);
  }

  private finish(): void {
    this.cancelTimer();
    this.busySignal.set(false);
    this.completedSignal.update((count) => count + 1);
  }

  private apply(status: GameClockStatus): void {
    this.gameNowSignal.set(status.gameNow);
    this.nextMatchdaySignal.set(status.nextMatchdayAt);
  }

  private cancelTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private message(error: unknown): string {
    return error instanceof ApiError ? error.detail : 'The clock could not be advanced.';
  }
}
