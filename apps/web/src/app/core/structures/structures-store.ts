import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { isEmbargo } from '../finance/embargo';
import { StructuresApi } from './structures-api';
import { kindLabel, readyOn } from './structures-presentation';
import { StructureKind, Structures } from './structures.models';

/**
 * The snack bar's and the merchandise shop's view state (`STRC-1`…`STRC-9`).
 *
 * A feature-scoped store following `StadiumStore`. Nothing is optimistic: levels, takings and the purse come
 * from the server, which is the only authority on money, so an upgrade is reflected only once the server has
 * charged for it. An order is made against the version the server last returned for that building. A stale
 * one is not a failure to hide: the order is refused, both buildings are reloaded, and the manager is told
 * nothing was charged so they decide again against what is really there (`§11.2`).
 */
@Injectable({ providedIn: 'root' })
export class StructuresStore {
  private readonly api = inject(StructuresApi);

  private readonly structuresSignal = signal<Structures | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly upgradingSignal = signal<StructureKind | null>(null);
  private readonly upgradeErrorSignal = signal<string | null>(null);
  private readonly upgradeEmbargoedSignal = signal(false);
  private readonly upgradedMessageSignal = signal<string | null>(null);

  /** The club's two buildings. */
  readonly structures = this.structuresSignal.asReadonly();

  /** Whether the first read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** The building an upgrade order is in flight for, or null. */
  readonly upgrading = this.upgradingSignal.asReadonly();

  /** Why the last upgrade order was refused. */
  readonly upgradeError = this.upgradeErrorSignal.asReadonly();

  /** Whether the last order was refused because the club is in the red (`DEBT-3`). */
  readonly upgradeEmbargoed = this.upgradeEmbargoedSignal.asReadonly();

  /** What the last successful upgrade order bought, and when the work finishes. */
  readonly upgradedMessage = this.upgradedMessageSignal.asReadonly();

  /** Whether a read has produced the buildings. */
  readonly loaded = computed(() => this.structuresSignal() !== null);

  /**
   * Reads the buildings.
   *
   * @param showProgress Whether the screen should swap to its loading state. A reload after a refused order
   *   keeps the buildings on screen instead, so the manager sees them change rather than vanish.
   */
  load(showProgress = true): void {
    this.loadingSignal.set(showProgress);
    this.errorSignal.set(null);

    this.api.get().subscribe({
      next: (structures) => {
        this.structuresSignal.set(structures);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your buildings could not be loaded.',
        );
      },
    });
  }

  /**
   * Orders the next level of one building.
   *
   * The version sent is always the one the server last returned for that building, never a remembered copy.
   */
  upgrade(kind: StructureKind): void {
    const structures = this.structuresSignal();

    if (structures === null || this.upgradingSignal() !== null) {
      return;
    }

    const building = kind === 'snack_bar' ? structures.snackBar : structures.merchandise;

    this.upgradingSignal.set(kind);
    this.upgradeErrorSignal.set(null);
    this.upgradeEmbargoedSignal.set(false);
    this.upgradedMessageSignal.set(null);

    this.api.upgrade(kind, building.version).subscribe({
      next: (upgraded) => {
        this.structuresSignal.set(upgraded);
        this.upgradingSignal.set(null);

        const started = kind === 'snack_bar' ? upgraded.snackBar : upgraded.merchandise;

        this.upgradedMessageSignal.set(
          started.building === null
            ? `${kindLabel(kind)} upgrade ordered.`
            : `${kindLabel(kind)} upgrade to level ${started.building.targetLevel} ordered. The work finishes ${readyOn(started.building.dueAt)}.`,
        );
      },
      error: (error: unknown) => this.onUpgradeError(error),
    });
  }

  /** Clears the upgrade messages, so a stale confirmation does not outlive the tab it belonged to. */
  dismissMessages(): void {
    this.upgradeErrorSignal.set(null);
    this.upgradeEmbargoedSignal.set(false);
    this.upgradedMessageSignal.set(null);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.structuresSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
    this.upgradingSignal.set(null);
    this.upgradeErrorSignal.set(null);
    this.upgradeEmbargoedSignal.set(false);
    this.upgradedMessageSignal.set(null);
  }

  private onUpgradeError(error: unknown): void {
    this.upgradingSignal.set(null);

    if (error instanceof ApiError && error.isPreconditionFailed) {
      // The building changed under the manager: another device ordered, or the work finished. Show what is
      // there now and say so; the order was not paid for.
      this.load(false);
      this.upgradeErrorSignal.set(
        'The building changed since you last looked. It has been reloaded and nothing was charged. Check it and order again.',
      );

      return;
    }

    this.upgradeErrorSignal.set(
      error instanceof ApiError ? error.detail : 'The upgrade could not be ordered.',
    );
    this.upgradeEmbargoedSignal.set(isEmbargo(error));
  }
}
