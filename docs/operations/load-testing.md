# Load testing

The k6 suite that measures the game against its service-level objectives (master plan §14, §16 Stage 14).
It runs locally and by hand; nothing is wired into CI yet ([ADR-0046](../architecture/adr/0046-load-supply-chain-and-restore-drills.md)).
The scripts live in [`tests/load/`](../../tests/load/README.md).

## The population model

One world of six countries with eighteen clubs each is **108 clubs**, so one human manager per club is
**108 concurrent users**. The stage requires at least three times headroom, which the suite models as
**324 virtual users**. Both numbers live in `tests/load/config.js`; every scenario scales from them.

## The objectives

The suite asserts the plan's starting SLOs (§14) and the threat model's worst case:

| Objective | Target | Where it is asserted |
|---|---|---|
| Read API latency | p95 < 500 ms | `dashboard-reads`, `matchday-polling` |
| Command API latency | p95 < 800 ms | `login-burst`, `auction-contention` |
| Availability (per scenario) | fewer than 0.1% failed requests | every scenario |
| Matchday publication | published inside the window | `matchday-publication` |

A losing bid is a `409`, which `auction-contention` treats as an expected answer rather than a failure,
so `http_req_failed` keeps its meaning: a `5xx` is what fails.

## Running it

```bash
# 1. Raise the rate limits — a 324-client login burst and a 108-account seed would otherwise be
#    throttled, and the run would measure the limiter instead of the system.
#    Windows (cmd):  set RateLimiting__AuthPermitLimit=100000   (and the market limits)
#    macOS / Linux:  RateLimiting__AuthPermitLimit=100000 npm run dev
npm run dev

# 2. Prepare the world for load, driving the real API.
npm run load:seed

# 3. Run a scenario, or all of them.
npm run load:reads
npm run load:all
```

`--smoke` shrinks any scenario to a few virtual users over five seconds, which checks the plumbing
rather than the objectives: `npm run load:reads -- --smoke`. `LOAD_WINDOW=5m` extends a run.

## Reading the result

k6 prints a summary that ends with `✓`/`✗` per threshold; a crossed threshold exits non-zero. Read the
`http_req_duration{kind:read}` / `{kind:command}` lines against the objectives above, not the raw
`http_req_duration` average, which mixes both.

The suite produces **evidence**, not tuning. When a run misses an objective, the follow-up is an
index, a pool size, or a concurrency limit changed on the strength of that evidence — a separate change
with its own before/after measurement.

## The matchday publication scenario

`matchday-publication` needs a round that is actually due. On a real-time stack the next kickoff is days
away and the scheduler has already materialised the round's jobs, so the diagnostics trigger is a no-op
and the scenario fails fast with that explanation. Run it against the compressed-clock matchday stack
([ADR-0015](../architecture/adr/0015-compressed-test-clock.md)), where the deadline arrives within the
run, with `Diagnostics__EnableMatchdayTrigger=true` and the worker running. `LOAD_FORCE=1` runs it on a
real-time stack anyway. In staging, the real five-minute publication SLO is measured from the SLO
dashboards rather than from this drill.

## After an auction run

Contention staying inside its thresholds does not prove the money is right. Confirm the invariant after
a run: the listing has exactly one completed transfer in `GET /api/v1/transfers/history`, and the club's
ledger reconciles — the ledger replay in
[`backup-and-restore.md`](backup-and-restore.md) is the same check.
