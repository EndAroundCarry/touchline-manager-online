import { Locator, Page, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';
import { isCompact, navigateTo } from '../support/navigation';

/**
 * The breakpoint suite (`F-44`).
 *
 * The core journeys prove the game works; this proves it works at more than one width. It is tagged
 * `@responsive` so the tablet project selects it and the desktop and mobile projects run it beside
 * everything else, and it asserts the things a screenshot cannot: which navigation the shell offers,
 * which of the two layouts a dense screen presents, that the shown controls are big enough for a
 * thumb, and that no screen makes the page itself scroll sideways.
 */

/** Fails if the document is wider than the viewport, i.e. content escapes the page rather than a table's own scroll box. */
async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );

  expect(overflow, 'the page should not scroll sideways').toBeLessThanOrEqual(1);
}

/** Fails if a control is shorter than the 44px touch target the shared controls are built to (`F-44`). */
async function expectTouchTarget(locator: Locator): Promise<void> {
  await expect(locator).toBeVisible();

  const box = await locator.boundingBox();

  expect(box, 'the control should have a box').not.toBeNull();
  expect(box!.height, 'the control should be at least 44px tall').toBeGreaterThanOrEqual(44);
}

test.describe('@responsive the app adapts to the breakpoint', () => {
  test('the shell and the dense screens adapt, and nothing overflows or is too small to tap', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    // Onboarding's club step is itself a dense screen, so it is the first thing checked.
    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();

    const compact = isCompact(page);

    await expect(page.getByTestId(compact ? 'clubs-cards' : 'clubs-table')).toBeVisible();
    await expect(page.getByTestId(compact ? 'clubs-table' : 'clubs-cards')).toBeHidden();
    await expectTouchTarget(page.getByRole('button', { name: /^Take over/ }).first());
    await expectNoHorizontalOverflow(page);

    await page.getByRole('button', { name: /^Take over/ }).first().click();
    await expect(page).toHaveURL(/\/dashboard$/);

    // Navigation: from `md` up the sidebar is present and there is no menu button; below it the
    // destinations are behind the header disclosure.
    if (compact) {
      const menu = page.getByRole('button', { name: 'Menu' });

      await expect(menu).toBeVisible();
      await expect(menu).toHaveAttribute('aria-expanded', 'false');
      await expectTouchTarget(menu);

      await menu.click();
      await expect(menu).toHaveAttribute('aria-expanded', 'true');
      await expect(
        page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Squad' }),
      ).toBeVisible();

      // Escape is the expected dismissal for a disclosure.
      await page.keyboard.press('Escape');
      await expect(
        page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Squad' }),
      ).toHaveCount(0);
    } else {
      await expect(page.getByRole('button', { name: 'Menu' })).toHaveCount(0);
      await expect(
        page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Squad' }),
      ).toBeVisible();
    }

    // The squad: a card list on a phone, the sortable table from `md` up, never both.
    await navigateTo(page, 'Squad');
    await expect(page).toHaveURL(/\/squad$/);

    const roster = page.getByTestId(compact ? 'squad-cards' : 'squad-table');

    await expect(roster).toBeVisible();
    await expect(page.getByTestId(compact ? 'squad-table' : 'squad-cards')).toBeHidden();
    await expectTouchTarget(roster.getByRole('link', { name: /^Open / }).first());
    await expectNoHorizontalOverflow(page);

    // Training follows the same pattern, and its primary action is a touch target.
    await navigateTo(page, 'Training');
    await expect(page).toHaveURL(/\/training$/);
    await expect(page.getByTestId(compact ? 'training-cards' : 'training-table')).toBeVisible();
    await expectTouchTarget(page.getByRole('button', { name: /(Set|Save) plan/ }));
    await expectNoHorizontalOverflow(page);

    // The read-heavy screens keep their tables, but the table scrolls inside its own box rather than
    // pushing the page sideways.
    for (const destination of ['Finances', 'Competitions', 'Transfers', 'Scouting', 'Inbox']) {
      await navigateTo(page, destination);
      await expectNoHorizontalOverflow(page);
    }

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();
    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });
});
