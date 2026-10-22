# ADR-0081: Every attribute develops, up to a limit set by the player's potential (`training-v4`)

- **Status:** Accepted
- **Date:** 2026-10-21
- **Stage:** Training v4 plan (`while-working-on-the-radiant-globe.md`), milestones 1-4
- **Related:** [ADR-0057](0057-training-programmes-age-curve-and-hidden-aptitude.md), [ADR-0060](0060-per-attribute-training-progress.md), [ADR-0076](0076-potential-drawn-before-skills.md), game-rules `TRN-1`, `TRN-4`, `TRN-10`, `TRN-18`, `TRN-19`, `TRN-20`; `DailyProgression`, `DevelopmentLimit`, `GeneratedPotential`

## Context

The finance sandbox (finance plan M0) showed that generated youths never become the adults the generator makes. A world with youth intake got weaker every season, and "buy, train and sell at a profit" had nothing to work with. The causes, checked in the code and in an in-memory run of 1,200 youths:

- A programme trains 6-10 attributes and nothing else ever grows. Set pieces is in no programme at all.
- Every attribute stopped at `min(20, potential)`, but the generator builds an adult at `potential + position bonus + jitter` (ADR-0076). Generated adults of 24-29 sat 2.9 points above the training ceiling on their programme attributes, so the ceiling was below the players it was meant to describe.
- When a programme's attributes were capped the day's budget was thrown away: 39% of it at 20-21, about all of it from 24. Youths stopped at own-position skill 9.4.
- Even with nothing lost, the budget (140 thousandths a day) was about half of what 24 attributes need.
- Youth intake drew potential from the same tier mean that builds the starting world, so reseeding the world at a lower mean would cap every future gem.

The game's references (Football Manager, EA FC, Hattrick, Top Eleven, ManagerZone) all grow every attribute, let a focus concentrate without excluding, put the cap on the overall or make it soft, and never simply discard development.

## Decision

**1. Every attribute a player develops grows every day; the programme is the focus.** An outfielder develops his 24 outfield attributes (his 4 goalkeeping ones never grow); a keeper develops all 28. The programme takes 60% of the day's budget (`ProgrammeSharePermille`), split by weight as before (3, 2, 1); the other 40% is shared equally by his other developing attributes. An attribute at its limit takes no share and passes it on; when one group has nothing left, the other takes the whole budget. Recovery still develops nothing. Supersedes what ADR-0057 decision 2 and ADR-0060 decision 2 left in place: the day's budget covered only the programme's attributes.

**2. The limit is the overall plus headroom (`DevelopmentLimit`).** Each developing attribute has a formula value, the generator's own adult value without jitter: `clamp(potential + position bonus, 1, 20)`. Development stops when the **sum** of the developing attributes reaches the sum of their formula values, an integer, so the rule is exact with no rounding. No attribute may go more than `CeilingHeadroom` = **2** points above its own formula value, so the programme tilts a player towards its focus and no skill runs away. The plan proposed 1; it was raised to 2 after M4 to reach the plan's 3-5 twenties for an ordinary winger (see Measured). Both are derived from potential and primary position, which never change, so nothing is stored and there is no migration. Supersedes ADR-0060's `min(20, Potential)` ceiling.

