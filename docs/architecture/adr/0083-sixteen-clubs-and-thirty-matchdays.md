# ADR-0083: Sixteen clubs and thirty matchdays

- **Status:** Accepted
- **Amended by:** [ADR-0085](0085-fair-seeded-squads-and-two-tiers-from-the-first-season.md): a seeded world is nine countries of two tiers of 16, **288 clubs**, not six countries of 16.
- **Amended by:** [ADR-0087](0087-promotion-play-off.md): promotion and relegation are no longer three up and three down: the first two go up, the last two go down, and a single-match play-off decides a third place (two or three clubs move each way).
- **Date:** 2026-10-23
- **Stage:** League size plan (`create-a-plan-based-luminous-cosmos.md`), Milestones 1-8
- **Related:** [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0015](0015-compressed-test-clock.md), [ADR-0031](0031-season-rollover-state-machine.md), [ADR-0049](0049-non-production-stepped-game-clock.md), [ADR-0057](0057-training-programmes-age-curve-and-hidden-aptitude.md), [ADR-0078](0078-fan-base-satisfaction-and-the-crowd.md), [ADR-0080](0080-club-structures-snack-bar-and-merchandise-shop.md), [ADR-0081](0081-every-attribute-develops-to-a-limit-set-by-potential.md), game-rules `WORLD-4`, `PYR-2`, `PYR-4`, `PYR-8`, `CAL-1`, `CAL-6`, `CAL-9`, `MAT-7`, `TRN-10`, `TRN-16`, `CON-3`, `FAN-5`, `FAN-6`, `FAN-8`

## Context

Every division had 18 clubs and a season of 34 matchdays. The product wants a smaller table: **16 clubs and 30 matchdays**, with promotion and relegation unchanged at three up and three down. (A play-off will replace the straight line later, in its own plan.)

Most server code reads two constants, `ClubsPerDivision` and `MatchdaysPerSeason`. The risk was in everything that quietly assumed 18 and 34: literals, bands of table places, the economy sandbox, check constraints, copy. The goal was the same game on a smaller table:

- the season's money stays the same;
- player development per season stays the same;
- fan growth per season stays the same;
- objectives and standing steps still make sense on 16 places.

The calendar had a second, older problem. A season was 34 matchdays and a seven-day rollover, 84 days, so the start weekday rotated. The economy assumed 11⅓ weekly runs and the training constants assumed 86 days, while the real calendar had 12 runs and 84 days. Changing the size was the moment to make those agree.

## Decision

**1. The shape (`WORLD-4`, `CAL-1`).** `ClubsPerDivision` is **16** and `MatchdaysPerSeason` **30**, so a round has 8 fixtures, a club plays 15 home matches and a division-season holds 240 fixtures. A seeded world is six countries of 16, **96 clubs**. Promotion and relegation stay at three and three. The rule set becomes `world-rules-v18`.

**2. A fixed 11-week season cycle (`CAL-6`).** New `WorldRuleSet.SeasonCycleWeeks = 11`. The next season starts on the same weekday, **77 days** after the first matchday of the last one. Thirty matchdays on Tuesday, Thursday and Sunday fill exactly ten weeks (the last is a Sunday, day 68), so the rollover lasts **nine days**; `RolloverDays` (7) stays as the least it may last, and `SeasonCalendar.Window` throws if a cycle would leave less. The start weekday never rotates. A season is then exactly **11 weekly finance runs, 11 free-agent batches, 11 fan reviews and 77 training days**, and `EconomyRuleSet.WeeklyRunsPerSeason` and the training age curve read the cycle instead of a formula that gave 11⅓ and 86.

**3. Check constraints stay permissive for history; the domain guards are exact.** `LargestDivisionEver` (18) and `LongestSeasonEver` (34) are read only by the database check constraints, so a past 18-club season stays valid data. One migration, `SixteenClubsThirtyMatchdays`, changes `ck_divisions_capacity` to `capacity in (16, 18)`. The standings-rank, matchday-round, sponsor target-place and standing-event place constraints keep their 18 and 34 ranges, built from those two constants so the EF model does not move. The guards in `Division`, `Matchday`, provisioning, the rollover, the world generator and the seeder enforce the exact current size.

