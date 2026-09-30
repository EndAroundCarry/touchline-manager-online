# Operations runbook

This is the on-call reference for diagnosing and recovering the live game. Every procedure names **who may
act, the prechecks, the exact command, the validation, the notification, and the evidence to retain**, as
master plan §13 requires. Behavioural rules live in [`../product/game-rules.md`](../product/game-rules.md);
this file is how you operate the system that enforces them.

The commands below are the operator surface of [ADR-0042](../architecture/adr/0042-operator-access-and-mfa.md)
(access and MFA), [ADR-0043](../architecture/adr/0043-operator-read-console.md) (the read console),
[ADR-0044](../architecture/adr/0044-operator-recovery-commands.md) (the recovery commands), and
[ADR-0045](../architecture/adr/0045-administrative-repairs-and-broadcasts.md) (ownership and finance repairs,
broadcasts, and flags), with the read-only incident switch of
[ADR-0047](../architecture/adr/0047-incident-read-only-mode.md) (`F-51`). The rollover resume is the
non-production diagnostics control of
[ADR-0034](../architecture/adr/0034-operator-rollover-preview-and-resume.md).

## Who may act

| Role | Read the console | Act (mutations) |
|---|---|---|
| `support` | Yes | **No** — excluded from every mutation (`E-3`) |
| `operator` | Yes | Yes |
| `admin` | Yes | Yes |

A session only reaches the console after completing a second factor. Every **mutation** additionally carries
a fresh `X-MFA-Code`, so possession is re-asserted per action (ADR-0002). Roles are granted and revoked with
the `access-admin` tool, never over HTTP (ADR-0042).

## The command contract

Every mutation (`POST /api/v1/admin/...`) requires all of:

| Item | Value |
|---|---|
| `Authorization` | `Bearer <access token>` for an operator/admin session with a completed second factor |
| `X-MFA-Code` | A current TOTP code from the operator's authenticator |
| `Idempotency-Key` | Any stable string ≤ 80 characters, unique per intended action |
| Body | `{ "reason": "<why>" }`, ≤ 200 characters, stored in the audit trail |

A missing reason or idempotency key is `400`; a missing or stale code is `403`; a non-operator is `403`; an
unauthenticated caller is `401`. Replaying a command is a `409`: the first action already moved the row.

## Getting an operator session

```bash
API=https://<host>/api/v1

# 1. Sign in. An account with a confirmed second factor returns 202 with a challenge, not a session.
curl -s $API/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"ops@example.com","password":"..."}'
# -> {"challengeToken":"...", ...}

# 2. Complete the second factor. This returns the access token the console is used with.
curl -s $API/auth/mfa/login -H 'Content-Type: application/json' \
  -d '{"challengeToken":"...","code":"123456"}'
# -> {"accessToken":"...", ...}
```

## Diagnosis first

Read before acting. Every read is `AdminRead` (support, operator, or admin with a factor) and never cached.

| Question | Command |
|---|---|
| Is the game healthy? Queue depth, dead letters, oldest overdue job | `GET /api/v1/admin/health/game` |
| Is the game read-only, and why? | `GET /api/v1/admin/health/game` (`readOnly`, `readOnlyMessage`) |
| Which jobs are dead-lettered or overdue? | `GET /api/v1/admin/jobs?status=dead_letter` |
| Why is this round stuck? | `GET /api/v1/admin/matchdays/{id}` |
| What has already been done, and by whom? | `GET /api/v1/admin/audit?action=admin.` |
| Is the API failing or slow, and on which route? | The Grafana dashboards — see below |

The matchday read is the one that answers "why is this round stuck": it carries the nine fixtures with each
fixture's latest simulation attempt — its error category and message — and the round's lock, resolution, and
publication job rows.

The **SLO overview** and **API (RED)** dashboards answer the same question for the API as a whole, and they
are where an alert sends you. Start them with `npm run obs:up` and open them at
`http://localhost:13000`; the rules behind the alerts, the PromQL, and what each one does *not* cover are in
[`observability.md`](observability.md) (`F-48`, ADR-0048).

## Alerts

