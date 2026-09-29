import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';
import { isCompact, navigateTo } from '../support/navigation';

/**
 * The Stage 4 exit criteria for the squad screens (F-16, F-17).
 *
 * A manager onboards, reads the squad the club was generated with, sorts and filters it, and opens a
 * player. The two things this journey exists to prove are the ones a unit test cannot: that the squad a
 * takeover inherits is the twenty-two players the generator produced, and that an attribute on screen
 * carries a number **and** the word for its band, because master plan §11.3 forbids colour being the only
 * signal.
 *
 * Like the onboarding journey, it gives its club back at the end, so the shared persistent world is left
 * as it was found and the suite can run repeatedly.
 */
test.describe('squad', () => {
  test('a manager reads the inherited squad, sorts it, filters it, and opens a player', async ({
    page,
    request,
  }) => {
    const account = createAccount();

    await createVerifiedManager(page, request, account);

    // Onboard the same way a manager does, so the squad read is scoped to a club they actually hold.
    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await page.getByRole('button', { name: /^Take over/ }).first().click();

    await expect(page).toHaveURL(/\/dashboard$/);

    // The shell now offers the squad, which is how a manager reaches it.
    await navigateTo(page, 'Squad');

    await expect(page).toHaveURL(/\/squad$/);
    await expect(page.getByRole('heading', { name: /squad$/ })).toBeVisible();

    // Below `md` the squad is a card list, not the table, with the same twenty-two players (`F-44`),
    // so every assertion is scoped to the layout this breakpoint is showing.
    const compact = isCompact(page);
    const roster = compact ? page.getByTestId('squad-cards') : page.getByTestId('squad-table');
    const rows = compact ? roster.locator('li') : roster.locator('tbody tr');

    await expect(roster).toBeVisible();

    // The inherited squad is the generated one: twenty-two players, each with a profile to open (SQ-1).
    await expect(rows).toHaveCount(22);
    await expect(roster.getByRole('link', { name: /^Open / })).toHaveCount(22);
    await expect(page.getByText(/22 players/)).toBeVisible();

    // Sorting is the table's own, and it announces the direction rather than only tinting the header
    // (master plan §11.1, §11.3). The compact card list filters but does not sort, so this is the
    // table's contract to prove. Scoped to the squad table, because the contract list below it also has
    // an "Age" column.
    if (!compact) {
      const age = roster.getByRole('columnheader', { name: /^Age/ });

      await age.click();
      await expect(age).toHaveAttribute('aria-sort', 'ascending');

      await age.click();
      await expect(age).toHaveAttribute('aria-sort', 'descending');
    }

    // Filtering is client-side over the bounded response (§10.3), and applies to both layouts.
    const firstRowName = compact
      ? (await rows.first().locator('h3').first().innerText()).trim()
      : (await rows.first().locator('th').first().innerText()).split('\n')[0].trim();
    const surname = firstRowName.slice(firstRowName.lastIndexOf(' ') + 1);

    await page.getByLabel('Search by name').fill(surname);

    await expect(page.getByText(/Showing \d+ of 22 players/)).toBeVisible();
    await expect(rows.first()).toContainText(surname);

    await page.getByRole('button', { name: 'Clear filters' }).click();
    await expect(rows).toHaveCount(22);

    // The player profile is where the attribute grid lives (F-17).
    await roster.getByRole('link', { name: /^Open / }).first().click();

    await expect(page).toHaveURL(/\/players\//);
    await expect(page.getByRole('heading', { name: 'Attributes', level: 2 })).toBeVisible();

    // The gate: the number and the band word are both on screen, so the colour is decoration.
    const finishing = page.locator('app-attribute-value', { hasText: 'Finishing' });

    await expect(finishing).toContainText('Finishing');
    await expect(finishing).toContainText(/\d+/);
    await expect(finishing).toContainText(/Low|Average|Strong/);

    // State is shown on the user-facing scale the API converts to, not in basis points (TRN-8).
    await expect(page.getByRole('heading', { name: 'State', level: 2 })).toBeVisible();
    await expect(page.getByText('Match sharpness')).toBeVisible();

    await page.getByRole('link', { name: 'Back to the squad' }).click();
    await expect(page).toHaveURL(/\/squad$/);

    // Give the club back, as the onboarding journey does.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the squad', async ({ page }) => {
    await page.goto('/squad');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
