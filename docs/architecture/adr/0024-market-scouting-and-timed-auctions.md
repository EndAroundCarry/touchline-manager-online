# ADR-0024: The market is a `market` module; auctions resolve at a daily window under a reservation held in the ledger

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 10
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0009](0009-time-identity-and-concurrency.md), [ADR-0011](0011-squad-schema-and-hidden-player-values.md), [ADR-0016](0016-non-production-matchday-trigger.md), [ADR-0022](0022-append-only-club-ledger.md), [ADR-0023](0023-income-expenses-and-gate-revenue.md), master plan §6.7, §7.2, §10.6, §16 Stage 10, game rules §14, `SCT-1`…`SCT-3`, `TRF-1`…`TRF-15`, `INT-1`…`INT-6`

## Context

Stage 10 is the transfer market: scouting, shortlists, listings, bids, and the resolution that moves a player.
The data model (master plan §6.7) and the rules (game-rules §14) already exist, but several decisions were left
to implementation.

**1. Where the tables live.** The master plan contradicts itself about `shortlists` (§5.2 puts them in `market`,
§6.5 writes `squad.shortlists`). `data-model.md` §7 resolves this to `market.shortlists`, because a module writes
only its own schema (`MOD-1`) and the shortlist API surface is the market's.

**2. When a listing resolves.** `TRF-2` requires fixed daily resolution windows with at least 48 hours of
exposure; `TRF-3` forbids resolving within six hours before a matchday kickoff. Both were open deferred items
("Minimum bid increment value", "auction resolution windows") with no value chosen.

**3. How a bid commits money without spending it.** `FIN-10` says a club cannot bid beyond its cash *after
existing reservations*, and `TRF-7` says the leading bid's funds are reserved while the former leader's
reservation is released. ADR-0022 rejected a separate reservation table: a reservation is a ledger posting.

**4. What happens in the resolution transaction.** `TRF-9` requires revalidating account status, balance
reservation, squad limits, seller minimum squad, active contracts, and listing status in one serializable
transaction; `TRF-10` requires the payment, the credit, the registration move, and both contracts to be atomic.

## Decision

**1. The market is a module of its own**, with the `market` schema and `market.shortlists`,
`market.transfer_listings`, `market.transfer_bids`, and `market.transfer_outcomes` (master plan §6.7). Its
tables are written only by market use cases; the transfer move reaches the squad and finance modules through
their ports, staged in one unit of work (`MOD-1`, `MOD-2`).

**2. The rule set becomes `world-rules-v7`.** It gains `AuctionResolutionUtc` (12:00 UTC), the 48-hour minimum
exposure (`TRF-2`), the six-hour pre-kickoff blackout (`TRF-3`), the minimum bid increment (`TRF-5`), and the
shortlist note bound (`SCT-3`). The daily window sits outside the blackout by construction, and the windows are
computed by a pure `AuctionWindows` helper that reads no clock.

**3. A reservation is a ledger posting, not a row.** The leading bid reserves its amount through
`BidReservation` (`reserved +amount`, cash untouched); outbidding or cancelling posts `ReservationRelease`
(`reserved -amount`); settlement posts `TransferPayment` (cash and reserved both `-fee`, consuming the
reservation) and `TransferProceeds` (`cash +fee`). The correlation keys include the amount, so a raise's
re-reservation and a later release are distinct entries while a retry of either still collides with its first
(`FIN-17`, `TRF-7`).

**4. Resolution is one serializable transaction driven by a durable job.** The materialiser enqueues
`market.resolve-auction` with the business key `listing:{id}:resolve`; the handler calls `ResolveListing`, which
runs at `SERIALIZABLE`, chooses the winner by highest amount then lowest database-assigned `bid_sequence`
(`TRF-8`), revalidates it (`TRF-9`), and settles it: payment, credit, the seller's contract closed as
`transferred`, its registration ended, and the buyer's contract and registration created (`TRF-10`). A valid
bid that fails revalidation is invalidated and the listing expires, because bids ascend and only the leader
holds a reservation (`TRF-11`); every outcome is audited.

**5. Idempotency is structural.** A listing and a bid store the idempotency key that created them, backed by a
filtered unique index, so a retried command replays the row it already wrote; a reused key with a different
request is refused (`T-4`). `unique (listing_id)` on `transfer_outcomes` makes a retried resolution a no-op.

## Consequences

**Positive**

- The ledger stays the single path a balance moves by (`FIN-12`, `FIN-18`); a reservation is visible in the
  ledger a manager already reads, and no second balance mechanism exists.
- The daily window and the blackout are rule-set values, so a balancing change is a version bump rather than a
  hunt for a constant (`RULE-1`, `RULE-3`).
- The resolution's serializable transaction and destination uniqueness mean a retried job cannot double-charge
  or double-transfer, which is what the threat model's auction row requires.
- Scouting, listings, and bids share one refusal vocabulary and one keyset-cursor codec, so the module reads
  like the rest of the product.

**Negative**

- A bid writes a ledger entry, so the bid path carries the account rollback semantics of the finance module; a
  bug there is a money bug, not a listing bug.
- Only the leading bid is ever active, so on a revalidation failure the listing expires rather than falling to
  the next bid. `TRF-11` permits this ("cancelled or skipped per documented rules") but it is a weaker reading
  than re-reserving an earlier bidder, and it is recorded here rather than silently applied.
- The AI transfer market (`TRF-12`) is deferred to a follow-up milestone, so for now only human managers list
  and bid. `market.ai_market_decisions` does not exist yet.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Model reservations as their own table | ADR-0022 already rejected it: a reservation outside the ledger would be a balance moved by a path `FIN-11` forbids. |
| Resolve listings on the client's schedule, or with an anti-sniping extension | `TRF-2` fixes a daily window and `TRF-13` puts anti-sniping extensions out of MVP scope. |
| Resolve with ordinary row locks instead of `SERIALIZABLE` | `TRF-9` names a serializable transaction, and the resolution reads squad counts and accounts that must not change under it. |
| Store a database-assigned `bid_sequence` only implicitly via UUIDv7 | `TRF-8` and `T-5` want a stored sequence so a tie resolves to the commit order rather than to arrival time; the identity column makes that explicit. |
| Give the seller an explicit `resolution_job_id` column | The business key `listing:{id}:resolve` already identifies the job deterministically, so the column would be a second statement of the same fact (ADR-0003). |
