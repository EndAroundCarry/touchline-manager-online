# ADR-0049: A non-production stepped game clock, advanced by an operator

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 15
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0009](0009-time-identity-and-concurrency.md),
  [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0015](0015-compressed-test-clock.md),
  [ADR-0016](0016-non-production-matchday-trigger.md), master plan §16 Stage 15, §17.12, game rules
  `TIME-6`, `TIME-7`
- **Supersedes:** [ADR-0015](0015-compressed-test-clock.md) decision 4 ("There is no HTTP control surface")

## Context

Stage 15's exit criteria begin with "at least one complete closed-beta season and rollover succeeds under
human activity". Reaching that means playing a season — thirty-four matchdays over about eleven weeks — and
watching it, which is exactly what the compressed clock (ADR-0015) exists for. But the compressed clock is a
poor fit for a *person* testing a season:

- It cannot be stepped. It is an affine map of real time, chosen at composition and only changed by editing
  configuration and restarting a host. A tester cannot hold a moment still to inspect it, nor take one day,
  or one round, at a time.
- It runs continuously. At a season-compressing rate a round publishes whether or not anyone was ready to
  look, the way an automated test needs and a human does not.
- ADR-0015 decision 4 explicitly rejected an HTTP control surface, on the grounds that a runtime-writable
  "now" is a deadline-critical write §10 does not define and §17.12 keeps unreachable features out of the API.

What Stage 15 needs is the opposite of automation: a control a tester presses, which moves the world on by
one game day, or to the next kickoff, and lets everything downstream happen through the real worker.

Three facts shape the answer. The whole game reads time through one seam (`IClock`, `TIME-2`), so a clock
that can be moved on demand teaches nothing new to any scheduler, job, or use case. The API and the worker
are separate processes, so a mutable "now" has to live somewhere both can read — the database. And a
matchday's jobs already exist as durable rows with calendar-derived deadlines (`ADR-0003`, `ADR-0014`), so
moving "now" past a deadline is enough to make the worker run it, with no new workflow.

## Decision

**1. A stepped clock holds one stored instant, frozen between steps.**

`ClockMode.Stepped` is a third mode beside real time and compression. Its clock reads a single persisted
instant from `ops.game_clock` rather than computing one from real time, so game time does not move at all
until it is advanced. That is the property a compressed clock cannot offer: a stepped world is still between
steps, and the same step from the same instant produces the same deadlines every time.

**2. The instant is stored, because two processes must agree on it.**

`IGameClockStore` reads and writes the one `ops.game_clock` row; `SteppedClock` returns the last value this
process observed, and a one-second poller keeps each host's copy fresh. One second of staleness is bounded
and deliberate: it is how a host notices a step without reading the row on every `UtcNow` call, and it affects
only the toolbar's date and request-time reads, never the worker's own claim decision — the advance job writes
the row in the same process that claims the jobs it materialises.

**3. An advance is a real worker job, not a change the endpoint makes.**

`POST /api/v1/ops/diagnostics/advance-game-clock` resolves the target — the start of the next game day, or the
next unpublished round's kickoff — and enqueues `ops.advance-game-clock` due now, under a business key derived
from the target. The worker's handler writes the stored instant and then asks every domain materialiser
(`IJobMaterializer`, the same materialisation each scheduler already runs on its interval) for the deadline
rows that instant makes due. Those rows are ordinary jobs the queue then runs. The endpoint does exactly what
an operator's decision does and nothing else; the worker remains the only component that advances a game
(ADR-0016's pattern, applied to time).

**4. Under a frozen clock, a transient failure is retried at once.**

A retry backoff is measured from "now", so a job rescheduled thirty seconds ahead would never become due while
the clock is frozen. The queue therefore reschedules a transient failure at the current instant when the clock
is stepped, and the attempt budget — not the delay — is what bounds a failing job. Without this an ordinary
transient failure (a serializable publication losing a race, say) would stall a stepped world until the next
press.

**5. It has no production surface, and a Production host refuses it.**

`Clock:Mode=Stepped` is refused in Production exactly as compression is (`TIME-6`), and the endpoints are
mapped only when `Diagnostics:EnableGameClockControl` is on *and* the clock is stepped, so they do not exist
on a real-time or compressed host. The toolbar hides itself when the status read answers `404`, so a manager
on a live world sees no test control rather than a broken one.

**6. The compressed clock remains.**

This supersedes only ADR-0015 decision 4, which forbade a runtime HTTP control over time. The compressed clock
is still the right tool for an automated or unattended acceleration; the stepped clock is the tool for a human
who wants to press a button and watch.

## Consequences

**Positive**

- A tester plays a season in minutes, one press at a time, through the real queue, handlers, engine, and
  publication — nothing is stubbed or fabricated, and every result is what the calendar would have produced.
- The safety property is structural, not procedural: a stepped clock is opt-in, off by default, refused in
  Production, and its endpoints are unmapped unless both the flag is on and the clock is stepped.
- Determinism is preserved and strengthened. A compressed world advances while nobody looks; a stepped world
  holds still, so the same instant yields the same materialised deadlines however long the world sits there.
- Nothing downstream knows the clock exists. The materialisers are the schedulers' own code, extracted behind
  one interface, so the stepped and calendar paths cannot drift.

**Negative**

- **A frozen clock reaches no future deadline.** Leases do not expire, prompt deadlines do not pass, and a
  retry scheduled ahead would not run, so the queue retries transient failures immediately (decision 4) and a
  lease left by a crashed worker is only reclaimed at the next step. Both are acceptable for a world an
  operator is stepping by hand, and both are recorded here rather than hidden.
- **The clock is another stored row the API and the worker both read.** It is one row, fixed by identity and
  constrained to it, and it exists only while a stepped clock does, so it adds no path to the live game.
- **A step is not instantaneous to observe.** The endpoint returns as soon as the job is enqueued; the day is
  only finished once its jobs have run. The toolbar waits for the clock to reach the target and then a moment
  for the day to settle before refreshing, which is a small amount of client timing rather than a server
  guarantee.
- **Two stepping requests are idempotent per target, not per press.** Pressing twice for the same target
  enqueues once; the second press is a no-op rather than a second day.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep only the compressed clock (ADR-0015) | It cannot be stepped or stopped, and it needs a restart to change; a human testing a season a press at a time cannot use it. |
| An in-memory clock an operator sets over HTTP | The API and the worker are separate processes; a per-process "now" would let them disagree about whether a deadline has passed, which is the exact failure ADR-0015 anchored away. |
| A `tools/` CLI that plays a season | It would need the whole engine, pipeline, and database applied inline, and it would not exercise the API, the worker, or the browser a beta tester actually uses; the staged-run tests already cover the unattended case. |
| A client-supplied target instant the server trusts | A client must not influence a result (`MAT-2`); the server resolves the target from its own stored instant and the calendar, and the client only chooses "day" or "matchday". |
| Mutable time behind the `ops.feature_flags` store | It is a deployment-shape decision, not a product toggle — ADR-0015's own reasoning — and configuration plus the diagnostics gate is where every other non-production surface lives. |
