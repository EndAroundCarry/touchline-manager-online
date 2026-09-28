import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  MyMarketActivity,
  TransferHistoryPage,
  TransferListing,
  TransferListingsPage,
} from './transfers.models';

/**
 * The transfers module's HTTP surface (master plan §10.6; `TRF-1`, `TRF-4`, `INT-6`).
 *
 * The club is derived from the authenticated tenure, so nothing here names a seller or a bidder. A listing,
 * a cancellation, and a bid are commands and carry an idempotency key, so a retried request is a replay
 * rather than a second listing or a double charge (`INT-2`, `T-4`).
 */
@Injectable({ providedIn: 'root' })
export class TransfersApi {
  private readonly api = inject(ApiClient);

  /** Browses open listings. */
  getListings(cursor: string | null): Observable<TransferListingsPage> {
    const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`;

    return this.api.get<TransferListingsPage>(`/transfers/listings${query}`);
  }

  /** Reads one listing. */
  getListing(listingId: string): Observable<TransferListing> {
    return this.api.get<TransferListing>(`/transfers/listings/${listingId}`);
  }

  /** Reads the club's own listings and bids. */
  getMine(): Observable<MyMarketActivity> {
    return this.api.get<MyMarketActivity>('/transfers/mine');
  }

  /** Reads the public transfer history. */
  getHistory(cursor: string | null): Observable<TransferHistoryPage> {
    const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`;

    return this.api.get<TransferHistoryPage>(`/transfers/history${query}`);
  }

  /** Lists an owned player for sale. */
  create(
    playerId: string,
    minimumFeeMinor: number,
    seasons: number,
    idempotencyKey: string,
  ): Observable<TransferListing> {
    return this.api.post<
      TransferListing,
      { playerId: string; minimumFeeMinor: number; seasons: number }
    >('/transfers/listings', { playerId, minimumFeeMinor, seasons }, { idempotencyKey });
  }

  /** Withdraws a listing. */
  cancel(listingId: string, idempotencyKey: string): Observable<TransferListing> {
    return this.api.delete<TransferListing>(`/transfers/listings/${listingId}`, { idempotencyKey });
  }

  /** Places or raises a bid. */
  bid(listingId: string, amountMinor: number, idempotencyKey: string): Observable<TransferListing> {
    return this.api.post<TransferListing, { amountMinor: number }>(
      `/transfers/listings/${listingId}/bids`,
      { amountMinor },
      { idempotencyKey },
    );
  }
}
