import { APIRequestContext, expect, Page, test } from '@playwright/test';
import { createAccount, TestAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';
import { confirmKitColours } from '../support/kit-colours';
import {
  apiAccessToken,
  playAuction,
  readMyActivity,
  waitForMyListing,
  waitForTransfer,
} from '../support/market';

/**
 * The Stage 10 exit criterion at the interface level (master plan §15.5 journey 4, §16 Stage 10):
 * a seller lists a player, two rivals bid, the displaced one is told, and the auction settles with the
 * winner's club taking the player and the money moving.
 *
 * Three managers drive the real screens on three browser contexts, and the harness resolves the auction
 * through the non-production trigger. Nothing is stubbed: the listing, the bids, the reservations, and the
 * settlement are the server's own, performed by the real worker against a real database.
 *
 * It runs against its own throwaway database (`playwright.market.config.ts`), reset and reseeded each run,
 * because a settled transfer permanently moves a player.
 */

const fee = 1_000_000;

/** Onboards a manager into a club the way the onboarding journey does, and returns their access token. */
async function onboard(
  page: Page,
  request: APIRequestContext,
  account: TestAccount,
): Promise<string> {
  await createVerifiedManager(page, request, account);

  await page.goto('/onboarding/manager');
  await page.getByRole('button', { name: 'Create my profile' }).click();
  await page.getByRole('button', { name: 'See the clubs' }).first().click();
  await page
    .getByRole('button', { name: /^Take over/ })
    .first()
    .click();
  await confirmKitColours(page);

  await expect(page).toHaveURL(/\/dashboard$/);

  return apiAccessToken(request, account.email, account.password);
}

/** Locates the row of the open-listings table for a player. */
function listingRow(page: Page, playerName: string) {
  const openListings = page
    .locator('table')
    .filter({ has: page.getByRole('columnheader', { name: 'Leading' }) });

  return openListings.locator('tbody tr', { hasText: playerName });
}

/**
 * Locates the section whose own heading is the given one.
 *
 * The direct-child selector matters: the transfers component is itself a `<section>`, so a plain
 * `section:has(h2)` would match the whole page and an assertion scoped to it would pass on text anywhere in
 * it — the "Leading" column header rather than a bid's status.
 */
function section(page: Page, heading: string) {
  return page.locator(`section:has(> h2:text-is("${heading}"))`);
}

test.describe('the transfer market', () => {
  test('a manager lists a player, two rivals bid, and the auction settles', async ({
    browser,
    request,
  }) => {
    const sellerAccount = createAccount();
    const firstRivalAccount = createAccount();
    const secondRivalAccount = createAccount();

    const sellerContext = await browser.newContext();
    const sellerPage = await sellerContext.newPage();
    const sellerToken = await onboard(sellerPage, request, sellerAccount);

    // The seller lists a player through the transfers screen (TRF-1, CON-5).
    await sellerPage.goto('/transfers');

    const playerSelect = sellerPage.locator('#list-player');

    await expect(playerSelect.locator('option').first()).toBeAttached();
    await playerSelect.selectOption({ index: 0 });
    await sellerPage.locator('#list-fee').fill(String(fee));
    await sellerPage.getByRole('button', { name: 'List for sale' }).click();

    const listing = await waitForMyListing(request, sellerToken);

    expect(listing.status).toBe('open');

    // The first rival bids the minimum through the listing's own row (TRF-4).
    const firstRivalContext = await browser.newContext();
    const firstRivalPage = await firstRivalContext.newPage();
    const firstRivalToken = await onboard(firstRivalPage, request, firstRivalAccount);

    await firstRivalPage.goto('/transfers');

    const firstRow = listingRow(firstRivalPage, listing.playerName);

    await expect(firstRow).toBeVisible();
    await firstRow.locator('input[name="amount"]').fill(String(fee));
    await firstRow.getByRole('button', { name: 'Bid' }).click();

    await expect(section(firstRivalPage, 'Your bids').getByText('leading')).toBeVisible();

    const afterFirstBid = (await readMyActivity(request, sellerToken)).myListings[0];

    expect(afterFirstBid.bidderCount).toBe(1);
    expect(afterFirstBid.leadingAmountMinor).toBe(fee);

    // The second rival outbids by the server's own minimum raise (TRF-5, TRF-7).
    const secondRivalContext = await browser.newContext();
    const secondRivalPage = await secondRivalContext.newPage();
    const secondRivalToken = await onboard(secondRivalPage, request, secondRivalAccount);

    await secondRivalPage.goto('/transfers');

    const secondRow = listingRow(secondRivalPage, listing.playerName);

    await expect(secondRow).toBeVisible();
    await secondRow.locator('input[name="amount"]').fill(String(afterFirstBid.minimumAcceptableBidMinor));
    await secondRow.getByRole('button', { name: 'Bid' }).click();

    await expect(section(secondRivalPage, 'Your bids').getByText('leading')).toBeVisible();

    const winningBid = (await readMyActivity(request, secondRivalToken)).myBids[0];

    expect(winningBid.status).toBe('leading');
    expect(winningBid.amountMinor).toBe(afterFirstBid.minimumAcceptableBidMinor);

    // The seller sees the leading amount and the two bids, not who placed them (TRF-4).
    const listed = (await readMyActivity(request, sellerToken)).myListings[0];

    expect(listed.bidderCount).toBe(2);
    expect(listed.leadingAmountMinor).toBe(afterFirstBid.minimumAcceptableBidMinor);

    await sellerPage.reload();

    const sellerRow = listingRow(sellerPage, listing.playerName);

    await expect(sellerRow.locator('td').nth(4)).toHaveText('2');
    await expect(sellerRow.locator('td').nth(3)).not.toHaveText('No bids');

    // The displaced bidder is told, and holds no bid against the listing (TRF-7).
    const displaced = (await readMyActivity(request, firstRivalToken)).myBids[0];

    expect(displaced.status).toBe('outbid');

    // Resolve the listing now, rather than waiting for its window (ADR-0016), and watch it settle.
    await playAuction(request, listing.listingId);

    const transfer = await waitForTransfer(request, sellerToken, listing.listingId);

    expect(transfer.playerName).toBe(listing.playerName);
    expect(transfer.feeMinor).toBe(listed.leadingAmountMinor);

    // The seller's screen publishes the completed transfer in the public history (INT-6).
    await sellerPage.reload();

    const history = section(sellerPage, 'Recent transfers');

    await expect(history.getByText(listing.playerName)).toBeVisible();
    await expect(history.getByText(transfer.buyerClubName)).toBeVisible();

    // The winner's bid won and the displaced bidder's is outbid, in each club's own screen (TRF-10).
    const settled = (await readMyActivity(request, secondRivalToken)).myBids[0];

    expect(settled.status).toBe('won');

    await secondRivalPage.reload();
    await expect(section(secondRivalPage, 'Your bids').getByText('won')).toBeVisible();

    await firstRivalPage.reload();
    await expect(section(firstRivalPage, 'Your bids').getByText('outbid')).toBeVisible();

    await sellerContext.close();
    await firstRivalContext.close();
    await secondRivalContext.close();
  });
});
