import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { confirmEmail, createVerifiedManager, register, signIn, signOut } from '../support/auth-flows';

/**
 * The Stage 2 exit criterion at the interface level: the whole account lifecycle works from a
 * browser, and the session behaves the way ADR-0002 says it should.
 */
test.describe('the account lifecycle', () => {
  test('a manager registers, confirms, signs in, keeps the session across a reload, and signs out', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await register(page, account);

    // The account exists but is not yet usable for writes.
    await confirmEmail(page, request, account);
    await signIn(page, account);

    // The shell believes in the session.
    await expect(page.getByText(account.displayName)).toBeVisible();

    // A cold start restores the session from the refresh cookie rather than bouncing to sign-in.
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
    await expect(page.getByText(account.displayName)).toBeVisible();

    await signOut(page);

    // The manager area closes again once the session is gone.
    await page.goto('/settings');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fsettings/);
  });

  test('an unconfirmed account may sign in but may not change its manager name', async ({ page }) => {
    const account = createAccount();

    await register(page, account);
    await signIn(page, account);

    // The restriction is explained rather than merely enforced with a dead control.
    await expect(
      page.getByText(/Confirm your email address before you can change your manager name/),
    ).toBeVisible();
    await expect(page.getByLabel('Manager name', { exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Save manager name' })).toBeDisabled();
  });

  test('a manager can close their account and cannot sign in afterwards', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.getByLabel('Confirm with your password').fill(account.password);
    await page.getByLabel(/I understand this closes my account/).check();
    await page.getByRole('button', { name: 'Close my account' }).click();

    await expect(page).toHaveURL(/\/welcome$/);

    await page.goto('/login');
    await page.getByLabel('Email address', { exact: true }).fill(account.email);
    await page.getByLabel('Password', { exact: true }).fill(account.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    // A closing account is refused, without the screen implying the address was unknown.
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Settings' })).toHaveCount(0);
  });
});
