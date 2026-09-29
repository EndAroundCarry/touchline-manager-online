import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { NewsApi } from './news-api';
import { NewsItem } from './news.models';

/**
 * The news feed's state (`COM-1`).
 *
 * One store for one screen, following the inbox: a keyset walk that appends rather than replaces, and
 * a failure that blanks the list rather than leaving the last page up.
 */
@Injectable({ providedIn: 'root' })
export class NewsStore {
  private readonly api = inject(NewsApi);

  private readonly itemsSignal = signal<readonly NewsItem[]>([]);
  private readonly nextCursorSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadingMoreSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly hasMoreSignal = signal(false);

  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly loadingMore = this.loadingMoreSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly hasMore = this.hasMoreSignal.asReadonly();

  /** Reads the first page from the top. */
  load(divisionId: string | null = null, countryId: string | null = null): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.itemsSignal.set([]);
    this.nextCursorSignal.set(null);

    this.api.list(null, divisionId, countryId).subscribe({
      next: (page) => {
        this.itemsSignal.set(page.items);
        this.nextCursorSignal.set(page.nextCursor);
        this.setHasMore(page.nextCursor !== null);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.setHasMore(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'The news feed could not be loaded.',
        );
      },
    });
  }

  /** Reads the next page and appends it. */
  loadMore(divisionId: string | null = null, countryId: string | null = null): void {
    const cursor = this.nextCursorSignal();

    if (cursor === null || this.loadingMoreSignal()) {
      return;
    }

    this.loadingMoreSignal.set(true);

    this.api.list(cursor, divisionId, countryId).subscribe({
      next: (page) => {
        this.itemsSignal.update((items) => [...items, ...page.items]);
        this.nextCursorSignal.set(page.nextCursor);
        this.setHasMore(page.nextCursor !== null);
        this.loadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingMoreSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Older news could not be loaded.',
        );
      },
    });
  }

  /** Drops the feed, on the session ending. */
  clear(): void {
    this.itemsSignal.set([]);
    this.nextCursorSignal.set(null);
    this.hasMoreSignal.set(false);
    this.errorSignal.set(null);
  }

  /**
   * Records whether a further page exists.
   *
   * `hasMore` is a signal derived from the cursor rather than a stored flag; keeping the derivation in
   * one place stops the two from disagreeing.
   */
  private setHasMore(value: boolean): void {
    this.hasMoreSignal.set(value);
  }
}
