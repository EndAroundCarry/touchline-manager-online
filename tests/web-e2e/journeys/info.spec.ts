import { expect, test } from '@playwright/test';

/**
 * The player-facing information pages (`F-55`, master plan §16 Stage 15, ADR-0050).
 *
 * The stage promises rules, privacy, terms, status and support pages. This journey proves they are reachable
 * the way a visitor reaches them — from the footer, while signed out — that the register consent leads to the
 * terms and privacy pages it names, and that the pages which show a version and a live status do so. It
 * carries no tag, so it rides the desktop and mobile projects the suite already has.
 */
test.describe('information pages', () => {
  test('a signed-out visitor reads every page from the footer', async ({ page }) => {
    await page.goto('/welcome');

    const footer = page.getByRole('navigation', { name: 'Information' });

    const pages = [
      { link: 'Rules', path: '/rules', heading: 'Game rules' },
      { link: 'Privacy', path: '/privacy', heading: 'Privacy' },
      { link: 'Terms', path: '/terms', heading: 'Terms of service' },
      { link: 'Status', path: '/status', heading: 'Service status' },
      { link: 'Support', path: '/support', heading: 'Support' },
    ];

    for (const info of pages) {
      await footer.getByRole('link', { name: info.link }).click();

      await expect(page).toHaveURL(new RegExp(`${info.path}$`));
      await expect(page.getByRole('heading', { name: info.heading, level: 1 })).toBeVisible();

      await page.goBack();
    }
  });

  test('the register consent leads to the terms and privacy pages it names', async ({ page }) => {
    await page.goto('/register');

    await page.getByRole('link', { name: 'terms of service' }).click();
    await expect(page).toHaveURL(/\/terms$/);
    await expect(page.getByRole('heading', { name: 'Terms of service', level: 1 })).toBeVisible();

    await page.goBack();

    await page.getByRole('link', { name: 'privacy policy' }).click();
    await expect(page).toHaveURL(/\/privacy$/);
    await expect(page.getByRole('heading', { name: 'Privacy', level: 1 })).toBeVisible();
  });

  test('a versioned page names the published version', async ({ page }) => {
    await page.goto('/terms');

    await expect(page.getByText(/Published version/)).toBeVisible();
  });

  test('the status page reports the service is operational', async ({ page }) => {
    await page.goto('/status');

    await expect(page.getByRole('heading', { name: 'Service status', level: 1 })).toBeVisible();
    await expect(page.getByText('Operational')).toBeVisible();
  });
});
