# Game Rules — Configurable Values (Rule Set v1)

Normative source: `docs/product/master-plan.md` §3. Every value below is stored in a **versioned rule set** (active rule-set version on `world.game_worlds` and `competition.seasons`), not scattered as magic constants. Engine-only constants live in `EngineRulesV1` (ADR-0004) and are referenced here where they affect rules.

Config keys are the canonical naming (`section.key`). Changing a value that affects money, deadlines, competition fairness, ownership, security, or persistent history requires an ADR + audit event (plan §3.3, §17.3).

## 3.1 World model

| Config key | Value | Notes |
|---|---|---|
| `world.count` | 1 persistent `GameWorld` in production | Schema keeps `world_id` for future shards/test worlds |
| `world.launchCountries` | England, Spain, Germany, Italy, France, Romania | Fictional/generic display names; no protected league branding |
| `division.capacity` | 18 clubs | Every active division, always |
| `world.initialState` | Tier 1 active in all six countries, all clubs AI-controlled | Seeder output |
| `tenure.takesOverAsIs` | true | Squad, contracts, cash, table, fixtures, suspensions, injuries, transfer commitments, history all inherited — no reset |
| `tenure.model` | Time-bounded `ClubTenure` rows; clubs never store a mutable manager FK | Clubs persist forever unless audited admin retirement |
| `onboarding.lowestTierOnly` | true | New managers may claim only the country's **lowest active tier**; upper-tier vacancies stay AI |

## 3.2 Dynamic pyramid expansion

| Config key | Value | Notes |
|---|---|---|
| `pyramid.monotonic` | true | A tier is never removed when occupancy later falls |
| `pyramid.evaluateOn` | every successful takeover + every tenure-status change | |
| `pyramid.triggerOccupancy` | 18/18 active human tenures on the lowest tier | And no existing provisioning record for `N+1` |
| `pyramid.concurrencyControl` | country-scoped advisory lock + serializable transaction | Unique `(country_id, target_tier)` backstop |
| `pyramid.generationActor` | durable `ProvisionDivision` worker job | 18 clubs, ~396 players, finances, entries, fixtures |
| `pyramid.seasonAlignment` | new division uses current country season + same matchday calendar | |
| `pyramid.backfill` | deterministic AI-vs-AI bootstrap simulation of passed matchdays, in sequence | Produces a legitimate inherited table |
| `pyramid.claimableAfter` | generation + validation + backfill complete | Never claimable while incomplete |
| `pyramid.rolloverInteraction` | if season rollover is locked, target the next season | Never mutate a closing season |
| `pyramid.capacityResponse` | `409 CAPACITY_PROVISIONING` + polling | Stable code; never a partial club assignment |
| `pyramid.maxTier` | none (generic `N+1` path) | Tier 3 reuses the identical algorithm |

## 3.3 Human occupancy and abandonment

| Config key | Value | Notes |
|---|---|---|
| `tenure.activeHuman` | active `ClubTenure` whose account is not suspended and release date is null | Definition of "active human" |
| `inactivity.warnDays` | 10 days without login → inactivity warning | |
| `inactivity.inactiveDays` | 14 days → tenure status `inactive`; AI makes safe lineup/training decisions; manager resumes on login | |
| `inactivity.closeDays` | 21 days → close tenure, club returns fully to AI, after inbox/email warning | |
| `inactivity.preservesCommitments` | true | Fixtures, auctions, bids, transfers, financial commitments continue; never rewound |
| `tenure.resignCooldownDays` | 7 real-time days after voluntary resignation | Resignation closes immediately |
| `suspension.writeBlock` | immediate loss of write access for suspended accounts | Admin may assign temporary AI control before standard inactivity |
| `inactivity.thresholdsAreConfig` | true | Every threshold change emits an audit event |

## 3.4 Season calendar

| Config key | Value | Notes |
|---|---|---|
| `season.matchdays` | 34 | Double round-robin: every opponent home + away |
| `season.kickoffDays` | Tuesday, Thursday, Sunday | Shared by all six countries (MVP) |
| `season.kickoffUtc` | 19:00 UTC | UTC is authoritative; UI shows viewer's local timezone |
| `teamsheet.lockMinutesBeforeKickoff` | 30 | Team sheets lock; writes after lock affect later fixtures only |
| `season.firstStart` | configured future Tuesday with onboarding lead time | Never derived from deployment time |
| `season.rolloverDays` | 7 days after matchday 34 | Next season begins at first configured matchday after rollover |
| `schedule.algorithm` | circle/Berger, mirrored second half | Validated: 34 fixtures/club, 1 fixture/club/round, one home + one away per pair, acceptable home/away streaks |

