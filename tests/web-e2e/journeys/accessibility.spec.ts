import { Page, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { collectA11yViolations, expectFocusRing, renderA11yViolations } from '../support/accessibility';
import { createVerifiedManager } from '../support/auth-flows';
import { navigateTo } from '../support/navigation';

/**
 * The accessibility gate (`§11.3`, `§15.6`).
 *
 * Master plan §11.3 sets the bar at WCAG 2.2 AA and §15.6 asks for automated axe checks on the core
 * routes. This journey runs axe over every public screen and every manager screen a claimed club can
 * reach, and guards the focus ring the shared controls rely on. It is tagged `@a11y` so it can be run on
 * its own (`npm run test:a11y`) and so it rides the desktop and mobile projects the suite already runs —
 * on a phone the card layouts are scanned, on a desktop the tables. The Canvas viewer's own scan lives in
 * the matchday journey, where a real replay exists.
 */

/** Collects each screen's violations instead of failing on the first, so one run reports every offending
 *  page rather than only the one nearest the start of the journey. */
function auditor(page: Page) {
  const problems: string[] = [];

  return {
    async scan(label: string): Promise<void> {
      // Let the screen finish rendering and its reads settle before axe inspects it.
      await page.waitForLoadState('networkidle');

      const violations = await collectA11yViolations(page);

      if (violations.length > 0) {
        problems.push(`on ${label}:\n${renderA11yViolations(violations)}`);
      }
    },

    expectClean(): void {
      expect(problems, `axe reported WCAG 2.2 AA violations:\n${problems.join('\n')}`).toEqual([]);
    },
  };
}

test.describe('@a11y the public screens meet WCAG 2.2 AA', () => {
  test('no signed-out screen has an accessibility violation', async ({ page }) => {
    const audit = auditor(page);

    for (const path of [
      '/welcome',
      '/register',
      '/login',
      '/forgot-password',
      '/reset-password',
      '/verify-email',
      '/rules',
      '/privacy',
      '/terms',
      '/status',
      '/support',
    ]) {
      await page.goto(path);
      await audit.scan(path);
    }

    audit.expectClean();

    // The shared text controls carry `focus:outline-none`; the ring survives only through the global
    // `:focus-visible` rule, so its presence is asserted rather than assumed (WCAG 2.4.7).
    await page.goto('/login');
    await expectFocusRing(page.getByLabel('Email address', { exact: true }));
  });
});

test.describe('@a11y the manager screens meet WCAG 2.2 AA', () => {
  test('no signed-in screen has an accessibility violation', async ({ page, request }) => {
    const audit = auditor(page);
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    // Onboarding is itself a set of screens a manager must pass through, so each is scanned before the
    // club is taken over.
    await page.goto('/onboarding/manager');
    await audit.scan('onboarding: manager profile');

    await page.getByRole('button', { name: 'Create my profile' }).click();
    await expect(page).toHaveURL(/\/onboarding\/country$/);
    await audit.scan('onboarding: country');

    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await expect(page).toHaveURL(/\/onboarding\/club(\?|$)/);
    await audit.scan('onboarding: club');

    await page.getByRole('button', { name: /^Take over/ }).first().click();
    await expect(page).toHaveURL(/\/dashboard$/);

    // A player profile is a detail route, so its id is read off the squad's own link rather than guessed.
    await navigateTo(page, 'Squad');
    await expect(page).toHaveURL(/\/squad$/);

    const profileHref = await page
      .getByRole('link', { name: /^Open / })
      .first()
      .getAttribute('href');

    expect(profileHref, 'the squad should offer a player profile link').not.toBeNull();

    const routes = [
      '/dashboard',
      '/squad',
      profileHref!,
      '/tactics',
      '/training',
      '/competitions',
      '/fixtures',
      '/scouting',
      '/transfers',
      '/finances',
      '/inbox',
      '/news',
      '/history',
      '/settings',
      '/help',
    ];

    for (const route of routes) {
      await page.goto(route);
      await audit.scan(route);
    }

    audit.expectClean();

    // Give the club back, as the other journeys do, so the shared world is left as it was found.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();
    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });
});
