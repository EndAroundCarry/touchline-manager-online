import { APIRequestContext, expect } from '@playwright/test';

/**
 * The helpers the market journey needs beyond the browser.
 *
 * Signing in to drive the API as the harness and asking the non-production trigger to resolve a listing go
 * through the real endpoints, so a journey that passes has exercised the same code a client would. The JSON
 * shapes mirror the API's own response records (`TransferListingResponse`, `MyMarketActivityResponse`,
 * `TransferHistoryResponse`).
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

/** One listing as the market reads it. */
export interface MarketListingRead {
  readonly listingId: string;
  readonly playerId: string;
  readonly playerName: string;
  readonly status: string;
  readonly minimumFeeMinor: number;
  readonly leadingAmountMinor: number | null;
  readonly bidderCount: number;
  readonly minimumAcceptableBidMinor: number;
}

/** One of the caller's own bids. */
export interface MarketBidRead {
  readonly listingId: string;
  readonly playerName: string;
  readonly amountMinor: number;
  readonly status: string;
}

/** The caller's own listings and bids. */
export interface MyMarketActivityRead {
  readonly myListings: readonly MarketListingRead[];
  readonly myBids: readonly MarketBidRead[];
}

/** One completed transfer. */
export interface TransferHistoryEntryRead {
  readonly listingId: string;
  readonly playerName: string;
  readonly sellerClubName: string;
  readonly buyerClubName: string;
  readonly feeMinor: number;
}

/** The public transfer history. */
export interface TransferHistoryRead {
  readonly transfers: readonly TransferHistoryEntryRead[];
}

/** Signs in through the API so the journey can act as the harness. */
export async function apiAccessToken(
  request: APIRequestContext,
  email: string,
  password: string,
): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email, password },
  });

  expect(response.ok()).toBe(true);

  return ((await response.json()) as { accessToken: string }).accessToken;
}

/** Reads the club's own listings and bids. */
export async function readMyActivity(
  request: APIRequestContext,
  token: string,
): Promise<MyMarketActivityRead> {
  const response = await request.get(`${apiBaseUrl}/api/v1/transfers/mine`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(response.ok()).toBe(true);

  return (await response.json()) as MyMarketActivityRead;
}

/** Reads the public history of completed transfers. */
export async function readTransferHistory(
  request: APIRequestContext,
  token: string,
): Promise<TransferHistoryRead> {
  const response = await request.get(`${apiBaseUrl}/api/v1/transfers/history`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(response.ok()).toBe(true);

  return (await response.json()) as TransferHistoryRead;
}

/** Waits for a listing to appear in the club's own activity, which the transfers form writes. */
export async function waitForMyListing(
  request: APIRequestContext,
  token: string,
  timeoutMs = 20_000,
): Promise<MarketListingRead> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const activity = await readMyActivity(request, token);

    if (activity.myListings.length > 0) {
      return activity.myListings[0];
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  throw new Error(`No listing appeared in the club's activity within ${timeoutMs}ms.`);
}

/**
 * Asks the non-production trigger to resolve a listing now.
 *
 * The endpoint enqueues the listing's real resolution job; the worker settles it exactly as it would at its
 * window (ADR-0016). It is reached without a bearer token, exactly as the matchday trigger is, because it is
 * gated by configuration rather than by who is asking.
 */
export async function playAuction(request: APIRequestContext, listingId: string): Promise<void> {
  const response = await request.post(`${apiBaseUrl}/api/v1/ops/diagnostics/play-auction`, {
    data: { listingId },
  });

  expect(response.status(), `the auction trigger should accept the listing (${listingId})`).toBe(202);
}

/** Waits for a listing to appear in the public transfer history. */
export async function waitForTransfer(
  request: APIRequestContext,
  token: string,
  listingId: string,
  timeoutMs = 60_000,
): Promise<TransferHistoryEntryRead> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const history = await readTransferHistory(request, token);
    const transfer = history.transfers.find((entry) => entry.listingId === listingId);

    if (transfer !== undefined) {
      return transfer;
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`Listing ${listingId} did not transfer within ${timeoutMs}ms.`);
}
