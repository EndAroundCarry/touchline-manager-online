import { APIRequestContext, Page, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager, signOut } from '../support/auth-flows';
import { pathOf, waitForEmailLink } from '../support/mailbox';

/** Completes the reset form and waits for the acknowledgement. */
async function chooseNewPassword(
  page: Page,
  link: string,
  newPassword: string,
): Promise<void> {
  await page.goto(pathOf(link));

  await page.getByLabel('New password', { exact: true }).fill(newPassword);
  await page.getByLabel('Confirm the new password', { exact: true }).fill(newPassword);
  await page.getByRole('button', { name: 'Change my password' }).click();
}

/** Requests a reset link through the forgotten-password screen. */
async function requestResetLink(
  page: Page,
  request: APIRequestContext,
  email: string,
): Promise<string> {
  await page.goto('/forgot-password');

  await page.getByLabel('Email address', { exact: true }).fill(email);
  await page.getByRole('button', { name: 'Send the reset link' }).click();

  // The answer never reveals whether the address has an account.
  await expect(page.getByText(/a reset link is on its way/)).toBeVisible();

  return waitForEmailLink(request, email, 'Reset');
}

/**
 * The password-reset journey, which is the one flow that has to work while nobody is signed in.
 */
test.describe('password reset', () => {
  test('a manager can replace a forgotten password and the old one stops working', async ({
    page,
    request,
  }) => {
    const account = createAccount();
    const replacement = 'fresh-horse-battery-staple';

    await createVerifiedManager(page, request, account);
    await signOut(page);

    const link = await requestResetLink(page, request, account.email);
    await chooseNewPassword(page, link, replacement);

    await expect(page.getByText(/Your password has been changed/)).toBeVisible();

    // The old password is refused, with the deliberately neutral wording.
    await page.getByRole('link', { name: 'Sign in with the new password' }).click();
    await page.getByLabel('Email address', { exact: true }).fill(account.email);
    await page.getByLabel('Password', { exact: true }).fill(account.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('alert')).toHaveText('The email address or password is incorrect.');

    // The new one works.
    await page.getByLabel('Password', { exact: true }).fill(replacement);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
  });

  test('a reset link stops working once it has been used', async ({ page, request }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);
    await signOut(page);

    const link = await requestResetLink(page, request, account.email);
    await chooseNewPassword(page, link, 'fresh-horse-battery-staple');

    await expect(page.getByText(/Your password has been changed/)).toBeVisible();

    // A single-use token, so replaying the same link must not set a second password.
    await page.goto(pathOf(link));
    await page.getByLabel('New password', { exact: true }).fill('one-more-horse-battery');
    await page.getByLabel('Confirm the new password', { exact: true }).fill('one-more-horse-battery');
    await page.getByRole('button', { name: 'Change my password' }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByText(/Your password has been changed/)).toHaveCount(0);
  });
});
