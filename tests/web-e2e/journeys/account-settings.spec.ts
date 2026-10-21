import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The settings screen covers what Stage 2 introduces: the profile and ending sessions.
 */
test.describe('account settings', () => {
  test('the manager name chosen at sign-up shows in the shell and cannot be edited in settings', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await expect(page.getByText(account.displayName)).toBeVisible();
    await expect(page.getByLabel('Manager name', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Save manager name' })).toHaveCount(0);
  });

  test('signing out everywhere ends the session on this device too', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.getByRole('button', { name: 'Sign out everywhere' }).click();

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();

    await page.goto('/settings');

    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fsettings/);
  });
});
