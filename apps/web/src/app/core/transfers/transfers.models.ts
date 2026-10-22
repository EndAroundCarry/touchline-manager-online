/**
 * The transfers module's transport mirror (master plan §10.6; `TRF-4`, `INT-6`).
 *
 * Hand-written to match `Tactical11Manager.Contracts.Market`. Amounts are minor units; the screens format
 * them and the server does every piece of arithmetic that matters (`INT-1`).
 */
import { WageImpact } from '../finance/finance.models';

/** Who sells a listed player (`FA-5`). */
export type ListingKind = 'club' | 'free_agent';

/** One transfer listing as the market reads it (`TRF-4`, `CON-5`, `TRF-5`). */
export interface TransferListing {
  readonly listingId: string;
  readonly playerId: string;
  readonly playerName: string;
  readonly playerShortName: string;
  readonly primaryPosition: string;
  readonly age: number;

  /** The displayed ability, the mean of the attributes a scouting card shows (1-20). */
  readonly ability: number;

  /** Who sells the player: a `club`, or the weekly `free_agent` pool (`FA-5`). */
  readonly kind: ListingKind;

  /** The selling club, or null for a free agent. */
  readonly sellerClubId: string | null;
  readonly sellerClubName: string | null;
  readonly minimumFeeMinor: number;
  readonly generatedBuyerSeasonWageMinor: number;
  readonly generatedContractSeasons: number;
  readonly opensAt: string;
  readonly endsAt: string;
  readonly status: string;
  readonly leadingAmountMinor: number | null;
  readonly bidderCount: number;
  readonly minimumAcceptableBidMinor: number;
  readonly yourBidMinor: number | null;
  readonly version: number;

  /**
   * What signing the player would do to the viewer's club, priced at the smallest bid the listing accepts (`WAGE-1`).
   * Absent on the club's own listing and on one it already leads.
   */
  readonly wageImpact?: WageImpact | null;
}

/** One page of listings. */
export interface TransferListingsPage {
  readonly listings: readonly TransferListing[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}

/** One of the club's own bids (`TRF-4`). */
export interface MyMarketBid {
  readonly listingId: string;
  readonly playerId: string;
  readonly playerName: string;
  readonly amountMinor: number;
  readonly status: string;
  readonly endsAt: string;
}

/** The club's own market activity. */
export interface MyMarketActivity {
  readonly myListings: readonly TransferListing[];
  readonly myBids: readonly MyMarketBid[];
  readonly serverTime: string;
}

/** One completed transfer in the public history (`INT-6`). */
export interface TransferHistoryEntry {
  readonly listingId: string;
  readonly playerId: string;
  readonly playerName: string;

  /** The selling club, or null when the player was a free agent. */
  readonly sellerClubId: string | null;
  readonly sellerClubName: string | null;
  readonly buyerClubId: string;
  readonly buyerClubName: string;
  readonly feeMinor: number;
  readonly resolvedAt: string;
}

/** One page of public transfer history. */
export interface TransferHistoryPage {
  readonly transfers: readonly TransferHistoryEntry[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}
