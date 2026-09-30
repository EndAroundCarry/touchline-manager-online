# ADR-0044: Operator recovery commands — job retry and cancel, and matchday resume

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** master plan §7, §10.8, §13, §16 Stage 14, `F-46`, ADR-0003, ADR-0014, ADR-0031,
  ADR-0034, ADR-0042, ADR-0043, threat-model `E-3`

## Context

§10.8 lists the admin routes and ends with the rule that all admin *mutations* require a role, a reason, an
idempotency key, and an audit event. ADR-0042 built the gate and the account commands; ADR-0043 built the
diagnostic reads. ADR-0043 §8 deferred the remaining mutations, among them the three that complete the
Stage 14 exit criterion — "an on-call operator can **diagnose and resume** failed matchday, auction,
provisioning, and rollover workflows" — of which only the diagnosis half existed.

What exists to build on: `IJobQueue.RequeueAsync(jobType, businessKey)` resets a dead-lettered row with a
fresh attempt budget, and ADR-0034 already recorded that it is "a general operator primitive (Stage 14
needs it for the admin UI)". `ResumeSeasonRollover` is the resume template — validate a reason, guard the
terminal state, requeue with a fallback enqueue under the same business key, audit, commit. The read console
exposes a dead letter and a stuck round; what it lacks is the action.

Two gaps the reads exposed:

- **No way to act on a dead letter.** `ops.jobs` has no operator mutation at all: a dead-lettered job stays
  dead, and the only requeue path is the rollover resume.
- **No way to stop a job.** `JobStatuses` has four values and no terminal `cancelled`; §7 requires
  "dry-run/retry/cancel operations with audit reasons".

## Decision

**1. Three commands, all on the existing `AdminMutate` gate.** `POST /api/v1/admin/jobs/{id}/retry`,
`POST /api/v1/admin/jobs/{id}/cancel`, and `POST /api/v1/admin/matchdays/{id}/resume`. Each requires the
`AdminMutate` policy (operator or admin, support excluded — `E-3`), a fresh `X-MFA-Code` through
`MfaStepUpFilter`, an explicit `reason`, and an `Idempotency-Key` header, and each records an audit entry in
the same unit of work as the change (§10.8).

**2. `cancelled` is a fifth, terminal job status — and its own migration.** `JobStatus.Cancelled` and
`JobStatuses.CancelledCode = "cancelled"` join the four existing codes, the `ck_jobs_status` check
constraint is widened, and `Stage14JobCancellation` drops and re-adds it. Nothing else about `ops.jobs`
changes: no column and no index. The queue's `CancelSql` moves a `pending`, `leased`, or `dead_letter` row
to `cancelled` and clears the lease; a `completed` or already-`cancelled` row is left alone. Cancelling a
`leased` job is safe because every worker terminal statement (`CompleteSql`, `DeadLetterSql`,
`RescheduleSql`) is guarded by `status = 'leased'`, so a cancelled job can never also complete.

**3. The queue port owns its table, so the read and the cancel live there.** `IJobQueue` gains
`FindByIdAsync` (a small `JobSnapshot`) and `CancelAsync`. A retry is `FindByIdAsync` → confirm
`dead_letter` → the existing `RequeueAsync(jobType, businessKey)`. This keeps `IAdminQueries` a pure read
projection over several tables (ADR-0043) and puts `ops.jobs` access in one port.

**4. Matchday resume reuses the rollover shape and adds no domain transition.** A round is stuck when its
governing job dead-lettered: resolution while the round is `pending`, publication while it is `staged`. The
command requeues that job under the round's deterministic key and, when no dead-lettered row exists,
enqueues a fresh one under the same key. No `Retry()` is added to `Fixture` or `Matchday`, because
resolution already re-runs the fixtures that never staged — a fixture left `simulating` is one of them, and
`MatchSnapshotFactory.FreezeAsync` returns the stored snapshot untouched rather than rebuilding it
(ADR-0014). A `published` round cannot be resumed. When the queue already holds a healthy `pending` or
`leased` row, nothing is requeued and the round is reported **not resumable** (409): resume means "recover a
failed workflow", never "force a healthy round forward" — that is the diagnostics `play-matchday` probe,
off in production.

**5. Retry is generic; a workflow that needs a domain checkpoint reset has its own resume.** The generic job
retry returns *any* dead-lettered job to the queue, because §10.8 specifies one route and a per-type
"retryable" registry is not worth its weight. A job whose handler refuses to run until a domain row is
reset — today only the season rollover — will simply dead-letter again, so the runbook points it at
`ResumeSeasonRollover` (ADR-0034) instead.

**6. Replay is a 409, matching the existing admin contract.** The `Idempotency-Key` is presence- and
length-checked only, exactly as suspend and restore do it. A replayed retry or cancel finds the row no
longer in a state the action applies to and returns the same `409` the first successful action's follow-up
would (`JOB_NOT_RETRYABLE`, `JOB_NOT_CANCELLABLE`, `MATCHDAY_NOT_RESUMABLE`). No dedup store is introduced.

**7. Status codes.** Retry and matchday resume return `202 Accepted` (they re-queue asynchronous work,
matching `ResumeSeasonRollover`); cancel returns `200 OK` (an immediate terminal transition, with the
resulting status in the body).

## Consequences

**Positive**

- The stage's exit criterion now has its action half for the matchday and job workflows: the queue, a stuck
  round with its failed attempts, and the commands that put the work back or stop it — each audited with
  who, what, and why.
- The recovery primitives are the same ones the rollover resume already proved, so a later resume command
  (provisioning, auction) is a use case and a route, not new queue machinery.
- Matchday resume adds no schema and no domain transition; only the job-cancel status touches storage, and
  its migration is a single check-constraint swap.

**Negative**

- Cancelling a `leased` job stops it from completing but cannot roll back work its handler already
  committed, because the queue is at-least-once and each step commits on its own. This is inherent to the
  workflow design (ADR-0014, ADR-0031) and is why recovery, not cancellation, is the tool for a partially
  applied workflow; the runbook says so.
- The generic retry can be pointed at a job that immediately dead-letters again when its workflow needs a
  domain reset. The mitigation is documentation, not code.
- A `cancelled` job is a new terminal state every future reader of `ops.jobs` must account for. The one
  reader that matters — the game-health snapshot — already counts only `pending`/`leased` and
  `dead_letter`, so a cancelled job is correctly neither.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Retry by business key instead of id | The console shows a job row; the operator acts on what they see. The id is the identity the read returns, and the id → type/key lookup is one query. |
| A `cancelled` status without a migration | The check constraint is the on-disk vocabulary (ADR-0003); widening it silently is exactly the "must move with a migration" case the code warns about. |
| Refuse to cancel a `leased` job | The worker's terminal statements are already guarded, so a cancel wins cleanly; refusing would leave an operator unable to stop a job the worker is wedged on. |
| A `Retry()` on `Fixture`/`Matchday` | Resolution already re-runs unstaged fixtures and re-reads a frozen snapshot idempotently; a new transition would be dead code that the next reader has to trust. |
| A per-job-type "retryable" registry | Over-engineering for one route; the rollover case is handled by pointing the runbook at its own resume. |
| An idempotency-record store for these three | The existing admin commands do not consume the key, and every action is already a guarded single-row transition, so a replay is a harmless 409. |
| Build the Angular console here | Follows ADR-0043's separation: the contract is frozen here, and the web milestone consumes it. |
