# ADR-0009: Time, identity, and concurrency conventions

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (applied from Stage 1 onward)
- **Related:** [`../../product/game-rules.md`](../../product/game-rules.md), [`../data-model.md`](../data-model.md)

## Context

Almost every bug class that would hurt this product is a time, identity, or concurrency bug:

- A fixture locking at a "server local" time.
- Two managers claiming the same club in the same second.
- A manager editing a tactical plan on a phone while a team sheet locks.
- Player aging driven by wall-clock time instead of game seasons.
- Auction resolution raced by a bid placed at the same instant.

These must be impossible by convention rather than by vigilance, because they will be
implemented dozens of times across many entities.

## Decision

**Time**

- All persisted instants are UTC `timestamptz`; all API values are ISO 8601 with an explicit
  offset. Server local time is never used or stored.
- Time is injected through `IClock`. Tests use a fake clock. Compressed test clocks exist only
  in non-production environments.
- **Real time and game time are distinct.** A *game season* advances at season rollover; a
  *game year* advances at rollover too. Player aging, contract years, and attribute
  development are driven by game seasons, never by 365 real days. Deadlines, kickoffs, auction
  windows, and inactivity are driven by real UTC time.
- Deadlines are always returned to clients as absolute instants plus the server's current
  time, so client clock drift cannot cause a manager to act on a false deadline.

**Identity**

- Server-generated UUIDv7 identifiers. No client-supplied identifiers for new aggregates.
- Sequential display-facing identifiers are never used as primary keys or as ordering
  authority. Tie-breaking never uses database row order.
- Database naming is `snake_case`; C# naming is `PascalCase`; the mapping is explicit and
  tested.

**Concurrency**

- Every mutable aggregate carries `version bigint`. The API exposes it as a strong ETag and
  requires `If-Match` for conflicting updates, returning `412 Precondition Failed` with the
  changed state so the client can present a deliberate reapply.
- Optimistic concurrency is the default. Pessimistic locking (`FOR UPDATE`, advisory locks) is
  reserved for workflows where two writers must not interleave: club takeover, auction
  resolution, matchday publication, provisioning, and rollover.
- Serialization-sensitive workflows declare an explicit isolation level (usually
  `Serializable`) and are covered by concurrency tests that prove exactly one winner.
- Financial, claim, bid, rollover, and publication operations carry an explicit idempotency or
  correlation key. Retrying them is safe by construction, not by luck.

## Consequences

**Positive**

- The three most damaging bug classes (wrong-clock deadlines, duplicate claims, lost updates)
  have a single conventional answer that review can check mechanically.
- Tests are deterministic in time, which makes deadline behaviour testable at all.
- Accelerated seasons do not desynchronize player aging from contracts, because both advance
  on the season boundary.

**Negative**

- More plumbing: `IClock` everywhere, ETag handling in every mutable endpoint and Angular
  facade, version columns on mutable tables.
- `Serializable` transactions can fail with serialization errors; callers must retry. This
  retry behaviour is part of each workflow's design and its tests, not an afterthought.
- UUIDv7 primary keys are wider than integers and are not human-debuggable in logs; mitigated
  by including stable human-readable context (club slug, fixture label) in structured logs.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `DateTime.Now` / server local time | A DST transition would move kickoffs. Non-negotiable for fixed matchdays. |
| Storing wall-clock ISO strings without zone | Ambiguous on parse; standard source of off-by-one-hour matchday bugs. |
| Sequential integer primary keys | Leaks volume, invites enumeration, and encourages row-order tie breaks. |
| Pessimistic locking everywhere | Serializes harmless requests, hides design problems, and creates lock-ordering deadlocks. |
| Last-write-wins conflicts | Losses are silent; a manager's saved tactics could vanish with no explanation. This is unacceptable for a game where users invest time in preparation. |
| Ageing players by real time | Would desynchronize from accelerated seasons: an 18-year-old could retire before his contract's second season. |
