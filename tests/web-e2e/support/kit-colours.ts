import { expect, Page } from '@playwright/test';

/**
 * Confirms the colours a club was generated with, on the step that follows taking a club over.
 *
 * Every journey that claims a club passes through this step before it reaches the dashboard, and none of
 * them is about colours, so they accept what the club arrived in.
 */
export async function confirmKitColours(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { name: 'Choose your colours' })).toBeVisible();
  await page.getByRole('button', { name: 'Confirm colours' }).click();
}