**4. Existing worlds are not migrated (`RULE-4`).** An 18-club world cannot roll over under v18: the rollover refuses a division of the wrong size. That is accepted and documented in the runbook. The dev world is reseeded (`npm run migrate`, `npm run seed`).

**5. Training and renewal (`TRN-10`, `TRN-16`, `CON-3`).** The yearly decline per day follows the 77 days. `BaseDailyMilli` goes from 400 to **436** (400 × 84 / 77, since 84 were the real days a season had), so a player develops as far in a season as before; `training-v5`. The old 86-day assumption flattered `training-v4` by about 2%. The renewal quote's playing-time bands go from 20 and 10 appearances to **18 and 9**, the same shares of a season.

**6. Money (`EconomyRuleSet`).** Season totals (TV money, sponsor, wages, operating cost) are unchanged and divide by 30 per matchday and by 11 per weekly run.

- A club now has 15 home matches, not 17. To take the same season's income, ticket prices and the snack bar's spend per spectator go up **34/30**, and the shop's spend per fan per week **34/33**, since the tuned season assumed 11⅓ runs and now has 11. Build and upgrade costs stay, because season takings stay.
- The prize table keeps its shape on 16 places: a straight line from 16th to 3rd, 2nd = 3rd × 1.15, champion = 2nd × 1.20, so a division shares **140.42** shares (176.48 on 18). The tier-1 share goes from 300,000 to **340,000**, which keeps the average club's prize at about 2.98 million (was 2.94) and the champion's at 6.57 million (was 6.62).
- **Objectives** read their place from the table size: win the league (1st), top three, top six, **top half = N / 2 (8th)**, **eleventh = N − 5 (11th, replacing the twelfth)**, **stay up = N − relegated (13th)**. The code `twelfth` becomes `eleventh` (migration `EleventhPlaceObjective` also rewrites old rows). Exceeded-by stays at three places.
- **Standing steps**: champion +6, top three +4, top six +2, the bottom six places (above `N − 6`) −2 unless the club went down, relegated −5. The structure is the one that 13th to 15th had on 18.

**7. Fans (`FAN-5`, `FAN-6`, `FAN-8`; `fan-rules-v2`).** The per-match joins and leaves are the 34-match league's amounts times 34/30 (680 / 510 / 368 / 113 and 340 / 283, random −17 to +57), so a 30-match season grows the fans as fast as a 34-match one. `FirstSeasonMatches` reads the season length. `GapLimitPlaces` goes from 6 to **5**, a third of the table. **`NeutralOffsetBp` is 90, not 88 × 34/30 = 99**: it is a per-match balance (the mean mood shift over a 16-side league, measured by a test), so scaling it with the season would make a club that exactly meets expectations drift upward. The mood constants are not scaled either; the weekly review (11 a season) follows `played / seasonGames` and needs nothing.

**8. Supply.** Free-agent batches go from 12 to 11 a season and the daily AI market and auction passes from 84 to 77, about 8% fewer. The pool is 10% of the contracted players a batch, so it follows the club count; the pool per club per season fell about 8%, under the 10% that would have asked for a change to `FreeAgentShareBp`, so none was made.

**9. Experience stays per match (`EXP-2`).** 50 bp a match, a level about 40 matches. A regular player gains about 12% less in a season. No `experience` version bump. Cards, injuries and bans are counted in fixtures and did not change.

**10. Text and tests follow the constants.** Web copy that stated 18 or 34 reads the server (`clubsInLowestTier`, `matchdaysInSeason`) or says 16 and 30; the sponsor objective label is place-free ("Finish mid-table or better") and the page appends the server's place. Tests read the constants where they can. The load suite models **96 managers and 288 virtual users**, and the restore-drill check expects `capacity / 2` fixtures in a published round.

## What changes in the written rules

