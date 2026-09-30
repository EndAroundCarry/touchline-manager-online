#!/usr/bin/env node
//
// Prepares a world for the load suite (master plan §16 Stage 14).
//
// It brings the stack up, migrates and seeds the world, then drives the **real public API** to create
// the projected launch population: one verified manager per club, a manager profile each, a claimed
// club each, and a handful of open listings for the auction scenario. It writes the account pool and
// the ids the scenarios need to `tests/load/.artifacts/credentials.json`.
//
// The path is the product's own — register, confirm the email through the mail catcher, sign in,
// onboard, claim — because a load fixture that bypassed the API would not exercise what it is meant
// to measure. It is meant for a freshly seeded world, and to run against an API whose rate limits are
// raised: 108 registrations from one address would otherwise be throttled (see the README).
//
//   npm run load:seed
//
// Environment: LOAD_API, LOAD_MAILPIT, LOAD_ACCOUNTS (limit the pool), LOAD_SKIP_SETUP=1 (assume the
// stack is already up, migrated, and seeded).

import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const repositoryRoot = resolve(here, '..', '..');
const artifactsDirectory = join(here, '.artifacts');

const API = process.env.LOAD_API ?? 'http://localhost:5080/api/v1';
const MAILPIT = process.env.LOAD_MAILPIT ?? 'http://localhost:8025';

const CLUBS_PER_COUNTRY = 18;
const POPULATION = process.env.LOAD_ACCOUNTS
  ? Number(process.env.LOAD_ACCOUNTS)
  : 6 * CLUBS_PER_COUNTRY;
const CONCURRENCY = 6;
const LISTING_COUNT = 5;

const PASSWORD = 'load-test-password';
const VERIFICATION_TIMEOUT_MS = 30_000;

main().catch((error) => {
  process.stderr.write(`\nThe load seed failed: ${error.message}\n`);
  process.exit(1);
});

async function main() {
  if (process.env.LOAD_SKIP_SETUP !== '1') {
    prepareStack();
  }

  process.stdout.write(`\nOnboarding ${POPULATION} manager(s) through ${API}...\n`);

  const managers = await pool(
    Array.from({ length: POPULATION }, (_, index) => index),
    CONCURRENCY,
    onboardManager,
  );

  const accounts = managers.filter((manager) => manager !== null);
  process.stdout.write(`  ${accounts.length}/${POPULATION} account(s) ready.\n`);

  await claimClubs(accounts);

  const claimed = accounts.filter((account) => account.clubId !== null);
  process.stdout.write(`  ${claimed.length}/${accounts.length} club(s) claimed.\n`);

  const listings = await createListings(claimed);
  const matchdayId = await nextMatchday(claimed[0]);

  mkdirSync(artifactsDirectory, { recursive: true });
  writeFileSync(
    join(artifactsDirectory, 'credentials.json'),
    `${JSON.stringify({ generatedAt: new Date().toISOString(), accounts: claimed, matchdayId, listings }, null, 2)}\n`,
  );

  process.stdout.write(
    `\nWrote tests/load/.artifacts/credentials.json: ${claimed.length} account(s), `
    + `${listings.length} listing(s), next matchday ${matchdayId ?? 'none'}.\n`,
  );
}

/** Brings up PostgreSQL, applies migrations, and seeds the world, exactly as the e2e setup does. */
function prepareStack() {
  process.stdout.write('Preparing the stack (PostgreSQL, migrations, world)...\n');

  run('docker', ['compose', '-f', 'infra/compose.yaml', 'up', '-d', '--wait']);
  run('dotnet', ['tool', 'restore']);
  run('dotnet', ['dotnet-ef', 'database', 'update', '--project', 'src/TouchlineManager.Infrastructure', '--startup-project', 'src/TouchlineManager.Infrastructure']);
  run('dotnet', ['run', '--project', 'tools/world-seeder']);
}

/**
 * Registers one manager and completes their onboarding up to a live session: register, confirm the
 * email through the mail catcher, sign in, and create the manager profile.
 */
async function onboardManager(index) {
  const email = `load-${index}@example.com`;

  const registration = await json('POST', `${API}/auth/register`, {
    email,
    displayName: `Load Manager ${index}`,
    password: PASSWORD,
    acceptTerms: true,
  });

  if (registration.status === 429) {
    throw new Error('registration was rate limited — start the API with the load rate limits raised (see the README)');
  }

  if (registration.status !== 201 && registration.status !== 409) {
    return null;
  }

  // A replayed seed finds the account already registered; the rest of the journey still needs to run.
  if (registration.status === 201) {
    const link = await waitForVerification(email);

    if (link === null) {
      return null;
    }

    await json('POST', `${API}/auth/verify-email`, { userId: link.userId, token: link.token });
  }

  const login = await json('POST', `${API}/auth/login`, { email, password: PASSWORD });

  if (login.status === 429) {
    throw new Error('login was rate limited — start the API with the load rate limits raised (see the README)');
  }

  if (login.status !== 200) {
    return null;
  }

  const token = login.body.accessToken;

  await json('POST', `${API}/manager-profile`, { locale: 'en-GB', timeZone: 'Europe/London' }, token);

  return { email, password: PASSWORD, token, clubId: null, divisionId: null };
}

