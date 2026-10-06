import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { StadiumApi } from './stadium-api';
import { Stadium, StadiumStandCode } from './stadium.models';

/**
 * The stadium's view state (`STAD-1`…`STAD-6`).
 *
 * A feature-scoped store following `FinanceStore`. Nothing is optimistic: places, prices and the purse come
 * from the server, which is the only authority on money, so a build is reflected only once the server has
 * charged for it. A stale version is not a failure to hide — the order is refused, the current ground is
 * reloaded, and the manager is told it changed so they decide again against what is really there (`§11.2`).
 */
@Injectable({ providedIn: 'root' })
export class StadiumStore {
  private readonly api = inject(StadiumApi);

  private readonly stadiumSignal = signal<Stadium | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly buildingSignal = signal<StadiumStandCode | null>(null);
  private readonly buildErrorSignal = signal<string | null>(null);
  private readonly builtMessageSignal = signal<string | null>(null);

  /** The club's stadium. */
  readonly stadium = this.stadiumSignal.asReadonly();

  /** Whether the first read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** The stand a build order is in flight for, or null. */
  readonly building = this.buildingSignal.asReadonly();

  /** Why the last build order was refused. */
  readonly buildError = this.buildErrorSignal.asReadonly();

  /** What the last successful build order bought. */
  readonly builtMessage = this.builtMessageSignal.asReadonly();

  /** Whether a read has produced a stadium. */
  readonly loaded = computed(() => this.stadiumSignal() !== null);

  /**
   * Reads the stadium.
   *
   * @param showProgress Whether the screen should swap to its loading state. A reload after a refused order
   *   keeps the ground on screen instead, so the manager sees it change rather than vanish.
   */
  load(showProgress = true): void {
    this.loadingSignal.set(showProgress);
    this.errorSignal.set(null);

    this.api.get().subscribe({
      next: (stadium) => {
        this.stadiumSignal.set(stadium);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your stadium could not be loaded.',
        );
      },
    });
  }

  /**
   * Adds places of one kind.
   *
   * The version sent is always the one the server last returned, never a remembered copy, so a second order
   * after a successful first one is made against the ground the first one produced.
   *
   * @param phrase What is being built, worded for the confirmation ("10 standing places", "1 seat").
   */
  build(stand: StadiumStandCode, count: number, phrase: string): void {
    const stadium = this.stadiumSignal();

    if (
      stadium === null ||
      this.buildingSignal() !== null ||
      !Number.isInteger(count) ||
      count < 1
    ) {
      return;
    }

    this.buildingSignal.set(stand);
    this.buildErrorSignal.set(null);
    this.builtMessageSignal.set(null);

    this.api.build({ stand, count }, stadium.version).subscribe({
      next: (built) => {
        this.stadiumSignal.set(built);
        this.buildingSignal.set(null);
        this.builtMessageSignal.set(
          built.level > stadium.level
            ? `${phrase} built. The stadium is now level ${built.level}.`
            : `${phrase} built.`,
        );
      },
      error: (error: unknown) => this.onBuildError(error),
    });
  }

  /** Clears the build messages, so a stale confirmation does not outlive the form it belonged to. */
  dismissMessages(): void {
    this.buildErrorSignal.set(null);
    this.builtMessageSignal.set(null);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.stadiumSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
    this.buildingSignal.set(null);
    this.buildErrorSignal.set(null);
    this.builtMessageSignal.set(null);
  }

  private onBuildError(error: unknown): void {
    this.buildingSignal.set(null);

    if (error instanceof ApiError && error.isPreconditionFailed) {
      // The ground changed under the manager — another device built, or an earlier order landed. Show the
      // current ground and say so; the order was not paid for.
      this.load(false);
      this.buildErrorSignal.set(
        'The stadium changed since you last looked. It has been reloaded and nothing was charged. Check it and order again.',
      );

      return;
    }

    this.buildErrorSignal.set(
      error instanceof ApiError ? error.detail : 'The places could not be built.',
    );
  }
}
