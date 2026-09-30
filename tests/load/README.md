# Load suite

k6 scenarios for the Stage 14 load deliverable (master plan §14 SLOs, §16 Stage 14). They run locally
and by hand; nothing is wired into CI yet (ADR-0046). The narrative and the triage notes live in
[`docs/operations/load-testing.md`](../../docs/operations/load-testing.md).

## The population model

The MVP world is one tier of eighteen clubs in each of six countries — **108 clubs**. One human manager
per club is **108 concurrent users**, and the stage requires three times headroom: **324 virtual users**.
The scenarios scale from `tests/load/config.js`, so changing the model moves the whole suite.

## What each scenario measures

| Script | npm command | What it puts under load | Threshold |
|---|---|---|---|
| `login-burst.js` | `npm run load:logins` | 324 clients signing in at once | login p95 < 800 ms, no 5xx |
| `dashboard-reads.js` | `npm run load:reads` | dashboard, fixtures, table and `/sync` reads | read p95 < 500 ms, no 5xx |
| `matchday-polling.js` | `npm run load:polling` | the real 60-second `/sync` cadence at 324 clients (11 req/s) | read p95 < 500 ms, no 5xx |
| `auction-contention.js` | `npm run load:auctions` | many managers bidding on one listing | bid p95 < 800 ms, no 5xx |
| `matchday-publication.js` | `npm run load:publication` | a round locking, simulating, and publishing | published inside the drill bound |

The read and command SLOs are the plan's: p95 read under 500 ms, p95 command under 800 ms, and fewer
than 0.1% failures (master plan §14).

## Running it

k6 runs in the official container, so nothing needs installing. The runner picks the network for your
platform: on Linux it uses host networking, elsewhere it points the container at `host.docker.internal`.

1. **Raise the rate limits.** A login burst and a 108-account seed both hammer the auth limiter, so the
   run must not measure it instead of the system:

   ```bash
   # Windows (cmd)
   set RateLimiting__AuthPermitLimit=100000
   set RateLimiting__MarketListingPermitLimit=100000
   set RateLimiting__MarketBidPermitLimit=100000
   npm run dev

   # macOS / Linux
   RateLimiting__AuthPermitLimit=100000 RateLimiting__MarketListingPermitLimit=100000 \
     RateLimiting__MarketBidPermitLimit=100000 npm run dev
   ```

2. **Seed the world for load.** This brings the stack up, migrates, seeds the world (unless
   `LOAD_SKIP_SETUP=1`), then drives the real API to create one verified manager per club with a claimed
   club each, plus a few listings:

   ```bash
   npm run load:seed                  # 108 managers (use LOAD_ACCOUNTS=6 for a small pool)
   ```

3. **Run a scenario**, or all of them:

   ```bash
   npm run load:reads
   npm run load:all
   ```

   Add `--` `--smoke` to any command to shrink it (a handful of VUs, a five-second window) and check the
   plumbing in seconds: `npm run load:reads -- --smoke`. `LOAD_WINDOW=5m` extends a run.

## The matchday publication scenario

`matchday-publication.js` needs a round that is actually due. On a real-time stack the next kickoff is
days away, the scheduler has already materialised its jobs, and the diagnostics trigger is a no-op — so
the scenario fails fast and says so rather than polling for nothing. Run it against the compressed-clock
matchday stack ([ADR-0015](../../docs/architecture/adr/0015-compressed-test-clock.md)), where the
deadline arrives within the run, or set `LOAD_FORCE=1` to run it anyway. It also needs
`Diagnostics__EnableMatchdayTrigger=true` on the API and the worker running.

## After an auction run

The scenario proves the system stays inside its thresholds under contention, not that the money is
right. After it, confirm the invariant: the listing has exactly one completed transfer in
`GET /api/v1/transfers/history` and the club's ledger reconciles. The checker is the same ledger replay
the restore drill runs (`docs/operations/backup-and-restore.md`).
