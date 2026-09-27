import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The match center's route and its reachable states in the main suite (`§9.5`, `§11.1`).
 *
 * The main stack seeds a world whose calendar has not been played and does not start the worker, so a
 * browser journey here can honestly assert the route itself: the guard that keeps a visitor out, and the
 * not-found state a manager gets for a match that has never been played.
 *
 * Watching a complete fixture end to end — prepare, play, replay — is the matchday journey's job
 * (`matchday/matchday.spec.ts`, run by `playwright.matchday.config.ts`), which starts the worker and plays
 * a real round on its own throwaway database (ADR-0016). The replay's contract is also pinned by the API
 * integration tests and by the playback and renderer unit tests.
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
