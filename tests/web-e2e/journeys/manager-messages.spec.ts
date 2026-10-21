import { APIRequestContext, Browser, BrowserContext, Page, expect, test } from '@playwright/test';
import { createAccount, TestAccount } from '../support/account';
import { expectNoA11yViolations } from '../support/accessibility';
import { createVerifiedManager } from '../support/auth-flows';
import { confirmKitColours } from '../support/kit-colours';
import { openNavIfNeeded } from '../support/navigation';

/**
 * Messages between managers (`COM-10`…`COM-16`).
 *
 * Two managers each take over a club, in two browser contexts so each has their own session. The first opens
 * the other's club page, sees the manager's name, and writes to them. The second sees the unread badge, opens
 * the thread, and replies; the reply shows on the first side. What this proves that a unit test cannot is the
 * whole route: the club page link, the compose form, the real endpoints, the sync poll's count and the read
 * mark, in a browser.
 *
 * Like the other journeys it gives both clubs back at the end, so the shared world is left as it was found.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface MyFixturesRead {
  readonly clubId: string;
  readonly clubName: string;
}

/** A second browser context that looks like the first: same device, same base address. */
async function secondContext(browser: Browser): Promise<BrowserContext> {
  const use = test.info().project.use;

  return browser.newContext({
    baseURL: use.baseURL,
    viewport: use.viewport ?? undefined,
    userAgent: use.userAgent,
    isMobile: use.isMobile,
    hasTouch: use.hasTouch,
    deviceScaleFactor: use.deviceScaleFactor,
  });
}

async function takeOverAClub(page: Page): Promise<void> {
  await page.goto('/onboarding/manager');
  await page.getByRole('button', { name: 'Create my profile' }).click();
  await page.getByRole('button', { name: 'See the clubs' }).first().click();
  await page
    .getByRole('button', { name: /^Take over/ })
    .first()
    .click();
  await confirmKitColours(page);

  await expect(page).toHaveURL(/\/dashboard$/);
}

async function clubOf(request: APIRequestContext, account: TestAccount): Promise<MyFixturesRead> {
  const login = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email: account.email, password: account.password },
  });

  expect(login.ok()).toBe(true);

  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const mine = await request.get(`${apiBaseUrl}/api/v1/fixtures/mine`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(mine.ok()).toBe(true);

  return (await mine.json()) as MyFixturesRead;
}

async function resign(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await page.getByRole('button', { name: 'Resign from this club' }).click();

  await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
}

test.describe('messages between managers', () => {
  test('a manager writes to another, who sees the badge, reads it and replies', async ({
    page,
    browser,
    request,
  }) => {
    test.setTimeout(150_000);

    const first = createAccount();
    const second = createAccount();
    const secondContextHandle = await secondContext(browser);
    const secondPage = await secondContextHandle.newPage();

    await createVerifiedManager(page, request, first);
    await takeOverAClub(page);

    await createVerifiedManager(secondPage, request, second);
    await takeOverAClub(secondPage);

    const theirClub = await clubOf(request, second);

    // The first manager opens the other's club page: it names the manager and offers to write to them.
    await page.goto(`/clubs/${theirClub.clubId}`);

    await expect(page.getByTestId('club-manager')).toContainText(`Manager: ${second.displayName}`);

    await page.getByRole('link', { name: 'Send a message' }).click();

    await expect(page.getByRole('heading', { name: 'New message', level: 1 })).toBeVisible();
    await expect(page.getByTestId('compose-recipient')).toContainText(
      `To ${second.displayName}, manager of ${theirClub.clubName}`,
    );
    await expectNoA11yViolations(page);

    await page.getByLabel('Subject').fill('A friendly before the cup');
    await page.getByLabel('Message', { exact: true }).fill('Fancy a game?\nWe have a free week.');
    await page.getByRole('button', { name: 'Send message' }).click();

    // Sending lands on the thread, with the message as written.
    await expect(page).toHaveURL(/\/inbox\/managers\/[0-9a-f-]+$/);
    await expect(
      page.getByRole('heading', { name: 'A friendly before the cup', level: 1 }),
    ).toBeVisible();
    await expect(page.getByTestId('thread-messages')).toContainText('Fancy a game?');
    await expectNoA11yViolations(page);

    // The second manager's shell reads the count when it loads, so a fresh load shows the badge.
    await secondPage.goto('/dashboard');

    // The shell has rendered once the header's sign-out button is there; the menu is checked only then.
    await expect(secondPage.getByRole('button', { name: 'Sign out' }).first()).toBeVisible();
    await openNavIfNeeded(secondPage);

    const inboxLink = secondPage
      .getByRole('navigation', { name: 'Main' })
      .getByRole('link', { name: /Inbox/ });

    // The badge adds the game's welcome message and the direct message together (`COM-16`).
    await expect(inboxLink).toHaveAccessibleName('Inbox 2 unread messages');
    await inboxLink.click();

    // The Managers section carries its own count, and the thread is listed as unread.
    const managersTab = secondPage
      .getByRole('navigation', { name: 'Inbox sections' })
      .getByRole('link', { name: /Managers/ });

    await expect(managersTab).toContainText('1');
    await managersTab.click();

    await expect(secondPage.getByTestId('conversation-list')).toContainText(
      'A friendly before the cup',
    );
    await expect(secondPage.getByTestId('conversation-list')).toContainText('1 unread');

    await secondPage.getByRole('link', { name: 'A friendly before the cup' }).click();

    await expect(secondPage.getByTestId('thread-messages')).toContainText('Fancy a game?');
    await expectNoA11yViolations(secondPage);

    await secondPage.getByLabel('Your reply').fill('Yes, Saturday works.');
    await secondPage.getByRole('button', { name: 'Send reply' }).click();

    await expect(secondPage.getByTestId('thread-messages')).toContainText('Yes, Saturday works.');

    // Opening the thread marked it read: the list no longer says unread.
    await secondPage.goto('/inbox/managers');

    await expect(secondPage.getByTestId('conversation-list')).not.toContainText('unread');

    // The first manager sees the reply in the thread.
    await page.goto('/inbox/managers');

    await expect(page.getByTestId('conversation-list')).toContainText('1 unread');

    await page.getByRole('link', { name: 'A friendly before the cup' }).click();

    await expect(page.getByTestId('thread-messages')).toContainText('Yes, Saturday works.');

    // Give both clubs back, as the other journeys do.
    await resign(page);
    await resign(secondPage);
    await secondContextHandle.close();
  });

  test('a visitor with no session cannot reach the messages', async ({ page }) => {
    await page.goto('/inbox/managers');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
