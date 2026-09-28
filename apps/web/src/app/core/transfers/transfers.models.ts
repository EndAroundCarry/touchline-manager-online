/**
 * The transfers module's transport mirror (master plan §10.6; `TRF-4`, `INT-6`).
 *
 * Hand-written to match `TouchlineManager.Contracts.Market`. Amounts are minor units; the screens format
 * them and the server does every piece of arithmetic that matters (`INT-1`).
 */

/** One transfer listing as the market reads it (`TRF-4`, `CON-5`, `TRF-5`). */
export interface TransferListing {
  readonly listingId: string;
  readonly playerId: string;
  readonly playerName: string;
  readonly playerShortName: string;
  readonly primaryPosition: string;
  readonly age: number;
  readonly sellerClubId: string;
  readonly sellerClubName: string;
  readonly minimumFeeMinor: number;
  readonly generatedBuyerWageMinor: number;
  readonly generatedContractSeasons: number;
  readonly opensAt: string;
  readonly endsAt: string;
  readonly status: string;
  readonly leadingAmountMinor: number | null;
  readonly bidderCount: number;
  readonly minimumAcceptableBidMinor: number;
  readonly yourBidMinor: number | null;
  readonly version: number;
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
  readonly sellerClubId: string;
  readonly sellerClubName: string;
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
