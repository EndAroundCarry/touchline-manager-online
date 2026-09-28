# ADR-0023: Income and expenses are postings made by the workflow that causes them, and the gate is drawn inside publication

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 9
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0022](0022-append-only-club-ledger.md), master plan §6.8, §7.2, §7.4, §16 Stage 9, game rules §13, `FIN-3`…`FIN-9`, `FIN-16`, `FIN-17`

## Context

ADR-0022 settled that a club's money is an append-only ledger and that its account is a projection of it, but
it deliberately landed only the primitive: opening balances. The rest of Stage 9 is the postings themselves —
gate revenue (`FIN-3`), the weekly sponsorship credit (`FIN-4`), wages (`FIN-7`), the operating cost (`FIN-9`),
the position award (`FIN-5`), and the emergency grant (`FIN-16`) — and each has to decide three things.

**1. Who writes the entry, and when.** Master plan §7.4.7 lists "post gate revenue" inside the *publication*
transaction, beside the standings and the player statistics, while §7.2 gives wages, sponsorship, and the
operating cost to a `WeeklyFinanceRun`. A posting therefore belongs to the workflow that causes it, not to a
single central income service.

**2. How a retried workflow avoids paying twice.** The ledger's idempotency is `unique (correlation_id,
category)` (`FIN-17`), so the correlation key each factory chooses *is* the retry guarantee. It has to name the
operation and the thing it moved — a fixture, a club's week — and never the amount.

**3. Where a new money value lives.** `RULE-1` forbids magic constants and `RULE-3` makes a change that affects
money a version change, so the formulas and their baselines belong in `WorldRuleSet` under a new version.

## Decision

**1. The rule set becomes `world-rules-v6`.** It gains the gate fraction and form factor, the weekly
sponsorship and operating-cost baselines, the position-award scale, the weekly boundary time, and the payroll
risk horizon, each as a named constant or formula beside the existing finance baselines (`RULE-1`, `RULE-3`).

**2. Each income or expense is a `LedgerPostings` factory that names its own correlation key.** `GateReceipt`
keys on the fixture (`matchday:{fixtureId}`), so a retried publication collides with the gate it already drew.
The four weekly postings — sponsorship, wages, operating cost, and the emergency grant — share one key per club
and week (`weekly-run:{date}:{clubId}`) but carry four different categories, so one retried week posts each
line exactly once (`FIN-17`).

**3. Gate revenue is posted inside the publication transaction.** `PublishMatchday` composes a
`MatchdayFinances` collaborator, the exact counterpart of the existing `MatchdayNotifications`: it stages
entries and never saves, so the result and the money it earned become public together, and a publication that
fails leaves neither behind. The gate is priced from the table the round *produced*, because that is the form
the fixture has just changed.

**4. The web of income and expense is settled by the workflow that owns each source.** `MatchdayFinances` owns
the gate; the weekly run owns sponsorship, wages, and the operating cost and, where wages would take a club
below zero, posts a logged emergency grant first (`FIN-16`). No posting is written by a caller that does not own
the cause, which is the same rule that keeps module writes behind application use cases (ADR-0001).

## Consequences

**Positive**

- `FIN-18` continues to hold: the gate test asserts, over a real seeded world, that each published club's
  ledger entries sum exactly to its account balance.
- Idempotency is structural rather than procedural — the correlation key a factory chooses is what makes a
  retried publication or a retried weekly run harmless, and the database refuses the second write.
- The rate constants are in one versioned place, so the multi-season balancing the stage requires is an edit to
  `WorldRuleSet` and a version bump, not a search for magic numbers.

**Negative**

- A published round now writes nine more rows (the gate) inside its transaction. That is the deliberate cost of
  `FIN-3`'s "in the same transaction as the result"; the alternative — a second job that pays after publication
  — would let a published result exist without the money it earned.
- The weekly run touches every club, so its retry story rests entirely on the correlation key and the category
  uniqueness. A future change that gave two postings the same category and key would silently collapse them,
  which the factory comments exist to prevent.
- `WorldRuleSet` continues to grow with each stage that introduces money or time; it is the single rule set the
  plan requires, but it is not a small file.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A single `SettleIncome` job that posts everything after publication | It would break `FIN-3`'s "post gate revenue" inside the publication transaction and let a public result exist without its gate. |
| Key the gate on the matchday rather than the fixture | A matchday has nine hosts, so one key would collapse nine gates into one under `unique (correlation_id, category)`. |
| Give each weekly posting its own correlation key | They share the operation, and a shared key is what lets one collision check cover the whole week; separate keys would need four unique indexes to say the same thing. |
| Put the rate formulas in the application service instead of the rule set | `RULE-1` requires the tunable values in one versioned rule set so a historical season can be explained by the rules in force; a formula in the service would be invisible to that. |
