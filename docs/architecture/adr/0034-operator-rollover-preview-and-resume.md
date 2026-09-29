# ADR-0034: Operator preview and resume for the season rollover

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 12
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0016](0016-non-production-matchday-trigger.md), [ADR-0020](0020-projection-rebuild-and-reconciliation.md), [ADR-0031](0031-season-rollover-state-machine.md), [ADR-0032](0032-rollover-continuity.md), game rules `PR-4`, `TBL-13`, master plan §7.5, §16 Stage 12, §17.12

## Context

ADR-0031 shipped the rollover as one world-scoped resumable job advancing a checkpoint row, and it decided
two things this ADR has to live beside. First, the rollover "closes as a consequence of play, never of a
client command, and there is no HTTP surface for it" — it is registered only by the worker and gated by
`Rollover:EnableRollover`. Second, a failure is classified, not retried: drift in a projection marks the
rollover `Failed` and dead-letters its job, because "an operator decision is needed before a failed rollover
runs again". `SeasonRollover.Retry` exists for exactly that decision and has had no caller.

Master plan §7.5 step 1 makes preflight an explicit step — "all matchdays published, projections reconcile" —
and §16 Stage 12 asks for "operator preview/dry-run and resume controls". The pieces were missing: there was
no way to see what a rollover would do before it ran, no way to start one off its calendar in a staging
environment, and no way to resume a failed one — the queue cannot re-enqueue a dead-lettered job and the port
had no operation that could.

## Decision

**1. A read-only preview, computed from the rollover's own plan.** `PreviewSeasonRollover` loads the closing
season's shape through the same `SeasonRolloverPlanLoader` the machine uses, computes movement with the same
pure rule (`PromotionRelegation`, `promotion-relegation-v1`), and reconciles each division with the same dry
run (`RebuildDivisionProjections(apply: false)`, ADR-0020). It reports unpublished matchdays, per-division
reconciliation, the promotion/relegation plan and totals, and the position awards the finalize phase would pay
(`WorldRuleSet.PositionAwardMinorFor`). It **writes nothing** and takes no transaction or lock, so a dry run
can never alter what it describes. The plan loader is extracted from `RunSeasonRollover` rather than copied,
for the ADR-0031 decision-5 reason: two readers would drift.

**2. A run-now trigger that only enqueues the real job.** `TriggerSeasonRollover` enqueues
`competition.season-rollover` under the scheduler's own business key `season:{id}:rollover` and payload, due
now — exactly what `SeasonRolloverScheduler` does when the deadline passes. The worker claims it and runs the
resumable machine, preflight included; nothing about a season closing moves into the API. This is ADR-0016's
pattern for the matchday, applied to the rollover.

**3. A resume control that retries the checkpoint and requeues the job.** `ResumeSeasonRollover` is the
operator decision ADR-0031 anticipated. It requires only a `Failed` rollover, returns the row to `Started`
(every phase is idempotent, so a resume re-does nothing), records an audit entry with the operator's reason,
and puts the job back on the queue. Because a dead-lettered row cannot be re-enqueued by
`IJobQueue.EnqueueAsync` (the business key already exists), the queue port gains
`RequeueAsync(jobType, businessKey)`: it resets a `dead_letter` row to `pending` with a fresh attempt budget
and returns whether it acted, and a resume whose dead-lettered row is gone falls back to a fresh enqueue under
the same key.

**4. Non-production and feature-gated.** All three are mapped under `/api/v1/ops/diagnostics/` only when
`Diagnostics:EnableRolloverTrigger` is set, off by default and never set outside a test environment, like
every other walking-skeleton and staging surface (§17.12, ADR-0016). This keeps ADR-0031 §7's guarantee — no
HTTP surface closes a season — while giving staging the controls §16 asks for. The production admin surface,
its MFA, and its runbooks are Stage 14's.

**5. The resume is an operator action, and it is audited as one.** The audit entry
(`world.season_rollover.resumed`) carries `AuditActorTypes.User`, the acting account, the rollover target, and
a required reason capped at the audit column's 200 characters — the same reason-required discipline already
enforced on `SeasonRollover.Fail`.

## Consequences

**Positive**

- An operator can see exactly what a rollover would move and pay, and whether it would pass preflight, before
  it runs — and can prove the preview is truthful because it and the machine share one plan loader.
- A failed rollover is no longer a dead end: the operator's decision is a first-class, audited control rather
  than a manual database edit, and the resumed run is the same idempotent machine, so it moves nothing twice.
- Staging can close a season on demand, which is the prerequisite for the multi-season staging run.
- `IJobQueue.RequeueAsync` is a general operator primitive (Stage 14 needs it for the admin UI), and it only
  ever touches a dead-lettered row, so it cannot disturb work the queue already owns.

**Negative**

- Three more HTTP endpoints exist, even if gated off by default and reachable only in non-production. The
  precedent is ADR-0016's accepted cost; the gate is configuration, not convention.
- The preview reads a whole season's standings, entries, and projection reconciliations, so on a large world
  it is not free. It is a read and holds no lock, and it is a manual operator action, not a scheduled one.
- `RequeueAsync` widens the queue port by one operation, and the resume's fallback enqueue means a resumed
  rollover can run even when its original job row has been cleaned up.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Operator-gated production endpoints now (`AuthorizationPolicies.Operator`) | ADR-0031 §7 forbids an HTTP surface that closes a season, and the operator surface with MFA and runbooks is Stage 14's; a non-production trigger is the smaller, consistent step (§17.12). |
| Run the rollover synchronously in the API process | It would produce movement outside the worker and outside the durable queue, which is exactly what ADR-0001/ADR-0008 reserve to the worker. The trigger only enqueues. |
| Re-enqueue a failed rollover under a new business key | Throwaway keys accumulate, and a second key for the same season invites two jobs racing; the world lock would serialise them, but the queue would hold duplicates of one business action. `RequeueAsync` resets the one row instead. |
| A second plan/preflight reader for the preview | It would drift from the machine it previews; extracting `SeasonRolloverPlanLoader` keeps one reader (ADR-0031 decision 5). |
| Make the preview project squad continuity too | Retirements, renewals, and replacements are decided by write-only policies; projecting them needs a read-only path the milestone does not yet have, and preflight plus movement plus awards is what §7.5's first step and the operator actually need. |
| Let the preview take the world lock | It writes nothing, so a lock would only serialise a read against the rollover for no benefit and would make a diagnostic able to block a live rollover (ADR-0020's dry-run rule). |
