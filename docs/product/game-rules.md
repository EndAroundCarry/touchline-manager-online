# Game Rules (rule set v4)

> **Status:** Settled for the public MVP. This document is the normative rule source; the
> master plan [`master-plan.md`](master-plan.md) explains intent, this file defines behaviour.
> **Applies to:** rule set version `4`, engine version `4`.
> **Change control:** see [§19](#19-change-control).

Every rule carries a stable reference (`WORLD-4`, `CAL-2`, …) so that migrations, tests,
operator runbooks, and support answers can cite the exact rule they implement.

Conventions used below:

- **MUST / SHOULD / MAY** follow the master plan's normative language.
- Times are UTC. "Game season" and "game year" are not real time — see `TIME-*`.
- Money is `bigint` minor units of a single display currency. No floating point.

---

## 1. Rule-set versioning

| Ref | Rule |
|---|---|
| RULE-1 | All tunable values live in a single versioned rule set, never as magic constants in code. |
| RULE-2 | The active rule-set version is recorded on the world, on every season, and on every match input snapshot. |
| RULE-3 | Changing a value that affects results, money, deadlines, or fairness requires a new rule-set or engine version. Released versions are never edited in place. |
| RULE-4 | Promotions, relegations, contracts, and finances already applied are never recomputed because a rule changed. |
| RULE-5 | A rule change is recorded as an audit event including actor, previous value, and new value. |

---

## 2. World model

| Ref | Rule |
|---|---|
| WORLD-1 | Production runs exactly one persistent `GameWorld`. The schema must still carry or derive `world_id` so test and future shard worlds are possible. |
| WORLD-2 | Launch countries: **England, Spain, Germany, Italy, France, Romania** (stable codes `ENG`, `ESP`, `GER`, `ITA`, `FRA`, `ROU`). |
| WORLD-3 | All club, competition, and player identity is fictional. See [`content-and-fictional-data-policy.md`](content-and-fictional-data-policy.md). |
| WORLD-4 | Every active division has **exactly 18 clubs**. There is no other size in the MVP. |
| WORLD-5 | A new world begins with **tier 1 active in all six countries**, and every club starts under AI control. |
| WORLD-6 | Clubs persist forever. Only an audited administrative repair may retire a club. |
| WORLD-7 | Managers never own club records. Control is expressed through time-bounded `ClubTenure` rows. |
| WORLD-8 | A new manager may take over a club **only in the country's lowest active tier**. Upper-tier vacancies stay under AI control until promotion/relegation changes them or a post-MVP job market exists. |
| WORLD-9 | A takeover inherits the club exactly as it exists: squad, contracts, cash, table record, fixtures, suspensions, injuries, transfer commitments, and history. Nothing resets. |
| WORLD-10 | Every active division in a country shares the same season and the same matchday calendar. |

---

## 3. Pyramid expansion

Expansion is **monotonic**: once a tier exists it is never removed, even if occupancy later
falls. See [ADR-0005](../architecture/adr/0005-dynamic-pyramid-and-backfill.md).

| Ref | Rule |
|---|---|
| PYR-1 | After every successful takeover and every tenure-status change, evaluate the country's current lowest active tier. |
| PYR-2 | If all 18 clubs in that tier hold active human tenures and no provisioning record exists for the target tier, create a `DivisionProvisioningRequest` for tier `N + 1`. |
| PYR-3 | The evaluation runs in a PostgreSQL transaction holding a **country-scoped advisory lock**. `unique (country_id, target_tier)` prevents duplicate tiers. |
| PYR-4 | A durable worker generates the new tier: 18 AI clubs, balanced squads, finance accounts, division-season entries, fixtures, and a historical baseline. |
| PYR-5 | The new division uses the **current country season** and the **same matchday calendar**. |
| PYR-6 | Matchdays already passed in the current season are simulated in sequence as deterministic AI-vs-AI **bootstrap** results, and their table/player/finance effects are applied, so the new division inherits a legitimate table. |
| PYR-7 | Bootstrap results are marked as generated history. They never appear as matches a human manager played. |
| PYR-8 | The division becomes claimable only after generation, validation, and backfill complete. Validation asserts 18 clubs, 18 legal squads (two goalkeepers each), a complete 34-round schedule, one standing per club, and standings reconciling with published bootstrap fixtures. |
| PYR-9 | If provisioning is requested while rollover holds the country lock, it targets the **next** season. The closing season is never mutated. |
| PYR-10 | If capacity is temporarily unavailable, onboarding returns a stable `CAPACITY_PROVISIONING` response with request status and a polling hint. It never partially assigns a club. |
| PYR-11 | When tier 2 fills, tier 3 provisions through the identical generic path. There is no hard-coded maximum tier. |
| PYR-12 | Losing human occupancy never deletes or shrinks a tier. |
| PYR-13 | A tier provisioned during a season participates in promotion/relegation at the next rollover. |
| PYR-14 | Each provisioning run records a generation seed and generator version; the same seed and version reproduce the same logical tier. |

---

## 4. Occupancy, inactivity, and abandonment

"Human managed" means an **active** `ClubTenure` whose account is not suspended and whose
release date is null.

| Ref | Rule | Value |
|---|---|---|
| OCC-1 | Send an inactivity warning | after **10 days** without a login |
| OCC-2 | Mark the tenure `inactive` and let AI make safe lineup/training decisions; the manager may resume simply by logging in | after **14 days** |
| OCC-3 | Close the tenure and return the club fully to AI control, after an inbox/email warning | after **21 days** |
| OCC-4 | Voluntary resignation closes the tenure immediately and starts a cooldown before another takeover | **7 days** |
| OCC-5 | Existing fixtures, auctions, bids, transfers, and financial commitments continue through inactivity and abandonment. Abandonment never rewinds club state. | — |
| OCC-6 | A suspended account loses write access immediately. An administrator may assign temporary AI control before the standard inactivity period. | — |
| OCC-7 | All OCC thresholds are configuration values. Changing one requires an audit event. | — |
| OCC-8 | An inactive tenure still counts as human occupancy for pyramid capacity purposes until it closes. | — |
| OCC-9 | A manager with an active or inactive tenure cannot claim another club. | — |

---

## 5. Season calendar

| Ref | Rule | Value |
|---|---|---|
| CAL-1 | One season is a double round-robin | **34 matchdays**; each club plays every other club once home and once away |
| CAL-2 | Standard kickoff | **19:00 UTC** on **Tuesday, Thursday, Sunday** |
| CAL-3 | Team sheets lock before kickoff | **30 minutes** |
| CAL-4 | Deadlines are displayed in the manager's time zone; UTC remains authoritative. The zone is a per-manager preference, seeded from the viewer's browser at onboarding and editable afterwards, and the stored value is what every screen renders with (ADR-0036). | — |
| CAL-5 | The first production season begins on a **configured future Tuesday** with onboarding lead time. It is never derived from deployment time. | — |
| CAL-6 | Rollover period after matchday 34 | **7 days**; the next season starts on the first configured matchday after rollover |
| CAL-7 | All six countries share the same real-time cadence. Jobs, data, and standings remain country/division scoped. | — |
| CAL-8 | Schedules are generated with a tested circle/Berger algorithm and mirrored for the second half. | — |
| CAL-9 | Schedule validation asserts: exactly 34 fixtures per club; exactly one fixture per club per round; each pair meets once home and once away; home/away streaks acceptable. | — |
| CAL-10 | A division's matchday resolves as a unit. Individual fixtures are never published early. | — |
| CAL-11 | If simulation is delayed, the scheduled kickoff is not changed and no forfeit is invented. Operators display status instead. | — |

---

## 6. League table and tie-breakers

Points: **3 win / 1 draw / 0 loss** (`TBL-1`). Ordering is applied in this exact sequence,
first difference deciding:

| Ref | Tie-breaker |
|---|---|
| TBL-2 | Points |
| TBL-3 | Goal difference |
| TBL-4 | Goals scored |
| TBL-5 | Wins |
| TBL-6 | Head-to-head points among the tied clubs |
| TBL-7 | Head-to-head goal difference among the tied clubs |
| TBL-8 | Fewer red cards |
| TBL-9 | Fewer yellow cards |
| TBL-10 | Deterministic season draw derived from season ID and club IDs |

| Ref | Rule |
|---|---|
| TBL-11 | The final draw key is generated **before** the season, stored, and visible in competition rules. |
| TBL-12 | Database row order or insertion order is never a tie-breaker. |
| TBL-13 | Standings are a transactional projection of published fixtures and must be exactly rebuildable from them. |
| TBL-14 | A division's table is reconciled by a rebuild that recomputes it from its published fixtures; the rebuild must reproduce the live projection exactly, correct only what has drifted, and leave nothing behind. |

The ordering is shown to managers rather than described: `GET /divisions/{divisionId}/rules` returns the
points, this sequence as stable codes, and the season's stored draw — the seed, its published hash, and each
club's derived key — so `TBL-11`'s "visible in competition rules" is a page anyone who can sign in can open.
The criteria arrive as codes and the wording is the client's, the same split the commentary and inbox
templates use; the codes are the ones `StandingsCalculator` ranks with, so the page describes the order the
table actually follows.

---

## 7. Promotion and relegation

| Ref | Rule | Value |
|---|---|---|
| PR-1 | Promotion and relegation automatically apply between adjacent active tiers | **3 up / 3 down** |
| PR-2 | The lowest active tier has no relegation. | — |
| PR-3 | Movement applies to **clubs**, so a human manager stays with the club. | — |
| PR-4 | Movement occurs only during season rollover, and only after all fixtures, discipline, finance postings, and table validations are final. | — |
| PR-5 | All movements in a country are applied in one country-scoped serializable transaction, followed by creation of the next season's entries and fixtures. | — |
| PR-6 | Season-entry and history rows are immutable. A prior season's division membership is never rewritten. | — |
| PR-7 | Promoted/relegated clubs retain squad, contracts, cash, and history. | — |
| PR-8 | If a tier was provisioned during the closing season, it takes part in promotion/relegation at that rollover (subject to `PR-2`). | — |
| PR-9 | There is no promotion out of the **top active tier** of a country; there is no higher division to promote into. | — |
| PR-10 | A country with only one active tier therefore has neither promotion nor relegation in that tier until a lower tier exists. | — |

Movement is computed by a pure, versioned rule (`promotion-relegation-v1`) from each active tier's finalized
ordering. Three up and three down between every adjacent pair of active tiers means the top tier promotes
nobody and the lowest relegates nobody as consequences of there being no adjacent tier rather than as special
cases, and a club moves at most one tier. It is applied once, at rollover, by the world-scoped resumable state
machine in [ADR-0031](../architecture/adr/0031-season-rollover-state-machine.md): the season it closes and the
season it opens are distinct rows, so a finished season's membership, results, and table are never rewritten
(`PR-6`), and the movement a finished season earned is reproducible from its stored standings.

---

## 8. Squad and registration

| Ref | Rule | Value |
|---|---|---|
| SQ-1 | Generator target squad size | **24 senior players** |
| SQ-2 | Minimum registered senior squad | **18**, including **at least 2 goalkeepers** |
| SQ-3 | Maximum registered senior squad | **27** |
| SQ-4 | Match team sheet | exactly **11 starters**, up to **7 substitutes** |
| SQ-5 | Maximum substitutions per match | **5**, chosen by deterministic engine rules because matches are asynchronous |
| SQ-6 | A player has exactly one active club contract and one current registration. | — |
| SQ-7 | A player transferred after a fixture snapshot locks is eligible only for later fixtures. | — |
| SQ-8 | If expiry or an administrative repair would leave a club below the minimum, **audited emergency replacement players** on minimum contracts are created. This is a safety net, not a squad-building route; it emits an operations alert. | — |
| SQ-9 | Players below the minimum are never silently fielded. A club below minimum must be repaired before its next lock. | — |

---

## 9. Tactics

### 9.1 Formations (MVP presets)

| Ref | Formation |
|---|---|
| TAC-1 | 4-4-2 |
| TAC-2 | 4-3-3 |
| TAC-3 | 4-2-3-1 |
| TAC-4 | 4-1-4-1 |
| TAC-5 | 3-5-2 |
| TAC-6 | 5-3-2 |
| TAC-11 | 4-4-1-1 |
| TAC-12 | 4-5-1 |
| TAC-13 | 4-3-2-1 |
| TAC-14 | 4-2-2-2 |
| TAC-15 | 3-4-3 |
| TAC-16 | 3-4-2-1 |
| TAC-17 | 5-4-1 |

AI clubs keep choosing from `TAC-1`…`TAC-6`; the later presets are for human managers.

The wide slots of `TAC-1` 4-4-2, `TAC-4` 4-1-4-1, `TAC-11` 4-4-1-1 and `TAC-12` 4-5-1, and the outer midfield slots of `TAC-17` 5-4-1, ask for the **wide midfielder** role (positions RM and LM). `TAC-5` 3-5-2, `TAC-15` 3-4-3 and `TAC-16` 3-4-2-1 keep their wing-backs, and `TAC-2` 4-3-3 and `TAC-3` 4-2-3-1 keep their wingers. Out of possession, a side with wing-backs (`TAC-5`, `TAC-6` 5-3-2, `TAC-15`, `TAC-16`, `TAC-17`) slides across when the ball is on a flank in its middle third: the wing-back on the ball's side goes out to the opponent's widest man and the one on the far side tucks in toward the middle. In a 3-4-3 the wide forwards also help defend: they screen passing lanes and track the opposing flank man, so the shape defends as a 5-4-1. A winger, a full-back or a wing-back in a wide-midfielder slot, and a left or right midfielder in a wing-back slot, are at home (no out-of-position warning).

| Ref | Rule |
|---|---|
| TAC-7 | Slot positions are fixed by the chosen formation. Managers cannot drag slots; changing the formation is the only way to change where players stand. |
| TAC-8 | Every slot has a position family and a role. |
| TAC-9 | Slot coordinates are stored as scaled integers normalized to 0–10,000. |
| TAC-10 | A plan names exactly eleven slots. Its default lineup names either nobody or all eleven: a plan with some but not all of its slots filled is refused, so a saved side is never short (see `SQ-4`). The client's `If-Match` version is required to save. |

### 9.2 Team instructions

| Instruction | Allowed values | Ref |
|---|---|---|
| Mentality | defensive, cautious, balanced, positive, attacking | INS-1 |
| Tempo | low, normal, high | INS-2 |
| Passing | short, mixed, direct | INS-3 |
| Width | narrow, normal, wide | INS-4 |
| Pressing | low block, mid block, high press | INS-5 |
| Defensive line | deep, normal, high | INS-6 |
| Tackling | stay on feet, normal, aggressive | INS-7 |
| Time wasting | off, situational, on | INS-8 |

| Ref | Rule |
|---|---|
| INS-9 | Every instruction has a bounded effect **and** a trade-off. No tactic may multiply team strength without a counter-cost (fatigue, space conceded, discipline risk, or reduced chance quality). |
| INS-10 | Out-of-position players receive a deterministic familiarity penalty derived from slot position family versus player position and familiarity. |
| INS-11 | A club has exactly one default tactical plan; a fixture may have its own draft team sheet referencing a plan version. |
| INS-12 | Humans and AI are validated by the identical validator. AI receives no bypasses. |

An AI-controlled club — one no manager holds — is set up by a pure, versioned policy (`ai-policy-v1`) run
by the worker's daily evaluation job. The policy draws a formation and the eight instructions from the
club's identity, picks its default eleven by position suitability, condition, ability, and stable player
ID — the same ordering `DIS-6` repairs a side with — and trains at `normal` intensity, giving every player the programme that matches their position (`TRN-1`). Every
plan it produces is accepted by the same `TacticalPlanValidator` a manager's save goes through, so `INS-12`
is enforced by the code path rather than by intention. It fills only a missing plan: a side or training
plan a manager or an earlier pass set is left exactly as it is, and a club a manager holds — active or
merely inactive (`OCC-8`) — is never considered.

The plan an AI club gets also carries five match instructions (`MAT-6`), written by a second pure policy
(`ai-match-plan-v1`) that is seeded from its own label and draws from its own generator, so adding it moved
no club's formation, instructions or eleven. A club has one of three temperaments — cautious, balanced or
bold — which moves the minutes and the size of the steps. Every club protects a lead of two or more late
(one step more defensive, time-wasting raised when it was off), holds a one-goal lead later still with a
fresher player for its most tired attacker, chases the game when behind with one step more attacking and a
fresh forward for its most tired midfielder, goes all out (All-out attack, high press) late when still behind,
and settles down when one of its players is sent off (one step more defensive, one forward fewer). The
instructions name families, never players, are held to `MatchInstructionValidator`, and are ordinary rules
a manager could write; a takeover inherits them and may edit or delete them. An existing club's plan is
never rewritten, so a world seeded before the policy existed keeps its clubs' rule-less plans until it is
reseeded.

---

## 10. Training and player state

| Ref | Rule | Value |
|---|---|---|
| TRN-1 | A player trains one **programme**: a weighted list of attributes (3 core, 2 important, 1 supporting). The programmes are `goalkeeper`, `defender`, `wingback`, `midfielder`, `winger`, `forward`, `mental`, `physical`, `recovery`, and `widemid` (the wide midfielder's). The catalogue is served by the API, never repeated by a client. A programme is the player's **focus**: every attribute he develops grows each day, and the programme takes the larger share (TRN-10). `recovery` focuses on no attributes, develops nothing that day and recovers extra fatigue. | 10 programmes |
| TRN-2 | Programmes are per player only; there is no club-wide programme. A player trains the programme that matches their primary position (gk → goalkeeper; cb → defender; rb, lb → wingback; dm, cm, am → midfielder; rm, lm → widemid; rw, lw → winger; st → forward) unless the manager sets an override, and clearing it returns them to that default. Intensity (light, normal, intense) is the one club-wide training setting. | — |
| TRN-3 | Daily progression job runs | **02:00 UTC** |
| TRN-4 | Attribute scale | **1–20** displayed; neither development nor decline can push a displayed attribute outside 1–20, and development never lifts a player past the limit his hidden potential and primary position set (TRN-19; how potential is drawn: TRN-18, TRN-20) |
| TRN-5 | Condition | **0–10,000** basis points |
| TRN-6 | Fatigue | **0–10,000** basis points |
| TRN-7 | Morale | **0–10,000** basis points |
| TRN-8 | APIs convert basis points into user-facing values; the database is authoritative in basis points. | — |
| TRN-9 | Development and decline are deterministic from player identity, day, programme, intensity, age, fatigue, hidden potential, hidden aptitude, and the progression version. They are reproducible for the same inputs. Potential and aptitude are never sent to a client. | — |
| TRN-10 | **Per-attribute progress and the daily split.** Each attribute carries its own progress towards its next point, in millionths of a point, so no day's progress is lost to rounding and a manager can see how close a skill is to rising. Every attribute a player develops (TRN-19) grows each day. The programme takes **60%** of the day's budget, split across its attributes in proportion to their weights (core 3, important 2, supporting 1); the other **40%** is shared equally by his other developing attributes. The budget does not depend on how many attributes a programme covers, so a narrow programme concentrates its share on fewer skills. An attribute at its limit takes no share and its share moves to the others; when either group has nothing left to develop the other takes the whole budget, so nothing is lost until the player reaches his limit. A point is gained when an attribute's progress reaches one. | budget 400 thousandths of a point a day at peak age, neutral aptitude, normal intensity; programme 60% |
| TRN-11 | Matches consume condition and increase fatigue based on minutes, intensity, stamina, and tactics. Rest and recovery restore them. | — |
| TRN-12 | Training injuries and match injuries create explicit unavailability records measured in **fixtures**, not wall-clock days. | — |
| TRN-13 | Morale reacts to playing time, results, contracts, and transfers with bounded changes. | — |
| TRN-14 | **Age growth curve.** The growth factor is 100% to age 19, falls 10 points a year to 40% at 25, then is multiplied by 0.75 a year with a floor of 2%, so a 30-year-old still develops, very slowly. Intensity scales it (light 0.70, normal 1.00, intense 1.35) and fatigue above 60% reduces it linearly to 0.6 at 100%. | see description |
| TRN-15 | **Hidden aptitude.** Each player has a training aptitude of 55%–145% (triangular around 100%) derived purely from their identity and never stored. It scales development, so two players of equal potential reach it at different speeds. | 550–1450 permille |
| TRN-16 | **Decline.** Each attribute declines from its own start age by a yearly slope that grows each year past it: pace and acceleration from 28, agility from 29, stamina and jumping from 30, strength from 31, some technical skills from 29 and the rest from 32, work rate from 31, goalkeeper reflexes from 32 and the other goalkeeping skills from 34. Other mental attributes do not decline. An attribute the player's programme trains declines at half the rate. Decline comes off the same per-attribute progress as growth, so a skill that is trained and ageing nets the two; a point is lost when the progress reaches minus one, and an attribute never falls below 1. | 86 progression days a season |
| TRN-17 | **History.** Every progression day records, per player, the programme and intensity in force, the development and decline earned, the whole points gained and lost, and which attributes moved. A manager reads it for their own players only. | kept in full |
| TRN-18 | **Potential.** A generated player's hidden potential is drawn first, as `clamp(tier mean + offset, 1, 20)`, and his skills are derived from it: `potential + age adjustment`, where the adjustment is 24 − age below 24, zero from 24 to 29, and age − 29 above 29. So a 17-year-old starts 7 under his ceiling, a 24 to 29-year-old is at it, and an older player's potential is his peak, with his current skills showing the decline. Position bonuses and per-skill jitter are unchanged. Training develops the player towards the same formula without the jitter (TRN-19). The offset is a band: **normal** 80%, two four-faced dice less four (−4 to +2, most often −1); **talent** 15%, +3 to +5; **gem** 5%, +6 to +7. The bands do not overlap, and the mean offset is about +0.1, so a tier's squad strength is kept. Existing worlds get the new potentials by reseeding. The tier mean here is the generated squads' ability mean (`ability_mean_tier1`); a youth-intake player is drawn on his own scale (TRN-20). | 80% / 15% / 5% (balancing) |
| TRN-19 | **Development limit.** An outfielder develops his 24 outfield attributes; his 4 goalkeeping attributes never grow. A goalkeeper develops all 28. Each developing attribute has a **formula value**, the generator's adult value for his potential and primary position, `clamp(potential + position bonus, 1, 20)` with no jitter. Development stops when the **sum** of his developing attributes reaches the sum of their formula values, so on average a trained player finishes where the generator would have made him; and no attribute may be trained more than **2 points** above its own formula value, so a programme can tilt a player towards its focus (a trained player has more 20s than a generated one) but never let one skill run away. Both are derived from potential and primary position, which never change, so nothing is stored, and the limit is never sent to a client (TRN-9). Decline (TRN-16) can still take an attribute below it. | sum of formula values; headroom 2 |
| TRN-20 | **Youth potential.** A youth-intake player's potential is drawn with the same bands as TRN-18 around the **youth mean** of his club's tier, `clamp(13 − 2 × (tier − 1), 1, 20)`, which has its own constants. So reseeding the starting world at a lower ability mean never lowers the talent a later intake can reach: a tier-1 gem is still 19–20. | 13 at tier 1, −2 a tier (balancing) |
| EXP-1 | **Experience** is stored per player, in basis points of the maximum like `TRN-5`…`TRN-7`: 0–10,000, where 100 points are 10,000. The APIs show points to one decimal, with the level and the bonus (`TRN-8`). It is public, like age, so it shows on another club's player. | 0–10,000 bp |
| EXP-2 | A player earns **half a point** for every match in which he plays at least **15 minutes**; substitutes count the same way, an unused substitute earns nothing. It is applied by the matchday publication in the same transaction as the match load (`TRN-11`, ADR-0017), from the stored player lines, so a round that is already published loads nobody twice. The maximum is about 200 matches. | 50 bp, 15 min |
| EXP-3 | Five **levels**, one every 20 points: level 1 at 20 points (about 40 matches), 2 at 40, 3 at 60, 4 at 80, 5 at 100. Under 20 points a player has no level. | 2,000 bp a level |
| EXP-4 | Level *n* adds **n% to the skills the tick engine plays with** (passing, shooting, tackling, heading, keeping, decisions and the rest). The percentage multiplies the value the engine plays with after the skill curve and tiredness (`MAT-15`) and is **never capped at the top of the 1–20 scale**: a sheet 20 at level 5 plays as 21 where the engine reads it raw and as 15.75 instead of 15.00 where it reads the curved value. Pace, Acceleration, the movement physics, the attack edge (the raw-attribute reader of §14.43) and the legacy engine do not get it. The sheet attributes stay 1–20 on every screen; training, the squad list, position ratings and the AI read them as before. | +1% a level, +5% at most |
| EXP-5 | **Starting experience** is by age, with no spread: 17–18 year olds start at 0, then 4 points a year, capped at 50 points from age 30. The same rule gives a newly generated player his experience (`PlayerGenerator`, world seeding and rollover replacements) and backfilled the players that already existed when experience arrived. | 400 bp a year, cap 5,000 |
| EXP-6 | The **bonus, not the experience**, is frozen into the match snapshot when the team sheet locks, so the engine never learns what a level is and a replay re-simulates from the stored snapshot. The bonus is part of the snapshot hash only when it is above zero, so every match played by players without a level keeps its hash and `EngineVersions` did not change (ADR-0076). | 0–500 bp |

Match load is a pure, versioned rule (`match-load-v1`) over the frozen facts of a result — each player's
minutes, their stamina, their side's instructions, and the scoreline — and it is applied by the matchday
publication in the same transaction that publishes the results, alongside cards and injuries (ADR-0017).
Condition and fatigue are additive costs the daily recovery is measured against; morale moves with the
result in proportion to the minutes a player contributed, so a player who did not appear carries none of it.
Contracts and transfers are the remaining `TRN-13` inputs and arrive with the stages that own them.

---

## 11. Injuries and discipline

| Ref | Rule | Value |
|---|---|---|
| DIS-1 | Injury severities map to fixture absences | **1–6 fixtures** |
| DIS-2 | League yellow cards trigger a suspension | **5 yellows → 1 match** |
| DIS-3 | Yellow accumulation resets at season rollover. | — |
| DIS-4 | A red card produces a suspension | **1 match** in MVP rules |
| DIS-5 | Suspensions are served against the club's **next eligible league fixtures**. | — |
| DIS-6 | If a locked team sheet contains a player who became ineligible, the snapshot builder repairs it deterministically from the bench, then from reserves, using position suitability, condition, ability, and stable player ID tie-breaking. | — |
| DIS-7 | Every repair is recorded and reported to the affected manager in the inbox. | — |
| DIS-8 | An outstanding suspension that was not served by season end carries into the next season. | — |
| DIS-9 | A division's discipline — every player the season's cards have touched, with the suspension they still owe — is public game data, read from the stored accumulation and the open absences rather than kept a second time. The rows are ranked most sendings-off first, then most bookings, then name. | — |

The division's discipline is the detail behind the table's card columns (`TBL-8`, `TBL-9`), which break
ties but name nobody: a book is the season's accumulation (`DIS-2`, `DIS-4`) and the suspension figure is
read from the same open absence the publication serves (`DIS-5`), so the page cannot disagree with the side
a manager may actually name. It is read beside the table, the statistics, and the competition rules, and
neither the ordering nor the counts are stored a second time.

---

## 12. Contracts

| Ref | Rule | Value |
|---|---|---|
| CON-1 | Contract length | **1–3 game seasons** |
| CON-2 | Wages are charged | **weekly, after the Sunday matchday** |
| CON-3 | Renewal presents a deterministic server-calculated quote derived from ability, potential, age, playing time, morale, tier, and remaining term. | — |
| CON-4 | The manager accepts or declines the quote. Free-form negotiation is post-MVP. | — |
| CON-5 | A transfer closes the seller's contract and creates the buyer's contract, which was displayed before the bid was placed. | — |
| CON-6 | Expired players become free agents at rollover unless renewed. | — |
| CON-7 | **Built** (`FA-1`…`FA-8`, §14.5): free agents are signed through the same timed-auction mechanism with no seller, so no seller fee, and an explicit signing wage frozen on the listing. | — |
| CON-8 | Contract years advance at **season rollover**, never on the real-world anniversary. | — |
| CON-9 | At rollover, a club nobody manages (no tenure, or an inactive one) renews its best expiring players up to the AI target (**23**, the AI market's own target, so a generated squad of 24 loses at most one player) and releases the rest; a present manager's expiring contracts are renewed by the board (`CON-11`). | — |
| CON-11 | The board automatically renews every expiring contract of a club with a present manager at rollover, for a two-season deal on the deterministic renewal quote, so no manager loses a player to an expiring contract. Expiry to free agency remains for the surplus of clubs nobody manages (`CON-9`). | — |
| CON-12 | A manager may **release** a player of their own club now, from the player's Contract tab. It costs **half the weekly wages left on the contract**, paid at once (rounded up to whole weeks: the rest of this season plus whole seasons after it). The contract closes as `released`, the registration ends, and the player joins the next Monday's free-agent batch (`FA-5`). Refused while the player has an open listing, when it would leave fewer than **18** players or fewer than **2** goalkeepers, during rollover, or when the club's available cash (cash less reservations) cannot cover the payout. A match snapshot that is already locked keeps him (`SQ-7`). | `release_payout_bp` 5,000 |
| CON-10 | **Announce-then-play retirement**: from **32** a player may announce that the coming season is their last; the chance rises each season and is gated by ability and fitness, with a forced announcement age and a forced retirement age capping it. An announced player plays one final season. | — |

The retirement rule's numbers are a hidden mechanic: a manager knows an announcement is possible from the
start age and that the chance grows with age, but never by how much. Only the announcement itself — a fact
about the coming season, delivered as an inbox message — is ever surfaced. The start age, the per-season
growth, the ability and fitness gate, and the forced caps are all named rule-set values (`§18`), never
serialized to a client.

---

## 13. Finances

Money is stored as `bigint` minor units of one canonical in-game display currency
(`FIN-1`). Floating-point money is forbidden (`FIN-2`).

### 13.1 Income

| Ref | Source | Basis |
|---|---|---|
| FIN-3 | Home-match gate revenue | the club's stadium (places and ticket prices), tier, and its fans and their mood |
| FIN-4 | Weekly sponsorship credit | fixed baseline per tier |
| FIN-5 | Promotion award and final-position award | tier and final rank |
| FIN-6 | Transfer income | completed auction proceeds |
| STRC-4 | Snack bar takings at a home match | the crowd, the snack bar's level, and the result judged against what the fans expected (§13.7) |
| STRC-6 | Merchandise takings, weekly | the club's fans, the shop's level, and the fans' mood (§13.7) |

### 13.2 Expenses

| Ref | Expense |
|---|---|
| FIN-7 | Weekly player wages |
| FIN-8 | Transfer fees |
| FIN-9 | A small fixed weekly operating cost |
| STRC-8 | The weekly upkeep of the snack bar and the merchandise shop, one charge for both (§13.7) |

### 13.3 Rules

| Ref | Rule |
|---|---|
| FIN-10 | A club cannot place a bid that exceeds available cash **after existing reservations**. |
| FIN-11 | Cash and reserved funds update transactionally together with an append-only ledger entry. |
| FIN-12 | The ledger is append-only. Corrections use compensating entries; balances are never edited directly. |
| FIN-13 | Cash and reserved balances are never negative. Enforced by database check constraints. |
| FIN-14 | No loans, debt, overdrafts, owner injections, or real-money purchases exist in the MVP. A club buys with its cash players, new places in its stadium (`STAD-4`), and upgrades of its snack bar and merchandise shop (`STRC-3`). |
| FIN-15 | AI clubs obey identical affordability constraints. They never receive hidden discounts or unlimited money. |
| FIN-16 | A safety job detects clubs that cannot field a legal squad or pay the next wage run and applies a **logged emergency grant** only when required to preserve competition integrity. It emits an operations alert and is tuned out through balancing. |
| FIN-17 | Every financial operation is idempotent under retry and carries a correlation key. |
| FIN-18 | Ledger replay must exactly reconstruct cash and reserved balances. |
| FIN-19 | A season's finance summary — opening and closing cash plus one total per ledger category — is written once, at rollover, and never edited. |

A club's money is an append-only ledger. Every balance change is a `finance.ledger_entries` row that records
the move — a cash delta and a reserved delta, either of which may be zero — beside the balances it produced,
its category, its source, and the correlation key of the operation it belongs to (`FIN-11`, `FIN-17`).
`finance.club_accounts` is the projection those rows sum to, so opening a club's account records its `FIN-1`
opening balance as the first entry rather than writing a starting value onto the row, and no path moves a
balance without an entry (`FIN-12`, `FIN-18`). A posting is refused where it would take cash or reserved funds
below zero, or reserve more than the cash behind it (`FIN-10`, `FIN-13`); a retried operation carries the same
correlation key and category and collides with its first entry rather than posting a second time. This is the
record the income and expense sources above, the awards, and the market's reservations are added to, each
milestone contributing its own category as it lands.

### 13.4 The income and expense formulas

Each of the sources above is a named value in the rule set (`RULE-1`), so a balancing change is a version bump
rather than a hunt for a constant. The study of the six leagues staying viable over several seasons is what
calibrates the baselines; the shapes below are fixed from this version on.

- **Gate revenue (`FIN-3`)** is what the club's own stadium takes in at a home match (§13.5): for each kind of
  place, the lesser of the places the club has and the crowd that wants them, times that place's ticket price,
  summed. The crowd is the club's fans times 30% (the share who want to come, `STAD-5`) times an attendance
  factor of 8000–12000 basis points that rises in a straight line with the fans' mood (8000 at mood 0, 10000 at calm
  50, 12000 at 100). It is posted for the host club of every published fixture, inside the publication
  transaction, drawn by the fans and mood going into the round: the result moves the fans only after the gate is
  posted, because the crowd came before the match.
- **Weekly sponsorship (`FIN-4`)** is a fixed tier-1 credit halved per tier below the first.
- **Weekly player wages (`FIN-7`)** are the sum of the club's active contracts' weekly wages, charged after the
  Sunday matchday.
- **The weekly operating cost (`FIN-9`)** is a small fixed tier-1 cost halved per tier below the first.
- **Snack bar takings (`STRC-4`)** are the spectators of a home match (the places sold, `STAD-5`) times the snack bar's
  spend for its level, lifted by a result bonus of up to 30% (§13.7). They are posted for the host club of every
  published fixture, in the publication transaction, right after the gate, under the same correlation key.
- **Merchandise takings (`STRC-6`)** are the club's fans times the shop's spend for its level, lifted by a mood
  bonus of up to 25%, and **structure upkeep (`STRC-8`)** is one charge for both buildings. Both are posted in the
  weekly finance run, beside sponsorship, wages and the operating cost.
- **The promotion and position award (`FIN-5`)** is a tier-1 champion's award decayed linearly by final
  position and halved per tier, settled at rollover.
- **The emergency grant (`FIN-16`)** is not a formula: it is the exact shortfall the safety step computes when a
  club cannot cover its next wage run, recorded as its own category and source so it is visible in the ledger.
- **The season finance summary (`FIN-19`)** is the season's ledger movement grouped by category over the
  season's own window, with opening cash read from before the season and closing cash as opening plus the
  category totals. It is written once, at rollover, for reporting; the settlement award (`FIN-5`) is the next
  season's first money and lies outside the window.

### 13.5 The stadium

Every club owns one stadium (`world.club_stadiums`). It is a count of places of four kinds, and everything else —
its size, its level, the picture a manager sees — is read from those counts.

| Ref | Rule |
|---|---|
| STAD-1 | A stadium's **level** is the number of 5,000-place blocks it spans: 1 up to 5,000 places, 2 from 5,001, and so on to **level 10 at 50,000 places**, which is the largest ground there is. Nothing but the places sets the level, so the level and its picture can never disagree with what a manager bought. |
| STAD-2 | Every club opens with **5,000 places**: 3,000 standing, 1,000 seating, 900 covered seating and 100 VIP, whatever its tier. |
| STAD-3 | A ticket costs, at a tier-1 club, **8.00** standing, **15.00** seating, **25.00** covered seating and **90.00** VIP. Prices halve per tier below the first, like every other baseline (`WORLD-4`). |
| STAD-4 | A manager may add as many places as they like, of any kind, up to the top level. A place costs, at a tier-1 club, **300.00** standing, **600.00** seating, **1,000.00** covered seating and **4,000.00** VIP, halved per tier. The club pays from its available cash (`FIN-10`) through one ledger entry (`stadium_construction`), and the order is refused if the club cannot pay or the ground has no room. The ground's version is the entity tag the order carries, so a retried or doubled order is refused rather than paid for twice (`CONC-1`, `FIN-17`). |
| STAD-5 | A home match sells, for each kind of place, the lesser of the places the club has and the crowd that wants them. The crowd is **30% of the club's fans** (a new tier-1 club has 10,000 fans and draws 3,000; tier 2 draws 2,250 and tier 3 1,688, a quarter fewer per tier) times the mood factor of `FIN-3`, so it grows and shrinks with the fans; the crowd splits across the kinds 45% standing, 20% seating, 28% covered and 7% VIP. Places nobody turns up for earn nothing. |
| STAD-6 | Places are only ever added. A club's ground cannot shrink, and a place is paid for once. |

A new tier-1 club's opening crowd of 3,000 sells 2,890 of the 5,000 places (1,350 standing, 600 seating, 840 covered, all 100 VIP) and takes 49,800.00 a home match; the full house is 70,500.00, which a club reaches when its fans have grown. A place that sells out pays for
itself in roughly two and a half seasons (17 home matches); one that does not sell pays nothing, so a manager builds
toward the crowd and not toward the cap. The values are balancing values in `StadiumRuleSet`, to be calibrated with
the rest of the economy.

### 13.6 Fans

Every club, AI clubs included, has one number of **fans** and one **mood**. They decide how many people want to come to a home match (`STAD-5`, `FIN-3`), they grow after good results and shrink after bad ones, and they judge the club against the place they hoped it would finish. The model is the one Hattrick publishes (a mood judged against an expectation, a soft cap per division, ±10% for a change of division, a halfway reset at season end) with the size of each response fitted to the tick engine. ADR-0078.

| Ref | Rule | Value |
|---|---|---|
| FAN-1 | **One fan count per club.** A tier-1 club opens with **10,000 fans**, of whom 30% want to come to a home match (3,000 people); every tier below keeps **¾** of the tier above (tier 2 7,500, tier 3 5,625). There is no spread between clubs of a tier: they pull apart through results. The count never falls below a **floor** of 40% of the tier's opening fans (tier 1: 4,000), the loyal core that never leaves. Above a **soft cap** of 55,000 people who want to come in tier 1 (183,333 fans), a little above the biggest stadium, a club wins no more fans from results, and every season end a club above the cap of the tier it plays in loses 25% of the excess. Every club that exists has a fan base; a missing one is opened on the spot at its tier's opening numbers. | `fan_opening_tier1` 10,000; `fan_want_to_come_share_bp` 3,000; `fan_floor_share_bp` 4,000; `fan_soft_cap_want_to_come_tier1` 55,000; `fan_tier_retained` ¾; `fan_over_cap_drift_bp` 2,500 |
| FAN-2 | **Mood** is 0 to 100 (stored as 0–10,000 basis points) and is shown as a word: Furious 0–10, Angry 10–25, Unhappy 25–40, Calm 40–60, Content 60–75, Delighted 75–90, Euphoric 90–100 (a band includes its lower edge). A club opens Calm, at 50. | `fan_mood_calm_bp` 5,000 |
| FAN-3 | **How the fans size a team up.** Strength is the mean ability of the best eleven of the squad in a 4-4-2, picked as the AI lineup policy picks it: an outfield player counts as the mean of his 24 outfield attributes, a goalkeeper as the mean of his 4 goalkeeping attributes. The fans add the team's form this season, points a game against an even game, converted at 0.22 points a game for each attribute point, and form's weight is `games ÷ (games + 10)`: nothing at the first match, about three quarters at the last. | `fan_form_half_weight_games` 10; `fan_form_points_per_attribute_point_c` 22 |
| FAN-4 | **What the fans expect from a match.** The strength gap, with home advantage, goes through an ordered curve fitted to 1,800 tick-engine matches between the world's own squads (0.34 per attribute point, home worth 1.23 points, draws about 28% in an even game) and gives the chances of a win, a draw and a defeat, and from them the expected points `3 × win + draw`. The label is read from the expected points: **Should hammer them** 2.40 or more, **Expect a win** 2.00, **Should win** 1.60, **Could go either way** 1.20, **A point would do** 0.80, **Tough game** 0.50, **Damage limitation** below that. A predicted score (the most likely score from two Poisson means, home `exp(0.58 + 0.117 × gap)` and away `exp(0.23 − 0.143 × gap)`) is chosen among the scores the label allows, so a "Should win" never predicts a defeat. The expectation is **frozen when the team sheets lock** (`fans.fixture_fan_expectations`; for a fixture the lock never reached, when it is resolved), so the result is judged against what the fans held at kick-off; before the lock the page works it out live from both squads and the table. | `fan-rules-v1`; labels at 240 / 200 / 160 / 120 / 80 / 50 hundredths of a point |
| FAN-5 | **A published match** moves both clubs' fans, after the gate is posted (`FIN-3`). *Mood:* the surprise is the points won minus the points expected; the mood moves by `3 × |surprise|^¾` mood points (a big surprise counts for less than double), **1.5 times as much for a bad surprise**, plus up to ±1 for the goal margin (full at 4 goals), then 2% of the distance back toward 50 and a constant **+0.88** that keeps a club that exactly meets its expectations averaging Calm. *Count, in people who want to come:* **joins** after a win as the underdog (expected under 1.00 points) **600**, an even game (1.00 to 1.59) **450**, as the favourite (1.60 or more) **325**, and after a draw as the underdog **100**; **leaves** after a defeat as the favourite **300** and an even game **250**; nothing joins or leaves otherwise. Each amount carries a random −15 to +50 (never below nothing), drawn from the fixture and the club, so the same match always gives the same number. Amounts scale ¾ per tier. A gain fades out over the last quarter below the soft cap and stops at it. The count never falls below the floor. | `fan_join_*`, `fan_leave_*` as listed; `fan_surprise_size_bp` 300; `fan_loss_weight_bp` 15,000; `fan_pull_to_calm_bp` 200; `fan_neutral_offset_bp` 88 |
| FAN-6 | **A club's very first season** brings extra fans over its 34 matches: **5% of the count for each step the tier is above the third** (tier 1 two steps, about +7% by the end of the season; tier 2 one step, about +3.5%; tier 3 and below none). A promotion or a relegation does not give it back. | `fan_first_season_boost_per_step_bp` 500 |
| FAN-7 | **A change of division** at rollover multiplies the count by **1.10 for each tier climbed** and **0.90 for each tier fallen** (never below the new tier's floor). A club above its new cap is left there, and the season-end drift of `FAN-1` brings it down over the next seasons, which spreads the 5–35% a relegated club loses in real football over time. | `fan_promotion_bp` 1,000; `fan_relegation_bp` 1,000 |
| FAN-8 | **The weekly review.** Every **Sunday at 23:30 UTC**, after the Sunday round and the weekly finance run (`fans.weekly-review:{date}`), the fans judge every club against **the place they hoped it would finish**: until season objectives exist, the club's rank by squad strength in its division when the season began (set at rollover). A club's *gap* is its projected place (by points a game, the published table breaking a tie) minus that place; positive is worse than hoped. The mood moves by `−0.4 × gap` (the gap limited to ±6) plus `1.0 × (last week's gap − this week's gap)`, scaled by games played ÷ 34. A club is reviewed once a week and a club that has played no game is skipped. At **season end** the fans give a last verdict of `−1.5 × the final gap`, and then the mood moves **halfway back to 50**. | `fan_weekly_place_bp` 40; `fan_weekly_trend_bp` 100; `fan_gap_limit_places` 6; `fan_verdict_place_bp` 150; `fan_season_end_keep_bp` 5,000; `fan_weekly_review_utc` Sunday 23:30 |
| FAN-9 | **A new manager gets a clean slate.** When a human manager takes a club over (the first claim, or a return to a club they resigned from), in the same transaction, its fans go to **the tier's opening numbers** (the count, Calm 50, no last week's gap) and the season target is worked out again from the squad as it stands, however well or badly the club was doing. A retried claim resets nothing a second time. A resignation changes nothing: the club goes back to the AI with its fans as they are. | — |
| FAN-10 | **History and reads.** Every change to a club's fans is an append-only row (`Opening`, `Match`, `Weekly`, `SeasonReset`, `Promotion`, `Relegation`, `Takeover`) with the count and mood before and after, and a match, week or season row exists once per club (a retried job adds nothing). The Fan Base page draws satisfaction and fan count from them, starting at the current manager's takeover with a switch for the whole club history. A manager reads their own club's fans in full; anyone reads another club's count and mood word only. A result the manager has not seen yet stays behind the result gate: the page holds back the fans' reaction to it. | — |

The fans' reaction is a result in disguise, which is why it follows the result gate. The crowd that came to a match, and so its gate, depends on the fans and mood going into it, never on the result (`FIN-3`).

**Measured** (M7: one scratch world, the three top tiers of one country, 54 clubs all run by the AI, three consecutive seasons through the real publication pipeline and two rollovers, the weekly review on, no stadium built):

- **The expectation is fair.** The average surprise (points won minus points expected) is between −0.05 and +0.03 points a match in every tier and season, so a club that plays to its squad averages Calm. The end-of-season mood averages 42 to 53 in every tier and season.
- **Mood is wide.** At the end of a season the best three clubs of tier 1 average 82 (Euphoric end) and the worst three 3 (Furious); the spread across a tier is 23 to 32 points, and in a season most tiers see a club touch 0 and another touch 100.
- **Growth in people who want to come, tier 1, season 1** (which includes the first-season boost, about +300 for a club that stays level): finishing 1st to 3rd **+8,830**, 4th to 9th **+5,095**, 10th to 15th **+2,779**, 16th to 18th **+734**. The sandbox targets were +6,085 for a club finishing about 3rd, +3,782 (62% of that) for about 9th and +1,956 (52% of that) for about 15th, and the real league lands on them: a mid-table club grows by about 55–60% of a top-three club and a bottom-half club by about half again. In season 3 the same bands are +6,731, +3,840, +2,464 and +1,768.
- **The fan count** of a tier-1 club that started at 10,000 averages 24,062 after one season (best 44,515, worst 8,221) and, counting the promotions and relegations of two rollovers, 51,070 after three (best 83,945, worst 26,455). Tier 2 goes 7,500 → 16,721 and tier 3 5,625 → 11,703 in a season. No club comes near the soft cap (183,333 in tier 1) or the floor (4,000 in tier 1; the lowest count seen is 8,221) in three seasons.
- **A change of division at rollover:** a promoted club's fans rise 10% (24,893 → 27,382 for the three promoted from tier 2) and a relegated one's fall 10% (12,448 → 11,204 from tier 1); the halfway reset brings a mood of 3 to 25, 13 to 29, and 84 to 68.
- **The gate against the old formula** (a full house for everybody, because the old crowd of 9,000 always filled the 5,000-place opening ground): in season 1 the new gate is **0.84** of the old one in tier 1 (0.96 for the best three, 0.69 for the worst three), 0.75 in tier 2 and 0.68 in tier 3; in season 2 it is 0.98, 0.93 and 0.86; in season 3 it is 1.00, 0.98 and 0.97. A tier-1 club takes about 1.01 million (minor units ÷ 100) a season in season 1 against 1.20 million at a full house.
- **The opening ground is outgrown inside the first two seasons.** Demand passes the 5,000 places at 84% of the best tier-1 clubs' home matches in season 1 and 100% in season 2; by season 3 even the bottom three of tier 1 sell out at every home match (demand 23,001 for the best three, 11,146 for the worst three, against 5,000 places). From then on the gate no longer tells a good club from a bad one: money only follows the fans once the manager builds (`STAD-4`). This is the open question of the plan, and the rules leave it as it is.

### 13.7 Structures

Every club has two more buildings besides its stadium: a **snack bar** and a **merchandise shop**. Each has ten levels. A manager upgrades them with cash and waits for the builders; the buildings earn from the crowd and from the fans, and cost a little each week to keep. The values are provisional and live in `StructureRuleSet` so the planned finance rework can retune them in one place. ADR-0080.

| Ref | Rule |
|---|---|
| STRC-1 | Every club has a snack bar and a merchandise shop. Each opens at **level 1, free**, and goes up to **level 10**. |
| STRC-2 | Only a human manager can order an upgrade. A computer-run club stays at the level it has (level 1). |
| STRC-3 | An upgrade is **paid in full when ordered**, from available cash (`FIN-10`), in one ledger entry (`structure_upgrade`). It takes **1 day** to reach level 2 or 3, **2** for 4 or 5, **3** for 6, **4** for 7, **5** for 8, **6** for 9 and **7** for 10. One upgrade per building at a time; the building keeps trading at its old level until the work is finished, when an inbox message says so. There is no cancelling. The order carries the building's version, so a retried or doubled order is refused rather than paid for twice (`CONC-1`, `FIN-17`). |
| STRC-4 | **Snack bar takings** for a home match = spectators × the spend for the level × (1 + the result bonus). The spectators are the places the match sold (`STAD-5`). The spend per spectator is **1.00** at level 1 and rises by **0.25** a level to **3.25** at level 10, in every tier. They are booked with the gate when the round is published, under the same correlation key. |
| STRC-5 | **The result bonus** is judged against the home side's frozen expectation (`FAN-4`), with the fans' own thresholds (underdog: under 1.00 expected points; favourite: 1.60 or more). A **win as the underdog +30%**, a **win in an even game +15%**, a **win as the favourite +5%**, a **draw as the underdog +10%**. Any other result earns nothing. The cap is 30%. Where no expectation was frozen, the figure for two evenly matched sides with one at home (1.64 expected points, so a favourite) stands in, as it does for the fans. |
| STRC-6 | **Merchandise takings** for a week = the club's fans × the spend for the level × (1 + the mood bonus). The spend per fan per week is **0.30** at level 1 and rises by **0.10** a level to **1.20** at level 10, in every tier. They are booked in the weekly finance run, right after sponsorship. |
| STRC-7 | **The mood bonus** is read from the fans' mood word at the week's close (`FAN-2`): Furious 0%, Angry 0%, Unhappy +2%, Calm +5%, Content +10%, Delighted +18%, Euphoric +25%. The weekly finance run (Sunday 23:00) comes before the fan review (23:30), so the mood is the one the week ended on. |
| STRC-8 | **Upkeep.** Each building costs a weekly upkeep for its level, at a tier-1 club **300.00** at level 1, then 450.00, 600.00, 800.00, 1,000.00, 1,250.00, 1,500.00, 1,800.00, 2,150.00 and **2,500.00** at level 10. Both are booked as **one** entry in the weekly finance run (`structure_upkeep`), after the operating cost, and they count toward the shortfall check that triggers an emergency grant (`FIN-16`). A week with no home match therefore shows a small loss on the snack bar. |
| STRC-9 | **Money scales by tier like every other baseline** (`WORLD-4`): upkeep and the cost of an upgrade are halved per tier below the first. The spend per spectator and per fan is **not** scaled: a lower division earns less because its crowds and fans are smaller. These values are provisional until the finance rework. |

The cost of an upgrade at a tier-1 club, by the level it reaches: **2,000.00** for level 2, then 2,300.00, 2,500.00, 2,900.00, 3,000.00, 3,400.00, 3,500.00, 3,600.00 and **3,800.00** for level 10, halved per tier. It is set so that, at a tier-1 club with its opening crowd and fans, the first upgrade of either building repays itself in about **two weeks** and the last in about **five**, counting the upkeep each step adds: the buildings are there to help a club grow. Bringing both buildings from level 1 to 10 costs 54,000.00; the build time of `STRC-3` paces a manager more than the price does. Each building's history (a sale for each home match, a sale for each week, and each week's upkeep) is kept as append-only rows, which the Structures page reads as the last eight weeks, the last eight home matches and the season's totals.

**Measured** (M6: a probe with no match simulated, one season of 34 matchdays with 17 home matches and 11⅓ weekly runs, both buildings at one level, fans Calm, and a typical home season for the snack bar: 5 wins in an even game, 2 as the underdog, 2 as the favourite, 1 underdog draw, which lifts the base by about 9%):

- **A tier-1 club at its opening numbers** (10,000 fans, 5,000 places, a crowd of 2,890, a gate of 846,600 a season) earns from the two buildings a profit, after upkeep, of **82,510** at level 1 (9.7% of the gate), **167,852** at level 5 (19.8%) and **260,364** at level 10 (30.8%). Bringing both from level 1 to 5 costs 19,400 and to 10 costs 54,000.
- **A big tier-1 club** (40,000 fans, 20,000 places, a crowd of 11,560, a gate of 3,386,400) earns **350,438** at level 1 (10.3%), **739,409** at level 5 (21.8%) and **1,211,457** at level 10 (35.8%).
- **A tier-4 club at its opening numbers** (4,219 fans, a crowd of 1,264, a gate of 53,322) earns **37,659** at level 1 (70.6% of its gate), **79,205** at level 5 and **129,367** at level 10 (2.4 times its gate).
- **Weeks for one step to pay for itself**, snack bar / shop: level 1 to 2 takes 1.9 / 2.2 weeks at a tier-1 opening club, 0.4 / 0.5 at a big one and 0.5 / 0.6 at a tier-4 club; level 4 to 5 takes 3.0 / 3.4, 0.6 / 0.7 and 0.7 / 0.9; level 9 to 10 takes 4.6 / 5.4, 0.9 / 1.0 and 1.0 / 1.2.
- **Every step adds weekly profit** at the opening crowd of every tier tested.
- **A lower division gains much more from the buildings than a higher one**, because the spend per customer is the same in every tier while upkeep and upgrade costs halve per tier. This is left open for the finance rework (ADR-0080).

---

## 14. Scouting and transfers

### 14.1 Scouting

MVP scouting means a **global searchable player database with exact public attributes**,
filters, sorting, player profiles, and private shortlists (`SCT-1`). Attribute uncertainty,
fog of war, and staffed scouting networks are post-MVP (`SCT-2`). Shortlist notes are private
and length-limited (`SCT-3`).

### 14.2 Timed auctions

| Ref | Rule | Value |
|---|---|---|
| TRF-1 | A seller may list an eligible player with a minimum fee. | — |
| TRF-2 | Listings resolve at fixed daily resolution windows with at least **48 hours** of exposure. | — |
| TRF-3 | Auction resolution is never scheduled within **6 hours** before a matchday kickoff. | — |
| TRF-4 | Bids are ascending; the current amount and bidder count are visible. Manager identity may stay hidden until completion. | — |
| TRF-5 | A configured minimum increment is enforced. | — |
| TRF-6 | A club has at most one active bid per listing, and may raise it. | — |
| TRF-7 | The leading bid's funds are reserved transactionally; the former leader's reservation is released when outbid. | — |
| TRF-8 | Resolution uses the highest valid amount. Equal amounts resolve to the **lowest database-assigned `bid_sequence`** (earliest committed), then to the immutable bid ID. | — |
| TRF-9 | On resolution, account status, balance reservation, squad limits, seller minimum squad, active contracts, and listing status are revalidated in one **serializable** transaction. | — |
| TRF-10 | The winning club pays, the seller receives funds, registration changes, the old contract closes, and the generated new contract begins — atomically. | — |
| TRF-11 | A bid failing an invariant is cancelled or skipped per documented rules and the next valid bid is considered. Every outcome is audited. | — |
| TRF-12 | AI clubs list surplus players and bid within valuation, positional need, squad size, and budget bands. | — |
| TRF-13 | Direct offers, private negotiation, loans, swaps, clauses, installments, anti-sniping extensions, and transfer windows are post-MVP. | — |
| TRF-14 | A player with an active listing cannot be listed again. | — |
| TRF-15 | Cancelling a listing releases all active reservations and invalidates its bids with an audited reason. | — |

### 14.3 The AI market

`TRF-12` is implemented as a pure, versioned policy (`ai-market-v1`) run by the worker's daily evaluation.
An AI-controlled club — one no manager holds, active or inactive (`OCC-8`) — reads its own squad and the
listings that were already open before the day began, and decides:

- **What to list.** It trims a squad that is above `ai_market_target_squad_size`, or a family above its
  generator quota (`SQ-1`), choosing the least valuable players first — a lower squad standing, then lower
  ability, then the shorter remaining term. It never lists a player whose sale would take it below `SQ-2`,
  and it lists nobody while it already has a listing open, so one pass cannot flood the market. The asking
  price is the player's value, varied by a factor stable to the club.
- **What to bid.** It buys where it has a positional need — a family below its quota — or where the listed
  player improves on the weakest it already holds in that family; never on its own player or its own
  leading bid, never above the player's valuation, never above a bounded share of its spendable cash
  (`FIN-10`), and never once its squad is at the maximum (`SQ-3`). Each pass is bounded to a handful of
  listings and bids per club.

A player's **value** is a versioned number of weeks of the wage they command (`TRF-12`), moved by bounded
age and potential factors, so a squad, a renewal, and a fee are priced by one family of rules. The value is
class C2 and never reaches a manager-facing response (`MAT-11`); the asking price a listing shows is the
only figure derived from it that a manager sees.

The policy reads no clock, database, culture, or global random source, and every choice it makes is carried
out through the same listing and bid writers a manager's command uses, so `INS-12` holds by the code path:
an AI club is refused by the same eligibility, squad-legality, and affordability rules a human is. Every
decision is recorded in `market.ai_market_decisions` with the digest of the inputs it read and the policy
version. The value and band constants are in §18.

### 14.4 Integrity controls

| Ref | Rule |
|---|---|
| INT-1 | Clients never submit outcomes, ratings, balances, seller identity, player value, or deadlines as authoritative. |
| INT-2 | Listing and bid commands require an idempotency key and verified club ownership. |
| INT-3 | Bid IP/device-risk hashes are recorded for support analysis. Shared households are never auto-punished. |
| INT-4 | Repeated below-value transactions, reciprocal patterns, account-overlap signals, and rapid resign/reclaim behaviour create review cases. Results are never silently altered. |
| INT-5 | Listing and bid commands are rate-limited per manager and per club. |
| INT-6 | Completed market history is public enough for transparency. |

### 14.5 Free agents

A weekly pool of players with no club, sold through the timed auction (`CON-7`, ADR-0077). They are squad depth
for a new or weak team, plus a small chance of a hidden gem to train into a star.

| Ref | Rule | Value |
|---|---|---|
| FA-1 | Every **Monday at 12:00 UTC** (the daily auction window) a batch of generated free agents opens: `round(10% × the players under active contract)` when the batch runs, split across the countries in proportion to each country's contracted players, with that country's nationality and name pool. A batch is unique per world and Monday and its players are a pure function of the world seed and the week, so a retried or late job reproduces the same people and never lists a week twice. A worker that was down runs the batch late, unless the week has already closed. | `free_agent_share_bp` 1,000 |
| FA-2 | Positions are spread **15% goalkeepers, 28% defenders, 29% midfielders, 28% attackers** (exact sums per country by largest remainder), cycling the positions inside a family so a batch has full-backs and wingers as well as centre-backs and strikers. | `free_agent_family_share_bp` 1,500 / 2,800 / 2,900 / 2,800 |
| FA-3 | A generated free agent is **age 20 to 32** (most often the middle) and shows a **1–20 ability of 6 to 10** (most often 8), guaranteed by shifting all 28 attributes together. His hidden potential is his ability plus 0 to 2 at age 23 or under and none after, **never above 11**. | `free_agent_ability_min/max` 6 / 10; age 20–32; potential cap 11 |
| FA-4 | **1%** of the generated players are **hidden gems**: age **17 to 21**, a shown ability of 6 to 10 like any other free agent, and a hidden potential of **13 to 16**. Nothing in any response marks one, and nothing visible or priced reads his potential (`FA-8`). A gem grows only where daily training progression is on. | `free_agent_gem_permille` 10; gem age ≤ 21; potential 13–16 |
| FA-5 | A listing from the pool has **no seller** (it belongs to the batch). It closes at the **next Monday 12:00**, seven days after it opened, when the auction scheduler resolves it: **a leading bid signs the player** (even one placed on the last day), and **a listing with no bid expires** and the player leaves the page. Only **human managers** may bid, and the AI market neither lists nor bids on them. A bid is refused up front when the bidder's squad already holds **27** players (`SQUAD_FULL`); at the close the buyer's squad must still be at **26 or fewer** and the cash still there, else the listing expires. The fee is paid by the buyer and **leaves the game**; no club receives it. The contract starts in the current season, and if the season is in rollover when the listing resolves, the resolution is retried (700 attempts) until the next season is active. **Released players join on top of the 10%**: every player an AI club released at rollover (`CON-9`) or a manager released (`CON-12`) since the previous batch, with his real ability and age, listed once, at the price in `FA-6`. | window Monday 12:00 → Monday 12:00; squad cap 27 / 26 |
| FA-6 | Price is the valuation at the **lowest active tier** of the player's country. A **generated** free agent is valued on his **ability with potential set equal to ability**, and his minimum fee is the larger of the minimum bid increment and **25%** of that. A **released** player is a former club player and is valued **in full, with his real potential**, as a club listing's asking price is: his minimum fee is the larger of the minimum bid increment and **100%** of it. The **weekly wage** is the renewal quote on ability alone for both, frozen on the listing and shown before the bid (`CON-5`). | `free_agent_minimum_fee_bp` 2,500; `free_agent_released_fee_bp` 10,000 |
| FA-7 | The contract is **3 seasons at age 21 or under, 2 at 22 to 29, 1 at 30 or over**, and the player joins as a **prospect** at 21 or under, otherwise as **rotation**. Both depend on age alone, so neither points at a gem. | — |
| FA-8 | A free agent's ability, age and position are public, like any player's. His potential, whether he is a gem, and the batch's gem count are class C2 and never reach a response; a released player's potential moves his fee, as a club listing's does, and is never shown. The scouting card and the public profile read any `FreeAgent` player with no contract, with no club, including a player released since the last batch who has no listing yet. | — |

**Measured** (one scratch world, 108 clubs, 2,592 players; nothing else tuned):

- The first batch listed 259 players, a tenth of 2,592, all showing ability 6 to 10 (mean 8.1) at ages 20 to 32, from 14% goalkeepers, 28% defenders, 30% midfielders and 28% attackers, 43 or 44 in each of the six countries. Minimum fees run 2,500 to 6,143 (median 3,084, about 0.5% of a top club's opening cash) and wages 93 to 311 a week (median 182); 227 of the 259 are on a 2-season deal.
- The first measurement, with the rollover target at 21 and released players at 25%, gave a released pool larger and stronger than the generated one: 318 released (ability 4 to 16, mean 9.0, 64 with potential 13 or more) against 227 generated, at fees of 2,500 to 13,789. Two changes followed (ADR-0077): the target went to 23 and a released player is priced in full.
- After one rollover now (nobody managed a club, so every club released what it could not keep under `CON-9`), 108 contracts expired, one a club. The next batch listed 248 generated players (a tenth of the 2,484 under contract) **and** those 108 released: the weakest expiring player of each club (ability 4 to 11, mean 7.5, mostly aged 18 to 24; 13 with potential 13 or more). Their fees run 5,477 to 31,853 (median 16,074, about 3% of a top club's opening cash) and wages 47 to 344.
- Gems: 0 of 259 in the first batch and 3 of 248 in the second (ages 17, 18 and 18, ability 7 to 8, potential 13 to 14); a unit test asserts 1% +/- 0.3% over 10,000 drawn players.

## 15. Match rules

| Ref | Rule |
|---|---|
| MAT-1 | Matches are simulated only by the worker, only from an immutable input snapshot, and only with a versioned engine. |
| MAT-2 | There is no public command that simulates or influences a match. |
| MAT-3 | Simulation covers 90 regulation minutes plus deterministic stoppage time, modelled as a sequence of possessions. Each half has its own clock and its own stoppage: the first half runs 1'…45' and the second 46'…90', and the second half is never started or ended by the first half's added time. |
| MAT-4 | Goals are produced by resolved chances, never by an independent per-minute roll. |
| MAT-5 | The final score equals the goal events. Statistics reconcile exactly with events. |
| MAT-6 | A manager makes no live in-match changes. A tactical plan may carry up to 10 match instructions: each is a rule written before the match (a minute, a score, an event such as a sending-off, a booking, a tired or badly playing player) with changes it makes when its conditions hold (a new formation, instruction changes, up to 5 substitutions). The engine carries them out; the engine's own fatigue substitutions still run unless the plan switches them off. |
| MAT-7 | A division matchday publishes atomically: either all nine fixtures or none. |
| MAT-8 | Commentary and highlights consume events and cannot change the outcome. |
| MAT-9 | Match input and output hashes are stored; re-running the same snapshot, seed, and engine version reproduces the same output hash. |
| MAT-10 | A void/replay requires an operator reason and uses the original snapshot and seed unless a documented engine defect requires a versioned remediation. |
| MAT-11 | Hidden attributes, the raw seed, internal valuations, and engine diagnostics never appear in player-facing responses. |
| MAT-12 | A dead ball belongs to the side the rules name, and is taken from where the rules put it: the conceding side kicks off after a goal (home the first half, away the second); the defending side restarts from its own goal area after a save, a miss off target, or a missed penalty; the fouled side takes the free kick for a foul with no shot; the defending side takes the free kick for an offside. A restart is consumed by the very next possession and never survives it; a loose ball (a block, a rebound, a cleared corner, a turnover) is contested. |
| MAT-13 | A match instruction is checked at every stoppage and at half-time, in the manager's order, fires at most once a match, and uses no random number. Injuries are dealt with before rules, and the 5-substitution limit (`SQ-5`) and the one-appearance rule still hold: a substitution whose player has left the pitch, is not on the sheet or would exceed the limit is skipped. |
| MAT-14 | A new formation set by a rule is a best fit of the players on the pitch (a deterministic assignment that keeps the goalkeeper in goal, places the manager's own overrides first, and drops the slot whose loss costs least when a player is short). A rule's placements may name a bench player the same rule brings on by name: its substitutions are made before the shape is fitted. A placement in a slot the player is not at home in, or of a player the same rule takes off by name, is a warning, not an error. Humans and AI clubs write rules through the same validator (`INS-12`). |
| MAT-15 | In the tick engine a player's skills fall with his live condition, the one the match centre shows: nothing at 100%, 2% of the sheet value at 90%, 4% at 80%, 7% at 70%, 10% at 60%, 20% at 40% and below (mental skills half as much; pace and acceleration none, because the body already slows). A substitute comes on fresh. The legacy engine (`engine-v11`, stored matches) keeps its own tiredness rule. |

Detailed engine formulas (unit ratings, possession and chance resolution, the continuous passage, bounded
tactical modifiers, and all versioned constants) live in `docs/product/match-engine.md`, which covers
engine version 5. A possession is played as a real passage — a chain of touches from where the last one
left the ball into the attacking third — so a shot, a foul, a free kick, a corner, and every other event
carries the pitch coordinates it actually happened at (ADR-0051). The replay is one continuous film of the
whole match, played at one constant pace in about ten minutes, with a companion highlights reel over the same
data (ADR-0052, ADR-0054).

### 15.1 Player statistics

A published match carries each participant's assists and a match rating (`engine-v2`), and the matchday
publication advances a player's season totals from the result and its events (master plan §6.4). The line
is a projection of published results, like the table, and is read from the stored result rather than
re-simulated.

| Ref | Rule |
|---|---|
| STA-1 | A player's season statistics are advanced by the matchday publication, in the same transaction that publishes the round (`MAT-7`). A republished round advances no line twice. |
| STA-2 | The line is appearances, starts, minutes, goals, assists, shots, shots on target, saves, passes attempted and completed, dribbles attempted and completed, yellow cards, red cards, and average rating, keyed on `(division-season, player, club)`. |
| STA-3 | Goals, assists, minutes, cards, and the match rating are the engine's player line; shots and saves are counted from the match's events. The match rating is the engine's own and is never recomputed by the projection. |
| STA-4 | A player who did not take the pitch produces no line. A penalty has no assister; every other goal has exactly one. |
| STA-5 | A match rating is stored in basis points (0–10,000) and exposed on a 0.0–10.0 scale (`TRN-8`). A player who did not appear is given no rating. |
| STA-6 | A player's season statistics are reconciled by the same rebuild as the table, which recomputes every line from the division-season's published results and removes a line no published result supports. |

A player's profile carries their own line of the same projection (master plan §11.1): the goals, minutes,
cards, and rating the division leaderboard shows for them, read from the stored row rather than recomputed,
so the profile and the leaderboard cannot disagree about a player's season. A player who has not appeared
has no line, and the profile says so rather than showing a row of zeros (`STA-4`). Like the leaderboard and
the table, it is the owning manager's read — the public, unattached profile is a scouting feature.

The same lines, summed across every season a player has appeared for, are the player's **career**, shown on
the profile beside the season (`STA-2`). A career is derived from the season rows — which survive rollover —
rather than stored, and its average rating is recomputed from the summed basis points and rated appearances,
so a season with more rated games carries the weight it should.

---

## 16. Communications

What the game tells a manager, and how (master plan §6.9, §10.7, F-41).

| Ref | Rule |
|---|---|
| COM-1 | A message is addressed to the manager who held the club when the event it describes happened. An AI-controlled club is told nothing; a message outlives the tenure that received it. |
| COM-2 | A message carries a stable template key and its parameters, never stored prose. The English a manager reads is derived from those two, so a message can be re-rendered in another language without being rewritten (`MAT-8`). |
| COM-3 | A message's content is public game data — a result, a league position, a card, an injury, or a repaired side — and never a hidden value (`MAT-11`). |
| COM-4 | A message is written in the same transaction as the event it describes, so a result and the news of it become public together (`MAT-7`). |
| COM-5 | The inbox is per manager and paged by a keyset cursor, newest first. Reading is the only mutation of a message, and reading one that is already read is a no-op. |
| COM-6 | The division news feed is public game data. An item is scoped to a world and optionally to a country and a division, carries a stable template key and parameters like an inbox message, and is paged by a keyset cursor, newest first. It is written in the same transaction as the event it reports. |
| COM-7 | A deadline reminder is sent **once per round**, to every club a human holds in it, when the round's team-sheet lock falls within `reminder_lead_hours` of now. A round that has already locked or published is never reminded. |
| COM-8 | Notification preferences are per manager and govern **email only**. Every switch defaults on, and a manager who has never set one has no row and is treated as all-on. An email is written to the outbox only when the matching preference is on; the inbox message is never suppressed. |
| COM-9 | An email leaves the game asynchronously: the workflow records the intention in `ops.outbox_messages` inside the transaction that produced the event, and the worker's dispatcher sends it later, at least once (`MOD-4`). A transport fault is rescheduled; an unknown message kind, an unreadable payload, or an exhausted attempt budget is dead-lettered and alerted. Auth transactional email is the one exception and stays on the request path. |
| COM-10 | A conversation is between exactly two managers. It has a subject and an ordered list of plain-text messages. It is addressed to managers, not clubs, so it outlives either side's tenure (like `COM-1`). |
| COM-11 | A conversation is started from a club page. The recipient is the manager who holds that club's open tenure at that moment. The sender must hold a club themselves. An AI-run club, your own club, and a manager whose account is closing cannot be messaged. |
| COM-12 | Either participant can reply at any time, with or without a club. A reply to a manager whose account is closing is refused. |
| COM-13 | A subject is 1–`dm_subject_max_chars` and a body 1–`dm_body_max_chars` characters after trimming. Control characters other than a line break are removed. Text is stored as written and always rendered as text, never as markup. |
| COM-14 | A manager may send at most `dm_sends_per_hour` messages (starts and replies together). Over that, the send is refused with 429. |
| COM-15 | A manager can block another manager and later unblock them. While a block exists in either direction, neither can start a conversation with the other or reply to them. The sender is told plainly that the manager is not accepting their messages. Existing messages stay readable. |
| COM-16 | A direct message is unread until its recipient opens the conversation. Opening it marks every message addressed to them in it read. Messages are never edited or deleted by a manager. |

The events that produce an inbox message are the ones the matchday, market, occupancy, and
reminder workflows decide: a club's result, its changed league position, a player's suspension,
a player's injury, a repaired side (`DIS-7`), an outbid or completed transfer, an inactivity
warning, a return-to-AI notice, a welcome, a deadline reminder, and a club's promotion or
relegation at rollover. The news feed carries the
division-provisioned, transfer-completed, and result-published events. A message that is written
but never shown is not built early.

Direct messages (`COM-10`…`COM-16`) are the other kind of message: written by a manager, kept apart from
the game's own in a second Inbox tab, and delivered by the same 60-second `/sync` poll. Nothing is pushed.
The club page names the manager who runs it, or "AI manager", and offers "Send a message" on a human-run
club that is not your own ([ADR-0079](../architecture/adr/0079-direct-messages-between-managers.md)).

---

## 17. Time, identity, and concurrency rules

| Ref | Rule |
|---|---|
| TIME-1 | Real instants are UTC `timestamptz`; APIs expose ISO 8601 with an explicit offset. |
| TIME-2 | Server local time is never used or stored. Time enters through `IClock`; tests use a fake clock. |
| TIME-3 | **Game seasons and game years are not real time.** Player aging, contract years, and progression advance at season rollover. |
| TIME-4 | Deadlines, kickoffs, auction windows, and inactivity are real UTC time. |
| TIME-5 | Responses return absolute deadlines plus server current time so client clock drift cannot mislead a manager. |
| TIME-6 | A compressed or stepped test clock is permitted only in non-production environments; a Production host refuses to start with one configured. |
| TIME-7 | A stepped test clock is frozen at a stored instant and moves only when an operator advances it, to the next game day or the next matchday. The advance is a non-production diagnostics control: the worker sets the instant and materialises the moment's jobs, so no result is produced and no deadline is skipped (`ADR-0049`). |
| ID-1 | Identifiers are server-generated UUIDv7. Clients never propose identifiers for new aggregates. |
| ID-2 | Database names are `snake_case`, C# names are `PascalCase`. |
| CONC-1 | Mutable aggregates carry `version bigint`, exposed as a strong ETag, with `If-Match` required for conflicting updates. |
| CONC-2 | Club takeover, auction resolution, matchday publication, provisioning, and rollover use explicit transactions and, where required, serializable isolation. |
| CONC-3 | Financial, claim, bid, rollover, and publication operations carry explicit idempotency or correlation keys. |
| CONC-4 | Equal-amount or equal-priority outcomes are always resolved by a stable, stored, deterministic key — never by row order or arrival timing. |

---

## 18. Consolidated constant reference

Values referenced by more than one rule. Changing any value here is a rule change (RULE-3).

| Constant | Value | Ref |
|---|---|---|
| `clubs_per_division` | 18 | WORLD-4 |
| `launch_countries` | ENG, ESP, GER, ITA, FRA, ROU | WORLD-2 |
| `matchdays_per_season` | 34 | CAL-1 |
| `kickoff_utc` | 19:00 | CAL-2 |
| `kickoff_weekdays` | Tue, Thu, Sun | CAL-2 |
| `team_sheet_lock_minutes` | 30 | CAL-3 |
| `max_consecutive_home_or_away` | 4 | CAL-9 |
| `rollover_days` | 7 | CAL-6 |
| `points_win` / `points_draw` / `points_loss` | 3 / 1 / 0 | TBL-1 |
| `promoted_per_tier` / `relegated_per_tier` | 3 / 3 | PR-1 |
| `promotion_relegation_version` | `promotion-relegation-v1` | PR-1 (FIC-8) |
| `squad_target` | 24 | SQ-1 |
| `squad_min` | 18 | SQ-2 |
| `squad_min_goalkeepers` | 2 | SQ-2 |
| `squad_max` | 27 | SQ-3 |
| `starters` | 11 | SQ-4 |
| `substitutes_max` | 7 | SQ-4 |
| `substitutions_max` | 5 | SQ-5 |
| `attribute_min` / `attribute_max` | 1 / 20 | TRN-4 |
| `state_basis_points` | 0–10,000 | TRN-5..7 |
| `daily_progression_utc` | 02:00 | TRN-3 |
| `slot_coordinate_scale` | 0–10,000 | TAC-9 |
| `team_instructions` | 8 enumerated | INS-1..INS-8 |
| `injury_absence_fixtures` | 1–6 | DIS-1 |
| `injury_severity_bands` | minor 1–2, moderate 3–4, major 5–6 | DIS-1 |
| `yellow_suspension_threshold` | 5 | DIS-2 |
| `yellow_suspension_matches` | 1 | DIS-2 |
| `red_suspension_matches` | 1 | DIS-4 |
| `contract_seasons_min` / `_max` | 1 / 3 | CON-1 |
| `inactivity_warning_days` | 10 | OCC-1 |
| `inactivity_ai_days` | 14 | OCC-2 |
| `inactivity_close_days` | 21 | OCC-3 |
| `resignation_cooldown_days` | 7 | OCC-4 |
| `provisioning_poll_seconds` | 30 | PYR-10 |
| `provisioning_generator_version` | `division-gen-v2` | PYR-14 |
| `reminder_lead_hours` | 24 | COM-7 |
| `notification_preferences_default` | all on: deadline reminders, inactivity warnings, market messages, news digest | COM-8 |
| `outbox_dispatch_bucket` | 1 minute | COM-9, `MOD-4` |
| `dm_subject_max_chars` | 80 | COM-13 |
| `dm_body_max_chars` | 1,000 | COM-13 |
| `dm_sends_per_hour` | 30 | COM-14 |
| `dm_conversation_page_size` | 25 conversations | COM-10 |
| `dm_thread_page_size` | 50 messages | COM-10 |
| `generator_version` | `world-gen-v3` (the bootstrap: clubs, squads, and fixtures) | FIC-8, PYR-14 |
| `club_generator_version` | `world-gen-v1` | FIC-8, PYR-14 |
| `player_generator_version` | `player-gen-v3` | FIC-8, PYR-14 |
| `schedule_generator_version` | `schedule-gen-v1` | CAL-8, PYR-14 |
| `player_attr_version` | `player-attr-v1` | FIC-8 |
| `player_name_pools_version` | `player-name-pools-v1` | FIC-8 |
| `training_progression_version` | `training-v4` | TRN-9 (FIC-8) |
| `training_base_daily_milli` | 400 thousandths of a point (peak age, neutral aptitude, normal intensity) | TRN-10 (balancing) |
| `training_programme_share_permille` | 600 (the rest, 400, goes equally to the other developing attributes) | TRN-10 (balancing) |
| `development_limit_headroom` | 2 points above an attribute's formula value | TRN-19 (balancing) |
| `world_rule_set_version` | `world-rules-v16` | FIC-8 |
| `match_load_version` | `match-load-v1` | TRN-11, TRN-13 (FIC-8) |
| `ai_policy_version` | `ai-policy-v1` | INS-12 (FIC-8) |
| `ai_match_plan_version` | `ai-match-plan-v1` | INS-12, MAT-6 (FIC-8) |
| `match_statistics_schema` | `match-statistics-v6` (carries the player lines, the assists, the rating, the passes and take-ons, and the tackles, crosses, penalties, free kicks and shots faced) | JSN-1, TRN-11 |
| `season_stats_version` | `season-stats-v3` | STA-1 (FIC-8) |
| `match_rating_scale` | 0–10,000 basis points (displayed 0.0–10.0) | TRN-8, STA-2 |
| `match_rating_base_bp` | 6,000 | STA-5 (balancing) |
| `match_rating_win_bp` / `_draw_bp` / `_loss_bp` | 600 / 120 / 350 | STA-5 (balancing) |
| `match_rating_goal_bp` / `_assist_bp` | 1,000 / 450 | STA-5 (balancing) |
| `match_rating_save_bp` (cap 400) | 60 | STA-5 (balancing) |
| `match_rating_yellow_bp` / `_red_bp` | 350 / 1,400 | STA-5 (balancing) |
| `match_condition_cost_per_minute_bp` | 10 | TRN-11 (balancing) |
| `match_fatigue_gain_per_minute_bp` | 7 | TRN-11 (balancing) |
| `match_stamina_factor_bp` | 12,000 (stamina 1) → 8,000 (stamina 20) | TRN-11 (balancing) |
| `match_intensity_bp` | 10,000 neutral, bounded 6,000–16,000 | INS-9 (balancing) |
| `match_morale_win_bp` / `_draw_bp` / `_loss_bp` | 350 / 40 / 350 | TRN-13 (balancing) |
| `name_pools_version` | `name-pools-v1` | FIC-8 |
| `club_name_capacity_per_pool` | 1,980 (220 × 9) | FIC-4, PYR-11 |
| `squad_composition` | 3 GK / 7 defenders / 9 midfielders (one a left and one a right midfielder) / 5 attackers | SQ-1 (balancing) |
| `player_age_range` | 17–34 game years | SQ-1 (balancing) |
| `ability_mean_tier1` | 13 / 20 | TRN-4 (balancing) |
| `ability_drop_per_tier` | 2 | TRN-4 (balancing) |
| `potential_band_shares` | normal 80% / talent 15% / gem 5% | TRN-18, TRN-20 (balancing) |
| `youth_potential_mean_tier1` / `youth_potential_drop_per_tier` | 13 / 2 | TRN-20 (balancing) |
| `experience_per_match_bp` / `_minimum_minutes` | 50 / 15 | EXP-2 |
| `experience_level_step_bp` / `_levels` | 2,000 / 5 | EXP-3 |
| `experience_skill_bonus_per_level_bp` | 100 (1% of the skill) | EXP-4 |
| `experience_start_age` / `_per_year_bp` / `_cap_bp` | 18 / 400 / 5,000 | EXP-5 |
| `experience_version` | `experience-v1` | EXP-1…EXP-6 |
| `free_agent_generator_version` | `free-agent-gen-v1` | FA-1…FA-4 (FIC-8) |
| `free_agent_share_bp` | 1,000 (a tenth of the players under contract) | FA-1 (balancing) |
| `free_agent_family_share_bp` | 1,500 goalkeepers / 2,800 defenders / 2,900 midfielders / 2,800 attackers | FA-2 (balancing) |
| `free_agent_ability_min` / `free_agent_ability_max` | 6 / 10 | FA-3 (balancing) |
| `free_agent_age_min` / `free_agent_age_max` | 20 / 32 | FA-3 (balancing) |
| `free_agent_potential_cap` | 11 (headroom 0–2 at age 23 or under) | FA-3 (balancing) |
| `free_agent_gem_permille` | 10 | FA-4 (balancing) |
| `free_agent_gem_age_max` / `free_agent_gem_potential_min` / `free_agent_gem_potential_max` | 21 / 13 / 16 | FA-4 (balancing) |
| `free_agent_week_start` | Monday 12:00 UTC (listings close the next Monday 12:00) | FA-5 |
| `free_agent_resolution_max_attempts` | 700 (club listings 8) | FA-5 |
| `free_agent_minimum_fee_bp` | 2,500 of the valuation, never below the minimum bid increment | FA-6 (balancing) |
| `free_agent_released_fee_bp` | 10,000 of the valuation with real potential, never below the minimum bid increment | FA-6 (balancing) |
| `free_agent_contract_seasons` | 3 (age ≤ 21) / 2 (22–29) / 1 (30+) | FA-7 |
| `free_agent_prospect_age_max` | 21 | FA-7 |
| `release_payout_bp` | 5,000 of the wages left on the contract | CON-12 (balancing) |
| `potential_band_offsets` | normal −4..+2 (two four-faced dice less 4) / talent +3..+5 / gem +6..+7, from the tier mean | TRN-18 (balancing) |
| `weekly_wage_minor_per_ability_squared` | 300 minor units | CON-2 (balancing) |
| `tier_scaling_factor` | 2^(tier − 1) | WORLD-4 (balancing) |
| `opening_cash_minor_tier1` | 50,000,000 minor units | FIN-1 (balancing) |
| `opening_reputation_tier1` | 70 / 100 | WORLD-3 (balancing) |
| `stadium_mood_factor_bp` | 8,000 at mood 0 to 12,000 at mood 100, straight line, 10,000 at calm | FIN-3 (balancing) |
| `stadium_seats_per_level` / `stadium_max_level` | 5,000 / 10 (50,000 places) | STAD-1 |
| `stadium_opening_places` | 3,000 / 1,000 / 900 / 100 | STAD-2 |
| `stadium_ticket_price_minor_tier1` | 800 / 1,500 / 2,500 / 9,000 minor units | STAD-3 (balancing) |
| `stadium_build_cost_minor_tier1` | 30,000 / 60,000 / 100,000 / 400,000 minor units per place | STAD-4 (balancing) |
| `stadium_matchday_demand` | the club's fans × `fan_want_to_come_share_bp` (3,000), × the mood factor | STAD-5 (balancing) |
| `stadium_demand_share_bp` | 4,500 / 2,000 / 2,800 / 700 | STAD-5 (balancing) |
| `structure_max_level` / `structure_opening_level` | 10 / 1 | STRC-1 |
| `structure_snack_spend_minor` | 100, 125, 150, 175, 200, 225, 250, 275, 300, 325 minor units per spectator, levels 1 to 10, every tier | STRC-4 (balancing) |
| `structure_merch_spend_minor` | 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 minor units per fan per week, levels 1 to 10, every tier | STRC-6 (balancing) |
| `structure_result_bonus_bp` | 3,000 underdog win / 1,500 even-game win / 500 favourite win / 1,000 underdog draw; cap 3,000 | STRC-5 (balancing) |
| `structure_mood_bonus_bp` | 0 / 0 / 200 / 500 / 1,000 / 1,800 / 2,500, Furious to Euphoric | STRC-7 (balancing) |
| `structure_upkeep_minor_tier1` | 30,000, 45,000, 60,000, 80,000, 100,000, 125,000, 150,000, 180,000, 215,000, 250,000 per building per week, levels 1 to 10 | STRC-8 (balancing) |
| `structure_upgrade_cost_minor_tier1` | 200,000, 230,000, 250,000, 290,000, 300,000, 340,000, 350,000, 360,000, 380,000, for levels 2 to 10 | STRC-3 (balancing) |
| `structure_build_days` | 1, 1, 2, 2, 3, 4, 5, 6, 7, for levels 2 to 10 | STRC-3 (balancing) |
| `fan_rule_version` | `fan-rules-v1` | FAN-4 (FIC-8) |
| `fan_opening_fans_tier1` / `fan_tier_retained` | 10,000 fans / ¾ for each tier below the first | FAN-1 (balancing) |
| `fan_want_to_come_share_bp` | 3,000 (30% of the fans want to come to a home match) | FAN-1, STAD-5 (balancing) |
| `fan_floor_share_bp` / `fan_soft_cap_want_to_come_tier1` | 4,000 of the opening fans / 55,000 people who want to come (183,333 fans) | FAN-1 (balancing) |
| `fan_over_cap_drift_bp` | 2,500 of the excess over the cap, each season end | FAN-1, FAN-7 (balancing) |
| `fan_join_after_win` | 600 underdog / 450 even game / 325 favourite; 100 after an underdog's draw (people who want to come) | FAN-5 (balancing) |
| `fan_leave_after_defeat` | 300 as the favourite / 250 in an even game; none as the underdog | FAN-5 (balancing) |
| `fan_match_random` | −15 to +50 on each join or leave, never below nothing, drawn from the fixture and the club | FAN-5 (balancing) |
| `fan_expectation_bands_c` | underdog below 100; favourite from 160 (hundredths of a point) | FAN-5 (balancing) |
| `fan_gain_fade_share_bp` | 2,500 (gains fade over the last quarter below the soft cap) | FAN-5 (balancing) |
| `fan_first_season_boost_per_step_bp` | 500 of the count for each step the tier is above the third | FAN-6 (balancing) |
| `fan_promotion_bp` / `fan_relegation_bp` | 1,000 / 1,000 for each tier climbed / fallen | FAN-7 (balancing) |
| `fan_surprise_size_bp` / `fan_loss_weight_bp` | 300 per point of surprise to the power ¾ / 15,000 for a bad surprise | FAN-5 (balancing) |
| `fan_margin_max_bp` / `fan_pull_to_calm_bp` / `fan_neutral_offset_bp` | 100 (full at 4 goals) / 200 a match / 88 a match | FAN-5 (balancing) |
| `fan_form_half_weight_games` / `fan_form_points_per_attribute_point_c` | 10 / 22 | FAN-3 (balancing) |
| `fan_label_thresholds_c` | 240 / 200 / 160 / 120 / 80 / 50 (hundredths of a point) | FAN-4 (balancing) |
| `fan_weekly_place_bp` / `fan_weekly_trend_bp` / `fan_gap_limit_places` | 40 / 100 / 6 | FAN-8 (balancing) |
| `fan_verdict_place_bp` / `fan_season_end_keep_bp` | 150 / 5,000 | FAN-8 (balancing) |
| `fan_weekly_review_utc` | Sunday 23:30 UTC | FAN-8 |
| `weekly_sponsorship_minor_tier1` | 3,000,000 minor units | FIN-4 (balancing) |
| `weekly_operating_cost_minor_tier1` | 1,000,000 minor units | FIN-9 (balancing) |
| `position_award_minor_tier1_winner` | 40,000,000 minor units | FIN-5 (balancing) |
| `weekly_finance_utc` | Sunday 23:00 UTC | CON-2 |
| `payroll_risk_weeks` | 4 | FIN-16 (balancing) |
| `listing_min_exposure_hours` | 48 | TRF-2 |
| `auction_blackout_hours_before_kickoff` | 6 | TRF-3 |
| `auction_resolution_utc` | 12:00 UTC daily | TRF-2 |
| `minimum_bid_increment_minor` | 250,000 minor units | TRF-5 (balancing) |
| `player_valuation_version` | `player-valuation-v1` | TRF-12 (FIC-8) |
| `player_valuation_weeks_of_wage` | 60 weeks of the player's wage | TRF-12 (balancing) |
| `player_valuation_age_factor_bp` | 13,000 (≤21) → 6,000 (32+) | TRF-12 (balancing) |
| `player_valuation_potential_factor_bp` | 9,000 + 150 × potential (9,150–12,000) | TRF-12 (balancing) |
| `ai_market_policy_version` | `ai-market-v1` | TRF-12 (FIC-8) |
| `ai_market_target_squad_size` | 23 | TRF-12 (balancing) |
| `ai_market_max_listings_per_club` | 3 per pass | TRF-12 (balancing) |
| `ai_market_max_bids_per_club` | 3 per pass | TRF-12 (balancing) |
| `ai_market_bid_budget_fraction_bp` | 5,000 (half of available cash) | TRF-12 (balancing) |
| `ai_contract_policy_version` | `ai-contract-v2` | CON-9 (FIC-8) |
| `ai_contract_target_squad_size` | 23 | CON-9 (balancing) |
| `retirement_policy_version` | `retirement-v1` | CON-10 (FIC-8) |
| `retirement_announcement_start_age` | 32 | CON-10 (balancing) |
| `retirement_announcement_base_chance_per_mille` | 300 | CON-10 (balancing) |
| `retirement_announcement_step_per_season_per_mille` | 150 | CON-10 (balancing) |
| `retirement_ability_factor_bp` | 6,500 (ability ≥ 16) → 14,000 (ability ≤ 9) | CON-10 (balancing) |
| `retirement_condition_factor_bp` | 8,500 (condition ≥ 8,000) → 12,000 (condition < 5,000) | CON-10 (balancing) |
| `retirement_forced_announcement_age` | 36 outfield / 38 goalkeeper | CON-10 (balancing) |
| `retirement_forced_age` | 37 outfield / 39 goalkeeper | CON-10 (balancing) |
| `refresh_token_lifetime_minutes` | 15 (access) | ADR-0002 |
| `replay_version` | `replay-v16` (the presentation is re-derived from the frozen snapshot, never stored) | MAT-8 (FIC-8) |
| `commentary_version` | `commentary-v3` | MAT-8 (FIC-8) |
| `match_film_seconds` | `clamp(played seconds ÷ 10, 570, 660)` (9:30 target floor, 11:00 hard ceiling that includes the half-time card); median 10:07 | ADR-0006, ADR-0054 |
| `match_film_pace` | 1.8–2.9× real time, one value for the whole film (median 2.53×); quiet play is condensed above 2.3×, the pace may rise to 3.0× before holds are shortened | ADR-0054 |
| `reel_seconds_max` | 720 (12:00) | ADR-0052 |
| `reel_clips_max` | 12 (goals excepted) | ADR-0052 |
| `reel_lead_in_match_seconds` | 600 | ADR-0052 |
| `highlight_target_seconds` | 5–8 (superseded for the film by `match_film_seconds`) | ADR-0006 |
| `match_presentation_payload_budget_kb` | 750 | ADR-0006 |
| `single_highlight_budget_kb` | 75 | ADR-0006 |

---

## 19. Change control

1. A rule change requires an ADR **and** an update to this file in the same change set.
2. Changing anything that affects results, money, fairness, deadlines, or persistent history
   requires a decision before implementation (master plan §17.3).
3. Rule-set and engine versions are recorded on the world, season, and match snapshot
   (RULE-2), so any historical result can be explained by the rules that produced it.
4. `docs/product/game-rules.md`, `docs/architecture/data-model.md`, and the operations runbooks
   must stay synchronized with executable behaviour.

### Open items deliberately deferred (not blockers)

These are recorded so they are not silently invented later:

| Item | Deferred to |
|---|---|
| Calibration of the opening cash, stadium, and reputation baselines and the per-tier scaling | Stage 9 (balancing) |
| Exact gate-revenue, sponsorship, award, and wage formula constants | Stage 9 (balancing), recorded in rule set |
| Exact AI valuation and bidding bands | Specified in §14.3 and §18 (Stage 10, AI market milestone) |
| Collusion review signals (`INT-4`) and market trace views | Stage 14, with the operator surface that would read them |
| Exact training development curve constants and age curve | Stage 4/5 |
| Whether free-agent signing is enabled during MVP | Stage 10, decided by market-health measurement |
