import { Page } from '@playwright/test';

/**
 * Reaching a destination regardless of breakpoint.
 *
 * The shell shows a persistent sidebar from `md` (768px) up, and a header disclosure below it
 * (`F-44`, master plan §11.3). A journey that clicked a sidebar link therefore breaks on a phone —
 * not because the destination is missing, but because it is behind the menu. These helpers make the
 * core suite breakpoint-agnostic, so the same journey runs at desktop and mobile.
 */
const SHELL_DESKTOP_MIN_WIDTH = 768;

/** Whether the current project renders the compact (mobile) shell. */
export function isCompact(page: Page): boolean {
  return (page.viewportSize()?.width ?? SHELL_DESKTOP_MIN_WIDTH) < SHELL_DESKTOP_MIN_WIDTH;
}

/** Opens the mobile navigation panel when the shell is showing it; a no-op on the sidebar. */
export async function openNavIfNeeded(page: Page): Promise<void> {
  const menu = page.getByRole('button', { name: 'Menu' });

  // `isVisible` is false when the button is absent or hidden, so this never waits on a desktop.
  if (await menu.isVisible()) {
    await menu.click();
  }
}

/** Reaches a shell destination whether the sidebar or the mobile disclosure is showing. */
export async function navigateTo(page: Page, label: string): Promise<void> {
  await openNavIfNeeded(page);
  await page.getByRole('link', { name: label }).click();
}
