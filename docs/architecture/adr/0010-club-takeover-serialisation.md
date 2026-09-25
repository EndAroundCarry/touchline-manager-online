# ADR-0010: Club takeover serialises with an advisory lock, not with `SERIALIZABLE`

- **Status:** Accepted
- **Date:** 2026-09-24
- **Stage:** 3
- **Related:** [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0009](0009-time-identity-and-concurrency.md), master plan §7.6, game rules `PYR-3`, `OCC-4`, `OCC-9`

## Context

Master plan §7.6 requires a takeover to "insert tenure and update last-active metadata in a serializable
transaction". `PYR-3` independently requires the capacity evaluation to run "in a PostgreSQL transaction
holding a country-scoped advisory lock", and ADR-0005 repeats that.

Those two instructions cannot both be satisfied usefully, and the reason is a property of how PostgreSQL
takes snapshots.

A snapshot-isolation transaction (`REPEATABLE READ`, and therefore `SERIALIZABLE`) fixes its snapshot at
its **first statement**. In this workflow the first statement has to be the lock request — the lock is
what makes the read-then-write safe. So the sequence is:

1. Transaction A opens.
2. Transaction A asks for the country advisory lock. Its snapshot is taken here.
3. Transaction B holds the lock and is mid-workflow.
4. Transaction A blocks.
5. Transaction B commits, creating a tenure.
6. Transaction A acquires the lock — and still cannot see B's tenure, because its snapshot predates
   B's commit.

Transaction A then reads a tier that looks one place emptier than it is and inserts a tenure that the
partial unique index rejects. The manager gets a 500, and the friendly, precise refusal the API promises
(`409 CLUB_ALREADY_CLAIMED`, §7.6) never happens. Making serialization failures safe would mean a retry
loop, which is more moving parts than the problem needs.

## Decision

**Club takeover and resignation run in a `READ COMMITTED` transaction and take two transaction-scoped
advisory locks, in this fixed order:**

1. `manager:{managerId}`
2. `country:{countryId}`

`READ COMMITTED` gives every statement the latest committed state, so once the lock has been granted the
counts and control flags read inside the critical section are current. The locks provide the exclusivity
the isolation level was meant to provide, and they scope it to exactly the rows involved rather than to
every row the transaction touches.

The locks are taken in one global order, so two transactions can never hold one lock each and wait for the
other.

The partial unique indexes (`ux_club_tenures_open_club`, `ux_club_tenures_open_manager`,
`ux_club_tenures_takeover_idempotency_key`, `ux_division_provisioning_requests_country_id_target_tier`)
remain, unchanged, as the backstop: the locks make the outcome friendly, the constraints make duplication
impossible.

**`SERIALIZABLE` is not abandoned for the product.** It stays available on
`IUnitOfWork.BeginTransactionAsync` for the workflows where it is the right tool — auction resolution and
season rollover, which compare and update several rows without a natural lock row and where a
serialization failure is retried by a durable job rather than surfaced to a manager.

## Consequences

**Positive**

- A concurrent takeover produces exactly one of the two documented answers — `Claimed` or
  `CLUB_ALREADY_CLAIMED` — instead of racing into a constraint violation and a 500.
- One manager claiming from two devices at once gets `MANAGER_HAS_ACTIVE_CLUB` rather than a constraint
  error, because the manager lock is taken first.
- The capacity evaluation sees the row that filled the tier, so `PYR-2`'s request is created exactly once
  by the takeover that caused it, rather than being missed and left to the next evaluator.
- Lock scope is two identities, not a table scan's worth of predicates, so unrelated takeovers in other
  countries do not contend.

**Negative**

- The exclusivity is explicit rather than declarative. A future workflow that reads-then-writes tenures
  must remember to take the same locks; the constraints still prevent corruption, but the refusal would
  be a 500 rather than a 409. This is the cost of the pattern and the reason it is written down here
  rather than inferred from the code.
- Two lock acquisitions per takeover are two extra round trips.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `SERIALIZABLE` with the snapshot problem tolerated, mapping the unique violation to `CLUB_ALREADY_CLAIMED` | Works, but only by treating a constraint violation as control flow, and it leaves the capacity count stale: the tier that just filled would be measured as having room, so `PYR-2`'s request could be skipped for that takeover. |
| `SERIALIZABLE` with automatic retry on `40001` | A retry loop with backoff and a bound, to work around a snapshot taken one statement too early. More code than the lock it replaces, and harder to reason about under load. |
| No locks, constraints only | The unfriendly outcome above, plus the missed capacity evaluation. |
| A row lock on the country row (`SELECT ... FOR UPDATE`) | There is no per-country row that every claim already touches, and locking the country row would serialise claims against unrelated country-level writes. The advisory lock names exactly what is being serialised. |
| An in-process lock | The API and the worker are separate processes, and a single-process lock would not survive a second instance. |