The alerts below are Prometheus rules evaluated by the local observability stack and read in Alertmanager at
`http://localhost:19093`. **Each one names its procedure in this file**, and `npm run obs:check` fails if that
link stops resolving. Routing them to a real pager, email, or chat receiver is the deployment milestone's
work, so on this machine Alertmanager's UI *is* the notification.

| Alert | Severity | Fires when | Procedure |
|---|---|---|---|
| `ApiAvailabilityBurnFast` | critical | More than 0.1% of responses are 5xx over five minutes, for two minutes — the monthly 99.9% budget | [The API is failing requests](#runbook--the-api-is-failing-requests) |
| `ApiReadLatencyHigh` | warning | p95 read (GET/HEAD/OPTIONS) over 500 ms for ten minutes | [The API is slow](#runbook--the-api-is-slow) |
| `ApiCommandLatencyHigh` | warning | p95 command (every other method) over 800 ms for ten minutes | [The API is slow](#runbook--the-api-is-slow) |
| `TelemetryPipelineDown` | warning | Prometheus has not scraped the OTLP collector for five minutes | [Telemetry is not arriving](#runbook--telemetry-is-not-arriving) |

An alert that fires on this stack is a statement about **the API and the worker**, not about the game's
rules. Nothing here can tell you a matchday is stuck — that is still `GET /api/v1/admin/matchdays/{id}`.

## Runbook — a matchday is stuck

**Symptom.** A round's kickoff has passed and it is not published.
`GET /api/v1/admin/matchdays/{id}` shows `publicationStatus` `pending` with a fixture whose latest attempt
is `failed`, and the resolution job row is `dead_letter`.

**Who may act.** Operator or admin.

**Prechecks.**
1. Read the round and record its `id`, `publicationStatus`, and every fixture's latest attempt error.
2. Confirm the governing job is dead-lettered (`GET /api/v1/admin/jobs?jobType=competition.resolve-matchday`).
   A round whose job is still `pending` or `leased` is not stuck — the queue owns it.

**Action.**

```bash
curl -s -X POST $API/admin/matchdays/$MATCHDAY_ID/resume \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: matchday-$MATCHDAY_ID-$(date +%s)" \
  -H 'Content-Type: application/json' \
  -d '{"reason":"resolution dead-lettered on invalid-input; snapshot defect fixed in <ticket>"}'
# 202 -> {"matchdayId":"...","step":"resolve","requeued":true}
```

The `step` is `resolve` for a `pending` round and `publish` for a `staged` one. The worker does all the work;
the command only puts the job back on the queue.

**Validation.** Poll `GET /api/v1/admin/matchdays/{id}` until `publicationStatus` is `published`. If it
returns to `dead_letter`, the underlying defect was not fixed — escalate; do **not** loop.

**Notification.** Results publish through the normal inbox path, so affected managers learn of the round's
result the usual way. No separate notice is sent.

**Rollback / compensation.** None. Resolution is idempotent per fixture — fixtures already staged are left
alone — so a resume never duplicates a result. A round that has already `published` returns `409` and cannot
be re-published.

**Evidence.** The `admin.matchday.resumed` audit row (actor, reason, correlation ID) and the simulation
attempt rows the resume produced.

## Runbook — a dead-lettered job

**Symptom.** `GET /api/v1/admin/health/game` shows `deadLetterJobs > 0`, or a job row is `dead_letter`.

**Who may act.** Operator or admin.

**Prechecks.** Read the job (`GET /api/v1/admin/jobs?status=dead_letter`) and its `lastError`. Decide whether
the cause is fixed. **Check the job type against the exceptions below before retrying.**

**Action (generic retry).**

```bash
curl -s -X POST $API/admin/jobs/$JOB_ID/retry \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: retry-$JOB_ID-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"reason":"stuck dependency recovered at <time>"}'
# 202 -> {"jobId":"...","status":"pending"}
```

**Validation.** The job is `pending` with `attemptCount` reset to `0`. Watch it complete or dead-letter
again.

**Rollback / compensation.** None on the queue. If the handler already applied partial work before failing,
the workflow's own idempotency (business key + guarded steps) is what keeps a retry safe; a partially applied
*workflow* is recovered with its own runbook, not with this one.

**Evidence.** The `admin.job.retried` audit row.

**Exceptions — do not retry blindly:**
- **Season rollover** (`competition.season-rollover`): the handler refuses to run while the rollover row is
  `Failed`. A generic retry dead-letters again. Use the runbook below.
- **Matchday resolution / publication**: use the matchday resume, which takes the round's advisory lock.
- **Auctions** (`market.resolve-auction`) and **provisioning** (`world.provision-division`): a generic retry
  is the current recovery path; if it dead-letters again, the workflow needs a domain checkpoint reset that
  does not exist yet — escalate and record it.

## Runbook — stop a stuck job

**Symptom.** A job is looping, or its work is no longer wanted.

**Who may act.** Operator or admin.

**Prechecks.** Read the job. Only `pending`, `leased`, and `dead_letter` jobs can be cancelled; `completed`
and `cancelled` are terminal.

**Action.**

```bash
curl -s -X POST $API/admin/jobs/$JOB_ID/cancel \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: cancel-$JOB_ID-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"reason":"superseded by <ticket>"}'
# 200 -> {"jobId":"...","status":"cancelled"}
```

**Validation.** The row is `cancelled` and is never claimed again.

**Rollback / compensation.** Cancellation stops future execution; it does **not** undo work a handler already
committed. If the workflow had partially applied, recover it with its own runbook.

**Evidence.** The `admin.job.cancelled` audit row.

## Runbook — a season rollover failed

**Symptom.** A rollover row is `Failed` (preflight found drift), and its job is dead-lettered. This is
distinct from the matchday and generic cases.

**Who may act.** Operator or admin, through the non-production diagnostics control (gated by
`Diagnostics:EnableRolloverTrigger`).

**Prechecks.** Preview the rollover and reconcile the referenced drift first.

**Action.**

```bash
curl -s -X POST $API/ops/diagnostics/resume-rollover \
  -H 'Content-Type: application/json' \
  -d '{"seasonId":"...","reason":"drift reconciled: <detail>"}'
# 202 -> {"seasonId":"...","rolloverId":"...","businessKey":"...","requeued":true,...}
```

**Validation.** The rollover row returns to `Started` and the job is `pending`. Watch it reach `Completed`.

**Evidence.** The `world.season_rollover.resumed` audit row.

## Runbook — hand a club to the AI

**Symptom.** A manager has asked support to step in, or is unreachable, and their club should be run by the
AI before the inactivity ladder (ADR-0027) would reach it. This is `OCC-6`.

**Who may act.** Operator or admin.

**Prechecks.**
1. Confirm the club has a human manager. `GET /api/v1/admin/audit?targetId=<clubId>` or the club dashboard
   shows an open tenure; a club with no manager is already AI-run and returns `409 CLUB_ALREADY_AI`.
2. Decide with the account team whether the *account* is also to be suspended. `assign-ai` does not touch the
   account; `suspend` does not touch the club. They are separate commands, often run together.

**Action.**

```bash
curl -s -X POST $API/admin/clubs/$CLUB_ID/assign-ai \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: assign-ai-$CLUB_ID-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"reason":"manager unreachable; support handover <ticket>"}'
# 200 -> {"clubId":"...","controlStatus":"ai","managerId":"..."}
```

**Validation.** The club's tenure is `closed` with `end_reason = administrator_closed`; the club is claimable
again and the AI's daily pass (ADR-0018) will set its tactics and training. If the country's lowest tier was
full of humans, this frees a place and can provision a new tier (`PYR-1`) — check
`GET /api/v1/countries/<id>/capacity` if that matters.

**Notification.** The former manager is **not** emailed by this command; if they should be told, send that
notice through your usual support channel. Their club's state is untouched (`OCC-5`): squad, contracts, cash,
fixtures, and commitments all continue.

**Rollback / compensation.** There is no un-assign: re-claiming the club is the ordinary takeover path, and
it is the manager's (or a successor's) choice. **No cooldown is started**, so the freed club can be claimed
immediately.

**Evidence.** The `world.club_tenure.assigned_ai` audit row (actor, reason, correlation ID).

## Runbook — correct a club's ledger

**Symptom.** A club's money is wrong in a way a workflow produced and cannot be re-run cleanly — a
double-counted gate, a mis-posted award. This is `FIN-12`: a balance is never edited.

**Who may act.** Operator or admin. Treat this as a last resort: prefer re-running the idempotent workflow.

**Prechecks.**
1. Find the offensive entry. `GET /api/v1/admin/audit` will not show balances; read the club's ledger through
   the manager-facing ledger view or the database, and record the exact `ledger_entries.id` and amount.
2. Compute the **signed** correction that restores the intended balance — a positive delta to add money, a
   negative one to remove it. A correction cannot take cash below zero, nor below the club's reserved funds.

**Action.**

```bash
curl -s -X POST $API/admin/finance/compensating-entry \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: repair-$CLUB_ID-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"clubId":"'$CLUB_ID'","cashDeltaMinor":-2500000,"reversesEntryId":"'$ENTRY_ID'","reason":"gate double-counted on fixture <id>; ticket <ref>"}'
# 201 -> {"entryId":"...","clubId":"...","cashDeltaMinor":-2500000,"resultingCashMinor":...,"reversesEntryId":"..."}
```

The `Idempotency-Key` becomes the new entry's correlation key, so a retried POST with the same key returns
`409 COMPENSATION_ALREADY_POSTED` rather than posting twice. `reversesEntryId` is optional but should be
given whenever the offending entry is known.

**Validation.** The new `finance.ledger_entries` row is `category = compensation`, `source_type =
admin_repair`, and names `reverses_entry_id`; the account's cash moved by exactly the delta. The original
entry is unchanged and the ledger still replays to the stored balances.

**Notification.** None automatic. If a manager's money changed in a way they would notice, tell them.

**Rollback / compensation.** A mistaken correction is corrected by another compensating entry — never by
editing or deleting the first. Record the reason for the reversal.

**Evidence.** The `finance.compensating_entry.posted` audit row (actor, reason, correlation key = the operator
idempotency key; target = the new entry's id).

## Runbook — publish an announcement

**Symptom.** Managers need to be told something that is not a game event — a maintenance window, a rule
clarification, a known-issue notice.

**Who may act.** Operator or admin.

**Prechecks.** Decide the scope: the whole world (no `countryId`/`divisionId`), one country, or one division.
Decide whether it should expire; a maintenance notice usually should.

**Action.**

```bash
curl -s -X POST $API/admin/announcements \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: announcement-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"title":"Scheduled maintenance","body":"Read-only tonight 22:00-22:30 UTC.","expiresAt":"2026-10-07T00:00:00Z","reason":"maintenance window announced"}'
# 201 -> {"newsItemId":"...","category":"announcement","publishedAt":"...","expiresAt":"..."}
```

**Validation.** The item appears on `GET /api/v1/news` within its scope. A country or division scope that
does not exist returns `404 ANNOUNCEMENT_SCOPE_NOT_FOUND`.

**Notification.** The announcement *is* the notice; it is a news-feed item, not an inbox message, so it
carries no per-manager unread state.

**Rollback / compensation.** There is no delete. A wrong announcement is corrected by publishing a
follow-up. (This command is presence-checked on the idempotency key only, so a duplicated POST posts a
second item — send it once.)

**Evidence.** The `admin.announcement.published` audit row.

## Runbook — set a feature flag

**Symptom.** A switch has to change without a deploy. This milestone makes flags **settable and audited**;
nothing reads them to gate behaviour yet — that is the incident-control milestone. Until then, the runtime
`EnableXxx` configuration is what actually gates work.

**Who may act.** Operator or admin.

**Prechecks.** Agree the key with the team that will read it. Keys are lower-case and dotted
(`matchday.enabled`, `market.auctions_enabled`). The value is any JSON document.

**Action.**

```bash
curl -s -X POST $API/admin/feature-flags/market.auctions_enabled \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: flag-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"value":{"enabled":false},"reason":"pausing auctions for a ledger repair <ticket>"}'
# 201 (created) or 200 (updated) -> {"key":"market.auctions_enabled","scope":"world","value":{"enabled":false},"version":1,"created":true}
```

**Validation.** The response's `version` increments on each set, and `value` echoes what was stored. A key
that is not a valid lower-case dotted name, or a value that is not valid JSON, is `400 FEATURE_FLAG_INVALID`.

**Rollback / compensation.** Set the flag back to its previous value; every set is audited, so the previous
value is in the trail.

**Evidence.** The `admin.feature_flag.set` audit row (actor, reason, key, version).

## Runbook — take the game read-only, and give it back

**Symptom.** A defect or data fault makes manager writes unsafe — a bad deploy, a snapshot fault, a finance
defect — and the game must stop accepting commands while reading stays available and the worker keeps
advancing deadlines.

**Who may act.** Operator or admin.

**Prechecks.**
1. Confirm the fault is in the write path: `GET /api/v1/admin/health/game` shows the world and
   `readOnly: false`. Decide separately whether the worker's deadlines should also stop; **this switch does
   not stop them** — by design the game stays readable and published content keeps advancing
   (`F-51`, ADR-0047).
2. Agree the reason. Every manager reads it on the maintenance banner.

**Action.**

```bash
curl -s -X POST $API/admin/feature-flags/incident.read_only \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $CODE" \
  -H "Idempotency-Key: read-only-$(date +%s)" -H 'Content-Type: application/json' \
  -d '{"value":{"enabled":true,"message":"Read-only while we repair the ledger; back shortly."},"reason":"<ticket>"}'
# 201 (created) or 200 (updated) -> {"key":"incident.read_only","scope":"world","value":{...},"version":1,"created":true}
```

**Validation.** A manager command now returns `503` with code `READ_ONLY_MODE`; a manager read still returns
`200`; `GET /api/v1/sync` carries `readOnly: true` and the message, so the shell shows the banner and disables
its controls; `GET /api/v1/admin/health/game` shows `readOnly: true`. The effect is immediate for the API
instance that served the command and lands within a few seconds on any other.

**Notification.** Tell managers through an announcement (see the announcement runbook) — the reason and the
expected window.

**Rollback / compensation.** Set the flag back:
`{"value":{"enabled":false},"reason":"writes restored"}`. There is nothing else to undo: no manager command
was accepted while it was on. Confirm `readOnly: false` and that a manager write succeeds.

**Evidence.** The two `admin.feature_flag.set` audit rows (actor, reason, key, version) — one to set and one
to clear.

## Runbook — the API is failing requests

**Symptom.** `ApiAvailabilityBurnFast` is firing in Alertmanager, or the SLO overview's error-ratio panel is
above 0.1%. Managers are getting `5xx` responses. This is master plan §14.1's "99.9% monthly availability"
objective being spent, not a rules question.

**Who may act.** Anyone may diagnose. Only an operator or admin may take the game read-only.

**Prechecks.**
1. Read the alert in Alertmanager (`http://localhost:19093`); its annotations carry the ratio that tripped it.
2. Open the **API (RED)** dashboard and read the 5xx-by-route panel. **One route failing is a defect to fix;
   every route failing is infrastructure** — check PostgreSQL (`infra/compose.yaml`, `npm run infra:up`) and
   whether the API process is still up at all.
3. If the failing route is a write, confirm the fault is in the write path before reaching for the switch:
   `GET /api/v1/admin/health/game` shows the world, the season, and whether the game is already read-only.

**Action.** There is no operator command that repairs a `5xx`. Decide between the two real responses:

- **The fault is in the write path and cannot be fixed forward in minutes** — take the game read-only so no
  manager command is accepted while you repair it. That is the read-only procedure below; it does not stop
  the worker, so published content keeps advancing.
- **The fault is in infrastructure** — restore it (PostgreSQL first), then let the alert resolve on its own.

**Validation.** The error ratio returns under 0.1% and the alert clears in Alertmanager within its
`resolve_timeout` (five minutes). If read-only was set, confirm a manager read still returns `200` and a
manager command returns `503 READ_ONLY_MODE`.

**Notification.** If managers saw failures, publish an announcement naming what broke and what they should
retry. Managers are not emailed automatically.

**Rollback / compensation.** If read-only was set, clear it with
`{"value":{"enabled":false},"reason":"writes restored"}` and confirm `readOnly: false`. Nothing else is
reversible: a request that returned `5xx` may have committed or may not have — the game's idempotency keys
are what make a manager's retry safe, and the affected workflow's own runbook is what recovers a partial one.

**Evidence.** The Alertmanager alert record (start, end, annotations) and, if it was used, the pair of
`admin.feature_flag.set` audit rows.

## Runbook — the API is slow

**Symptom.** `ApiReadLatencyHigh` (p95 read over 500 ms) or `ApiCommandLatencyHigh` (p95 command over
800 ms) is firing for ten minutes. This is §14.1's latency objective, not an availability failure: requests
are still answered.

**Who may act.** Anyone may diagnose. No operator command fixes latency.

**Prechecks.**
1. Open **API (RED)** and read p95-by-route. **Record the route.** The alert is on the aggregate objective,
   so the dashboard is what tells you which route is paying for it.
2. Read the **in-flight requests** panel. A line pinned at a constant value is a queue rather than load, and
   points at a saturated resource instead of a slow query.
3. Read the **outbound client p95** panel. If that is also elevated, the API is waiting on something else and
   the route is a symptom.
4. Pull a trace: in Grafana, **Explore → Tempo**, filter `service.name = touchline-api`, and sort the window's
   spans by duration. The slowest span names where the time actually goes.

**Action.** There is no command to run. This is a defect investigation: capture the route, the time window,
and the trace from step 4, and open a ticket with them. If the slow path is a write and the delay is a
symptom of a data fault rather than a code fault, treat it as the availability case above and consider taking
the game read-only while it is repaired.

**Validation.** p95 returns under the objective for ten minutes and the alert clears. A one-off dip is not
evidence: watch the panel, not a single scrape.

**Notification.** If managers are waiting on the slow route in normal play, publish an announcement naming
it; otherwise none.

**Rollback / compensation.** None. Nothing was changed.

**Evidence.** The ticket, and the trace ID from step 4 — that is what turns "the API was slow" into something
fixable.

## Runbook — telemetry is not arriving

**Symptom.** `TelemetryPipelineDown` is firing: Prometheus has not scraped the OTLP collector for five
minutes. The dashboards have gone flat. **This says nothing about the game.** It means you are now blind, and
that every other alert on this stack has stopped being able to fire.

**Who may act.** Anyone with access to the machine.

**Prechecks.**
1. Is the collector up? `docker compose -f infra/observability/compose.observability.yaml ps`.
2. Are the API and the worker exporting at all? Confirm the process that is running was started with
   `OTEL_EXPORTER_OTLP_ENDPOINT` set — the hosts read it from the environment, and with it unset the
   instrumentation is registered but nothing leaves the process (ADR-0041).
3. Distinguish the two failures: a **dead stack** means the collector container stopped; a **silent host**
   means the stack is fine and the application was never pointed at it.

**Action.**

```bash
npm run obs:up          # start or restart the stack; the compose file is idempotent
```

If the stack was already up, the problem is the host: restart the API and the worker with the endpoint set
(`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:14317`; `$env:OTEL_EXPORTER_OTLP_ENDPOINT = '...'` in
PowerShell). If both hosts are already exporting, read the collector's own logs — a bad configuration change
is the usual cause.

**Validation.** Prometheus's `/targets` page (`http://localhost:19090/targets`) shows `otel-collector` as
**UP**, and series reappear on the SLO overview within a scrape interval.

**Notification.** None. This stack is local tooling: no manager sees it and nothing about the game changed.
Do **not** announce a telemetry gap.

**Rollback / compensation.** None.

**Evidence.** The alert record, and a note of the window during which the game ran **unmeasured** — that gap
is the reason this alert exists, and it belongs in the incident timeline.

## Audit

Every action above writes one append-only `ops.audit_log` row carrying the actor, the action, the target, the
correlation ID, the instant, and the reason. Search it with
`GET /api/v1/admin/audit?targetId=<id>` or `?actorUserId=<id>`. The hashed client IP and the before/after
metadata are never exposed on the read ([ADR-0043](../architecture/adr/0043-operator-read-console.md)).