- **`WORLD-4`, `PYR-2`, `PYR-4`, `PYR-8`.** Sixteen clubs, sixteen legal squads, a 30-round schedule.
- **`CAL-1`, `CAL-6`, `CAL-9`, `MAT-7`.** Thirty matchdays; a fixed 11-week cycle and a nine-day rollover; thirty fixtures per club; a round of eight fixtures.
- **`TRN-10`, `TRN-16`.** Budget 436, 77 progression days.
- **`CON-3`.** The renewal playing-time bands.
- **`FAN-5`, `FAN-6`, `FAN-8`, `FAN-4`'s version, the constants in section 18.** As in decision 7.
- **Master plan, glossary, data model, context, modules, traceability, test strategy, load-testing and runbook** carry the new numbers. The Stage 0 review is kept as written, with a note.

## Measured

The economy sandbox (`econ`, 8 seeds × 10 seasons, 33 to 40 seconds a run), 16/30 against the 18/34 reference:

| Run | Season-1 income, tiers 1 to 4 | Season-10 tier-1 ground | Notes |
|---|---|---|---|
| 18/34 reference | 1.353B / 541M / 221M / 92M (minor units) | 37.3k | |
| 16/30 with the old money (baseline) | 1.277B / 514M / 212M / 87M (-3% to -5%) | 34.0k | fans grew about 12% slower; target 1 and 8 missed |
| After the money change | 1.329B / 534M / 222M / 91M (within 2%) | | target 1 and 8 hit again |
| After the fan change | unchanged | 38.2k | richest five 149M (reference 133M) |

- Targets 1 to 5, 7, 8 and 10 to 13 hit in both fresh seed sets of the tuning run. Wages 63 / 57-58 / 57-58 / 59-60% of season-1 income, prize share 17-22% (target 13: 15-25%), buildings 6.6-7.3%, relegated clubs stay out of administration.
- **Target 6 (elite income) is missed**, as it was on 18/34: the richest five earn 1.5-1.6 times the median against the 2.5 asked for (reference 1.6), though 3.1 to 3.5 clubs a league could pay 100M by season 10 and the first did in season 7 or 8 (reference 8). Per the finance-rework decision, target 6 is judged on human-managed clubs.
- **Target 9 (market and emergency grants) missed once**: one AI tier-4 club-season of 5,120 needed the emergency grant in one seed set (the 18/34 reference had 2 such rows, the baseline 5). Fee drift +1% and 0%.
- A club that beats its expectations fills the 75,000-place ground in **12.4 seasons** (12.65 on 18/34).
- Training probe: average-aptitude youths complete at median age 23, own-position skill at 24 is about 14.0, veterans from 28 to 35 lose 0.58 (target about 0.56).

## Alternatives considered

| Alternative | Why not |
|---|---|
| Keep 18 clubs and 34 matchdays | The product asked for a smaller table. |
| 30 matchdays with the 7-day rollover (a 75-day cycle) | The next season would start on a Sunday and the start weekday would rotate; a season would hold 10⅔ weekly runs. |
| Migrate existing worlds | `RULE-4`, and the stored matchday ordinals, per-matchday sponsor deals and club-identity ordinals would all need rewriting for a dev world that can be reseeded. |
| Scale experience per season | Experience is per match by design (`EXP-2`); the 12% is the cost of a shorter season. |
| Scale `NeutralOffsetBp` with the season | It is a per-match balance, not a season total (decision 7). |
| Pay the shop for the real 12 runs | The calendar now has 11 runs; against today's real 12, actual shop takings fall about 8%, noted and not corrected. |

## Consequences

- A world seeded before this change must be reseeded; the first rollover of an 18-club world fails at preflight.
- Left for old worlds and not fixed: the stored `MatchdayOrdinal` (season × 34) in `club_accounts.state_since_matchday`, sponsor deals stored per matchday (÷ 34), the club-identity ordinals `(tier - 1) × clubCount`, and the season version carried over at rollover.
- The play-off plan inherits objectives derived from the constants and a promotion rule that is still a straight line.
- Finance-rework milestone 8 (the real-engine run, its docs and the Economy v2 ADR) now runs on this shape; that plan's references to 1/34 and 18 places are superseded.
