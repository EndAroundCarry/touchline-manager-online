// Auction contention: many managers bidding on one listing at its close.
//
// This is threat D-5. A losing bid is a `409`, which is an expected answer rather than a failure, so
// the scenario tells k6 that a `400`/`409` is not a transport error and keeps `http_req_failed` for
// the thing that actually matters: a `5xx`.
//
// The money invariant is asserted after the run, not here: `GET /transfers/history` must show exactly
// one completed transfer for the listing and the ledger must reconcile (see the README).

import { check } from 'k6';
import http from 'k6/http';

import { credentials, POPULATION, SMOKE, WINDOW, vus } from './config.js';
import { commandThresholds, pick, postAs, signIn } from './lib.js';

// A bid that loses the race is `409` and a malformed one is `400`; neither is a failure of the system.
http.setResponseCallback(http.expectedStatuses(200, 201, 400, 409));

export const options = {
  scenarios: {
    bids: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: SMOKE ? '2s' : '10s', target: vus(POPULATION.virtualUsers) },
        { duration: WINDOW, target: vus(POPULATION.virtualUsers) },
        { duration: SMOKE ? '1s' : '5s', target: 0 },
      ],
    },
  },
  thresholds: commandThresholds('bid'),
};

export function setup() {
  const pool = credentials.accounts
    .map((account) => ({ ...account, token: signIn(account) }))
    // A manager cannot bid on their own listing.
    .filter((account) => account.token !== null && account.clubId !== credentials.listings[0].sellerClubId);

  return { pool };
}

export default function (data) {
  const account = pick(data.pool, __VU);
  const listing = credentials.listings[0];

  const response = postAs(
    account.token,
    `/transfers/listings/${listing.listingId}/bids`,
    { amountMinor: listing.minimumAcceptableBidMinor },
    'bid',
    { 'Idempotency-Key': `load-bid-${listing.listingId}-${__VU}-${__ITER}` },
  );

  check(response, {
    'a bid is accepted or refused, never a server error': (result) => result.status < 500,
  });
}
