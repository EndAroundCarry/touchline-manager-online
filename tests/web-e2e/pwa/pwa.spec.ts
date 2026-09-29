import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The installable PWA and its offline boundary (master plan §11.4, §16 Stage 13, `F-45`, ADR-0007).
 *
 * These run against the production build served from a static origin, because the service worker is only
 * enabled outside development — the stack the other journeys use cannot register one. What they prove that
 * a unit test cannot: the manifest is served, the worker takes control of the client, the app shell is
 * still served while offline, and a mutation control is actually disabled rather than merely styled so.
 *
 * The suite's TypeScript configuration has no DOM library, so the sliver of `navigator.serviceWorker` these
 * evaluations read is named locally rather than pulled in globally.
 */

interface ServiceWorkerHandle {
  readonly ready: Promise<unknown>;
  readonly controller: unknown;
}

test.describe('installable PWA', () => {
  test('serves the manifest and lets the service worker take control', async ({ page }) => {
    await page.goto('/welcome');

    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute(
      'href',
      'manifest.webmanifest',
    );

    const manifest = await page.request.get('/manifest.webmanifest');
    expect(manifest.ok()).toBeTruthy();
    expect(((await manifest.json()) as { display: string }).display).toBe('standalone');

    // Registration waits for the app to stabilise; `ready` resolves once the worker has activated. A
    // reload then hands this client to it — the precondition for loading the shell while offline.
    await page.evaluate(() =>
      (navigator as unknown as { serviceWorker: ServiceWorkerHandle }).serviceWorker.ready.then(
        () => undefined,
      ),
    );
    await page.reload();

    const controlled = await page.evaluate(
      () =>
        (navigator as unknown as { serviceWorker: ServiceWorkerHandle }).serviceWorker.controller !==
        null,
    );
    expect(controlled).toBe(true);
  });

  test('reads what is loaded offline and blocks mutations', async ({ page, request, context }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    // Sign-in lands on settings, whose notification save is a mutation that needs no club.
    const save = page.getByRole('button', { name: 'Save notification settings' });
    await expect(save).toBeEnabled();

    // Take control before going offline, so the reload below is served from the worker's cache.
    await page.evaluate(() =>
      (navigator as unknown as { serviceWorker: ServiceWorkerHandle }).serviceWorker.ready.then(
        () => undefined,
      ),
    );
    await page.reload();
    await expect(save).toBeEnabled();

    await context.setOffline(true);

    await expect(page.getByText('You are offline')).toBeVisible();
    await expect(save).toBeDisabled();

    // The worker still serves the shell, so the client loads instead of a browser error page: the read
    // the manager already had stays available, which is the whole point of the offline boundary.
    await page.reload();
    await expect(page.locator('app-root')).toBeAttached();

    await context.setOffline(false);
    await page.reload();
    await expect(page.getByText('You are offline')).toBeHidden();
  });
});
