import { APIRequestContext, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The Stage 6 criterion for the division table (F-21, `TBL-1`…`TBL-11`).
 *
 * A manager onboards, opens the table from the navigation, and sees their division's eighteen clubs in
 * the order the server ranked them, with their own club marked. The journey proves what a unit test
 * cannot: that the screen reaches the real endpoint, that resolving the manager's division from their
 * club works through the browser, and that the row the server carried back is the row that is marked.
 *
 * Like the other journeys it gives its club back at the end, so the shared persistent world is left as it
 * was found.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface MyFixturesRead {
  readonly clubId: string;
  readonly clubName: string;
  readonly divisionId: string;
  readonly divisionName: string;
}

interface LoginRead {
  readonly accessToken: string;
}

/** Signs in through the API, so the journey can name the division the screen should show. */
async function apiSession(
  request: APIRequestContext,
  email: string,
  password: string,
): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email, password },
  });

  expect(response.ok()).toBe(true);

  return ((await response.json()) as LoginRead).accessToken;
}

test.describe('division table', () => {
  test('a manager reads their division table from the navigation', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await page
      .getByRole('button', { name: /^Take over/ })
      .first()
      .click();

    await expect(page).toHaveURL(/\/dashboard$/);

    const token = await apiSession(request, account.email, account.password);
    const mine = (await (
      await request.get(`${apiBaseUrl}/api/v1/fixtures/mine`, {
        headers: { Authorization: `Bearer ${token}` },
      })
    ).json()) as MyFixturesRead;

    // The shell now offers the table, which is how a manager reaches it.
    await page.getByRole('link', { name: 'Competitions' }).click();

    await expect(page).toHaveURL(/\/competitions$/);
    await expect(page.getByRole('heading', { name: mine.divisionName })).toBeVisible();

    // Eighteen clubs, one per row, in the order the server ranked them (TBL-1…TBL-11).
    await expect(page.locator('tbody tr')).toHaveCount(18);

    // The manager's own row is a row header naming their club, and it is marked in words rather than by
    // a tint alone (§11.3).
    const ownRow = page.getByRole('rowheader', { name: new RegExp(mine.clubName) });

    await expect(ownRow).toBeVisible();
    await expect(ownRow.getByText('(your club)')).toBeVisible();

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the table', async ({ page }) => {
    await page.goto('/competitions');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);

    await page.goto(`/competitions/${crypto.randomUUID()}/table`);

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
