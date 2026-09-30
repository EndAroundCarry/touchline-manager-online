# ADR-0045: Administrative ownership and finance repairs, operator broadcasts, and the flag store

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** master plan §6.8, §6.9, §10.8, §13, §16 Stage 14, `F-46`, `F-47`, `FIN-12`, `OCC-3`,
  `OCC-4`, `OCC-5`, `OCC-6`, `COM-2`, `COM-6`, ADR-0003, ADR-0018, ADR-0022, ADR-0027, ADR-0042,
  ADR-0043, ADR-0044, threat-model `E-3`, `G-1`

## Context

§10.8 lists the admin routes, and ADR-0042/0043/0044 built the gate, the reads, and the job/matchday
recovery commands. Four mutations remained, and each one reaches something the surface had not touched:
club ownership, the money ledger, the news feed, and the operator switches §13 names.

- **Ownership.** `suspend` closes an account but does not touch its club: `OCC-5` says a suspended account
  keeps its club until the inactivity ladder (ADR-0027) closes the tenure, and the ladder deliberately skips
  a suspended account. `OCC-6` ("an administrator may assign temporary AI control before the standard
  inactivity period") had no implementation, and the domain's `ClubTenureEndReasons.AdministratorClosed` had
  no caller.
- **Money.** `FIN-12` says a balance is never edited and corrections are compensating entries, and
  `LedgerCategory.Compensation` + `LedgerSourceType.AdminRepair` + `FinanceAuditActions.CompensatingEntry`
  already existed — but `LedgerPostings.Compensation` recorded only amount and club, with no link to the line
  it corrected, and nothing called it.
- **Comms.** `comms.news_items` carries a world/country/division-scoped item with a template key and
  parameters (`COM-6`); the inbox is a per-manager fan-out (`COM-1`). There was no operator broadcast of any
  kind.
- **Flags.** §13 requires "feature flags and maintenance banners" and §6.9 documents an `ops.feature_flags`
  table, but no flag store existed: schedulers read `EnableXxx` configuration (ADR-0012 records this as a
  deliberate deferral).

## Decision

**1. `assign-ai` closes the tenure with the administrator reason.** `POST /api/v1/admin/clubs/{id}/assign-ai`
closes the club's open tenure with `ClubTenureEndReasons.AdministratorClosed`. A club is AI-controlled
exactly when it has no open tenure (`WORLD-7`), so there is no "AI tenure" to create: closing is the whole
transition. It returns the club fully to the AI (tactics, training, **and** market), frees the club's pyramid
occupancy, and leaves it claimable. **No takeover cooldown is started** — `OCC-4`'s cooldown exists to stop a
manager resigning to shop for clubs, and a repair the game makes on its own authority is not that. Nothing
else about the club moves (`OCC-5`), and the worker's daily AI pass (ADR-0018) fills the squad plan the club
now lacks; an API request never writes a squad decision. This implements `OCC-6`'s "assign AI control" as the
same close `OCC-3` describes, and leaves a club that already has no manager a `409`.

**2. A compensating entry records the line it corrects, in a new column.** `finance.ledger_entries` gains a
nullable `reverses_entry_id` (self-FK, `Restrict`), so §13's "preserved original record" is a fact in the
ledger and not only a sentence in an audit reason. `Stage14CompensatingEntry` adds the column, an index, and
`ck_ledger_entries_reverses` (`reverses_entry_id is null or (category = 'compensation' and
reverses_entry_id <> id)`). The entry stays **cash-only**: the existing `Compensation` posting moves cash and
leaves reserved funds alone, so a mis-stated *reservation* is still out of scope. The operator's
`Idempotency-Key` becomes the entry's `CorrelationId`, which makes the ledger's own
`unique (correlation_id, category)` index the idempotency guarantee — no `ops.idempotency_records` is added,
because this command has a natural key where the others did not.

**3. An announcement is a scoped news item, not a new inbox fan-out.** `comms.news_items` gains
`NewsCategory.Announcement` and the `news.announcement.published` template; `POST /api/v1/admin/announcements`
publishes a world-, country-, or division-scoped item, optionally with an expiry, read on the existing news
feed. The alternative — fanning one row per manager into `comms.inbox_messages` — buys per-manager unread
state at N rows per announcement, which is not worth it for an operator notice. `COM-2`'s "never stored
prose" rule is relaxed for this one template: the row still stores a template key and a parameter document,
but the parameters carry the operator's title and body. `COM-2` exists so engine-written messages stay
localizable; a notice an operator writes is content, not a generated message.

**4. The flag store lands now; reading it to gate behaviour is `F-51`.** `ops.feature_flags`
(`scope`, `key`, `value` jsonb, `rollout_metadata` jsonb, `version`; unique `(scope, key)`) and
`POST /api/v1/admin/feature-flags/{key}` let an operator set a world-scoped flag to an opaque JSON value. The
key is validated as a lower-case dotted name and the value must be well-formed JSON. **No scheduler reads a
flag yet.** Wiring the `EnableXxx` gates to the store is the incident-control milestone, which will read
through the same `IFeatureFlagStore`; this milestone only makes the switch settable and audited.

**5. All four are ordinary `AdminMutate` commands.** The `AdminMutate` policy (operator or admin, support
excluded — `E-3`), a fresh `X-MFA-Code` through `MfaStepUpFilter`, an explicit reason, and an `Idempotency-Key`
by presence, each recording an audit row in the same unit of work as the change (§10.8). The new audit
vocabulary is `world.club_tenure.assigned_ai`, `finance.compensating_entry.posted` (existing),
`admin.announcement.published`, and `admin.feature_flag.set`, with target types `club_tenure`, `ledger_entry`,
`news_item`, and `feature_flag`.

## Consequences

**Positive**

- `F-46`'s API half is complete: every §10.8 mutation now exists, each behind one gate and one audit contract.
- The finance repair is honest at the storage layer: the ledger shows which line a correction answers, so a
  reconciliation can be read without cross-referencing the audit trail.
- `OCC-6` and `OCC-3` share one close, so an operator repair and the inactivity ladder cannot drift into two
  different notions of "the AI controls this club".
- An announcement reuses the feed's read, paging, and caching unchanged, and the flag store's shape is the one
  §6.9 already specified.

**Negative**

- `assign-ai` on a full tier can provision a new tier (`PYR-1`), an immediate, visible pyramid change from a
  routine ownership repair. It is the same action a resignation takes and is documented in the runbook.
- The flag store is a new table with no reader in this milestone; a later reader must honour the
  `unique (scope, key)` shape and the opaque value when it lands.
- An announcement stores operator-authored prose, the one place the codebase does. The localization story for
  operator content is deliberately not solved here.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `assign-ai` marks the tenure `inactive` (temporary, reversible) | "Temporary AI control" would leave the club occupying capacity and keep the market with an absent manager. The intended repair — hand the club to the AI — is the close, and the reason token already names it. |
| A cooldown after an administrator close | `OCC-4`'s cooldown is a punishment for shopping for clubs; an operator repair is not the manager's choice. Applying it would lock a manager out of a repair the game made. |
| Reference the corrected entry only in the audit reason | §13 says "preserved original record"; a ledger that cannot name what it corrects cannot be reconciled on its own. The FK is one nullable column and a check constraint. |
| Let a compensating entry move reserved funds too | Widens the factory and the guards (`FIN-10`) for a case no workflow has produced yet. Cash-only is the existing semantics; the reservation case can arrive with evidence. |
| Fan an announcement into every manager's inbox | N rows per send and no dedup key, for unread state an operator notice does not need. The feed already carries scoped, expiring, template-rendered items. |
| A dedicated `comms.announcements` table | Duplicates the feed's scope, expiry, template, and paging machinery to say "an operator wrote it"; a category and a template say the same thing. |
| Wire the schedulers to `ops.feature_flags` here | Gating behaviour is incident control (`F-51`), with its own read path, caching, and tests. Shipping the store first keeps this milestone's contract small and reviewable. |
| Build the Angular console here | Follows ADR-0043/0044: the bearer-token contract is frozen and the web milestone consumes it. |
