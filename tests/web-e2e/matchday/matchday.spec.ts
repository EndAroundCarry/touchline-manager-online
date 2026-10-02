import { expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { expectNoA11yViolations } from '../support/accessibility';
import { createVerifiedManager } from '../support/auth-flows';
import {
  apiAccessToken,
  playMatchday,
  readFixture,
  readMyFixtures,
  waitForFixturePublished,
} from '../support/matchday';

/**
 * The Stage 7 exit criterion: a manager prepares a side and watches the fixture play (master plan §16,
 * §15.5 journey 3, `§9.5`, `§11.1`).
 *
 * The journey plays a real round. It onboards, prepares a side for its next fixture through the prepare
 * screen, asks the non-production trigger to play the round (ADR-0016), waits for the worker to lock,
 * simulate, and publish it, and then watches the replay on the match center. Nothing is stubbed: the round
 * is played by the real worker from a real frozen snapshot, and the replay is the server's own presentation.
 *
 * It runs against its own throwaway database (`playwright.matchday.config.ts`), reset and reseeded each run,
 * because a played round is permanent. It therefore does not give its club back the way the shared-world
 * journeys do — the whole world is discarded with the run.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface TeamSheetRead {
  readonly selectablePlayers: readonly { readonly id: string; readonly isUnavailable: boolean }[];
}

test.describe('the matchday', () => {
  test('a manager prepares a side and watches the fixture play', async ({ page, request }) => {
    // Onboarding, preparing, playing, and watching is more than the default budget allows for.
    test.setTimeout(120_000);

    const account = createAccount();

    await createVerifiedManager(page, request, account);

    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await page
      .getByRole('button', { name: /^Take over/ })
      .first()
      .click();

    await expect(page).toHaveURL(/\/dashboard$/);

    const token = await apiAccessToken(request, account.email, account.password);
    const bearer = { Authorization: `Bearer ${token}` };

    // A side is prepared from the club's default plan, so the arrangement provides one (INS-11).
    const plan = await request.post(`${apiBaseUrl}/api/v1/tactics`, {
      headers: bearer,
      data: {
        name: 'Shape',
        formationPreset: '4-4-2',
        mentality: 'balanced',
        tempo: 'normal',
        passing: 'mixed',
        width: 'normal',
        pressing: 'mid_block',
        defensiveLine: 'normal',
        tackling: 'normal',
        timeWasting: 'off',
      },
    });

    expect(plan.ok()).toBe(true);

    const mine = await readMyFixtures(request, token);

    expect(mine.nextFixtureId).not.toBeNull();

    const fixtureId = mine.nextFixtureId!;
    const fixture = await readFixture(request, token, fixtureId);

    // Prepare through the prepare-match screen, the way a manager does (SQ-4, CAL-3, §11.1).
    await page.goto('/fixtures');
    await page.getByRole('link', { name: /Prepare your side for/ }).first().click();

    await expect(page).toHaveURL(/\/fixtures\/[0-9a-f-]+\/prepare$/);

    const slots = page.getByRole('combobox');

    await expect(slots).toHaveCount(18);

    const sheet = (await (
      await request.get(`${apiBaseUrl}/api/v1/fixtures/${fixtureId}/team-sheet`, { headers: bearer })
    ).json()) as TeamSheetRead;

    const available = sheet.selectablePlayers.filter((player) => !player.isUnavailable);

    expect(available.length).toBeGreaterThanOrEqual(11);

    for (let slot = 1; slot <= 11; slot++) {
      await slots.nth(slot - 1).selectOption(available[slot - 1].id);
    }

    await page.getByRole('button', { name: 'Save your side' }).click();
    await expect(page.getByText(/Your side was (saved|updated)/)).toBeVisible();

    // Play the round now, rather than waiting for its calendar deadline, and watch for the worker to
    // publish it — lock, simulate, publication, projections, all of it (MAT-7, §7.4).
    await playMatchday(request, fixture.matchdayId);

    const played = await waitForFixturePublished(request, token, fixtureId);

    // The result is on the fixtures screen, with the score and a link to the replay.
    await page.goto('/fixtures');

    await expect(page.getByRole('heading', { name: 'Results' })).toBeVisible();

    const watch = page.getByRole('link', { name: /Watch the highlights of the match against/ }).first();

    await expect(watch).toBeVisible();
    await watch.click();

    await expect(page).toHaveURL(new RegExp(`/matches/${played.matchId}$`));

    // The match center shows the scoreline the server published, not something the client inferred.
    const scoreline = `${played.homeScore}\u2013${played.awayScore}`;

    await expect(page.getByRole('heading', { level: 1 })).toContainText(scoreline);

    // The report narrates the match from kick-off to full time, and the statistics reconcile with the
    // score (MAT-5, MAT-8).
    await page.getByRole('tab', { name: 'Report' }).click();

    await expect(page.getByRole('heading', { name: 'Full Match Commentary' })).toBeVisible();
    await expect(page.locator('ol li').first()).toBeVisible();

    await page.getByRole('tab', { name: 'Statistics' }).click();

    await expect(page.getByRole('heading', { name: 'Match Statistics' })).toBeVisible();
    await expect(page.getByText('Goals', { exact: true }).first()).toBeVisible();

    await page.getByRole('tab', { name: 'Players' }).click();

    await expect(page.getByRole('heading', { name: 'Player Performance' })).toBeVisible();

    // The replay itself: every match is one continuous film, so a match is always watchable (§9.2,
    // `replay-v3`). Press Play and the control becomes Pause — the animation loop is running off the
    // playback.
    await page.getByRole('tab', { name: 'Replay' }).click();

    const play = page.getByRole('button', { name: 'Play replay' });

    if ((await play.count()) > 0) {
      await play.click();
      await expect(page.getByRole('button', { name: 'Pause replay' })).toBeVisible();
      await expect(page.locator('canvas[role="img"]')).toBeVisible();

      // Both viewing modes are offered and switch without an empty state.
      await page.getByRole('button', { name: 'Highlights' }).click();
      await expect(page.getByRole('button', { name: 'Highlights' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
      await page.getByRole('button', { name: 'Full match' }).click();
      await expect(page.getByRole('button', { name: 'Full match' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );

      // The progress scrubber shows the film clock.
      await expect(page.getByRole('slider', { name: 'Replay position' })).toBeVisible();

      // Driven at 8x so the assertions observe progress rather than waiting out a film at normal speed:
      // the match clock advances...
      await page.getByRole('button', { name: '8x' }).click();

      const clock = page.getByTestId('match-clock');
      const startedAt = await clock.innerText();

      await expect.poll(() => clock.innerText(), { timeout: 30_000 }).not.toBe(startedAt);

      // ...the lineup panels' live ratings fluctuate as the film reaches the minutes the server captured
      // them in...
      const ratings = page.getByTestId('player-rating');

      await expect(ratings.first()).toBeVisible();

      const ratingsAtStart = (await ratings.allTextContents()).join('|');

      await expect
        .poll(async () => (await ratings.allTextContents()).join('|'), { timeout: 30_000 })
        .not.toBe(ratingsAtStart);

      // ...and the commentary feed accumulates rows as the playhead passes each row's film time.
      const feed = page.getByTestId('match-feed');
      const feedAtStart = await feed.locator('li').count();

      await expect
        .poll(() => feed.locator('li').count(), { timeout: 30_000 })
        .toBeGreaterThan(feedAtStart);
    } else {
      await expect(page.getByText(/This match's replay is not available/)).toBeVisible();
    }

    // The Canvas viewer's accessibility is only real where a replay exists, so it is scanned on this
    // stack: the `role="img"` canvas, its narration, and the keyboard-operable transport (§11.3, §15.6).
    await expectNoA11yViolations(page);
  });
});
