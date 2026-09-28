# ADR-0025: The AI transfer market is a pure versioned policy routed through the human write path

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 10 (AI market milestone)
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0004](0004-deterministic-match-engine.md), [ADR-0018](0018-ai-club-policy.md), [ADR-0022](0022-append-only-club-ledger.md), [ADR-0024](0024-market-scouting-and-timed-auctions.md), master plan §6.7, §7.2, §16 Stage 10, game rules §14, `TRF-12`, `INS-12`, `FIN-10`, `MAT-11`

## Context

Stage 10 shipped the human market and deferred the AI side of it: ADR-0024 records that "the AI transfer
market (`TRF-12`) is deferred to a follow-up milestone, so for now only human managers list and bid, and
`market.ai_market_decisions` does not exist yet." The stage's own exit criterion — *"AI uses no privileged
finance and produces a healthy measured market"* — is unmet until AI clubs list and bid.

`TRF-12` asks that AI clubs "list surplus players and bid within valuation, positional need, squad size,
and budget bands", and `INS-12` asks that AI and humans be validated by the identical rule set with no
bypass. The pieces to reuse already exist: the pure, versioned AI-worker pattern (`AiClubPolicy` →
`EvaluateAiClubs` → a daily scheduler, ADR-0018), the market's ledger-backed reservation and its
trusted-listings writers, and the squad-legality and affordability rules the human commands call.

Four decisions were left open.

**1. How the AI reaches the market.** `CreateListing` and `PlaceBid` are `userId`-scoped through
`ResolveOwnedClub`, so an AI club — which has no account — has no path in. A second, AI-only write path
would be a second place money moves and a second place `TRF-14`, `SQ-2`, and `FIN-10` are enforced: exactly
the drift `INS-12` and ADR-0022 forbid.

**2. What a player is worth.** `TRF-12` names "valuation" but no scale. A new money scale would let a fee,
a wage, and a renewal drift apart.

**3. Where the market's demand comes from.** A freshly generated world has balanced squads at the generator
quotas, so no club has a positional need — and nothing changes a squad until a transfer settles. A pure
need-based rule would list players nobody ever bid for, and the market would never start.

**4. How a retried pass stays idempotent.** The worker's at-least-once delivery means an evaluation can run
again. It must not list a second player, place a second bid, or reserve a second time.

## Decision

**1. The AI acts through the human write path.** The listing and bid cores are extracted into
`ListingWriter`/`BidWriter` (and their `IListingWriter`/`IBidWriter` ports), which stage a listing or a bid,
its ledger postings, its notification, and its audit row, and never save. `CreateListing`/`PlaceBid` call
them as a user actor; `EvaluateAiMarket` calls them as a service actor. Everything else is identical, so
`INS-12` holds by the code path rather than by intention (`MOD-2`).

**2. Valuation is a versioned multiple of the wage scale** (`player-valuation-v1`). A player is worth
`PlayerValuationWeeksOfWage` weeks of the wage the rule set already prices, moved by bounded age and
potential factors, so a squad, a renewal, and a fee are priced by one family of rules. The value is class C2
and never serialized (`MAT-11`); only a listing's asking price, which is derived from it, is shown.

**3. The policy is pure and versioned** (`ai-market-v1`), seeded from the club identity alone, and consumes
a frozen snapshot of the club and of the market open before the day began. Supply is a squad above
`AiMarketTargetSquadSize` or a family above its generator quota; demand is a positional need **or** a listed
player who improves on the club's weakest in that family. The upgrade clause is what gives the market its
first buyer without a prior sale. Both lists are bounded per pass, and a club never lists while it already
has a listing open.

**4. Idempotency is structural and daily.** Each decision carries a deterministic key
(`ai-l:{club}:{player}:{yyyyMMdd}` / `ai-b:{club}:{listing}:{yyyyMMdd}`) matched against the market tables'
existing unique indexes, and the market the pass bids on is bounded to listings opened before the current
UTC day. A retried pass therefore re-derives the same decisions and replays them; the whole pass commits in
one transaction.

**5. Every decision is recorded** in `market.ai_market_decisions` (master plan §6.7): the club, the instant,
the action, the player, the listing or bid it produced, the digest of the inputs the policy read, and the
policy version. It is the audit trail a later collusion review or support answer reads.

## Consequences

**Positive**

- `INS-12` is enforced where it matters: an AI listing or bid is refused by the same eligibility,
  squad-legality, and affordability rules a human's is, because it is the same code.
- The ledger stays the only path a balance moves by (`FIN-12`, `FIN-18`); an AI bid reserves through
  `LedgerPostings.BidReservation` exactly as a human's does, and a settlement pays and credits through the
  same resolution.
- The policy reads no clock, database, culture, or global random source, so a world can be replayed and a
  balancing change is a named version rather than a silent constant.
- The daily idempotency keys and the day-scoped market make an at-least-once job safe without a lock.

**Negative**

- The valuation and the band constants are balancing values with no long-run calibration yet; the measured
  market is bounded and money-conserving, but "healthy" is demonstrated by the milestone's tests rather than
  proven over seasons. Calibration is the multi-season simulation work Stages 12 and 15 own.
- The market bootstraps over two passes (a day of supply, then bidding on it), because the pass bids on what
  existed before the day began. A world's first day has listings and no bids by construction.
- A club that is over its target trims one player at a time and waits for that listing to resolve before
  listing another, so a bloated squad clears slowly. That is the price of not flooding the market.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Give the AI its own listing/bid writer | A second money path and a second place the eligibility and legality rules live — the drift ADR-0022 and `INS-12` forbid. |
| Invent a separate valuation scale | A fee, a wage, and a renewal would drift apart; deriving the value from the wage scale keeps them one family of rules. |
| Demand from positional need alone | A freshly seeded world has no needs, so nothing would ever be bid for and no transfer would settle. The upgrade clause is what starts the market. |
| Let a pass bid on listings it created earlier in the same pass | A same-day retry would then place bids the first pass did not, so the job would not be idempotent. Bounding the market to listings opened before the day began makes the pass a function of the day. |
| A separate `ai_market` scheduler with its own cadence | The daily UTC-day row is the pattern ADR-0018 already set for the AI worker, and one row per day is the deadline the plan's job model expects (`ADR-0003`). |
