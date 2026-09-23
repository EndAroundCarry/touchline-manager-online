# ADR-0003: PostgreSQL-backed durable job queue

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (implemented in Stage 1 minimal, Stage 6 full)
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0008](0008-deployment-topology.md)

## Context

Almost every rule in the game is a deadline: team sheets lock 30 minutes before kickoff,
matchdays resolve at kickoff, auctions resolve in fixed windows, training progresses daily,
wages run weekly, tenures expire after 14 days of inactivity, and a season rolls over on a
fixed boundary.

These deadlines decide results and money. A missed job silently corrupts a division; a
duplicated job double-charges a club. In-memory timers inside an API process cannot satisfy
this: a deploy, a crash, or a scale-down loses or repeats work with no record.

The job queue must also participate in database transactions — a club claim must be able to
insert the tenure **and** the follow-up capacity evaluation atomically, or a crash between
the two creates a permanently stuck country.

## Decision

Implement `PostgresJobQueue` in `TouchlineManager.Infrastructure/Jobs` on top of `ops.jobs`.

**Enqueue**

- Jobs are inserted in the same transaction as the domain state that requires them.
- `unique (job_type, business_key)` makes enqueue idempotent: re-running a use case cannot
  queue the same business action twice.
- The business key is derived from domain identity (`fixture:{id}:lock`, `listing:{id}:resolve`,
  `country:{id}:provision:tier:{n}`), never from a timestamp or a random value.

**Claim**

- Workers claim ready rows with `SELECT ... FOR UPDATE SKIP LOCKED`, ordered by priority then
  due time, and immediately set `lease_owner` + `lease_until`, then commit quickly.
- The claim transaction never performs the work. Long work in the claim transaction would
  hold row locks and serialize the queue.
- Expired leases become claimable again, so a crashed worker's work is retried rather than
  lost.

**Execute**

- Handlers run outside the claim transaction against application use cases.
- On success the job is marked completed and the lease cleared.
- On transient failure the job is rescheduled with exponential backoff plus jitter.
- On permanent domain failure the job moves to a dead-letter state, emits an operations
  alert, and surfaces in the admin queue views.
- Every handler is idempotent on its business key **and** protected by destination-table
  uniqueness (for example `unique (fixture_id)` on `match.matches`). Idempotency is verified
  by tests, not assumed.

**Locking beyond row leases**

- PostgreSQL advisory locks are reserved for genuine singletons: one country's provisioning,
  one division-matchday publication, one season rollover per world.
- Normal fan-out (nine fixtures per matchday) scales through row leases and bounded
  concurrency, not advisory locks.

**Operations**

- Queue depth, oldest due age, retry count, dead-letter count, lease expiries, and handler
  duration are exported as metrics.
- Admins can inspect, dry-run, retry, and cancel jobs with a recorded reason.

**Explicitly not used for business deadlines**

In-memory timers, `IHostedService` sleep loops, and OS schedulers may exist for *polling*
the queue, but never hold the authority for a deadline. The database row is the deadline.

## Consequences

**Positive**

- Deadlines survive deploys, crashes, and autoscaling because they are durable rows.
- Enqueue is transactional with domain state, eliminating the "committed the tenure but lost
  the follow-up" class of bug.
- At-least-once delivery plus idempotent handlers yields effectively-once outcomes.
- One dependency fewer: the queue lifecycle is covered by the same backup/PITR story as the
  game data.

**Negative**

- Every handler must be written idempotently and covered by a retry test; this is ongoing
  discipline, not a one-time cost.
- Polling adds load to the primary database. Mitigation: indexed ready-job query, bounded
  workers, adaptive poll interval, and `LISTEN/NOTIFY` only if measurement justifies it.
- Long-running handlers can starve the queue if concurrency is unbounded. Mitigation:
  per-job-type concurrency caps and lease expiry.
- Rollover requires a state machine with checkpoints rather than one big job; a failed rollover
  must resume, not restart. This is modelled explicitly in Stage 12.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `IHostedService` timers / in-process scheduler | Lost on restart or scale-down; no record of a missed deadline; cannot be transactional with domain state. |
| Quartz.NET with the ADO.NET job store | Viable, but the enqueue path is not naturally part of the same transaction as our domain aggregates, and misfire semantics are tuned for wall-clock recurrence rather than "resolve this specific fixture exactly once". |
| Hangfire | Similar transactional-enqueue limitation; storage schema becomes a second source of truth we do not control. |
| Cloud queue (SQS/Azure Storage Queue) | Cannot enqueue inside the domain transaction, so every producer needs its own outbox — i.e. we would build `ops.outbox_messages` and `ops.jobs` anyway, plus a bill. |
| `pg_cron` | Good for coarse recurring maintenance, insufficient for per-entity business deadlines and leases. |