## 3.5 League table and tie breakers

| Config key | Value |
|---|---|
| `table.points.win` | 3 |
| `table.points.draw` | 1 |
| `table.points.loss` | 0 |

Ordered tie breakers (strict sequence):

1. `tiebreak.1` Points
2. `tiebreak.2` Goal difference
3. `tiebreak.3` Goals scored
4. `tiebreak.4` Wins
5. `tiebreak.5` Head-to-head points among tied clubs
6. `tiebreak.6` Head-to-head goal difference
7. `tiebreak.7` Fewer red cards
8. `tiebreak.8` Fewer yellow cards
9. `tiebreak.9` Deterministic season draw (seeded from season ID + club IDs, **generated before the season, stored, and visible in competition rules**)

| Config key | Value | Notes |
|---|---|---|
| `tiebreak.finalDrawPreGenerated` | true | Stored at season creation |
| `tiebreak.dbRowOrderAllowed` | false | Never use database row order as a tie breaker |

## 3.6 Promotion and relegation

| Config key | Value | Notes |
|---|---|---|
| `promotion.spots` | 3 | Top three of the lower adjacent tier |
| `relegation.spots` | 3 | Bottom three of the higher tier |
| `relegation.lowestTier` | disabled | Lowest active tier has no relegation when no lower tier exists |
| `movement.appliesTo` | clubs (manager tenure follows the club) | |
| `movement.when` | season rollover only, after fixtures/discipline/finance/table validation is final | |
| `movement.newTierParticipation` | next rollover | Even if backfilled mid-season |
| `movement.transaction` | one country-scoped serializable transaction | Then create next season's entries + fixtures |
| `movement.historyImmutability` | prior season entries/division membership are immutable | Never rewritten |

## 3.7 Squad and registration

| Config key | Value |
|---|---|
| `squad.targetSize` | 22 senior players per club (generator target) |
| `squad.minRegistered` | 18 |
| `squad.minGoalkeepers` | 2 (within the 18) |
| `squad.maxRegistered` | 25 |
| `teamsheet.starters` | exactly 11 |
| `teamsheet.maxSubstitutes` | up to 7 |
| `match.maxSubstitutions` | 5, chosen by deterministic engine rules (async match) |
| `registration.oneActiveContractPerPlayer` | true (one active registration too) |
| `registration.snapshotBoundary` | players transferred after fixture snapshot lock are eligible only for later fixtures |
| `squad.emergencyReplacement` | if expiry/repair would drop below minimum: audited emergency replacements on minimum contracts (safety net only, not a squad-building route) |

## 3.8 Tactical rules

`tactics.formations` (MVP presets): **4-4-2, 4-3-3, 4-2-3-1, 4-1-4-1, 3-5-2, 5-3-2**

| Config key | Allowed values |
|---|---|
| `tactics.mentality` | defensive, cautious, balanced, positive, attacking |
| `tactics.tempo` | low, normal, high |
| `tactics.passing` | short, mixed, direct |
| `tactics.width` | narrow, normal, wide |
| `tactics.pressing` | low block, mid block, high press |
| `tactics.defensiveLine` | deep, normal, high |
| `tactics.tackling` | stay on feet, normal, aggressive |
| `tactics.timeWasting` | off, situational, on |

| Config key | Value | Notes |
|---|---|---|
| `tactics.slotEditing` | drag only within validated tactical zones | No overlapping or out-of-bounds positions |
| `tactics.boundedEffect` | every instruction has a bounded effect and trade-off | No tactic may multiply team rating without a counter-cost |
| `tactics.outOfPositionPenalty` | deterministic familiarity penalty | |

## 3.9 Training and player state

| Config key | Value | Notes |
|---|---|---|
| `training.teamFocus` | balanced, recovery, fitness, attacking, defending, technical, tactical | |
| `training.individualFocus` | optional; selects one attribute family | |
| `training.progressionJob` | daily 02:00 UTC | Development, fatigue, condition, morale, training-injury chance |
| `training.determinismInputs` | player, day, training plan, age curve, hidden potential, facilities baseline, engine version | |
| `attribute.min` / `attribute.max` | 1 / 20 | Training can never push a displayed attribute outside |
| `state.unit` | basis points, 0–10,000 | Applies to condition, fatigue, morale; APIs convert to user-facing scale |
| `state.matchEffect` | matches consume condition and raise fatigue by minutes, intensity, stamina, tactics | Rest/recovery restore |
| `unavailability.unit` | fixtures (not wall-clock days) | Both training and match injuries |
| `morale.changeBounds` | bounded changes from playing time, results, contracts, transfers | |

