import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';
import { navigateTo } from '../support/navigation';
import { confirmKitColours } from '../support/kit-colours';

/**
 * Guided help (`F-53`, master plan §16 Stage 13).
 *
 * The stage promises help for rules, deadlines, tactics, market and season cadence, plus guidance a new
 * manager meets first. This journey proves the reference page is reachable the way a manager reaches it,
 * that every promised subject is on it, that a topic leads to the screen that owns it rather than stopping
 * at an explanation, and that the first-steps guidance can be put away. It carries no tag, so it rides the
 * desktop and mobile projects the suite already has.
 */
test.describe('help', () => {
  test('a manager reads the help and puts the first steps away', async ({ page, request }) => {
    const account = createAccount();
    await createVerifiedManager(page, request, account);

    // Help and the first steps are for a manager who holds a club, so one is taken over first.
    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await expect(page).toHaveURL(/\/onboarding\/country$/);

    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await expect(page).toHaveURL(/\/onboarding\/club(\?|$)/);

    await page.getByRole('button', { name: /^Take over/ }).first().click();
    await confirmKitColours(page);
    await expect(page).toHaveURL(/\/dashboard$/);

    // The guidance is offered on the dashboard, and the manager can decline it.
    const firstSteps = page.getByRole('heading', { name: 'Your first steps' });
    await expect(firstSteps).toBeVisible();

    await page.getByRole('button', { name: 'Hide these first steps' }).click();
    await expect(firstSteps).toBeHidden();

    // The help page is a shell destination, so it is opened from the navigation.
    await navigateTo(page, 'Help');
    await expect(page).toHaveURL(/\/help$/);

    for (const subject of [
      'How the competition works',
      'Matchdays and deadlines',
      'Formations, roles and instructions',
      'Scouting and the transfer market',
      'The season and the rollover',
    ]) {
      await expect(page.getByRole('heading', { name: subject, exact: true })).toBeVisible();
    }

    // A topic leads to the screen that owns it.
    await page.getByRole('link', { name: 'Open your tactics board' }).click();
    await expect(page).toHaveURL(/\/tactics$/);

    // Give the club back, as the other journeys do, so the shared world is left as it was found.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();
    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });
});
