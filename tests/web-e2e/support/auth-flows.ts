import { APIRequestContext, Page, expect } from '@playwright/test';
import { TestAccount } from './account';
import { pathOf, waitForEmailLink } from './mailbox';

/**
 * The auth screens, driven the way a manager drives them.
 *
 * These helpers exist so a journey reads as the sequence of decisions it is testing rather than as a
 * pile of selectors. Nothing here reaches past the interface: every step goes through a visible
 * control, so a screen that breaks the flow fails the journey.
 */

/** Registers an account and waits for the confirmation panel. */
export async function register(page: Page, account: TestAccount): Promise<void> {
  await page.goto('/register');

  await page.getByLabel('Email address', { exact: true }).fill(account.email);
  await page.getByLabel('Manager name', { exact: true }).fill(account.displayName);
  await page.getByLabel('Password', { exact: true }).fill(account.password);
  await page.getByLabel(/I accept the terms of service/).check();

  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
}

/** Reads the confirmation link out of the mailbox, opens it, and confirms the address. */
export async function confirmEmail(
  page: Page,
  request: APIRequestContext,
  account: TestAccount,
): Promise<void> {
  const link = await waitForEmailLink(request, account.email, 'Confirm');

  await page.goto(pathOf(link));

  await expect(page.getByRole('heading', { name: 'Confirm your email' })).toBeVisible();

  // Confirmation is an explicit press, not an automatic request on load: the token is single-use,
  // and a link preview fetching the page must not be able to burn it.
  await page.getByRole('button', { name: 'Confirm my email address' }).click();

  await expect(page.getByText(/Your account is active/)).toBeVisible();
}

/** Signs in and waits for the manager area to open. */
export async function signIn(page: Page, account: TestAccount, secret = account.password): Promise<void> {
  await page.goto('/login');

  await page.getByLabel('Email address', { exact: true }).fill(account.email);
  await page.getByLabel('Password', { exact: true }).fill(secret);

  await page.getByRole('button', { name: 'Sign in' }).click();

  // Sign-in lands on the settings screen when no returnUrl was requested.
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
}

/** Registers, confirms, and signs in — the starting point for most journeys. */
export async function createVerifiedManager(
  page: Page,
  request: APIRequestContext,
  account: TestAccount,
): Promise<void> {
  await register(page, account);
  await confirmEmail(page, request, account);
  await signIn(page, account);
}

/** Signs out through the shell and waits for the sign-in screen. */
export async function signOut(page: Page): Promise<void> {
  // The settings screen also offers "Sign out", so the header's button is taken explicitly.
  await page.getByRole('button', { name: 'Sign out' }).first().click();

  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
}
