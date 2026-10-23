// The matchday polling load.
//
// Every visible client polls `/sync` once a minute (ADR-0007), so 864 clients are about 14.4 polls a
// second and a little under 29 requests a second with the fixture read beside it. The scenario is a
// constant-arrival-rate executor at exactly that rate rather than 864 looping virtual users, because
// the real traffic is a slow, wide trickle rather than a hot loop — modelling it faithfully is what
// makes the read SLO meaningful.

import { credentials, POPULATION, SMOKE, WINDOW, rate } from './config.js';
import { get, pick, readThresholds, signIn } from './lib.js';

const pollsPerSecond = Math.ceil((POPULATION.virtualUsers * 2) / 60);

export const options = {
  scenarios: {
    polling: {
      executor: 'constant-arrival-rate',
      rate: rate(pollsPerSecond),
      timeUnit: '1s',
      duration: WINDOW,
      preAllocatedVUs: Math.max(2, Math.round(POPULATION.virtualUsers / 20)),
      maxVUs: POPULATION.virtualUsers,
    },
  },
  thresholds: readThresholds,
};

export function setup() {
  const pool = credentials.accounts
    .map((account) => ({ ...account, token: signIn(account) }))
    .filter((account) => account.token !== null);

  return { pool };
}

export default function (data) {
  const account = pick(data.pool, __VU * 7 + __ITER);

  get(account.token, '/sync', 'sync');
  get(account.token, '/fixtures/mine', 'fixtures');
}
