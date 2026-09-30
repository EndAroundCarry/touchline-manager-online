// A sign-in burst: three times the projected launch population signing in at once.
//
// The fixture is the credential-stuffing worst case (threat D-1) — the point is not the happy path but
// whether the credential endpoint stays inside the command SLO and answers no 5xx under a sudden load.
//
// Run it with the rate limits raised, or it measures the limiter rather than the system (see the README).

import { check, sleep } from 'k6';

import { credentials, POPULATION, SMOKE, SLO, WINDOW, vus } from './config.js';
import { post } from './lib.js';

export const options = {
  scenarios: {
    login: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: SMOKE ? '2s' : '20s', target: vus(POPULATION.virtualUsers) },
        { duration: WINDOW, target: vus(POPULATION.virtualUsers) },
        { duration: SMOKE ? '2s' : '10s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: [`rate<${SLO.errorRate}`],
    'http_req_duration{name:login}': [`p(95)<${SLO.commandP95Ms}`],
  },
};

export default function () {
  const account = credentials.accounts[__VU % credentials.accounts.length];
  const response = post('/auth/login', { email: account.email, password: account.password }, 'login');

  check(response, { 'signed in': (result) => result.status === 200 });

  sleep(SMOKE ? 0.1 : 1);
}
