import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager, signOut } from '../support/auth-flows';

/**
 * Route guards decide who sees what, so both directions are exercised here: the manager area closes
 * on a signed-out visitor, and the sign-in screens refuse to replace a session that already exists.
 */
test.describe('route guards', () => {
  test('a signed-out visitor is sent to sign-in and returned to the page they asked for', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);
    await signOut(page);

    await page.goto('/settings');

    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fsettings/);

    await page.getByLabel('Email address', { exact: true }).fill(account.email);
    await page.getByLabel('Password', { exact: true }).fill(account.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    // The attempted destination is remembered, not discarded in favour of a default page.
    await expect(page).toHaveURL(/\/settings$/);
  });

  test('a returnUrl cannot be used to send a manager off the site', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);
    await signOut(page);

    // `returnUrl` is attacker-influenced input. An absolute external address must be ignored rather
    // than navigated to, or the sign-in screen becomes an open redirect.
    await page.goto('/login?returnUrl=https://evil.example/phishing');

    await page.getByLabel('Email address', { exact: true }).fill(account.email);
    await page.getByLabel('Password', { exact: true }).fill(account.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/settings$/);
    expect(page.url()).not.toContain('evil.example');
  });

  test('a manager who already holds a session is moved away from the sign-in form', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.goto('/login');

    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
  });
});
