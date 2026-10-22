import { expect, Page } from '@playwright/test';

/**
 * Takes the club over as it stands, on the choice of start that follows "Take over".
 *
 * Every takeover is offered two starts (keep the club, or a clean start). None of the journeys is about the start,
 * so they keep the club, which is what taking a club over has always meant.
 */
export async function keepClubAsItStands(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { name: /^How do you want to take over/ })).toBeVisible();
  await page.getByRole('button', { name: 'Keep the club as it is' }).click();
}

/**
 * Confirms the colours a club was generated with, on the step that follows taking a club over.
 *
 * Every journey that claims a club passes through this step before it reaches the dashboard, and none of
 * them is about colours, so they accept what the club arrived in. The choice of start comes first, and is
 * answered here too, so the journeys that click "Take over" and then call this keep working unchanged.
 */
export async function confirmKitColours(page: Page): Promise<void> {
  await keepClubAsItStands(page);
  await expect(page.getByRole('heading', { name: 'Choose your colours' })).toBeVisible();
  await page.getByRole('button', { name: 'Confirm colours' }).click();
}
