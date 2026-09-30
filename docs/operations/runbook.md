# Operations runbook

This is the on-call reference for diagnosing and recovering the live game. Every procedure names **who may
act, the prechecks, the exact command, the validation, the notification, and the evidence to retain**, as
master plan §13 requires. Behavioural rules live in [`../product/game-rules.md`](../product/game-rules.md);
this file is how you operate the system that enforces them.

The commands below are the operator surface of [ADR-0042](../architecture/adr/0042-operator-access-and-mfa.md)
(access and MFA), [ADR-0043](../architecture/adr/0043-operator-read-console.md) (the read console), and
[ADR-0044](../architecture/adr/0044-operator-recovery-commands.md) (the recovery commands). The rollover
resume is the non-production diagnostics control of
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
| Which jobs are dead-lettered or overdue? | `GET /api/v1/admin/jobs?status=dead_letter` |
| Why is this round stuck? | `GET /api/v1/admin/matchdays/{id}` |
| What has already been done, and by whom? | `GET /api/v1/admin/audit?action=admin.` |

The matchday read is the one that answers "why is this round stuck": it carries the nine fixtures with each
fixture's latest simulation attempt — its error category and message — and the round's lock, resolution, and
publication job rows.

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

## Audit

Every action above writes one append-only `ops.audit_log` row carrying the actor, the action, the target, the
correlation ID, the instant, and the reason. Search it with
`GET /api/v1/admin/audit?targetId=<id>` or `?actorUserId=<id>`. The hashed client IP and the before/after
metadata are never exposed on the read ([ADR-0043](../architecture/adr/0043-operator-read-console.md)).