## 3.10 Injuries and discipline

| Config key | Value |
|---|---|
| `injury.absenceFixtures` | severity maps to 1–6 fixture absences (MVP) |
| `discipline.yellowsPerSuspension` | 5 league yellows → 1-match suspension |
| `discipline.yellowReset` | at season rollover |
| `discipline.redSuspension` | 1-match suspension (MVP rule set) |
| `discipline.serviceBasis` | the club's next eligible league fixtures |
| `teamsheet.lockedRepair` | locked sheet with newly ineligible player is repaired deterministically before simulation (bench/reserve selection by position suitability, condition, ability, stable player-ID tie break); repairs messaged to manager |

## 3.11 Contracts

| Config key | Value |
|---|---|
| `contract.lengthMinSeasons` / `lengthMaxSeasons` | 1 / 3 game seasons |
| `contract.wageCadence` | weekly, charged after the Sunday matchday |
| `contract.renewalQuote` | deterministic server calculation: ability, potential, age, playing time, morale, tier, remaining term |
| `contract.negotiation` | accept or decline only (free-form negotiation post-MVP) |
| `contract.transferEffect` | seller contract closes; precomputed buyer contract created |
| `contract.expiry` | expired players become free agents at rollover unless renewed |
| `contract.freeAgents` | post-MVP unless needed for market health; then same auction mechanism with zero seller fee + explicit signing wage |

## 3.12 Basic finances

| Config key | Value | Notes |
|---|---|---|
| `finance.moneyType` | `bigint` minor units | No floating-point money; single canonical in-game display currency |
| `finance.income` | home gate revenue (tier, attendance factor, form, fixed stadium baseline); weekly sponsorship; promotion/final-position awards; transfer income | |
| `finance.expense` | weekly player wages; transfer fees; small fixed weekly operating cost | |
| `finance.bidAffordability` | bid ≤ available cash after existing reservations | Enforced transactionally with append-only ledger |
| `finance.prohibited` | loans, debt, overdrafts, owner injections, stadium spending, user purchases | MVP |
| `finance.aiSameRules` | true | AI clubs obey identical affordability constraints |
| `finance.safetyJob` | detects clubs unable to field a legal squad or pay next wages → logged emergency grant only when required for competition integrity | Emits operations alert; tuned out through balancing |

## 3.13 Scouting and transfers

Scouting (MVP): global searchable player database, **exact public attributes**, filters, sorting, player profiles, private shortlists. No attribute uncertainty or staffed scouting.

| Config key | Value | Notes |
|---|---|---|
| `auction.listingFee` | seller lists eligible player with a minimum fee | Positive fee |
| `auction.resolutionWindows` | fixed daily windows | |
| `auction.minExposureHours` | ≥ 48 hours | Listing end after minimum exposure |
| `auction.matchdayBlackoutHours` | no resolution within 6 hours before a matchday kickoff | |
| `auction.bidDirection` | ascending; current amount + bidder count visible | Manager identity may stay hidden until completion |
| `auction.minIncrement` | configured minimum raise | Enforced per bid |
| `auction.bidsPerClubPerListing` | 1 active bid; may raise | |
| `auction.reservation` | leading bid's funds reserved transactionally; former leader released when outbid | |
| `auction.tieBreak` | highest amount → lowest `bid_sequence` (earliest committed) → immutable bid ID | |
| `auction.resolutionTx` | serializable revalidation: account status, balance reservation, squad limits, seller minimum squad, active contracts, listing status | Win → pay, seller credited, registration + contract change atomically |
| `auction.failurePolicy` | failed invariant cancels/skips the invalid bid per documented rules, considers next valid bid; every outcome audited | |
| `auction.aiPolicy` | AI lists surplus players and bids within valuation, positional need, squad size, budget bands | No hidden discounts, no unlimited money |
| `auction.postMvp` | direct offers, loans, swaps, clauses, installments, anti-sniping extensions, private negotiation | Excluded from MVP |

---

## Change control

1. Update this file and the affected section of `docs/product/master-plan.md` together; never let them diverge (plan §17.20).
2. Bump `rule_set_version` on affected worlds/seasons when a value changes.
3. Threshold/config changes that touch occupancy, deadlines, or money emit `ops.audit_log` entries (§3.3).
4. Match-engine formula changes bump the engine version, not this file (ADR-0004).
