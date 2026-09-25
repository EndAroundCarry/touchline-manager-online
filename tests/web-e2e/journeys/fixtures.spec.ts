import { APIRequestContext, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The Stage 6 criteria for the fixture list and the prepare-match screen (F-18, F-19).
 *
 * A manager onboards, opens the fixtures screen, sees the season's next fixture with its deadline, opens
 * the prepare screen, picks a side, and saves it — then survives a version conflict: a second client — the
 * API, acting as another device — replaces the same side, and the manager's next save is refused rather
 * than overwriting it. The journey proves the two things a unit test cannot: that the screens reach the
 * real endpoints, and that the sheet's ETag contract holds through the browser (`SQ-4`, `CAL-3`, `CONC-1`,
 * §11.2).
 *
 * Like the other journeys it gives its club back at the end, so the shared persistent world is left as it
 * was found.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface MyFixturesRead {
  readonly nextFixtureId: string | null;
}

interface TeamSheetRead {
  readonly sheetVersion: number | null;
  readonly selectablePlayers: readonly { readonly id: string; readonly isUnavailable: boolean }[];
}

interface LoginRead {
  readonly accessToken: string;
}

/** Signs in through the API so the journey can act as a second client. */
async function apiSession(request: APIRequestContext, email: string, password: string): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email, password },
  });

  expect(response.ok()).toBe(true);

  return ((await response.json()) as LoginRead).accessToken;
}

test.describe('fixtures', () => {
  test('a manager prepares a side and survives a version conflict', async ({ page, request }) => {
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
    const bearer = { Authorization: `Bearer ${token}` };

    // A side is prepared from the club's default plan, so the arrangement provides one (INS-11).
    const plan = await request.post(`${apiBaseUrl}/api/v1/tactics`, {
      headers: bearer,
      data: {
        name: 'Shape',
        formationPreset: '4-4-2',
        mentality: 'balanced',
        tempo: 'normal',
        passing: 'mixed',
        width: 'normal',
        pressing: 'mid_block',
        defensiveLine: 'normal',
        tackling: 'normal',
        timeWasting: 'off',
      },
    });

    expect(plan.ok()).toBe(true);

    const mine = (await (
      await request.get(`${apiBaseUrl}/api/v1/fixtures/mine`, { headers: bearer })
    ).json()) as MyFixturesRead;

    expect(mine.nextFixtureId).not.toBeNull();

    // The shell now offers fixtures, which is how a manager reaches them.
    await page.getByRole('link', { name: 'Fixtures' }).click();

    await expect(page).toHaveURL(/\/fixtures$/);
    await expect(page.getByRole('heading', { name: 'Next fixture' })).toBeVisible();
    await expect(page.getByText(/Locks in|Team sheets are locked/).first()).toBeVisible();

    // The next-fixture block's own link, before the season list's per-fixture prepare links.
    await page
      .getByRole('link', { name: /Prepare your side for/ })
      .first()
      .click();

    await expect(page).toHaveURL(/\/fixtures\/[0-9a-f-]+\/prepare$/);

    // Eleven starting slots and a bench of seven, each a labelled select (SQ-4, TAC-7). The starters come
    // first, so an index maps to a slot number.
    const slots = page.getByRole('combobox');

    await expect(slots).toHaveCount(18);
    await expect(slots.first()).toHaveAccessibleName(/Slot 1/);
    await expect(slots.last()).toHaveAccessibleName(/Substitute 7/);

    const sheet = (await (
      await request.get(`${apiBaseUrl}/api/v1/fixtures/${mine.nextFixtureId}/team-sheet`, {
        headers: bearer,
      })
    ).json()) as TeamSheetRead;

    const available = sheet.selectablePlayers.filter((player) => !player.isUnavailable);

    expect(available.length).toBeGreaterThanOrEqual(12);

    for (let slot = 1; slot <= 11; slot++) {
      await slots.nth(slot - 1).selectOption(available[slot - 1].id);
    }

    await page.getByRole('button', { name: 'Save your side' }).click();
    await expect(page.getByText(/Your side was (saved|updated)/)).toBeVisible();

    // A second client — another device — replaces the same side, bumping its version.
    const current = (await (
      await request.get(`${apiBaseUrl}/api/v1/fixtures/${mine.nextFixtureId}/team-sheet`, {
        headers: bearer,
      })
    ).json()) as TeamSheetRead;

    const elsewhere = await request.put(`${apiBaseUrl}/api/v1/fixtures/${mine.nextFixtureId}/team-sheet`, {
      headers: { ...bearer, 'If-Match': `"${current.sheetVersion}"` },
      data: {
        selection: Array.from({ length: 11 }, (_, index) => ({
          slotNumber: index + 1,
          playerId: available[available.length - 1 - index].id,
        })),
      },
    });

    expect(elsewhere.ok()).toBe(true);

    // The manager edits without reloading, and the save is refused as stale rather than overwriting. The
    // replacement is a player outside the side the manager already picked, so the edit keeps eleven.
    await slots.nth(0).selectOption(available[11].id);
    await page.getByRole('button', { name: 'Save your side' }).click();

    await expect(page.getByText(/changed on another device/)).toBeVisible();

    // Reapplying sends the selection against the version that just arrived, and it succeeds.
    await page.getByRole('button', { name: 'Reapply my changes' }).click();
    await expect(page.getByText(/Your side was (saved|updated)/)).toBeVisible();
    await expect(page.getByText(/changed on another device/)).toHaveCount(0);

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the fixtures screens', async ({ page }) => {
    await page.goto('/fixtures');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);

    await page.goto(`/fixtures/${crypto.randomUUID()}/prepare`);

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
