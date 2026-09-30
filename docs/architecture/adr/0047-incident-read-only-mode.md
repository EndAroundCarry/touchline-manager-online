# ADR-0047: Read-only incident mode, gated at the request edge

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** [ADR-0002](0002-auth-and-session-model.md), [ADR-0012](0012-daily-progression-materialised-job.md),
  [ADR-0037](0037-pwa-update-ux-and-offline-boundary.md), [ADR-0042](0042-operator-access-and-mfa.md),
  [ADR-0045](0045-administrative-repairs-and-broadcasts.md), [ADR-0046](0046-load-supply-chain-and-restore-drills.md),
  master plan §13, §16 Stage 14, `F-51`

## Context

Master plan §13 requires "emergency read-only mode that blocks manager writes while keeping published
content available". Stage 14 built the operator's feature-flag store and the audited command that sets a
flag (`F-46`, [ADR-0045](0045-administrative-repairs-and-broadcasts.md)), but nothing read a flag to gate
behaviour; [ADR-0046](0046-load-supply-chain-and-restore-drills.md) deferred the switch itself to this
milestone, and `mvp-traceability.md` recorded `F-51` as open.

The failure it answers is operational: a bad deploy, a corrupted snapshot, or a finance defect makes writes
unsafe for a while, but the game must stay readable and the deadlines the worker owns must keep advancing so
published content does not go dark. An operator needs one switch that stops every manager command, is
audited, and cannot lock the operators themselves out.

## Decision

1. **The state is one world-scoped flag, `incident.read_only`**, whose value is
   `{"enabled": true, "message": "<why>"}`. The read is deliberately lenient: an unset flag, a value that is
   not an object with a boolean `enabled`, or `{"enabled": false}` all mean *not read-only*. Only an
   operator's explicit opt-in stops manager writes. The message is the reason a manager reads on the banner.

2. **One global middleware is the gate, not per-endpoint filters.** It runs after authorization — so an
   unauthorized caller still gets its `401`/`403` first — and before the endpoints. Manager endpoints are
   mapped across ~11 `Map*Endpoints` methods with no shared route group, so a single middleware is the only
   un-bypassable hook and by far the least churn. Safe methods (`GET`/`HEAD`/`OPTIONS`/`TRACE`) always pass.

3. **Only manager writes are blocked.** A mutation passes when it is operator surface — the endpoint
   requires `AdminRead`, `AdminMutate`, or `OperationalAnalyticsRead` — or when it is a non-game path:
   sign-in under `/api/v1/auth` (a manager must be able to sign back in to read) and anything outside
   `/api/v1` (the health probes). Everything else is answered `503 READ_ONLY_MODE`, carrying the operator's
   message. Because the exemption is by policy metadata, the admin surface stays reachable, so an operator
   can always lift the switch.

4. **The read is cached briefly and invalidated by the command.** The flag is read through the same store the
   operator writes it with; a 5-second absolute cache keeps a per-command database read off the hot path.
   The admin command invalidates the cache in the process that served it, so a toggle takes effect at once in
   the API — which is both the only writer and the only reader. The TTL is the safety net for any future
   reader that does not share that process.

5. **The client learns the state from the existing `/sync` poll.** The shell shows a maintenance banner with
   the operator's reason and disables commands through one derived `canMutate` (online and not read-only),
   folding the incident switch into the same place offline gating already lives
   ([ADR-0037](0037-pwa-update-ux-and-offline-boundary.md)). The server gate remains the backstop: a client
   that has not polled yet still cannot write.

6. **Worker deadlines are not paused, and no migration is added.** "Keeping published content available"
   means the game keeps running while manager writes are refused; a full freeze remains the worker's
   `Enable*` configuration, which is out of this milestone. `ops.feature_flags` and its migration already
   exist, so this is application and client code only.

## Consequences

**Positive**

- One switch stops every manager command, is audited with the operator and the reason, and shows the reason
  to the manager. No database change was needed.
- The gate cannot be bypassed from the client, and the operator surface, sign-in, and reads are exempt, so
  an operator cannot lock the game or themselves out.
- The effect is immediate in the API process, and the read costs nothing on the hot path.

**Negative**

- The exemption is by authorization policy, not by path: a new manager write is blocked automatically (it
  carries no exempt policy), but a new *operator* endpoint must use one of the exempt policies or be added
  to the exemption to stay reachable while read-only.
- The non-production diagnostics triggers under `/api/v1/ops` are manager-unclassified POSTs, so they are
  blocked while read-only. That is acceptable — they never exist in production — but it is a sharp edge.
- A second reader in another process (there is none today) would see a change only after the 5-second TTL.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| An `IEndpointFilter` on every manager endpoint | There is no shared route group; the middleware is one registration instead of eleven surfaces to keep in step, and it cannot be forgotten on a new endpoint. |
| A runtime `Diagnostics`/`Incident` configuration option instead of a flag | A flag is an operator action that is audited with a reason and takes effect without a deploy; the master plan §13 names feature flags as the incident control. |
| Return `409 Conflict` instead of `503` | Read-only mode is a temporary unavailability of the write path, which is what `503` means; `409` is for a state conflict on a request that was otherwise accepted. |
| Pause the worker schedulers too | The stage's wording keeps published content available; freezing deadlines is a separate, heavier decision that already exists as the worker's `Enable*` configuration. |
| Store the message in its own table or config | The flag value already carries JSON; a second store would be a second thing to audit and keep in step. |
| Read the flag from the database on every command | Unnecessary load on the hot path; the 5-second cache plus in-process invalidation is enough. |

## Deferred

- Wiring the schedulers' `Enable*` configuration to flags — the [ADR-0012](0012-daily-progression-materialised-job.md)
  / [ADR-0045](0045-administrative-repairs-and-broadcasts.md) deferral remains open; this milestone adds the
  manager-write gate only.
- Telemetry dashboards and SLO alerting (`F-48`); the deployment rollback and API/PWA-compatibility drill
  (`F-50`); and the staging game-day and rollover incident exercises.
