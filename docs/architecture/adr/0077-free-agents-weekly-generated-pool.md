# ADR-0077: Free agents are a weekly generated pool sold through the timed auction

- **Status:** Accepted
- **Date:** 2026-10-20
- **Stage:** Free agents plan (`create-a-plan-for-composed-falcon.md`), Milestones 1-6
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0024](0024-market-scouting-and-timed-auctions.md), [ADR-0076](0076-potential-drawn-before-skills.md), game-rules `CON-6`, `CON-7`, `CON-9`, `CON-12`, `TRF-1`…`TRF-15`, `FA-1`…`FA-8`

## Context

The transfer page only showed players other clubs had listed, and every player belonged to a club, so a new or weak team had nowhere cheap to strengthen. `Player.Status` already had `FreeAgent`, set when a contract expired at rollover, but nothing read it and nothing could sign those players. `CON-7` had deferred free-agent signing as post-MVP "if the market needs it".

## Decision

**1. A weekly pool.** Every Monday at 12:00 UTC (the daily auction window) a job generates `round(10% × the players under contract)` free agents, plus every player released since the previous batch, and lists them with no seller until the next Monday at 12:00. A listing with a leading bid signs the player at the close, even if the bid came on the last day; a listing with none expires and the player leaves the page. The batch is one row per world and week (`market.free_agent_batches`), unique on the Monday, so a retried or late job lists nothing twice.

**2. The existing auction, with no seller.** `transfer_listings.seller_club_id` and `transfer_outcomes.seller_club_id` / `old_contract_id` become nullable; a listing is a club's or a batch's, enforced by a check. Settlement posts only the buyer's payment, so the fee leaves the game. There is no second market and no new bidding rule: the same reservation, increment and serializable revalidation apply, with three additions: only human managers may bid, a bidder at 27 players is refused at bid time, and a resolution that lands while the season is in rollover throws and is retried (700 attempts, against 8 for a club listing) until the next season is active.

**3. Generated players are cheap depth, with a rare hidden gem.** Shown ability 6 to 10, age 20 to 32, hidden potential at most 11; 1% are gems aged 17 to 21 with potential 13 to 16 (`free-agent-gen-v1`, built on the same `Draft` path as the world generator, whose golden digest did not move). Price and wage are computed with potential set equal to ability at the lowest active tier, and the contract length and squad status depend on age alone, so nothing visible or priced points at a gem. AI clubs never see free agents, because they read hidden potential and would find every gem.

**4. Releasing a player.** A manager may release one of their own players now for half the weekly wages left on the contract, paid at once as a `release_payout` ledger entry (`CON-12`). It joins the next Monday's batch like an AI club's expired player.

**5. Released players are not limited to the generated band, and they are priced in full.** They keep their real ability and age, so the pool has two populations: generated depth at 6 to 10 and former club players at any ability. A generated player's minimum fee is 25% of the valuation of his ability; a released player's is 100% of his valuation with his real potential (`FreeAgentTerms.CalculateReleased`), the figure a club listing's asking price is built from, so the cut-price rate does not buy a former professional. His wage is still priced on ability alone, and a generated gem is still priced as 6 to 10 depth, so the price cannot give a gem away.

**6. The rollover keeps squads at the market's target.** `AiContractTargetSquadSize` goes from 21 to 23, the number `AiMarketTargetSquadSize` already was and the constant's own comment already claimed it matched (`ai-contract-v2`). At 21 a generated squad of 24 lost three players at its first rollover and, with the AI barred from the pool, stayed small; at 23 it loses at most one.

## Consequences

- **Measured** (one scratch world, 108 clubs): the first batch is 259 players for 2,592 under contract. With the target at 21 and released players at 25%, one rollover of unmanaged clubs released 318 players (ability 4 to 16, 64 with potential 13 or more) at fees of 2,500 to 13,789, a bigger and stronger pool than the 227 generated. After decisions 5 and 6 the same rollover released 108, one a club, beside 248 generated: the weakest expiring player of each club (ability 4 to 11, mean 7.5, mostly 18 to 24), at fees of 5,477 to 31,853 (median 16,074).
- The lower release count applies to every later rollover and to worlds seeded before this change, from their next rollover.
- Gems grow only where daily training progression is on; it is off in the base worker configuration and on only in `appsettings.Development.json`.
- Every signing moves cash and the wage bill, so the market-health measurements of `TRF-12` need re-running once real managers use the pool.
- A leading bid that fails the close (cash spent elsewhere, squad grown past 26) expires like a club listing's; the bid-time squad check keeps this rare.
- **Open, offered and not done:** a "do not renew" action for the end of a contract; AI clubs bidding on generated depth only; a calendar entry for the Monday opening; re-listing a released player nobody bid on (he stays a free agent with no listing).
