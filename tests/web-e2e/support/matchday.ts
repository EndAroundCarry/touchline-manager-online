import { APIRequestContext, expect } from '@playwright/test';

/**
 * The helpers the matchday watch journey needs beyond the browser.
 *
 * Two of the three things a journey needs to play a real round are not buttons: signing in to drive the
 * API as the harness, and telling the non-production trigger to play the next round (ADR-0016). Both go
 * through the real endpoints, so a journey that passes has exercised the same code a client would.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

/** The access token a sign-in answers with. */
export interface LoginRead {
  readonly accessToken: string;
}

/** One of a managed club's fixtures, as the club's own list reports it. */
export interface ClubFixture {
  readonly id: string;
  readonly status: string;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly matchId: string | null;
  readonly outcome: string | null;
}

/** A managed club's season, with the next fixture named. */
export interface MyFixturesRead {
  readonly nextFixtureId: string | null;
  readonly fixtures: readonly ClubFixture[];
}

/** One fixture in full, which is where its matchday comes from. */
export interface FixtureDetailRead {
  readonly id: string;
  readonly matchdayId: string;
  readonly status: string;
  readonly matchId: string | null;
}

/** Signs in through the API so the journey can act as the harness. */
export async function apiAccessToken(
  request: APIRequestContext,
  email: string,
  password: string,
): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email, password },
  });

  expect(response.ok()).toBe(true);

  return ((await response.json()) as LoginRead).accessToken;
}

/** Reads the manager's own club's season. */
export async function readMyFixtures(
  request: APIRequestContext,
  token: string,
): Promise<MyFixturesRead> {
  const response = await request.get(`${apiBaseUrl}/api/v1/fixtures/mine`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(response.ok()).toBe(true);

  return (await response.json()) as MyFixturesRead;
}

/** Reads one fixture in full, which names the round it belongs to. */
export async function readFixture(
  request: APIRequestContext,
  token: string,
  fixtureId: string,
): Promise<FixtureDetailRead> {
  const response = await request.get(`${apiBaseUrl}/api/v1/fixtures/${fixtureId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(response.ok()).toBe(true);

  return (await response.json()) as FixtureDetailRead;
}

/**
 * Asks the non-production trigger to play a round now.
 *
 * The endpoint enqueues the round's real lock and resolution jobs; the worker does the rest. It is reached
 * without a bearer token, exactly as the job probe is, because it is gated by configuration rather than by
 * who is asking.
 */
export async function playMatchday(request: APIRequestContext, matchdayId: string): Promise<void> {
  const response = await request.post(`${apiBaseUrl}/api/v1/ops/diagnostics/play-matchday`, {
    data: { matchdayId },
  });

  expect(
    response.status(),
    `the matchday trigger should accept the round (${matchdayId})`,
  ).toBe(202);
}

/**
 * Waits for a fixture to publish, polling the club's own list.
 *
 * The worker polls for jobs on its own interval in a separate process, so the journey cannot know the exact
 * moment the round plays; it watches for the result instead. A published fixture carries its score and the
 * match id the viewer reads.
 */
export async function waitForFixturePublished(
  request: APIRequestContext,
  token: string,
  fixtureId: string,
  timeoutMs = 60_000,
): Promise<ClubFixture> {
  const deadline = Date.now() + timeoutMs;
  const pollIntervalMs = 500;

  while (Date.now() < deadline) {
    const mine = await readMyFixtures(request, token);
    const fixture = mine.fixtures.find((candidate) => candidate.id === fixtureId);

    if (fixture !== undefined && fixture.status === 'published' && fixture.matchId !== null) {
      return fixture;
    }

    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
  }

  throw new Error(`Fixture ${fixtureId} did not publish within ${timeoutMs}ms.`);
}
