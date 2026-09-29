# ADR-0035: A five-season staging run proves rollover continuity

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 12
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0015](0015-compressed-test-clock.md), [ADR-0016](0016-non-production-matchday-trigger.md), [ADR-0020](0020-projection-rebuild-and-reconciliation.md), [ADR-0025](0025-ai-transfer-market.md), [ADR-0031](0031-season-rollover-state-machine.md), [ADR-0032](0032-rollover-continuity.md), [ADR-0034](0034-operator-rollover-preview-and-resume.md), master plan §7.5, §15.5, §16 Stage 12, game rules `PR-1`–`PR-6`

## Context

Stage 12 built the rollover as one world-scoped, resumable job with a checkpoint row ([ADR-0031]), then
gave it continuity — contract expiry, retirement, awards, and season finance summaries ([ADR-0032]) — and
operator controls — a preview, a run-now trigger, and an audited resume ([ADR-0034]). Its exit criteria
also ask for something the earlier milestones could only assert one season at a time:

> Run multiple compressed seasons in staging with human/AI mixes and dynamic tiers. … At least five
> consecutive automated staging seasons reconcile cleanly.

A one-season rollover test proves the machine once. It does not prove that a world survives the *second*
season — that the next season is a complete, playable season in its own right, that the pyramid's tiers
keep exchanging clubs, that money, squads, and history stay sound as they accumulate, and that nothing
double-applies when the exercise repeats. That is a property of repetition, so it needs a run that repeats.

Nothing in the repository drove more than one season. There was also a defect the multi-season shape
exposed: `AiMarketScheduler` was written to materialise the AI market's daily job (`TRF-12`,
[ADR-0025]) but was never registered in `AddJobQueueWorker`, so `market.evaluate-ai` could never be
produced and the AI never traded — a silent gap that a world where AI clubs are supposed to act across
many seasons cannot ship with.

## Decision

**1. The run is a worker integration test, not a surface.** Stage 12's exit criterion is an automated
assertion, and the plan already routes the multi-season runs to "staging". There is no staging deployment
yet (Stage 14/16), and no product need for an endpoint that plays seasons. So the run is a test over the
real worker's own composition and a real PostgreSQL 17 container, in
`tests/TouchlineManager.Worker.IntegrationTests`. It is the CI gate the criterion names, and it adds no
HTTP route, CLI, or configuration an operator must reason about.

**2. The run drives the real durable pipeline, not the use case.** Each season's rollover is enqueued
under the scheduler's own business key (`season:{id:D}:rollover`) and executed by the worker's queue and
handler, exactly as it would be on the calendar. Nothing in the test calls `RunSeasonRollover` to do the
work; the direct call is used only to prove a redelivery is a no-op. The rollover scheduler is present, as
it is on a staging worker; the trigger makes the job due immediately rather than waiting out the
materialiser's poll, and it is the same key, so a duplicate is impossible (ADR-0003, ADR-0034).

**3. The seasons are played by deterministic fabrication.** Every fixture is marked locked, staged, and
published with a deterministic score and the division's projections are rebuilt with the same tool the
live publication uses (`RebuildDivisionProjections`, [ADR-0020]), exactly as the single-season rollover
suite does. The subject of the run is continuity across seasons, not the match engine; the engine's
determinism and the matchday pipeline are pinned by their own suites. Simulation would multiply the run's
cost by roughly three orders of magnitude for no additional confidence in the property under test.

**4. The world has the shape the criterion names.** Six seeded countries (108 clubs) plus one tier
provisioned for one country through the real `ProvisionDivision`, so there is an adjacent pair for
three-up/three-down to act between and single-tier countries for the lowest-tier and top-tier no-ops. Two
human tenures are attached — one in each tier of that country — so the human/AI mix is real: the `squads`
phase renews the unmanaged clubs and lets a present manager's unrenewed players expire (`CON-6`), and the
movement message reaches only the attended clubs (`COM-1`).

**5. The run asserts the exit criteria it can prove, and reads one of them precisely.** After every
rollover it asserts the world's season pointer advanced once, the closing season completed, the next
season is active, starts on a legal matchday, and carries eighteen clubs, a thirty-four-round schedule,
three hundred and six fixtures, and an opening table in every active tier; that each closing division's
projections reconcile; that movement is exactly three up and three down; and that the season's finance
summary and awards exist. At the end it asserts six seasons, five completed rollovers, every club's squad
still legal (`SQ-2`), every account's balances replaying from its ledger (`FIN-18`), both contract
outcomes of the human/AI mix, and no dead-lettered rollover job. The "default tactic" criterion is read as
**every club with no active tenure has a side** (`INS-12`), because the seeder creates no plan and a
human-held club that never sets one is repaired by the snapshot builder at the lock (`TAC-10`); the run
records the reading rather than pretending a human club has a tactic it never set.

**6. `AiMarketScheduler` is registered, and a test pins it.** The missing hosted service was a defect: the
class, its options, its handler, and its job type all existed, but nothing placed the job. It is added to
`AddJobQueueWorker` beside the other materialisers, and a focused worker test starts the composition with
only that scheduler enabled and asserts the day's row appears. Without the registration the test fails;
with it, the AI market trades as `TRF-12` intends.

## Consequences

**Positive**

- Stage 12's last exit criterion is an executable assertion. A settlement, movement, placement, money, or
  aging regression that only bites on the second or third season now fails a test rather than a live
  world.
- The run is the real pipeline end to end — scheduler, queue, leases, handler, world lock, all six phases
  — so what it proves is the production path, not a hand-rolled shortcut.
- Deterministic fabrication keeps it a minutes-long integration test rather than an hours-long simulation,
  and the same seed makes it reproducible.
- The AI market gap is closed and cannot silently reopen.

**Negative**

- The run is a slow integration test (minutes), because it rolls over a 126-club world five times. It
  belongs to the worker integration layer, which the test strategy already allows to be slow.
- Fabricated play means the run does not exercise the matchday worker or the engine; those remain covered
  by their own suites, and a defect that only appears when a real simulated result meets a rollover would
  not be caught here.
- The "default tactic" exit criterion is only enforced for AI clubs. A human club with no saved plan is
  legal (the lock repairs it) but the run cannot assert a tactic the manager never set.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A `tools/` CLI or a worker configuration mode that plays seasons on demand | There is no staging deployment to run it against yet, and the exit criterion is an automated assertion; a runnable surface would be a feature-incomplete route (master plan §17.12) the test already satisfies. |
| A diagnostic HTTP trigger that plays a whole season | The existing triggers enqueue individual deadlines; a "play five seasons" endpoint would be a large, season-shaped write surface with no operator need, and the plan keeps simulation worker-only. |
| Simulate every match for real | Highest fidelity, but it turns a minutes-long test into one dominated by the engine, for no added confidence in the continuity property the milestone is about. |
| Let the rollover scheduler's poll drive each season | It would add up to the scheduler's check interval (seconds to tens of seconds) of dead wait per season; the trigger is the scheduler's own materialisation under the same business key, so the job and its guarantees are identical. |
| Assert `RunSeasonRollover`'s return value for each season | That would run the machine directly and stop proving the worker does it; the run reads the effects from the database instead and reserves the direct call for the redelivery no-op. |
| A new production seam to inject a failure at every checkpoint | The per-phase guard and idempotency are already pinned by the domain rollover suite and the failed-then-resumed integration case; adding a phase-failure seam to production code for a test would be a bypass the plan's §17.10 forbids. |
