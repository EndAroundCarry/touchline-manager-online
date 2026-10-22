import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { POSITION_FAMILY_OPTIONS, positionLabel } from '../../core/squad/squad-presentation';
import { SquadStore } from '../../core/squad/squad-store';
import { TransfersStore } from '../../core/transfers/transfers-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  NAME_LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  SELECT_INPUT,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';
import { BidForm } from '../../shared/ui/bid-form/bid-form';
import { EmbargoNotice } from '../../shared/ui/embargo-notice/embargo-notice';
import { PlayerCardTrigger } from '../../shared/ui/player-card/player-card-trigger';

/** The two sets of open auctions the screen browses. */
export type TransferTab = 'club' | 'free-agents';

/**
 * The transfers screen (master plan §11.1; `TRF-1`, `TRF-4`, `TRF-6`, `INT-6`, `FA-5`).
 *
 * Lists a player from the manager's own contracts, browses the open auctions in two tabs (the clubs' listings
 * and the weekly free-agent pool), and shows the club's own listings and bids beside the public history. Nothing here is optimistic: a listing, a bid, and a
 * cancellation are server decisions and the screen re-reads them (`§11.2`).
 */
@Component({
  selector: 'app-transfers',
  imports: [RouterLink, PlayerCardTrigger, BidForm, EmbargoNotice],
  templateUrl: './transfers.html',
})
export class Transfers {
  private readonly store = inject(TransfersStore);
  private readonly squad = inject(SquadStore);
  private readonly maintenance = inject(MaintenanceStore);

  protected readonly listings = this.store.listings;
  protected readonly myListings = this.store.myListings;
  protected readonly myBids = this.store.myBids;
  protected readonly history = this.store.history;
  protected readonly freeAgents = this.store.freeAgents;
  protected readonly freeAgentFamily = this.store.freeAgentFamily;
  protected readonly hasMoreFreeAgents = this.store.hasMoreFreeAgents;
  protected readonly freeAgentsLoading = this.store.freeAgentsLoading;
  protected readonly freeAgentsLoadingMore = this.store.freeAgentsLoadingMore;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly hasMore = this.store.hasMore;
  protected readonly error = this.store.error;
  protected readonly embargoed = this.store.embargoed;

  /** The manager's own contracts, which are the players they may list (`TRF-1`). */
  protected readonly contracts = this.squad.contracts;

  /** Whether a write is allowed; offline or read-only a listing, a bid, and a cancellation are refused. */
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly nameLinkClass = NAME_LINK;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly selectClass = SELECT_INPUT;
  protected readonly familyOptions = POSITION_FAMILY_OPTIONS;

  /** The tabs, in the order they are shown. */
  protected readonly tabs: readonly { readonly key: TransferTab; readonly label: string }[] = [
    { key: 'club', label: 'Transfer list' },
    { key: 'free-agents', label: 'Free agents' },
  ];

  /** The tab currently open. */
  protected readonly activeTab = signal<TransferTab>('club');
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
  protected onBid(listingId: string, amount: number): void {
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

  /** Reads the next page of free agents. */
  protected loadMoreFreeAgents(): void {
    this.store.loadMoreFreeAgents();
  }

  /** Narrows the free-agent pool to a position family, or to any when empty. */
  protected onFamilyChange(event: Event): void {
    this.store.setFreeAgentFamily((event.target as HTMLSelectElement).value);
  }

  /** Opens a tab. */
  protected selectTab(tab: TransferTab): void {
    this.activeTab.set(tab);
  }

  /** Moves between tabs with the arrow keys, Home and End, as the tabs pattern expects. */
  protected onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % this.tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % this.tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;

    if (target < 0) {
      return;
    }

    event.preventDefault();
    this.selectTab(this.tabs[target].key);
    (
      document.getElementById(`transfers-tab-${this.tabs[target].key}`) as HTMLElement | null
    )?.focus();
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** Formats a contract length. */
  protected seasons(count: number): string {
    return count === 1 ? '1 season' : `${count} seasons`;
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
