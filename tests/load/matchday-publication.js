// The matchday publication SLO: a round locks, simulates, and publishes inside its window.
//
// This is the stage's headline objective — 99% of matchdays published within five minutes of kickoff.
// The scenario asks the non-production diagnostics trigger to play the next round (ADR-0016) and then
// polls the division until the round reads `published`, recording how long it took. The worker does
// all the work, so the worker and `Diagnostics__EnableMatchdayTrigger` must both be running.
//
// It needs a round that is actually due. On the seeded world the next kickoff is days away, the
// scheduler has already materialised its jobs, and the trigger is a no-op — so the round cannot
// publish and the SLO is correctly reported unmet. Run this against the compressed-clock matchday
// stack (ADR-0015), where the deadline arrives within the run, or accept the report as "no round due".
// Set LOAD_FORCE=1 to run it on a real-time stack anyway.
//
// The drill asserts a tighter bound than the SLO (two minutes) because it runs on a quiet machine; the
// SLO's five minutes is what a loaded one is measured against in staging.

import { check, fail, sleep } from 'k6';
import { Trend } from 'k6/metrics';

import { credentials } from './config.js';
import { get, postAs, signIn } from './lib.js';

const publicationSeconds = new Trend('publication_seconds');

const DRILL_BOUND_SECONDS = 120;
const POLL_LIMIT_SECONDS = 300;
const DUE_SOON_MINUTES = 30;

export const options = {
  scenarios: {
    publication: {
      executor: 'per-vu-iterations',
      vus: 1,
      iterations: 1,
    },
  },
  thresholds: {
    publication_seconds: [`p(95)<${DRILL_BOUND_SECONDS}`],
    checks: ['rate==1'],
  },
};

export function setup() {
  const account = credentials.accounts[0];
  const token = signIn(account);

  return { token, divisionId: account.divisionId };
}

export default function (data) {
  const matchdayId = credentials.matchdayId;
  const due = minutesToKickoff(data);

  if (due === null) {
    fail('the seeded round is not in the division\'s calendar any more — re-run `npm run load:seed`');
  }

  if (due > DUE_SOON_MINUTES && __ENV.LOAD_FORCE !== '1') {
    fail(
      `the next round kicks off in about ${Math.round(due)} game minutes, so it cannot publish. `
      + 'Run this against the compressed-clock matchday stack (ADR-0015), or set LOAD_FORCE=1 to run it anyway.',
    );
  }

  const trigger = postAs(data.token, '/ops/diagnostics/play-matchday', { matchdayId }, 'play-matchday');

  check(trigger, {
    'the diagnostics trigger is enabled': (result) => result.status !== 404,
    'the round was triggered': (result) => result.status === 202,
  });

  let elapsed = 0;
  let status = 'pending';

  while (elapsed < POLL_LIMIT_SECONDS && status !== 'published') {
    sleep(2);
    elapsed += 2;

    const fixtures = get(data.token, `/divisions/${data.divisionId}/fixtures`, 'poll-publication');
    const round = (fixtures.json('matchdays') || []).find((matchday) => matchday.id === matchdayId);

    status = round ? round.publicationStatus : 'missing';
  }

  publicationSeconds.add(elapsed);

  check({ elapsed, status }, {
    'the round published': (result) => result.status === 'published',
    'inside the drill bound': (result) => result.elapsed < DRILL_BOUND_SECONDS,
  });
}

/** How many game minutes until the round's kickoff, read from the server's own clock. */
function minutesToKickoff(data) {
  const fixtures = get(data.token, `/divisions/${data.divisionId}/fixtures`, 'round-clock');
  const matchday = (fixtures.json('matchdays') || []).find((candidate) => candidate.id === credentials.matchdayId);

  if (!matchday) {
    return null;
  }

  const serverTime = fixtures.json('serverTime');

  return (Date.parse(matchday.kickoffAt) - Date.parse(serverTime)) / 60_000;
}
