# ADR-0012: The daily progression is a materialised, feature-gated world job

- **Status:** Accepted
- **Date:** 2026-09-25
- **Stage:** 4
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), master plan §3.9, §7.2, §8.5, game rules `TRN-3`, `TRN-9`, `TRN-10`, `TRN-12`

## Context

Stage 4's last milestone is training: a manager sets a club focus, an intensity, and optionally one
player's individual focus, and a deterministic daily job advances every player (`TRN-3`, `TRN-9`). Four
decisions in that work are hard to change once a world is running.

**1. Where the deadline lives.** ADR-0003 makes the database row, never a timer, the authority for a
deadline, and the worker only polls the queue — it never creates rows. A recurring job therefore has to be
*materialised* by something. The plan names `EnsureScheduleJobs` as the materializer for future lock and
matchday jobs (master plan §7.2), but that job does not exist and its real subject — fixtures — arrives in
Stage 6.

**2. How the progression is switched off.** Master plan §17.12 requires feature-incomplete behaviour to be
inaccessible in production, and §6.9 puts feature flags in `ops.feature_flags`. That table does not exist
yet; the only gating mechanism in the codebase is configuration bound to options, as `DiagnosticsOptions`
demonstrates.

**3. What "deterministic" means for a day.** `TRN-9` requires development to be reproducible from the
player, the day, the plan, the age curve, the hidden potential, the facilities baseline, and the engine
version, and `TRN-10` requires a partial day to carry forward. The facilities baseline is not modelled.

**4. Training injuries.** `TRN-12` says training and match injuries create explicit unavailability records,
and `PlayerUnavailability`'s own documentation says the band-to-fixture mapping is a Stage 8 discipline
rule.

## Decision

**1. A worker hosted service materialises the daily row; the handler never schedules its successor.**

`DailyProgressionScheduler` runs only in the worker (registered by `AddJobQueueWorker`, like the poller),
and its whole job is to ensure the business-keyed row exists for the most recent 02:00 UTC boundary and the
next one. The row's `due_at` is the deadline, so a worker that is down at the boundary runs the day late
rather than skipping it, and a restart re-derives the days from the clock instead of remembering state.
The handler does not enqueue tomorrow, because that would put scheduling authority back inside the
execution path. When Stage 6 lands the fixture calendar, the same service materialises its jobs; it is the
`EnsureScheduleJobs` of §7.2 in its first, progression-only form.

**2. The progression is gated by configuration, and the configuration is a documented placeholder.**

`Training:EnableDailyProgression` is bound in `AddInfrastructure` and defaults to off, so a deployment that
has not opted in never advances a player. This is deliberately the `DiagnosticsOptions` pattern rather than
`ops.feature_flags`, because the flag table does not exist; when it does, this is where its value is read.
The player-facing training endpoints are not gated: they are complete, and gating a finished surface would
be the wrong use of the switch.

**3. Development is a pure world-scoped function; the facilities baseline is one for now.**

`DailyProgression` is a pure function of the player, the day, the plan, the age curve, and the hidden
potential, seeded from the player identity and the day and versioned (`training-v1`). It reads no clock, no
database, and no global random source, so a replay is reproducible. The facilities baseline enters as a
constant because facilities are a post-MVP feature (§2.3); it becomes an input when there is a facility to
read.

**4. Training injuries are deferred to Stage 8.**

Stage 4 defines no injury band-to-fixture mapping, and `PlayerUnavailability` says that mapping is the
discipline stage's. A progression run that invented one would be a second, competing definition of injury
severity. The calculator therefore produces development and recovery only, and the injury path lands with
the rule that measures it.

## Consequences

**Positive**

- The queue stays the only deadline authority (ADR-0003): the materializer writes rows and nothing else
  executes outside a claim.
- The run is idempotent for a day — the `LastProgressionDate` guard and the date-derived business key mean
  at-least-once delivery cannot develop a player twice — which is what makes a retry safe.
- Human and AI clubs progress through one code path, so `INS-12`'s "same rules, no bypass" holds for AI
  without the AI existing yet.
- A pure calculator is testable without a clock or a database, and its golden determinism can be pinned.

**Negative**

- A day whose boundary passed while the worker was down is progressed late, not skipped, so a player's
  `LastProgressionDate` can lag the calendar. That is the intended trade — a lost day of training would be
  invisible and unrecoverable.
- The scheduler polls rather than sleeping until the boundary, so it issues a cheap idempotent insert every
  few minutes. Bounded and harmless, and simpler than a timer that would hold deadline authority.
- Configuration gating is coarser than a database flag: changing it needs a restart, and it cannot be
  turned on for one world while another stays off. Acceptable while there is one world.
- With training injuries deferred, a player can train intensely forever without ever getting hurt, which is
  a visible gap until Stage 8.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| The handler enqueues tomorrow's job | Puts scheduling authority in the execution path and makes a retried job double-schedule; a materialiser keeps the row as the deadline (ADR-0003). |
| A `System.Threading.Timer` inside the worker | ADR-0003 forbids a timer holding a deadline: it does not survive a restart and is not observable as a row. |
| Build `ops.feature_flags` now | Stage 4's scope is training; the flag store is a Stage 14 deliverable and building it here would be a stage's work inside a milestone. |
| Publish the run as a manager-triggered command | The plan forbids exposing simulation as a client command (§4.2, §12.3); training is a deadline, not a button. |
| Invent an injury mapping in the calculator | Would duplicate a Stage 8 rule and pin a fixed severity band before discipline is designed; `TRN-12` keeps the deferral honest. |
