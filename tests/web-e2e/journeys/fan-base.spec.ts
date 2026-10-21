import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { collectA11yViolations, renderA11yViolations } from '../support/accessibility';
import { createVerifiedManager } from '../support/auth-flows';
import { confirmKitColours } from '../support/kit-colours';
import { navigateTo } from '../support/navigation';

/**
 * The Fan Base page (`FAN-1`…`FAN-10`).
 *
 * A manager takes over a club, so its fans start at the tier's defaults (a clean slate, `FAN-9`). The page must
 * show exactly what the server holds, the fans' expectation for the next match must be the same chip on the Fan
 * Base page, the Office and the fixtures screen, and nothing on any of them may fail an automated accessibility
 * scan. Like the other journeys it gives its club back at the end.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface LoginRead {
  readonly accessToken: string;
}

interface FanBaseRead {
  readonly fans: number;
  readonly moodPhrase: string;
  readonly floorFans: number;
  readonly softCapFans: number;
  readonly history: string;
  readonly events: readonly { readonly kind: string }[];
  readonly nextFixture: { readonly expectation: { readonly labelPhrase: string } } | null;
}

test.describe('fan base', () => {
  test('a new manager sees the tier’s opening fans and what they expect from the next match', async ({
    page,
    request,
  }) => {
    const account = createAccount();

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

    const login = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
      data: { email: account.email, password: account.password },
    });

    expect(login.ok()).toBe(true);

    const bearer = { Authorization: `Bearer ${((await login.json()) as LoginRead).accessToken}` };
    const read = await request.get(`${apiBaseUrl}/api/v1/fans`, { headers: bearer });

    expect(read.ok()).toBe(true);

    const fans = (await read.json()) as FanBaseRead;
    const grouped = (value: number) => value.toLocaleString('en-US');

    // The takeover wrote its own event, and the tenure history starts there.
    expect(fans.history).toBe('tenure');
    expect(fans.events[0].kind).toBe('takeover');
    expect(fans.moodPhrase).toBe('Calm');
    expect(fans.nextFixture).not.toBeNull();

    // The Office carries the chip beside the next match.
    await expect(page.locator('app-fan-expectation-chip').first()).toContainText(
      fans.nextFixture!.expectation.labelPhrase,
    );

    // The page shows what the server holds.
    await navigateTo(page, 'Fan base');

    await expect(page).toHaveURL(/\/fans$/);
    await expect(page.getByRole('heading', { name: 'Fan base', level: 1 })).toBeVisible();
    await expect(page.getByTestId('fan-count')).toHaveText(grouped(fans.fans));
    await expect(page.getByTestId('fan-mood')).toContainText('Calm');
    await expect(page.getByTestId('fan-summary')).toContainText(grouped(fans.floorFans));
    await expect(page.getByTestId('fan-summary')).toContainText(grouped(fans.softCapFans));
    await expect(page.getByTestId('fan-next')).toContainText(
      fans.nextFixture!.expectation.labelPhrase,
    );
    await expect(page.getByTestId('fan-next')).toContainText(/\d+% win, \d+% draw, \d+% loss/);
    await expect(page.getByTestId('fan-satisfaction-chart').getByRole('img')).toBeVisible();
    await expect(page.getByTestId('fan-count-chart').getByRole('img')).toBeVisible();

    // The data table behind the charts lists the takeover.
    // Opened from the keyboard: Playwright's pointer hit-test trips over the page's custom-element host on a phone,
    // while a real tap opens it.
    await page.getByText('Data table of the plotted points').focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('fan-points')).toContainText('New manager');

    // The whole club history keeps the earlier rows, with the takeover among them.
    await page.getByRole('button', { name: 'Whole club history' }).click();
    await expect(page.getByRole('button', { name: 'Whole club history' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expect(page.getByTestId('fan-points')).toContainText('Opening count');

    // The same chip on the fixtures screen.
    await navigateTo(page, 'Fixtures');
    await expect(page.getByTestId('next-fixture')).toContainText(
      fans.nextFixture!.expectation.labelPhrase,
    );

    // The page meets WCAG 2.2 AA, with the chart and both tables on it.
    await navigateTo(page, 'Fan base');
    await expect(page.getByTestId('fan-count')).toBeVisible();
    await page.waitForLoadState('networkidle');

    const violations = await collectA11yViolations(page);

    expect(violations, renderA11yViolations(violations)).toEqual([]);

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the fan base', async ({ page }) => {
    await page.goto('/fans');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
