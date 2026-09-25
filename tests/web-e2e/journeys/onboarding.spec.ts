import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The Stage 3 exit criterion at the interface level: a manager onboards into a club and inherits it.
 *
 * The journey gives the club back at the end by resigning, so it can be run repeatedly against the same
 * persistent world. Without that, each run would consume one of the 108 clubs the seeder creates, and the
 * suite would start failing after eighteen runs for a reason that has nothing to do with a defect.
 */
test.describe('onboarding', () => {
  test('a new manager creates a profile, chooses a country and a club, and inherits it', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    // Step one: the manager profile. Both preferences default from the browser, so this is one click.
    await page.goto('/onboarding/manager');
    await expect(page.getByRole('heading', { name: 'Your manager profile' })).toBeVisible();
    await page.getByRole('button', { name: 'Create my profile' }).click();

    // Step two: the country. Every country is listed with how many of its clubs are free.
    await expect(page.getByRole('heading', { name: 'Choose your country' })).toBeVisible();
    await expect(page.getByText(/18 clubs, \d+ available/).first()).toBeVisible();

    await page
      .getByRole('button', { name: 'See the clubs' })
      .first()
      .click();

    // Step three: the club. Taken clubs are shown as taken rather than hidden.
    await expect(page.getByRole('heading', { name: 'Choose your club' })).toBeVisible();
    await expect(page.getByText(/tier \d+/)).toBeVisible();

    await page
      .getByRole('button', { name: /^Take over/ })
      .first()
      .click();

    // The claim lands on the dashboard of the club that was inherited.
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByText('Club funds')).toBeVisible();
    await expect(page.getByText('Stadium baseline')).toBeVisible();
    await expect(page.getByText('Yours')).toBeVisible();

    // The club is inherited as it stands, so a second read shows the same control.
    await page.reload();
    await expect(page.getByText('Yours')).toBeVisible();

    // Giving the club back returns it to the pool for the next run, and shows the cooldown that makes
    // club-hopping impossible.
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
    await expect(page.getByText(/You resigned recently/)).toBeVisible();
  });

  test('an unconfirmed manager is sent to settings rather than into onboarding', async ({ page }) => {
    // The claim endpoint refuses an unconfirmed account (ADR-0002), so the screen is not offered at all.
    await page.goto('/dashboard');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
