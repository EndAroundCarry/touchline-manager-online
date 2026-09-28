import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { FinanceApi } from './finance-api';
import { FinanceLedgerEntry, FinanceSummary } from './finance.models';

/**
 * The finance module's view state (master plan §11.1; `FIN-3`…`FIN-9`).
 *
 * A feature-scoped store following `InboxStore`. The summary is one read and the ledger is walked by cursor,
 * so the screen never asks for an offset the server does not offer and an entry written mid-read cannot shift
 * the window. Nothing here is optimistic: balances come from the server, which is the only authority on money
 * (`§11.2`).
 */
@Injectable({ providedIn: 'root' })
export class FinanceStore {
  private readonly api = inject(FinanceApi);

  private readonly summarySignal = signal<FinanceSummary | null>(null);
  private readonly ledgerSignal = signal<readonly FinanceLedgerEntry[]>([]);
  private readonly nextCursorSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadingMoreSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  /** The club's money and its season totals. */
  readonly summary = this.summarySignal.asReadonly();

  /** The ledger entries read so far, newest first. */
  readonly ledger = this.ledgerSignal.asReadonly();

  /** Whether a further page of the ledger exists. */
  readonly hasMore = computed(() => this.nextCursorSignal() !== null);

  /** Whether the first read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Whether a further page is in flight. */
  readonly loadingMore = this.loadingMoreSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /**
   * Reads only the summary, for the dashboard's warning card.
   *
   * The ledger is a separate, larger read and the dashboard does not show it, so asking for it there would be
   * work nobody looks at.
   */
  loadSummary(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    this.api.getSummary().subscribe({
      next: (summary) => {
        this.summarySignal.set(summary);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => this.fail(error, 'Your finances could not be loaded.'),
    });
  }

  /** Reads the summary, then the first page of the ledger. */
  load(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.ledgerSignal.set([]);
    this.nextCursorSignal.set(null);

    this.api.getSummary().subscribe({
      next: (summary) => {
        this.summarySignal.set(summary);

        // A second read, because the ledger is a different resource; a failure here leaves the summary
        // visible rather than blanking the whole screen.
        this.api.getLedger(null).subscribe({
          next: (page) => {
            this.ledgerSignal.set(page.entries);
            this.nextCursorSignal.set(page.nextCursor);
            this.loadingSignal.set(false);
          },
          error: (error: unknown) => this.fail(error, 'Your ledger could not be loaded.'),
        });
      },
      error: (error: unknown) => this.fail(error, 'Your finances could not be loaded.'),
    });
  }

  /** Reads the next page and appends it. */
  loadMore(): void {
    const cursor = this.nextCursorSignal();

    if (cursor === null || this.loadingMoreSignal()) {
      return;
    }

    this.loadingMoreSignal.set(true);
    this.errorSignal.set(null);

    this.api.getLedger(cursor).subscribe({
      next: (page) => {
        this.ledgerSignal.set([...this.ledgerSignal(), ...page.entries]);
        this.nextCursorSignal.set(page.nextCursor);
        this.loadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingMoreSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Older entries could not be loaded.',
        );
      },
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.summarySignal.set(null);
    this.ledgerSignal.set([]);
    this.nextCursorSignal.set(null);
    this.loadingSignal.set(false);
    this.loadingMoreSignal.set(false);
    this.errorSignal.set(null);
  }

  private fail(error: unknown, fallback: string): void {
    this.loadingSignal.set(false);
    this.errorSignal.set(error instanceof ApiError ? error.detail : fallback);
  }
}