/**
 * Claims one club per manager.
 *
 * Claims run one country at a time, sequentially, so each country's eighteen managers take eighteen
 * distinct clubs: the available list is read once, its available clubs are ordered by id, and slot
 * `n` takes the nth. Doing this concurrently would let two managers race for the same club as the
 * availability list shifted under them.
 */
async function claimClubs(accounts) {
  for (let start = 0; start < accounts.length; start += CLUBS_PER_COUNTRY) {
    const managers = accounts.slice(start, start + CLUBS_PER_COUNTRY);
    const countries = await json('GET', `${API}/countries`, undefined, managers[0].token);
    const country = countries.body?.[Math.floor(start / CLUBS_PER_COUNTRY)];

    if (!country) {
      continue;
    }

    const available = await json('GET', `${API}/countries/${country.id}/available-clubs`, undefined, managers[0].token);
    const clubs = (available.body?.clubs ?? [])
      .filter((club) => club.isAvailable !== false)
      .sort((left, right) => (left.id < right.id ? -1 : 1));

    for (const [slot, manager] of managers.entries()) {
      const club = clubs[slot];

      if (!club) {
        continue;
      }

      const claim = await json('POST', `${API}/club-claims`, { clubId: club.id }, manager.token, {
        'Idempotency-Key': `load-claim-${club.id}`,
      });

      // 201 the first time, 200 when the seed is replayed against the same club.
      if (claim.status === 201 || claim.status === 200) {
        manager.clubId = club.id;
        manager.divisionId = claim.body?.division?.id ?? null;
      }
    }
  }
}

/** Opens a few listings, one per club, so the auction scenario has something to bid on. */
async function createListings(accounts) {
  const listings = [];

  for (const account of accounts.slice(0, LISTING_COUNT)) {
    const squad = await json('GET', `${API}/clubs/${account.clubId}/squad`, undefined, account.token);
    const candidates = (squad.body?.players ?? []).filter((player) => player.primaryPosition !== 'GK');

    // The server refuses a listing that would take the club below its minimum squad (SQ-2), so try a
    // few players and stop at the first the server accepts.
    for (const player of candidates.slice(0, 3)) {
      const listing = await json(
        'POST',
        `${API}/transfers/listings`,
        { playerId: player.id, minimumFeeMinor: 25_000, seasons: 2 },
        account.token,
        { 'Idempotency-Key': `load-listing-${player.id}` },
      );

      if ((listing.status === 201 || listing.status === 200) && listing.body?.listingId) {
        listings.push({
          listingId: listing.body.listingId,
          sellerClubId: account.clubId,
          minimumAcceptableBidMinor: listing.body.minimumAcceptableBidMinor,
        });

        break;
      }
    }
  }

  return listings;
}

/** The round the publication scenario plays: the first round of a seeded club's division. */
async function nextMatchday(account) {
  if (!account?.divisionId) {
    return null;
  }

  const fixtures = await json('GET', `${API}/divisions/${account.divisionId}/fixtures`, undefined, account.token);

  return fixtures.body?.matchdays?.[0]?.id ?? null;
}

/** Polls the mail catcher for the verification link sent to an address. */
async function waitForVerification(email) {
  const deadline = Date.now() + VERIFICATION_TIMEOUT_MS;

  while (Date.now() < deadline) {
    const inbox = await json('GET', `${MAILPIT}/api/v1/messages?limit=500`);

    const message = (inbox.body?.messages ?? []).find((candidate) =>
      (candidate.To ?? []).some((recipient) => recipient.Address === email));

    if (message) {
      const full = await json('GET', `${MAILPIT}/api/v1/message/${message.ID}`);
      const body = full.body?.HTML || full.body?.Text || '';
      const match = /verify-email\?userId=([0-9a-fA-F-]+)&(?:amp;)?token=([^"'&<\s]+)/.exec(body);

      if (match) {
        return { userId: match[1], token: decodeURIComponent(match[2]) };
      }
    }

    await sleep(500);
  }

  return null;
}

/** Runs a list of items through an async worker with bounded concurrency. */
async function pool(items, limit, worker) {
  const results = new Array(items.length);
  let next = 0;

  async function drain() {
    while (next < items.length) {
      const index = next++;
      results[index] = await worker(items[index], index);
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, drain));

  return results;
}

/** Issues a JSON request and returns the parsed body with its status. */
async function json(method, url, body, token, headers = {}) {
  const response = await fetch(url, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const text = await response.text();
  let parsed = null;

  try {
    parsed = text.length > 0 ? JSON.parse(text) : null;
  } catch {
    parsed = null;
  }

  return { status: response.status, body: parsed };
}

function run(command, args) {
  execFileSync(command, args, { cwd: repositoryRoot, stdio: 'inherit' });
}

function sleep(milliseconds) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}
