# ADR-0030: Provisioning execution is a worker job under the country lock, and a backfilled round is marked as generated history

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 11
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0010](0010-club-takeover-serialisation.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), game rules `PYR-2`–`PYR-8`, `PYR-11`, `PYR-14`, master plan §6.3, §16 Stage 11

## Context

ADR-0005 fixed the shape of pyramid expansion in Stage 3: a takeover creates a durable
`DivisionProvisioningRequest` inside the country advisory lock, and a worker later generates the tier. Stage 3
shipped the request state machine, the `GenerationRun` record, `Division.Provision`, and a
`CapacityEvaluator.EnsureNextTierRequestedAsync` documented as "Stage 11 runs exactly this" — but nothing
turned a request into a tier. Stage 11 is that worker.

Four decisions were open: how generation avoids drifting from the seeder, what happens to the matchdays the
season has already played, whether the tier is claimable before it is validated, and how generation stays
reproducible when it is retried.

## Decision

**1. One generation path, shared by the seeder and the worker.** `WorldGenerator.BuildTier` is extracted from
`SeedWorld` and both callers use it, so a provisioned tier cannot drift from a seeded one (`PYR-14`). It
creates the division, its `DivisionSeason`, eighteen generated clubs with squads, accounts and opening
ledger postings, the matchdays and fixtures, and the opening table, all seeded from the
`DivisionProvisioningGenerator` helpers so both paths derive the same seeds and hashes.

**2. Provisioning is a worker job under the country advisory lock.** `ProvisionDivision` runs from
`world.provision-division`, materialised by `ProvisioningScheduler` (worker-only). It is deliberately **not one
transaction**: generation commits first, then the backfill runs the ordinary lock → resolve → publish
workflow once per passed round in round order (`PYR-6`), then validation and activation commit under the
country lock. A crash mid-backfill therefore leaves a real, inspectable tier and a resumable request rather
than nothing, and the backfill's condition, fatigue, morale, discipline, statistics, and gate receipts cascade
exactly as a live matchday's.

**3. A backfilled round is marked as generated history.** `Fixture.IsBootstrap` is set when the fixture's
kickoff is at or before the provisioning instant (`PYR-7`). It is surfaced on the fixture DTOs so a manager can
tell a generated result from one they played, and it is the one fact that distinguishes a backfilled round from
a live one.

**4. A division is claimable only after generation, validation, and backfill.** `BuildTier` is called with
`Activate: false`, and `Division.Activate` runs in the final phase only after `ValidateAsync` has checked the
tier against the invariants activation requires (`PYR-8`): eighteen clubs, eighteen legal squads (≥2
goalkeepers, `SQ-2`), 34 matchdays / 306 fixtures passing `ScheduleValidator`, eighteen standings, and a
projection rebuild that reconciles (`TBL-13`). A validation failure commits the failure, audits it, and throws
a **permanent** job failure — deterministic generation should reproduce, so a failure is a defect to alert on,
not a reason to burn the retry budget.

**5. Growth continues by the generic path.** On success, `EnsureNextTierRequestedAsync` runs under the same
country lock, so if the newly filled tier is itself full the next tier is requested the same way rather than by
a special case (`PYR-11`).

**6. Generation is reproducible on retry.** The seed is derived from the world identity, the country code, and
the target tier (not from mutable state), and `DivisionProvisioningRequest.Retry` reuses the same row and seed,
so two attempts at the same tier attempt the same world (`PYR-14`).

## Consequences

**Positive**

- The seeder and the worker cannot disagree about how a tier is built, because they are the same code.
- A crash anywhere in the workflow is recoverable: generation is idempotent by identity, the backfill's
  commands are individually idempotent, and activation is guarded by validation.
- A provisioned tier is indistinguishable from a seeded one except for the bootstrap provenance on its
  backfilled fixtures.

**Negative**

- A mid-season tier-2 provision simulates up to ~34 AI-vs-AI rounds in one job under the country lock, so the
  job is long-running and holds the country lock for its duration. It is bounded by the season length and
  observable through the job metrics.
- The workflow is multi-transaction, so a reader can observe a generated-but-not-yet-active tier. That is
  deliberate: the division stays in `Provisioning` (not claimable) until validation, so the intermediate state
  is not reachable by a manager.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A second generator for provisioned tiers | Two generators drift; `PYR-14` requires one reproducible path, so `SeedWorld`'s helpers were extracted instead. |
| One transaction for generation, backfill, and activation | The backfill simulates a season; holding one transaction across it would lock the country for the whole duration and lose all work on a late crash. |
| Generate and activate in one step, backfill afterwards | `PYR-8` makes a tier claimable only after validation; activating early would let a manager claim into an unvalidated tier. |
| Retry a validation failure automatically | Deterministic generation reproduces, so a failure is a defect; retrying would hot-loop a diagnosable fault. It dead-letters for an operator `Retry`. |
| Leave backfilled fixtures unmarked | A generated result would be indistinguishable from one the manager played, which is what `PYR-7` exists to prevent. |
