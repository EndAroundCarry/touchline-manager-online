// The suite's HTTP helpers, shared by every scenario.
//
// Each request carries a `kind` tag (`read` or `command`) and a `name` tag, so a scenario's thresholds
// can assert the read SLO and the command SLO separately, per endpoint, from one metrics stream.

import http from 'k6/http';

import { SLO } from './config.js';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080/api/v1';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

/** An authenticated GET, tagged as a read. */
export function get(token, path, name) {
  return http.get(`${BASE_URL}${path}`, {
    headers: { ...JSON_HEADERS, Authorization: `Bearer ${token}` },
    tags: { kind: 'read', name },
  });
}

/** An unauthenticated POST, tagged as a command. */
export function post(path, body, name) {
  return http.post(`${BASE_URL}${path}`, JSON.stringify(body), {
    headers: JSON_HEADERS,
    tags: { kind: 'command', name },
  });
}

/** An authenticated POST, tagged as a command. */
export function postAs(token, path, body, name, headers = {}) {
  return http.post(`${BASE_URL}${path}`, JSON.stringify(body), {
    headers: { ...JSON_HEADERS, Authorization: `Bearer ${token}`, ...headers },
    tags: { kind: 'command', name },
  });
}

/** Signs one account in and returns a live access token, or null when the sign-in did not succeed. */
export function signIn(account) {
  const response = post('/auth/login', { email: account.email, password: account.password }, 'login');

  return response.status === 200 ? response.json('accessToken') : null;
}

/** A stable pseudo-random pick, so a run is reproducible without k6's random source. */
export function pick(list, index) {
  return list[Math.abs(index) % list.length];
}

/** Per-endpoint read thresholds over the tagged read metric. */
export const readThresholds = {
  http_req_failed: [`rate<${SLO.errorRate}`],
  'http_req_duration{kind:read}': [`p(95)<${SLO.readP95Ms}`, `p(99)<${SLO.readP95Ms * 2}`],
};

/** Per-endpoint command thresholds over the tagged command metric. */
export const commandThresholds = (name) => ({
  http_req_failed: [`rate<${SLO.errorRate}`],
  [`http_req_duration{name:${name}}`]: [`p(95)<${SLO.commandP95Ms}`],
});
