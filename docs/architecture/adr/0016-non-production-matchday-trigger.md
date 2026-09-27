# ADR-0016: A non-production matchday trigger for the end-to-end watch journey

- **Status:** Accepted
- **Date:** 2026-09-26
- **Stage:** 7
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md),
  [ADR-0008](0008-deployment-topology.md), [ADR-0015](0015-compressed-test-clock.md), master plan §15.5,
  §16 Stage 7, §17.12, `MAT-2`

## Context

Stage 7's exit criteria include: *"A test manager can prepare and watch a complete scheduled fixture
end-to-end"* (master plan §16), which is the browser journey §15.5 lists as *"Worker lock/simulate test
hook → table/result/inbox update → watch commentary/highlights."*

The pieces to watch already exist — the two match reads, the `/matches/:matchId` viewer, the Canvas
renderer, the playback machine. What the suite cannot do is make a matchday *play*. Three facts stand in
the way:

- The Playwright stack starts the API and the web client but **not the worker**, and only the worker advances
  a matchday: the scheduler is worker-only by design (ADR-0001, ADR-0008) and the scheduler's deadlines are
  calendar rows, days apart.
- There is deliberately **no HTTP command that simulates a match** (`MAT-2`), and no endpoint moves time
  (ADR-0015, decision 4).
- The compressed clock (ADR-0015) is the mechanism for *a human* watching a season and for the multi-season
  rollover journey, but it is a poor fit for an automated "prepare, then wait, then watch" ordering: game
  time is `anchor + (realNow − anchor) × rate`, so the real gap before the test acts — API and dev-server
  boot, tens of seconds — is multiplied by the rate. At a season-compressing rate the round is published
  before the manager prepares, or the whole season ends mid-suite.

What is needed is a way to make *one chosen round* play *now*, deterministically, without giving the client
any influence over a result and without waiting for real time.

## Decision

**1. A configuration-gated, non-production diagnostics endpoint enqueues a round's real jobs.**

`POST /api/v1/ops/diagnostics/play-matchday` takes a matchday id and enqueues that round's **lock** and
**resolution** jobs — and nothing else — with a due time of now. It does not preview, freeze, simulate,
publish, or move time; it does exactly what the worker-only scheduler does when a deadline arrives.

**2. The worker still does all of the work.**

The uploaded jobs are claimed by the real queue and executed by the real handlers, which lock the round
from its frozen snapshots, simulate it with the real engine, and publish it atomically. The endpoint is a
*trigger*, not a shortcut: no result is produced in the API process, and `MAT-2`'s guarantee — that a client
cannot simulate a match — is untouched.

**3. It reuses the scheduler's identity, so it is idempotent and indistinguishable.**

The lock and resolution jobs carry the same business keys and payload the scheduler builds
(`MatchdayJobTypes`, `MatchdayJobPayload`), so a repeated trigger inserts nothing and a triggered round is
the same round the calendar would have produced. Publication is enqueued by the resolution itself, in the
transaction that marks the round staged; enqueuing it here would let it run before there was anything to
publish.

**4. It has no production surface.**

It is off by default in every environment through `Diagnostics:EnableMatchdayTrigger` and is mapped only when
that flag is set, so an unreachable feature cannot leak into the live game (§17.12). Like the Stage 1 job
probe, it is unauthenticated — the gate is configuration, and the surface does not exist in production at
all.

**5. The journey gets its own throwaway world.**

Because a played round permanently advances a season, the journey runs against a second Playwright stack
(`playwright.matchday.config.ts`) with its own database, reset and reseeded every run, and with the worker
started. The shared world the other journeys use is never consumed, so repeated local runs cannot exhaust it.

## Consequences

**Positive**

- Stage 7's last exit criterion becomes provable: a browser prepares a side, a real round plays through the
  real worker from a real frozen snapshot, and the replay is watched — with nothing stubbed (`MAT-2`,
  `MAT-7`, `MAT-9`).
- The trigger is one small use case and one gated endpoint. No scheduler, job, or use case knows it exists,
  and the clock and the engine are untouched.
- Isolation is structural: the main suite keeps its persistent shared world and its worker-free stack, and
  the matchday journey owns a database it discards.

**Negative**

- **It is a second HTTP surface that advances a matchday.** It is deliberately not the *only* writer — the
  worker is — but the API can now enqueue a round's deadlines, which the architecture otherwise reserves to
  the worker. The mitigation is that it is off everywhere by default, mapped only when asked, and does only
  what the scheduler does; an operator who enables it in production has opted into a matchday-advancing
  diagnostic.
- **The e2e harness grows.** A dedicated database, a second config, and a second CI step are more moving
  parts than one stack. The alternative — consuming the shared world one round per run — is the class of
  problem the club-resignation convention already exists to avoid, and was rejected for the same reason.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| The compressed clock (ADR-0015) | Multiplies the real boot delay between anchor and action by the rate, so an automated prepare-then-watch ordering is racy: the round publishes before the manager prepares, or the season ends mid-suite. It remains the right tool for a human watching a season and for the rollover journey. |
| A synchronous endpoint that runs lock/resolve/publish in the API process | Produces a result without the worker, the queue, or the handlers — which is exactly what the journey needs to exercise. The API integration tests already cover that path; the browser journey must cover the real one. |
| A public `POST /match/simulate` | Forbidden by `MAT-2`: match simulation is never a manager-triggered public command. |
| Seeding a world whose first matchday is imminent, and running the worker uncompressed | The seeder is idempotent (`WORLD-1`), so a second date needs a fresh database, and the first kickoff cannot be pinned to "a few seconds from now" — the round would still be days away or already past. |
| An operator endpoint that moves time | A runtime-writable "now" is a deadline-critical write surface §10 does not define; ADR-0015 rejected it, and it would make one world's clock disagree with every other read. |
