import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The settings screen covers what Stage 2 introduces: the profile under an optimistic concurrency
 * check, and ending sessions.
 */
test.describe('account settings', () => {
  test('a manager can rename themselves and the shell follows', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    const renamed = `${account.displayName}II`;

    await page.getByLabel('Manager name', { exact: true }).fill(renamed);
    await page.getByRole('button', { name: 'Save manager name' }).click();

    await expect(page.getByText('Your manager name has been saved.')).toBeVisible();

    // The change is reflected outside the form, so the whole client agrees on the new name.
    await expect(page.getByText(renamed)).toBeVisible();

    // And it survives a reload, because it was persisted rather than held in the form.
    await page.reload();
    await expect(page.getByLabel('Manager name', { exact: true })).toHaveValue(renamed);
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
