import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The match center's route and its reachable states (`§9.5`, `§11.1`).
 *
 * The viewer draws a replay from a published result, and the end-to-end stack seeds a world whose
 * calendar has not been played — the worker is not part of `webServer`, and its matchdays are days apart
 * in real time. So what a browser journey can honestly assert here is the route itself: the guard that
 * keeps a visitor out, and the not-found state a manager gets for a match that has never been played.
 *
 * Watching a complete fixture end to end needs a played round, which the harness does not yet produce;
 * it is recorded as deferred rather than faked with a stubbed endpoint. The replay's own contract is
 * covered by the API integration tests (a published round driven through the real workflow) and by the
 * playback and renderer unit tests.
 */

test.describe('match center', () => {
  test('a visitor with no session cannot reach a match', async ({ page }) => {
    await page.goto(`/matches/${crypto.randomUUID()}`);

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });

  test('a manager is told plainly when a match has not been played', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.goto(`/matches/${crypto.randomUUID()}`);

    await expect(page.getByRole('alert')).toContainText('has not been published');
  });
});
