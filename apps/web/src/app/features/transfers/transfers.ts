import { Component, inject } from '@angular/core';
import { ConnectivityStore } from '../../core/connectivity/connectivity-store';
import { SquadStore } from '../../core/squad/squad-store';
import { TransfersStore } from '../../core/transfers/transfers-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The transfers screen (master plan §11.1; `TRF-1`, `TRF-4`, `TRF-6`, `INT-6`).
 *
 * Lists a player from the manager's own contracts, browses the open auctions, and shows the club's own
 * listings and bids beside the public history. Nothing here is optimistic: a listing, a bid, and a
 * cancellation are server decisions and the screen re-reads them (`§11.2`).
 */
@Component({
  selector: 'app-transfers',
  templateUrl: './transfers.html',
})
export class Transfers {
  private readonly store = inject(TransfersStore);
  private readonly squad = inject(SquadStore);
  private readonly connectivity = inject(ConnectivityStore);

  protected readonly listings = this.store.listings;
  protected readonly myListings = this.store.myListings;
  protected readonly myBids = this.store.myBids;
  protected readonly history = this.store.history;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly hasMore = this.store.hasMore;
  protected readonly error = this.store.error;

  /** The manager's own contracts, which are the players they may list (`TRF-1`). */
  protected readonly contracts = this.squad.contracts;

  /** Whether a write is allowed; offline a listing, a bid, and a cancellation are all refused. */
  protected readonly canMutate = this.connectivity.isOnline;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly formErrorClass = FORM_ERROR;

  constructor() {
    this.store.load();
    this.squad.loadContracts().subscribe({ error: () => undefined });
  }

  /** Lists the chosen player for sale. */
  protected onList(event: Event): void {
    event.preventDefault();

    const data = new FormData(event.target as HTMLFormElement);
    const playerId = data.get('playerId');

    if (typeof playerId !== 'string' || playerId.length === 0) {
      return;
    }

    const fee = Number(data.get('minimumFeeMinor') ?? 0);
    const seasons = Number(data.get('seasons') ?? 1);

    this.store.list(playerId, fee, seasons);
  }

  /** Places or raises a bid from a listing's row. */
  protected onBid(event: Event, listingId: string): void {
    event.preventDefault();

    const data = new FormData(event.target as HTMLFormElement);
    const amount = Number(data.get('amount') ?? 0);

    this.store.bid(listingId, amount);
  }

  /** Withdraws one of the club's listings. */
  protected cancel(listingId: string): void {
    this.store.cancel(listingId);
  }

  /** Reads the next page of listings. */
  protected loadMore(): void {
    this.store.loadMore();
  }

  /** Formats an amount in minor units. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
