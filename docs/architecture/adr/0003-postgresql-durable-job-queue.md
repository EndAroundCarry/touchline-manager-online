# ADR-0003: PostgreSQL-Backed Durable Job Queue

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §4.4, §7.1, §7.2, §6.9

## Context

Business deadlines (fixture lock −30 min, matchday kickoff, daily 02:00 UTC progression, weekly wages, auction windows, inactivity sweeps, season rollover) must survive process crashes and deploys. Jobs must be idempotent, observable, retryable, and operable without touching the database by hand. The MVP already commits to one database and two deployables.

## Decision

Implement a custom durable queue in `FootballManager.Infrastructure/Jobs` over the existing PostgreSQL database:

- Jobs live in `ops.jobs` with `unique (job_type, business_key)` giving enqueue idempotency.
- Workers claim ready rows with `FOR UPDATE SKIP LOCKED`, stamping `lease_owner`/`lease_until`, and commit the claim quickly; handlers then run outside the claim transaction and record success/failure atomically.
- Expired leases become claimable again (crash recovery).
- Transient errors retry with exponential backoff + jitter up to `max_attempts`; permanent domain errors dead-letter and raise an operations alert.
- Domain events are written to `ops.outbox_messages` in the same transaction as state changes and dispatched by `DispatchOutbox`.
- PostgreSQL **advisory locks** are reserved for singleton country/season workflows (claim capacity evaluation, rollover); ordinary jobs scale through row leases.
- Metrics: queue depth, oldest-due age, failures, retries, lease expiry, execution duration.
- Admin surface: dry-run, retry, cancel — each requiring an audited reason.
- Required job catalog is fixed by plan §7.2 (`EnsureScheduleJobs`, `LockFixtureTeamSheets`, `ResolveDivisionMatchday`, `PublishDivisionMatchday`, `ResolveTransferAuction`, `DailyPlayerProgression`, `WeeklyFinanceRun`, `EvaluateAiClubs`, `EvaluateInactivity`, `ProvisionDivision`, season rollover steps, outbox/email dispatch, projection rebuild).
- **No in-memory timers for business deadlines** (plan §4.4): timers may only trigger a scan of the durable table.

## Consequences

- One datastore to operate; jobs, domain state, and outbox commit together (no dual-write gap).
- Throughput is bounded by database capacity — acceptable for matchday-scale fan-out (nine fixtures per division), with fixture simulations run as independent jobs.
- Queue semantics (backoff, leases, dead-letter) must be implemented and tested by us; Testcontainers cover `SKIP LOCKED`, lease expiry, and idempotent re-run.

## Alternatives considered

- **Hangfire/Quartz:** rejected — adds framework coupling, still needs the same table semantics, weaker fit for strict idempotency/audit requirements.
- **RabbitMQ/SQS-style broker:** rejected — second piece of infrastructure and a dual-write problem between broker and database.
- **In-memory timers/scheduler:** rejected explicitly by plan — lost on restart, violates durability requirement for deadlines.
