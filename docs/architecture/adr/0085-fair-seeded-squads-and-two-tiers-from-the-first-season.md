# ADR-0085: Fair seeded squads and two tiers from the first season

- **Status:** Accepted
- **Date:** 2026-10-23
- **Stage:** Fair squads plan (`create-a-plan-that-cheeky-dongarra.md`), Milestones 1-5
- **Related:** [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0030](0030-provisioning-execution-and-bootstrap-provenance.md), [ADR-0076](0076-potential-drawn-before-skills.md), [ADR-0077](0077-free-agents-weekly-generated-pool.md), [ADR-0082](0082-community-forum.md), [ADR-0083](0083-sixteen-clubs-and-thirty-matchdays.md), [ADR-0086](0086-matchday-publications-take-turns.md), game-rules `WORLD-2`, `WORLD-5`, `WORLD-8`, `WORLD-11`, `PYR-1`, `PYR-2`, `TRN-18`, `FA-6`, `FIC-4`, `FIC-5`

## Context

A fresh world was six countries, each with one tier of 16 AI clubs, and every player was drawn at random around a tier mean. So some clubs opened noticeably stronger than others (the best eleven of the strongest club was about 20% stronger than the weakest's), and with one tier nobody could be promoted or relegated until humans had filled all 16 clubs and a second tier had been generated.

The product wanted three things:

- **Fair squads.** Every new club of a tier opens with an equally skilled squad. A difference of 5 to 10% between clubs is acceptable, for a little diversity.
- **Tier 2 from the first season**, so promotion and relegation work from season 1 and a new manager can pick from two divisions.
- **Three more countries**, Belgium, Portugal and the Netherlands, each with a forum, a tier 1 and a tier 2.

"Rating" below is the displayed ability: the rounded mean of a player's 28 attributes, the number on the transfer and free-agent lists.

## Decision

**1. A squad is a template, not a draw (`WORLD-11`).** `SquadTemplate` gives a rating to each of the 24 slots of `PlayerGenerator.Composition`. The best players sit across the spine of the team (goalkeeper, centre back, central midfielder, striker), so every first eleven is comparable.

- **Tier 1 is rated 8 to 12**: three 12s, four 11s, five 10s, six 9s, six 8s (mean 9.67).
- **Tier 2 is rated 8 to 10**: three 10s, nine 9s, twelve 8s (mean 8.63).
- **A deeper tier** is tier 2's mix shifted down one rating per tier below the second, never below 5: tier 3 is 7 to 9, tier 4 is 6 to 8.

`PlayerGenerator.GenerateTemplateSquad` drafts every slot through `DraftOverride` with the slot's age, potential and rating, and `ShiftToAbility` makes the rounded mean land exactly on the rating. It needs no wage-band sizing loop: with fixed ratings the bill is fixed. `GenerateSquad`, `GenerateSizedSquad` and `GenerateYouth` are unchanged (tools and tests use them), and so are free agents.

**2. Diversity is seeded per club, and it is balanced.** A coin decides whether the club's left and right sides are mirrored (full backs, wide midfielders, wingers). Then four slots move one rating, **two up and two down**, always inside the tier's band (8 to 12 in tier 1, 8 to 10 in tier 2). A club's total ability is therefore exactly the template's and only its shape moves. (The plan allowed random-sign nudges and a total within ±4; they gave a best-eleven spread of 1.074 and 1.087, over the 1.07 cap, so the nudges were balanced.) The generator's per-skill jitter and position bonuses add the rest.

`DraftOverride.LandOnRating` lifts each player's exact 28-attribute mean to at least his rating, leaving the rounded ability unchanged. The AI picks its eleven by the integer-truncated mean (`AiClubPolicy.ChooseLineup`), so without it a 9 could tie an 8 and a random player id decided the eleven. Only the template and replacement paths use it, so free-agent and youth output is unchanged.

The test cap on the best-eleven strength of a tier (`TeamStrength.OfSquad`, maximum over minimum) is **1.07**, under the product's ceiling of 1.10; it measures about 1.05 in each tier.

**3. Potential: room to grow, and exactly one gem and three talents (`TRN-18`).** Ages are drawn as before. A normal player's potential is `rating + ceil(max(0, 24 − age) / 2)`, so a young player keeps some room. Each club then has **exactly one gem** (potential above the rating by 6 or 7) and **three talents** (by 3 to 5), picked by seed among players aged 23 or younger, or the youngest if there are not enough. A talent or gem is never lower than the normal potential plus 1 (talent) or 2 (gem), so the pick is never a no-op for a young player. Potential is capped at 20. The 80/15/5 draw of ADR-0076 stays for the youth intake and the legacy generators.

**4. Replacements and clean starts follow the template.** An emergency replacement (`SQ-8`) is drawn from the bottom of the tier's band: 8 or 9 in tier 1, the band's low (8, then 7, 6…) below it. A clean-start takeover builds its squad from the tier template, so it is as fair as a seeded one.

**5. Two tiers are seeded (`WORLD-5`).** `WorldRuleSet.SeededTiers = 2`. The seeder builds tier 1 and tier 2 in every country, both active, with the same season and calendar (`WORLD-10`), through the generic `WorldGenerator.BuildTier`. A tier provisioned later (`PYR-4`) uses the same template, so tier 3 is 7 to 9. With the three countries, a world is **9 countries × 2 tiers × 16 clubs = 18 divisions and 288 clubs**, 6,912 players.

**6. A manager may take any club of any active tier (`WORLD-8`).** Before, only the country's lowest active tier was claimable. Now a club is claimable if its division is active in its own country and it has no human. `CountryCapacity` measures the whole pyramid: `ClubsInCountry` and `HumanOccupiedClubs` over every active tier, and the pyramid grows (`PYR-2`) only when **the whole country is full**. The tier it grows to is still the lowest active tier + 1. The available-clubs list returns both tiers, each with its tier number and division name, and the club step of onboarding groups them under a tier heading.

- **The free-agent reference tier stays the lowest active tier (`FA-6`).** Prices in the free-agent pool read the lowest active tier's valuation; while tier 3 does not exist that is tier 2's.
- Promotion and relegation need no change: `PromotionRelegation.Compute` already moves three up and three down between adjacent active tiers, so season 1 does it (a test plays a season on the stepped clock and checks the swap).

**7. Belgium, Portugal and the Netherlands (`WORLD-2`).** `BEL` (nl-BE), `POR` (pt-PT) and `NED` (nl-NL), in sort order 7 to 9. Each has 20 invented places and a set of suffixes and qualifiers in the local style (`name-pools-v2`) and its own player given-name and surname pools (`player-name-pools-v2`). **All nine** player pools are now 36 given names × 37 surnames, which are coprime, so 1,332 pairs and 55 squads of 24 before a full name repeats: tiers 1 to 3 of a country never repeat one. The blocklist (`FIC-5`) gained the real clubs and famous players of the three countries. The forum needs no code (`ADR-0082`): a country's section comes from the manager's club.

**8. Eighteen divisions publishing at one kickoff need turns (ADR-0086).** Publishing 18 divisions at one kickoff collided on page-level read locks of a few small shared tables and dead-lettered on the stepped clock. That fix is ADR-0086 and shipped with milestone 2.

**9. Versions.** `player-gen-v4`, `world-rules-v19` (the input hash of the seed now includes `SeededTiers` and the template version `squad-template-v1`), `name-pools-v2`, `player-name-pools-v2`. Worlds are not migrated (`RULE-4`): the dev world is dropped and reseeded.

## What changes in the written rules

- **`WORLD-2`.** Nine launch countries; **`WORLD-5`** both tiers active at the start; **`WORLD-8`** a claim in any active tier; new **`WORLD-11`** (the seeded template).
- **`PYR-1`, `PYR-2`.** Expansion is evaluated on the whole country, and fires when every club in it has a human.
- **`TRN-18`.** The template's potential at seeding; the 80/15/5 draw applies to the legacy generators and does not describe a seeded squad.
- **Section 18.** `launch_countries`, `player_generator_version`, `world_rule_set_version`, the pool versions, the template constants.
- **README, master plan, glossary, traceability, context, modules, data model, load testing, runbook, restore drill, test strategy** carry the new counts. ADR-0005, 0076, 0082 and 0083 are marked as amended.

## Measured

Milestone 4 measured and changed nothing; the figures are recorded so the decisions below can be taken on them. Economy sandbox: `econ`, 10 seeds × 10 seasons, 40 seconds a run. Match probe: `tickfair`, 600 matches a cell, real AI plans.

**Money.** Tier-1 abilities fall from about 11.5 to 9.7 and a player's wage follows his ability, so the clubs are much richer than the economy was tuned for:

| Tier | Season-1 wages ÷ income, template squads | Same, old sized squads |
|---|---|---|
| 1 / 2 / 3 / 4 | 13 / 16 / 28 / 54% | 64 / 56 / 59 / 62% |

- Median end-of-season cash, tier 1: season 5 22M (old 8.8M), season 10 90M (old 31M). Nobody reaches the red or administration.
- A tier-1 club has 0.0 players of 15 or more in season 1 and 2.0 in season 10 (old 4.0 from season 1).
- A clean start now costs 16 to 18% of the forecast, not the 55 to 65% band of `CleanStartWageBand*`, which is now only read by tests.
- The economy sandbox targets 1, 2, 3, 4, 6 and 12 flip to missed. Pricing the template ratings as a higher market skill (a price curve) restored the wage shares, but it made tiers 3 and 4 run into the red late in the run and was taken out; the richer clubs are accepted (see Open).

**Match engine.** Absolute attributes are about 1.8 lower than before, and the engine scored more at that level:

| Matchup | Goals a match, at milestone 4 | After the keeper-dive change (`e0adac5`) |
|---|---|---|
| Old random tier 1 v tier 1 (baseline) | 1.78 | 1.78 |
| Template tier 1 v tier 1 | 1.96 | 1.51 |
| Template tier 2 v tier 2 | **4.27**, 19% of matches with 7+ goals | 2.38, 0 to 3% with 7+ |
| Tier 1 home v tier 2 | 3.06, 66/22/12 | 2.01, 52/34/14 |

The tier-2 scoring was a keeper whose reaction time crossed a 100 ms tick at Reflexes 19 and 20 (a keeper's dive started a whole tick late). The dive now starts part-way through a tick. The lab sample of all-13 sides fell from 2.98 to 1.47 goals as a side effect; the goals after the change are accepted (see Open).

**Free agents.** Ability 6 to 10 is now close to a tier-1 squad's lower half (a tier-1 floor of 8 is a quarter of the squad): 33% of free agents are 9 or 10 and upgrade an 8 in either tier for a wage of 26 to 67 thousand. Only humans bid on free agents (`FA-5`), so this is a human lever.

**Sponsors and fans.** Fan seeding reads strength only as a rank inside the division, so lower absolute strength changes nothing; but with a 2 to 4% spread between clubs, the rank-1 "win the league" objective is nearly arbitrary.

## Consequences

- Every club of a tier opens equal, to within the seeded diversity. The league of season 1 is decided by managers and by who is human, not by who drew the best random squad.
- Season 1 has promotion and relegation. A manager who takes a tier-2 club can be promoted in the first season.
- The AI market starts dead in an equal world: the upgrade clause (a listing better than the weakest player in the family) never fires when every club holds the same ratings. The AI-market tests lift a third of the players by SQL to start from a spread.
- The clubs are about three times as rich as the old economy allowed, and the matches score differently at low ability. Both are measured above and both are accepted as they are.
- Worlds are not migrated (`RULE-4`): a world seeded before this change keeps its random squads and one tier.

## Open

- **Accepted as measured (2026-10-23).** The richer clubs are accepted and the economy is not retuned for this change, and the goals per match after the keeper-dive change are accepted, so the engine is not recalibrated for it. ADR-0084 stays reserved for a later economy ADR.
- **Free agents.** Lower the 6 to 10 range, or leave the pool as a human lever.
- **Tiers 3 and 4.** Wages still outgrow income there late in a long run; real worlds seed only tiers 1 and 2, so tier 3 opens when a country fills.
- **Youth and free-agent names** are drawn separately, so they can repeat a squad player's name, and tier 4 repeats names after 55 clubs.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Keep random squads and only trim the spread | A tail of stronger clubs stays; the sizing loop had to guess wages. |
| Random-sign ±1 nudges | The best-eleven spread went over the 1.07 cap (1.074 and 1.087). |
| Seed tier 2 as empty or human-only | Promotion and relegation would not exist in season 1 and a new manager would have one division to choose from. |
| Claims in tier 2 only | Lowers the choice of a manager who wants the top flight. |
| Keep claims in the lowest tier and seed a third tier | More clubs for the same reason, and the lowest tier is the weakest one. |
| One potential draw (80/15/5) for seeded squads | A squad could have 0 or 4 gems, which is unfair in a world that aims for equal clubs. |
