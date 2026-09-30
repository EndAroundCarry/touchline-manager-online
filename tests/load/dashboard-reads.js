// A dashboard read burst: three times the projected launch population reading the screens a manager
// opens when they check in.
//
// The four reads are the ones every session repeats — the club dashboard, the manager's fixtures, the
// division table, and the `/sync` poll — and they are the query paths the read SLO covers.

import { credentials, POPULATION, SMOKE, WINDOW, vus } from './config.js';
import { get, pick, readThresholds, signIn } from './lib.js';

export const options = {
  scenarios: {
    reads: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: SMOKE ? '2s' : '15s', target: vus(POPULATION.virtualUsers) },
        { duration: WINDOW, target: vus(POPULATION.virtualUsers) },
        { duration: SMOKE ? '2s' : '5s', target: 0 },
      ],
    },
  },
  thresholds: readThresholds,
};

// Access tokens live fifteen minutes (ADR-0002); the run is shorter than that, so one sign-in per
// account in setup is enough and the burst measures reads rather than authentication.
export function setup() {
  const pool = credentials.accounts
    .map((account) => ({ ...account, token: signIn(account) }))
    .filter((account) => account.token !== null);

  return { pool };
}

export default function (data) {
  const account = pick(data.pool, __VU);

  get(account.token, `/clubs/${account.clubId}/dashboard`, 'dashboard');
  get(account.token, '/fixtures/mine', 'fixtures');
  get(account.token, `/divisions/${account.divisionId}/table`, 'table');
  get(account.token, '/sync', 'sync');
}
