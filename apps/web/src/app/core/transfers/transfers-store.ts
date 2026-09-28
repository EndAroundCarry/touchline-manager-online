import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { TransfersApi } from './transfers-api';
import { MyMarketBid, TransferHistoryEntry, TransferListing } from './transfers.models';

/**
 * The transfers module's view state (master plan §11.1; `TRF-1`, `TRF-4`, `INT-6`).
 *
 * A feature-scoped store following `FinanceStore`. Nothing is optimistic: a listing, a bid, and a
 * cancellation are server decisions and the screen re-reads them (`§11.2`).
 */
@Injectable({ providedIn: 'root' })
export class TransfersStore {
  private readonly api = inject(TransfersApi);

  private readonly listingsSignal = signal<readonly TransferListing[]>([]);
  private readonly nextCursorSignal = signal<string | null>(null);
  private readonly myListingsSignal = signal<readonly TransferListing[]>([]);
  private readonly myBidsSignal = signal<readonly MyMarketBid[]>([]);
  private readonly historySignal = signal<readonly TransferHistoryEntry[]>([]);
  private readonly historyCursorSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadingMoreSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  /** The open listings read so far. */
  readonly listings = this.listingsSignal.asReadonly();

  /** The club's own listings. */
  readonly myListings = this.myListingsSignal.asReadonly();

  /** The club's own bids. */
  readonly myBids = this.myBidsSignal.asReadonly();

  /** The public transfer history read so far. */
  readonly history = this.historySignal.asReadonly();

  /** Whether a further page of listings exists. */
  readonly hasMore = computed(() => this.nextCursorSignal() !== null);

  /** Whether a further page of history exists. */
  readonly hasMoreHistory = computed(() => this.historyCursorSignal() !== null);

  /** Whether the first reads are in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Whether a further page is in flight. */
  readonly loadingMore = this.loadingMoreSignal.asReadonly();

  /** Why the last request failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Reads the open listings, the club's own activity, and the first page of history. */
  load(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.listingsSignal.set([]);
    this.nextCursorSignal.set(null);
    this.historySignal.set([]);
    this.historyCursorSignal.set(null);

    this.api.getListings(null).subscribe({
      next: (page) => {
        this.listingsSignal.set(page.listings);
        this.nextCursorSignal.set(page.nextCursor);
      },
      error: (error: unknown) => this.fail(error, 'Listings could not be loaded.'),
    });

    this.api.getMine().subscribe({
      next: (activity) => {
        this.myListingsSignal.set(activity.myListings);
        this.myBidsSignal.set(activity.myBids);
      },
      error: (error: unknown) => this.fail(error, 'Your market activity could not be loaded.'),
    });

    this.api.getHistory(null).subscribe({
      next: (page) => {
        this.historySignal.set(page.transfers);
        this.historyCursorSignal.set(page.nextCursor);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => this.fail(error, 'Transfer history could not be loaded.'),
    });
  }

  /** Reads the next page of listings and appends it. */
  loadMore(): void {
    const cursor = this.nextCursorSignal();

    if (cursor === null || this.loadingMoreSignal()) {
      return;
    }

    this.loadingMoreSignal.set(true);

    this.api.getListings(cursor).subscribe({
      next: (page) => {
        this.listingsSignal.set([...this.listingsSignal(), ...page.listings]);
        this.nextCursorSignal.set(page.nextCursor);
        this.loadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingMoreSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'More listings could not be loaded.',
        );
      },
    });
  }

  /** Stories a player for sale, then refreshes the lists. */
  list(playerId: string, minimumFeeMinor: number, seasons: number): void {
    this.api.create(playerId, minimumFeeMinor, seasons, crypto.randomUUID()).subscribe({
      next: () => this.load(),
      error: (error: unknown) => this.fail(error, 'That player could not be listed.'),
    });
  }

  /** Withdraws a listing, then refreshes the lists. */
  cancel(listingId: string): void {
    this.api.cancel(listingId, crypto.randomUUID()).subscribe({
      next: () => this.load(),
      error: (error: unknown) => this.fail(error, 'That listing could not be cancelled.'),
    });
  }

  /** Places or raises a bid, then refreshes the lists. */
  bid(listingId: string, amountMinor: number): void {
    this.api.bid(listingId, amountMinor, crypto.randomUUID()).subscribe({
      next: () => this.load(),
      error: (error: unknown) => this.fail(error, 'That bid could not be placed.'),
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.listingsSignal.set([]);
    this.nextCursorSignal.set(null);
    this.myListingsSignal.set([]);
    this.myBidsSignal.set([]);
    this.historySignal.set([]);
    this.historyCursorSignal.set(null);
    this.loadingSignal.set(false);
    this.loadingMoreSignal.set(false);
    this.errorSignal.set(null);
  }

  private fail(error: unknown, fallback: string): void {
    this.loadingSignal.set(false);
    this.errorSignal.set(error instanceof ApiError ? error.detail : fallback);
  }
}