**3. The budget is retuned.** `BaseDailyMilli` goes from 140 to **400**, so an average-aptitude 17-year-old reaches his limit at about 24, the age the generator assumes (`TRN-18`). The age curve, intensity, fatigue, aptitude, jitter and decline (with the programme's half-rate shield) are unchanged.

**4. Youth potential has its own scale.** `GeneratedPotential.DrawYouth` draws with the generated players' bands around `WorldRuleSet.YouthPotentialMeanForTier(tier)` (`YouthPotentialMeanTier1` 13, `YouthPotentialDropPerTier` 2). The values equal today's, so intake is unchanged now; reseeding the starting world at a lower mean no longer lowers the talent a later intake can reach (a tier-1 gem is still 19-20), and the academy plan can drive these constants. The squad generator is untouched, so `player-gen-v3` and its golden digest stand. Supersedes the part of ADR-0076 that ties youth intake to the starting world's tier mean, and its consequence "training reads potential as the cap and is unchanged".

**5. Versions.** `DailyProgression.Version` is `training-v4` and `WorldRuleSet.Version` is `world-rules-v16`. No schema, API shape or client contract changed: the training screen's copy now calls the programme a focus, and the limit stays hidden (`TRN-9`).

## Measured

Economy sandbox, `training-v3` (a worktree of d44c567) against `training-v4`. The probe runs 1,200 youths of 17-18 through training (`ECON_PROBE=1 econ 1200`); the world run is `ECON_INTAKE=3 econ 10 5000 10` (10 seeds × 10 seasons, 4 tiers × 18 clubs, 3 youths per club a season).

| | `training-v3` | `training-v4` |
|---|---|---|
| Youth own-position skill at 24 (limit 13.5) | 9.4 | 14.0 |
| Youths at their limit by 24: slow / average / quick third of aptitude | none (they stop at 9.4) | 34% / 99% / 100% |
| Median age a youth reaches his limit: slow / average / quick | — | 25 / 23 / 22 |
| Keepers at their limit by 24 | — | 74% (0.3 short on average) |
| Potential 18-20 youths: median age at the limit, at it by 24 | — | 22, 95% |
| Veterans 28 to 35, own-position skill | −0.84 (13.61 at 35) | −0.56 (13.89 at 35) |
| Generated adults 24-29, drift over 3 seasons | +0.02 | +0.10 |
| World skill, seasons 1 / 5 / 10 | 8.9 / 8.0 / 6.8 | 9.0 / 9.1 / 9.0 |
| Players of 15+ in the world, season 1 to 10 | 122 → 49 | 126 → 162 |
| Players of 17+ in the world, season 1 to 10 | 30 → 5 | 33 → 32 |
| Players of 19+ in the world | about 0 | about 0 |

Twenties per player, a trained youth at 24-25 against a generated adult of 24-29: at headroom 1 a trained winger had 2.1-2.5 (generated 1.5-2.5), which is parity with the generator but short of the plan's 3-5. A 20 needs an attribute whose formula value reaches the limit, so neither speed nor the programme share can raise it; only the headroom does. At **headroom 2** (kept) trained wingers have 2.9-3.6 (generated 1.5-2.5), strikers 4.9 (generated 2.3), keepers 4.8, centre-backs 4.1, all other positions 2.9-4.6. The cost is more specialised players than the generator makes: the overall is the same, so what the programme's attributes gain the others lose. The rest of the table is unchanged within noise (re-run at headroom 2): complete by 24 slow 33% / average 98% / quick 100% (keepers 70%), veterans at 35 13.88 (-0.57), adult drift +0.10, world 15+ 126 -> 160 -> 164, 17+ 34 -> 32, skill 9.0 / 9.1 / 9.0.

Closing age profile on the 28-attribute mean (outfielders' goalkeeping attributes do not grow): 17-18 5.3, 19-20 7.4, 21-22 9.9, 23-24 10.6, 25-27 10.9, 28-30 10.7, 31+ 10.7, a youth-to-adult gap of about 5.6 for the finance plan's `player-valuation-v2`.

## Consequences

**Positive**

- A trained youth finishes like a generated adult of his potential and position, so intake keeps a world's quality instead of draining it, and the finance plan's "train and sell" loop has something to sell.
- Nothing a player earns in a day is thrown away until he is complete; a manager sees every attribute of a youth move.
- The programme still matters: it decides where a player's points land within his limit, by up to two points an attribute.
- Future talent no longer depends on the starting world's skill.

**Negative / accepted**

- Veterans hold up a little better (+0.28 at 35) and generated adults drift up slightly (+0.10 over 3 seasons), both inside the plan's targets.
- Slow learners (the bottom third of aptitude) are often short of their limit at 24 and finish around 25-26.
- The budget does not scale with the number of developing attributes, so a keeper (28) develops each attribute a little slower than an outfielder (24).
- 19-20 players stay a handful in the whole world, because the limit is the generator's formula; the finance plan anchors "the best player" to the measured top.
- A player who reached `min(20, potential)` under `training-v3` and sits below his new limit starts developing again from the next progression day. Existing worlds need no migration.

**Open**

- Match minutes as a development input (deferred).
- Youth intake itself belongs to the finance plan; the academy plan will drive the youth potential constants by academy quality.
