import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';
import { navigateTo } from '../support/navigation';
import { confirmKitColours } from '../support/kit-colours';

/**
 * The Stage 8 inbox (F-41, master plan §10.7, §11.1).
 *
 * A manager onboards, takes a club over, and opens the inbox from the navigation. Taking a club over
 * writes a welcome message (COM-1), so the journey asserts that message is on screen rather than an
 * empty state. What it proves is what a unit test cannot: the route is reachable from the shell's own
 * link, the screen reads the real endpoint, and the unread poll does not turn the message into an error.
 *
 * Like the other journeys it gives its club back at the end, so the shared world is left as it was found.
 */

test.describe('inbox', () => {
  test('a manager opens the inbox from the navigation and sees the takeover message', async ({
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

    // The shell now offers the inbox, which is how a manager reaches it.
    await navigateTo(page, 'Inbox');

    await expect(page).toHaveURL(/\/inbox$/);
    await expect(page.getByRole('heading', { name: 'Inbox', level: 1 })).toBeVisible();
    await expect(page.getByRole('heading', { name: /^Welcome to / })).toBeVisible();

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the inbox', async ({ page }) => {
    await page.goto('/inbox');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
