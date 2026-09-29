# ADR-0031: Season rollover is one world-scoped resumable job with a checkpoint row

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 12
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0009](0009-time-identity-and-concurrency.md), [ADR-0010](0010-club-takeover-serialisation.md), [ADR-0021](0021-competition-rules-and-visible-draw.md), [ADR-0030](0030-provisioning-execution-and-bootstrap-provenance.md), game rules `PR-1`–`PR-10`, `CAL-6`, `TIME-3`, master plan §3.6, §7.5, §16 Stage 12

## Context

Master plan §7.5 describes season rollover as "a resumable state machine with checkpoints, not one opaque
transaction", and §7.2 names three jobs — `PrepareSeasonRollover`, `ExecuteCountryRollover`,
`FinalizeSeasonRollover`. ADR-0003 had already declared "one season rollover per world" a genuine singleton
for the advisory-lock taxonomy, and Stage 3 shipped the primitives — `Season.BeginRollover`/`Complete`, the
`SeasonStatus.Rollover` state, `DivisionSeason.Complete`, `ClubSeasonEntry.Close`, and
`LedgerPostings.PositionAward` — with no caller for any of them.

The decisions open before Stage 12 were: how the state machine is expressed and resumed, how it is scoped
and serialised, whether the plan's three job names are three jobs, and how movement reuses the existing
generation path rather than duplicating it.

## Decision

**1. One durable job and a checkpoint row, not three jobs.** The rollover is a single
`competition.season-rollover` job advancing a `competition.season_rollovers` row through
`Started → Frozen → Finalized → Moved → Completed` (or `Failed`). The plan's three names are the phase
groups, not three separate rows: a job that dead-letters cannot be resumed, and the checkpoint row is a
reliable place to resume from, so expressing the state machine as one job whose committed phase *is* the
checkpoint satisfies §7.5's requirement directly. Each phase commits its own work together with the phase
it reached, so a worker that dies between phases is retried by the queue and resumes at the next one rather
than replaying a season's worth of movement.

**2. The machine is world-scoped; movement is country-scoped.** The rollover row is keyed
`unique (world_id, season_id)` and the whole run holds one world-scoped advisory lock
(`AdvisoryLockKey.SeasonRollover`), because a partial rollover — some countries moved and the game year not
yet advanced — is a corrupt world. Each country's movement and next-season generation is one transaction
(`PR-5`), so a failure in one country never corrupts a country that already completed.

**3. The next season is the same clubs re-arranged, not a new tier.** The move phase creates the next
`Season` (sequence + 1, game year + 1) and, per active division, the next `DivisionSeason`, `ClubSeasonEntry`
rows for the moved clubs, a fresh 34-fixture schedule, and an opening table. Clubs, squads, contracts, and
cash are untouched (`PR-7`), and the closing season's entries, fixtures, and tables are never rewritten
(`PR-6`).

**4. Movement is a pure, versioned function.** `PromotionRelegation.Compute` (`promotion-relegation-v1`)
takes each active tier's finalized ordering and returns one movement per club, applying `PR-1` (three up /
three down between adjacent active tiers), `PR-2` (lowest active tier relegates nobody), and `PR-9`/`PR-10`
(top active tier promotes nobody; a one-tier country moves nobody) as consequences of iterating adjacent
pairs rather than as special cases. It reads no clock or database, so a finished season's movement is
reproducible from its stored standings.

**5. Generation is shared, not duplicated.** The schedule and opening-table generation is extracted from
`WorldGenerator` into `DivisionScheduleGenerator`, used by the seeder, the provisioning worker, and the
rollover — the same rationale ADR-0030 used for `BuildTier`. A per-season seed derived from the world's
identity, the country code, the season number, and the tier folds into the schedule and tie-draw seeds, so a
season's fixture list is reproducible and a resumed rollover generates the same one (`CAL-8`, `TBL-11`).

**6. Failure is classified, not retried blindly.** A season that is not yet fully played is a *transient*
fault: a normal exception leaves the job to be retried and the row untouched, and the season is not frozen.
Drift in a projection is a *defect*: the rollover is marked `Failed`, audited, and dead-lettered for an
operator, because deterministic generation and publication should reconcile, and re-running it in a loop
would turn a diagnosable fault into a hot loop.

**7. Worker-only and feature-gated.** `SeasonRolloverScheduler` and `RunSeasonRollover` are registered only
by `AddJobQueueWorker`, gated by `Rollover:EnableRollover`. A season closes as a consequence of play, never
of a client command, and there is no HTTP surface for it (`MAT-2`, §17.12).

## Consequences

**Positive**

- Interrupting a rollover at any checkpoint and resuming is safe: each phase is idempotent (a repeated
  freeze, a repeated close, a find-or-create division-season), and the committed phase is the resume point.
- The world lock makes the whole workflow a singleton, so movement and the game-year increment cannot
  interleave with a peer rollover.
- Movement is exactly reproducible from stored standings, and the historical season is immutable.
- A new season begins with the same machinery the first one did, so its calendar and table cannot drift.

**Negative**

- A rollover reads a whole season's standings, entries, clubs, and accounts across every country, so it is a
  heavy, long-running job holding the world lock. The volume is bounded by the pyramid's size and observable
  through the job metrics and the phase row.
- The workflow is multi-transaction, so a reader can observe a frozen-but-not-yet-moved world. That is
  deliberate: the season is `Rollover` (claims refused), and no read exposes a moved club until the phase
  that moved it commits.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Three jobs (`Prepare`, `ExecuteCountryRollover`, `Finalize`) | Three jobs across which a crash is not resumable without a fourth coordination record; one job plus a checkpoint row is the same state machine with one place to resume from, and the plan's §7.5 requirement is the checkpoint, not the job count. |
| One big transaction for the whole rollover | Holding one transaction across the generation of every next-season schedule would lock the world for the duration and lose all work on a late crash. |
| A second schedule generator for the next season | Two generators drift; the seeder, provisioning, and rollover now share `DivisionScheduleGenerator`, so a new season's calendar cannot differ from the first one's except by seed. |
| Country-scoped advisory locks instead of a world lock | The next season's start date, the game-year increment, and the current-season pointer are world facts; two countries rolling over independently would let the pointer move to a season one country has not entered. |
| Reuse the closing season's seed for the next season | Every season's fixture list would be identical, which is not a schedule that varies; a season-scoped seed is required, and `DivisionScheduleGenerator.SeedsFor` derives one. |
| Retry a reconciliation failure automatically | Publication and generation should reconcile deterministically, so a drift is a defect worth alerting on; a retry loop would only burn the budget. |
