import { APIRequestContext, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The Stage 4 exit criteria for the training screen (F-20).
 *
 * A manager onboards, opens training, sets the club's focus and intensity, points a player at an attribute
 * family and then returns them to the team plan, and survives a version conflict: a second client — the API,
 * acting as another device — revises the same plan, and the manager's next save is refused rather than
 * overwriting it. The journey proves the two things a unit test cannot: that the screen reaches the real
 * endpoints, and that the plan's ETag contract holds through the browser (CONC-1, §11.2).
 *
 * Like the other journeys it gives its club back at the end, so the shared persistent world is left as it
 * was found.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface TrainingRead {
  readonly version: number;
  readonly isConfigured: boolean;
  readonly teamFocus: string;
  readonly intensity: string;
}

interface LoginRead {
  readonly accessToken: string;
}

/** Signs in through the API so the journey can act as a second client. */
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

test.describe('training', () => {
  test('a manager sets a plan and a focus, and survives a version conflict', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await page.getByRole('button', { name: /^Take over/ }).first().click();

    await expect(page).toHaveURL(/\/dashboard$/);

    // The shell now offers training, which is how a manager reaches it.
    await page.getByRole('link', { name: 'Training' }).click();

    await expect(page).toHaveURL(/\/training$/);
    await expect(page.getByRole('heading', { name: /training$/ })).toBeVisible();

    // The roster is the inherited squad, each player with a labelled focus control (SQ-1, TRN-2).
    await expect(page.locator('table tbody tr')).toHaveCount(22);
    await expect(page.getByLabel(/^Individual focus for /)).toHaveCount(22);

    // Set the club's plan and save it. A re-used club may already hold one, so either answer is correct.
    await page.getByLabel('Team focus').selectOption('fitness');
    await page.getByLabel('Intensity').selectOption('intense');
    await page.getByRole('button', { name: /(Set|Save) plan/ }).click();
    await expect(page.getByText(/Your training plan was (created|saved)\./)).toBeVisible();

    // A second client — another device — revises the same plan, bumping its version.
    const token = await apiSession(request, account.email, account.password);
    const bearer = { Authorization: `Bearer ${token}` };

    const read = await request.get(`${apiBaseUrl}/api/v1/training`, { headers: bearer });

    expect(read.ok()).toBe(true);

    const before = (await read.json()) as TrainingRead;

    const elsewhere = await request.put(`${apiBaseUrl}/api/v1/training`, {
      headers: { ...bearer, 'If-Match': `"${before.version}"` },
      data: { teamFocus: 'recovery', intensity: 'light' },
    });

    expect(elsewhere.ok()).toBe(true);

    // The manager edits without reloading, and the save is refused as stale rather than overwriting.
    await page.getByLabel('Intensity').selectOption('light');
    await page.getByRole('button', { name: 'Save plan' }).click();

    await expect(page.getByText(/changed on another device/)).toBeVisible();
    await expect(page.getByLabel('Intensity')).toHaveValue('light');

    // Reapplying sends the draft against the version that just arrived, and it succeeds.
    await page.getByRole('button', { name: 'Reapply my changes' }).click();
    await expect(page.getByText('Your training plan was saved.')).toBeVisible();
    await expect(page.getByText(/changed on another device/)).toHaveCount(0);

    // Point a player at a family they are not already on, then return them to the team plan (TRN-2).
    const focusSelect = page.getByLabel(/^Individual focus for /).first();
    const current = await focusSelect.inputValue();
    const target =
      ['technical', 'mental', 'physical', 'goalkeeping'].find((family) => family !== current) ??
      'technical';

    await focusSelect.selectOption(target);
    await expect(page.getByText(/focus was updated\./)).toBeVisible();

    await focusSelect.selectOption('');
    await expect(page.getByText(/now trains with the team\./)).toBeVisible();

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the training screen', async ({ page }) => {
    await page.goto('/training');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
