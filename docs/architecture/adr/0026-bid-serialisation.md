# ADR-0026: Bids on one listing are serialised with a transaction-scoped advisory lock

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 10 (hardening)
- **Related:** [ADR-0010](0010-club-takeover-serialisation.md), [ADR-0024](0024-market-scouting-and-timed-auctions.md), game rules `TRF-5`, `TRF-6`, `TRF-7`, `FIN-10`, master plan §7.7

## Context

A transfer listing's bids ascend, and only the **leader** holds a reservation (`TRF-7`). Every bid therefore
makes the same read-then-write decision: read the current leading bid, then either raise this club's own
leading bid, displace the leader and become the new one, or refuse.

The write path is shared by a manager's `PlaceBid` command and the AI's daily evaluation (`INS-12`,
`TRF-12`), and it is written as one unit of work: it stages the bid, the displaced reservation's release,
the new reservation, the outbid notification, and the audit row, and the caller commits.

The database's only structural guard was `ux_transfer_bids_leading_listing_club` — one leading bid per
listing **per club** (`TRF-6`). It does not stop two *different* clubs from both reading no leader at the
same moment and both inserting a leading bid. The outcome is two leading rows, two reservations standing
at once, and a listing whose resolution can settle only one of them: the other club's funds stay reserved
with no path that ever releases them. That is a competitive-integrity defect of the same family as the
outbid bug ADR-0025's milestone fixed, and it is invisible to a test that places one bid at a time.

## Decision

**A bid takes a listing-scoped, transaction-scoped advisory lock before it reads the current leader.**

- The key is `AdvisoryLockKey.Listing(listingId)`, implemented over `pg_advisory_xact_lock` like the
  existing country, manager, and matchday keys (`ADR-0010`, `PYR-3`).
- The lock is per listing, not per country or per manager, so bids on different listings still run in
  parallel and unrelated market traffic does not contend.
- The lock lives until the transaction ends, so the two callers — `PlaceBid` and `EvaluateAiMarket` — each
  run their bid writes inside one explicit `READ COMMITTED` transaction. The second arrival reads the
  first's now-committed leading bid and outbids it properly, or raises its own, or is refused by the same
  floor and affordability rules as before.
- The caller must have begun a transaction; `PostgresAdvisoryLock` already refuses a lock taken outside one,
  because such a lock is released immediately and is the same as not taking it.

The existing partial unique index is unchanged: it remains the per-club backstop `TRF-6` describes, and the
lock is what makes the listing-level decision correct.

`ResolveListing` is deliberately untouched: it already runs `SERIALIZABLE` and reads the listing and its
bids inside that transaction (`TRF-9`, ADR-0010), so a bid cannot slip in after the winner is chosen.

## Consequences

**Positive**

- Two clubs bidding on one listing at the same instant now leave exactly one leader and exactly one
  reservation, and the displaced club's funds are released as `TRF-7` requires.
- The decision "who leads now" is made from committed state rather than from a read that a peer can
  invalidate before the write.
- The lock scope is one listing, so the common case — many listings bid on at once — is unaffected.

**Negative**

- Two callers must remember to open a transaction before bidding. This is the same cost ADR-0010 accepts
  for takeover, and the lock provider's guard turns the mistake into a named failure rather than a silent
  race.
- A bid that arrives while another bid on the *same* listing is uncommitted waits for it. The wait is one
  bid's worth of work.
- The AI's whole pass now runs in one transaction that holds a lock per listing it bids on until the pass
  commits. A single pass is bounded (a handful of bids per club) and cannot run concurrently with itself,
  so the held-lock set stays small; a human bidding on a locked listing waits for the pass, which is short.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A partial unique index on `(listing_id) where status = 'leading'` | Its correctness depends on the outbid `UPDATE` being issued before the new leading `INSERT`, which EF Core does not guarantee; PostgreSQL cannot make a partial unique index deferrable, so the ordering cannot be deferred to commit either. |
| `SERIALIZABLE` for the bid path with retry on `40001` | Bids are a manager-facing command, so a serialization failure would surface as a retry or an error to a human, and the AI pass would lose all its other decisions. The lock names exactly the one decision that races. |
| A row lock (`SELECT ... FOR UPDATE`) on the listing | Equivalent in effect, but it needs a dedicated locking read the repository does not have, and the advisory lock is the mechanism the codebase already uses for exactly this kind of serialisation. |
| Accept two leaders and release the loser's reservation at resolution | Leaves two clubs believing they lead an open listing, and leaves a club's funds reserved for as long as the listing runs. It patches the symptom rather than the race. |
| An in-process lock | The API and the worker are separate processes; an in-process lock would not serialise a human bid against the AI pass. |
