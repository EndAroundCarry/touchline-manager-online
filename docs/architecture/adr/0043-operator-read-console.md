# ADR-0043: Operator read console — jobs, matchdays, and audit

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** master plan §10.8, §10, §13, §16 Stage 14, `F-46`, `F-47`, ADR-0002, ADR-0003,
  ADR-0014, ADR-0042, data-classification §2/§4, threat-model `E-1`, `I-11`

## Context

§13 fixes what the operator console must support: world, country, division, season, matchday, and worker
status; queue depth, dead jobs, retries, leases, and the oldest overdue job; fixture
snapshot/simulation/publication state without exposing hidden data; and a read-only audit search. §10.8
lists the routes and ends with the rule that all admin *mutations* require a role, a reason, an
idempotency key, and an audit event.

The first Stage 14 milestone (`F-46`, `F-47`, ADR-0042) made roles real, added TOTP MFA, and gated an
admin surface — but that surface is one aggregate read and two account commands. The console's
diagnostic reads do not exist:

- **No job list.** `ops.jobs` is read only by `IAdminQueries.GetGameHealthAsync`, which returns counts and
  the oldest overdue instant. There is no way to see a job's status, attempts, lease, or last error.
- **No per-matchday read.** A round's state is reachable only through the manager competition reads or a
  tracked aggregate load. The failed `SimulationAttempt` rows — the answer to "why is this round stuck?"
  (`§6.6`) — have no operator surface at all.
- **No audit read.** `ops.audit_log` is write-only: `IAuditWriter` stages rows, and the only readers are
  tests reaching into the `DbContext` directly. `F-47`'s trail records who did what and why, but an
  operator cannot search it.

What is already built and reused: the `IAdminQueries`/`AdminQueries` read port and its `IClock`-stamped
snapshot; the `AdminRead`/`AdminMutate` policies and the `X-MFA-Code` step-up (ADR-0042); the keyset
cursor convention (`InboxCursor`, master plan §10, "lists use cursor pagination"); the matchday lock,
resolution, and publication business keys (`MatchdayJobTypes`); the two-club join from
`CompetitionQueries.GetFixtureAsync`; and the durable `SimulationAttempt` rows `ResolveMatchday` writes
when a fixture cannot simulate.

## Decision

**1. The console is one read port, extended by three methods.** `IAdminQueries` gains
`ListJobsAsync`, `GetMatchdayAsync`, and `ListAuditAsync` and a `PageSize`. The reads are pure
projections, so no use case wraps them — the health read and the analytics read set that precedent for
operator reads — and one port keeps the single Infrastructure registration.

**2. Pages are keyset, not offset.** The queue and the audit trail grow from the top, so an offset would
skip or repeat rows whenever one was written between two pages. Both lists reuse the inbox shape: a page
of 25, one extra row fetched to prove a successor exists, and an opaque base64url cursor of
`"{unixMillis}:{guid}"` (`AdminJobCursor`, `AdminAuditCursor`). A cursor this build did not produce is a
`400` by name (`INVALID_CURSOR`), and the response carries `NextCursor` (null on the last page) and the
server's `ServerTime`.

**3. The job read withholds the payload.** A job's `payload` is internal JSON a client cannot act on and a
disclosure surface besides; the deterministic `business_key` is the correlation an operator needs. The
status filter is validated against the four `ops.jobs.status` codes, and a value outside them is a `400`
(`INVALID_FILTER`).

**4. The matchday read is the "why is this round stuck" view.** It returns the round and its
division/country/season context, the nine fixtures with their club names and each fixture's latest
`SimulationAttempt` (attempt number, status, error category and message), and the lock, resolution, and
publication job rows read by their deterministic business keys. It exposes no hidden engine data — no
snapshot, input/output hash, seed, or valuation. The error category and message are operator-only, behind
the role and a completed second factor.

**5. The audit read exposes who, what, and why — and no pseudonymous address.** Each row carries the
actor type and account, the action, the target, the correlation ID, the instant, and the reason. The
hashed client IP and the before/after metadata columns are withheld: the search exists to answer who did
what and why, and the metadata is never populated today.

**6. `JobStatuses` gives the four status literals one home.** The queue writes `pending`, `leased`,
`completed`, and `dead_letter` as literals; the domain gains a `JobStatuses` codes class (mirroring
`FixtureStatuses`, `MatchdayPublicationStatuses`, `OutboxMessageStatus`) so the query and the filter
validation share one vocabulary. The queue's own SQL is left as it is.

**7. Reads require the role and the factor; they are never cached.** All three routes sit behind
`AdminRead` (support, operator, or admin, with the `mfa` claim) and set `Cache-Control: no-store`. Having
no mutation, they carry no `X-MFA-Code`, reason, or idempotency key — those are the mutation contract.

**8. This milestone is the reads; the actions are the next one.** The remaining §10.8 commands (job
retry and cancel, matchday resume, assign-ai, a compensating finance entry, announcements, feature flags)
and the Angular console are deliberately deferred. The read contract is frozen here so the client can be
built against it.

## Consequences

**Positive**

- The stage's exit criterion — "an on-call operator can diagnose and resume failed matchday, auction,
  provisioning, and rollover workflows" — now has its diagnosis half: the queue, a stuck round with its
  failed attempts, and the audit trail that explains what was already done.
- `ops.jobs` and `ops.audit_log` gain their first read surface, and every read is a projection over rows
  the game already holds, so the console adds no table and cannot widen what a command can reach.
- The gate is asserted from every side, so a later admin read cannot be added without the same policy
  and the tests would fail if one were.

**Negative**

- The audit and job lists have no covering index for a global `(occurred_at)`/`(created_at)` page, so a
  first page scans the table. At operations volume this is immaterial; adding the index is deferred with
  the other schema work rather than folded into a read-only milestone.
- The console has no UI yet: it is a bearer-token surface until the web milestone consumes it.
- `support` can read a fixture's raw simulation error message. That is the point of the diagnostic, and
  it sits behind the role and a completed second factor, but it is a wider read than the aggregate
  health snapshot and is noted for the disclosure review.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Offset pagination | The queue and the audit trail grow from the top, so an offset skips or repeats rows whenever one arrives between pages. The inbox already settled on keyset (§10). |
| A use case per read | A pure projection does not need a handler; wrapping it adds a layer and a duplicate DTO, and the health and analytics reads are already endpoint-over-port. |
| A separate query port per surface | Three registrations for one concern. `IAdminQueries` is already the operator's read port. |
| Expose the job payload | Internal JSON the client cannot act on, and a disclosure surface. The business key is the correlation that matters. |
| Expose the audit IP hash and the before/after metadata | The search answers who, what, and why; the pseudonymous address is not needed, and the metadata is never populated. |
| Build the mutations in this milestone | A job cancel needs a new terminal status and a migration; the reads are a self-contained, testable contract that the mutations and the web client can be built against. |
| Add a global `audit_log(occurred_at)` index now | A read-only milestone takes no migration; the index is deferred to the milestone that touches the schema anyway. |
