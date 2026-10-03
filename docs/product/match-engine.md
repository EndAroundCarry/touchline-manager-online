# Match engine version 6

> **Status:** Executable specification for `engine-v5` / `engine-rules-v5`, implemented in
> `src/TouchlineManager.MatchEngine`.
> **Applies to:** engine version `5`, engine rules version `5`, rating weights `engine-ratings-v1`,
> tactical modifiers `engine-tactical-v1`, commentary `commentary-v3`, replay `replay-v4`.
> **Version 2** added the assists and the per-player match rating to a result's player lines (§8.1).
> **Version 3** made the play spatial: a possession resolves a loose-ball scramble, a 1v1 ground duel,
> and set pieces against player attributes on a normalised pitch, and samples a live condition and rating
> curve. **Version 4** makes the passage the unit of movement — a possession is played as a real chain of
> touches that starts where the last one left the ball (or at a restart), progresses into the attacking
> third, and ends at an outcome-appropriate point, so event coordinates, shot maps, direct free kicks,
> and the continuous film are meaningful (§7.2, §7.3). The outcome formulas and their calibrated
> distributions are unchanged from version 3 and were re-validated, not re-invented (§12).
> **Version 5** completes the passage. The clock is reset at half-time, so the second half is played from
> 45:00 with its own stoppage (§7.1); a dead ball belongs to somebody, so a kick-off, a goal kick, a keeper's
> ball, and a free kick are each taken by the side the rules name, from where the rules put it, and are
> consumed by the very next possession (`MAT-12`, §7.8); a shot travels to a target its outcome decides
> (§7.3); and the passage recorder carries the half, how the possession ended, the restart it began with, and
> where each event sits (§7.8). Two constants were retuned so the calibrated distributions are unchanged (§12).
> **Behavioural rules:** [`game-rules.md`](game-rules.md) §15 (`MAT-*`) is normative for *what* a match
> must be. This document is normative for *how* version 5 computes it.
> **Decisions:** [ADR-0004](../architecture/adr/0004-deterministic-match-engine.md) (purity, versioning,
> reproducibility), [ADR-0013](../architecture/adr/0013-engine-arithmetic-and-scoreline-effect.md)
> (integer arithmetic, the scoreline effect), [ADR-0051](../architecture/adr/0051-engine-v4-continuous-passages.md)
> (continuous passages, the passage recorder), [ADR-0052](../architecture/adr/0052-replay-v3-film-and-reel.md)
> (the film and the reel), [ADR-0053](../architecture/adr/0053-engine-v5-half-time-clock-and-restart-ownership.md)
> (the half-time clock, restart ownership, the complete recorder), [ADR-0054](../architecture/adr/0054-replay-v4-constant-pace-film.md)
> (the constant-pace film).

Every constant named below lives in `EngineRulesV2` and is covered by the rules hash, so a result can
always be explained by the configuration that produced it. **Changing any value, formula, draw order, or
event semantic is an engine-version change** (`MAT-9`, ADR-0004): the golden hashes move, and the old
version must remain compiled and replayable rather than being edited in place.

---

## 1. Scope and purity

The engine is one public method:

```csharp
MatchResultV1 MatchSimulator.Simulate(
    MatchInputV1 input,
    EngineRulesV2 rules,
    PlayerLiveMetricsRecorder? liveMetrics = null,
    MatchPassageRecorder? passages = null)
```

The two recorders are optional **side channels**: they capture the live condition and rating curve and the
recorded passages the replay is built from (§10), consume no random draw, and change no state, so the
output hash is identical with and without them. They exist so a presentation can be re-derived from the
frozen snapshot rather than stored on the result.

It reads nothing else. No clock, database, network, filesystem, culture, thread scheduling, or
`Random.Shared` (`DEP-2`, `DEP-7`, ADR-0004). The only randomness is `Pcg32`, seeded from the snapshot;
the only time is the snapshot's own clock. Both properties are enforced by tests, not by convention:
`EnginePurityTests` reflects over every field, property, parameter, and return type in the assembly and
fails on a clock or `System.Random`, and the architecture tests fail the build on a forbidden project
reference.

The engine refuses to simulate a snapshot frozen against a different engine version, a different rules
version, or a different formula-configuration hash. Simulating anyway would produce a plausible result
whose stored hashes claim a provenance it does not have — undetectable after the fact, and therefore
worse than a refusal.

---

## 2. Arithmetic: two scales, no floating point

*(ADR-0013.)* There is no `double` anywhere in the simulation.

| Scale | Range | Meaning |
|---|---|---|
| Rating | `0…100` | One attribute point is exactly `AttributeRatingFactor` = 5 units. An attribute of 13 is 65. |
| Basis point | `0…10_000` | 10_000 is certainty, 5_000 is even. Every probability and every multiplier is expressed here. |

Two probabilities compose exactly as `a * b / 10_000` in integer arithmetic. A random decision is always
an integer comparison against a draw from `Pcg32`, never a comparison against a floating-point threshold.
The consequence is that a decision at the end of a long chain depends only on the values that produced
it, and not on the rounding behaviour of any intermediate representation or platform.

Integer division truncates, so a chain of multiplicative modifiers loses a fraction of a basis point per
step. The loss is bounded, deterministic, and pinned by the golden hashes.

---

## 3. Determinism contract

- **Draw order is part of the version.** Every draw happens in a documented sequence. No method iterates
  a hash set or dictionary when the iteration order can reach a decision: candidates for a foul, a shot,
  a header, an offside, and an injury all come from the slot-ordered list of players on the pitch, and
  where a set must be consumed its members are sorted by identity first.
- **One decision, one draw, or a documented fixed number.** A goal consumes one draw and a miss consumes
  two; a card decision consumes exactly one whether or not a card comes out, so the stream advances
  identically either way.
- **Hashing.** `CanonicalMatchSerializer` writes every field as `name=value` on its own line, sorts every
  collection into a fixed order, and formats every number invariantly. Three digests are produced:

  | Digest | Covers | Used for |
  |---|---|---|
  | Content hash | The snapshot's facts, **excluding** the seed | The seed's HMAC input |
  | Input hash | The facts **and** the seed | Stored on the match (`MAT-9`) |
  | Output hash | The result, including the input hash | Stored on the match; the golden-hash contract |

- **The seed.** `MatchSeed.Derive` computes HMAC-SHA256, keyed by the world secret, over the fixture, the
  content hash, and the engine version (`MAT-9`, master plan §8.2). The content hash excludes the seed
  because hashing a seed into its own derivation is circular. `MatchSeed.CommitmentOf` produces the
  publishable commitment; the raw seed is released only under the operations policy (`MAT-10`, `MAT-11`).

---

## 4. Input snapshot (`MatchInputV1`)

An immutable description of one fixture at the moment it locked (`MAT-1`).

| Field | Notes |
|---|---|
| `FixtureId`, `WorldId`, `SeasonId` | Required identities. |
| `EngineVersion`, `RuleSetVersion` | Must match this build. |
| `Home`, `Away` | One `MatchSideV1` each: club, squad, eleven slots, instructions. |
| `HomeAdvantageBasisPoints` | A multiplier on the home side's ratings. |
| `FormulaConfigurationHash` | `EngineConfiguration.HashOf(rules)`; must match. |
| `Seed` | The derived secret seed. |

A side carries up to 18 participants — exactly 11 in the lineup plus up to 7 substitutes (`SQ-4`) — and
exactly 11 slots. A participant carries their identity, name, shirt number, primary and secondary
positions, all 28 attributes on the 1–20 scale (`TRN-4`), and condition, fatigue, morale, and sharpness in
basis points (`TRN-5`…`TRN-7`).

`Validate` refuses, by name: a missing identity, a club playing itself, fewer than 11 or more than 18
players, a lineup that is not 11 slots, a repeated slot number, a slot at an off-pitch or duplicated
coordinate, a slot naming a player who is not in the squad, a participant from another club, a repeated
participant, a role that disagrees with its slot's family, and a side that fields anything but exactly one
recognised goalkeeper.

**No `InputHash` field is trusted from the caller.** The engine computes it from the snapshot it was given.

---

## 5. Lineup resolution and role familiarity (`INS-10`)

Each slot's occupant is resolved once, at kickoff, into a *familiarity* multiplier:

| Case | Multiplier |
|---|---|
| The player's position is one the role naturally asks for | 10_000 (no penalty) |
| Right band, wrong job (a centre back at full back; a winger at striker) | `UnfamiliarRolePenaltyBasisPoints` = 9_400 |
| A secondary position covers the slot's band | `SecondaryPositionPenaltyBasisPoints` = 9_600 |
| Out of position entirely | `OutOfPositionPenaltyBasisPoints` = 8_800 |

Out of position is a **penalty and never a refusal**: `INS-10` makes a makeshift side a legitimate choice
that the engine prices. A formation change moves the slot, not the player, so a team sheet prepared
against a plan version keeps referring to the same eleven positions.

`NaturalPositionsOf(role)` is the sole definition of "in role":

| Role | Natural positions |
|---|---|
| Goalkeeper | Goalkeeper |
| Centre back | Centre back |
| Full back | Right back, left back |
| Wing back | Right back, left back, right winger, left winger |
| Defensive midfielder | Defensive midfielder, central midfielder |
| Central midfielder | Central, defensive, attacking midfielder |
| Attacking midfielder | Attacking midfielder, central midfielder |
| Winger | Right winger, left winger, attacking midfielder |
| Striker | Striker, attacking midfielder |

---

## 6. Unit ratings (master plan §8.4)

Six ratings, each a weighted mean of attributes over the players a weighting table says do that job.
`engine-v6` removed the Set pieces, Fitness, and Cohesion ratings: they were computed on every refresh and
read by nothing (see ADR-0055). Corners, free kicks, and tiredness now run on the individual players (§7.9).

### 6.1 The weighting tables (`UnitRatingWeights`, `engine-ratings-v2`)

| Unit | Attributes (weight) | Bands (weight) |
|---|---|---|
| Build-up | Passing 6, Technique 5, First touch 5, Composure 3, Decisions 3, Vision 3 | GK 1, DEF 4, MID 6, ATT 2 |
| Creation | Vision 6, Passing 5, Technique 5, Dribbling 4, Crossing 3, Decisions 3 | DEF 1, MID 6, ATT 5 |
| Finishing | Finishing 7, Composure 5, Technique 4, Heading 3, Anticipation 3, Pace 2 | MID 3, ATT 7 |
| Defensive pressure | Tackling 6, Work rate 5, Aggression 4, Stamina 4, Anticipation 4, Pace 3 | DEF 5, MID 5, ATT 1 |
| Defensive shape | Marking 6, Positioning 6, Anticipation 4, Decisions 4, Tackling 3, Strength 2 | GK 1, DEF 6, MID 4 |
| Goalkeeping | Handling 6, Reflexes 6, One-on-ones 4, Aerial ability 4, Positioning 4, Composure 2 | GK 1 |

Two rules shape the table. **No single attribute dominates a unit** — each spreads across five or six, so
a striker with one outstanding attribute is not automatically the best striker. **The weights say who does
the job, not who is best at it**: a defender contributes to build-up and a striker barely does, because
that is who touches the ball in that phase. The bands are the bands players are *deployed* in, not their
natural positions.

The table checks itself the first time it is read, so a weighting with no attributes, a non-positive
weight, or a unit nobody contributes to fails the first match simulated rather than dividing by zero.

### 6.2 From attributes to a rating

For each unit, over the players on the pitch (whoever is still on — a sending-off removes one):

```text
playerRating  = Σ(weight × attribute × AttributeRatingFactor × tirednessFactor(attribute)) / Σ(weight)
effective     = playerRating × familiarity / 10_000 × stateMultiplier / 10_000
unitRating    = Σ(bandWeight × effective) / Σ(bandWeight)
unitRating    = unitRating × tacticalModifier / 10_000
unitRating    = unitRating × homeAdvantage? × shortHandedPenalty^missing
unitRating    = clamp(unitRating, 0, MaxUnitRating = 1_150)
```

### 6.3 Effective skill: tiredness and the state multiplier

`EffectiveSkill` is the one definition of what a player can do *now*, read by the unit ratings and by every
duel (§7.9). Position fit, tiredness, and the state multiplier scale each skill.

**Tiredness lowers skills, by family.** The drop grows linearly with the condition lost — none when fresh, the
figure below at zero condition — and physical skills fall furthest, then technical, then mental. Goalkeepers
(and the goalkeeping skills) are exempt.

| Family | Drop at zero condition | At the 60% condition a match typically ends on |
|---|---|---|
| Physical | `TiredPhysicalDropBasisPoints` = 4_000 | −16% |
| Technical | `TiredTechnicalDropBasisPoints` = 2_000 | −8% |
| Mental | `TiredMentalDropBasisPoints` = 1_000 | −4% |

**The state multiplier** covers the other three state values. Each is a linear interpolation between its floor
and ceiling, and they compose multiplicatively. Condition is no longer one of them: it is the drop above.

| Input | Floor | Ceiling |
|---|---|---|
| Fatigue (inverted: freshness) | 8_750 | 10_000 |
| Morale | 9_500 | 10_000 |
| Sharpness | 9_600 | 10_000 |

### 6.5 Tactical modifiers (`engine-tactical-v2`)

Every instruction has a cost as well as a benefit, and this is where that is enforced. Each unit's
modifier is the sum of the applicable deltas, then clamped to `MinTacticalModifierBasisPoints` = 8_800 …
`MaxTacticalModifierBasisPoints` = 11_500 — about ±15% across the whole instruction set, worth roughly
three attribute points.

| Instruction | Effect on units |
|---|---|
| Mentality | Attacking: +creation, +finishing, −defensive shape. Defensive: the reverse. |
| Tempo | High: +creation, shorter possessions, faster fatigue. Low: the reverse. |
| Passing | Short: +build-up. Direct: −build-up, slightly +creation. |
| Width | Wide: +creation, −defensive shape, −build-up. Narrow: the reverse. |
| Pressing | High press: +defensive pressure, +creation, −defensive shape, faster fatigue. Low block: the reverse. |
| Defensive line | High: +build-up, +defensive pressure, −defensive shape. Deep: the reverse. |
| Tackling | Aggressive: +defensive pressure, −defensive shape, **and more fouls, more cards, more suspensions** (see §7.3). Stay on feet: the reverse. |
| Time wasting | Costs creation, buys defensive shape; while it applies (on, or situational and ahead) possessions run longer (§7.9). |

Goalkeeping takes no tactical modifier. The bound is what keeps attributes dominant
(`INS-9`): the worst possible instruction set does not overturn a whole division of quality — a 16-ability
side under the most hampering instructions still out-rates a 9-ability side under the best ones
(`UnitRatingTests.The_bounds_leave_attributes_in_charge`).

---

## 7. Simulation

### 7.1 The clock

90 regulation minutes plus stoppage, played as a sequence of possessions (`MAT-3`). A possession consumes
`PossessionSecondsMin`…`Max` = 17…46 seconds, multiplied by 8_000 at a high tempo or 12_000 at a low one,
floored at `MinEffectivePossessionSeconds` = 6 so the clock always advances. A match is therefore roughly
190 possessions — about 95 per side.

**Each half has its own clock.** The first half runs from 0:00. The second half is begun at 45:00
(`MatchState.BeginHalf` sets `ClockSeconds = HalfTimeMinute × SecondsPerMinute`), so it is played 46'…90' and
then its own stoppage. Carrying the first half's clock across half-time — what versions 1–4 did — started the
second half at about 48', ended its regulation three to five minutes early, and made `TotalMinutesPlayed`
count the first half's stoppage twice. A possession's start is read **before** the clock advances, so the
possessions tile each half with no gap and every one has a positive length. Match seconds therefore restart at
the second half; a recorded possession carries its `Period` for exactly that reason.

Stoppage is drawn per half — `StoppageBaseSeconds` = 150 plus up to `StoppageJitterSeconds` = 60 — and
accumulates as the half is played: 20 s per goal, 25 s per card, 15 s per substitution, 60 s per injury.
It is clamped to 1–10 minutes and the half ends when the clock reaches regulation plus stoppage, at the end of
the possession that crosses it. Measured on even sides the two halves are given 10.4 minutes of stoppage
between them (p05 8, p95 13): about 4 in the first half and 6 in the second, which carries the substitution
windows and most of the cards.

### 7.2 One possession, in order

Each step consumes its draws whether or not it is reached, and the order is the version. The outcome
decisions (the foul, the scramble, progression, the duel, creation, the chance) draw from the match's own
stream; the **geometry** — the ball's path and the participants of the generic touches — draws from a
per-possession derived stream, `new Pcg32(seed * 1_000_003 + ordinal)`, the same pattern `AssistPlanner`
uses. The geometry can therefore never advance an outcome draw or move a distribution (ADR-0051).

Since `engine-v5` a possession **decides first and records second**: the play draws that decide how it
ends — the foul, the scramble, the progression — are taken before the ball's path is written down, so a
possession that ends early records an approach that ends early, and a foul that gives a penalty is recorded
in the box. The only change to the play stream's draw order from version 4 is that a possession that begins
from a restart takes no possession draw (step 1); the foul, the fouler and the card are still the first
three draws, now taken by `RollFoul` and put on the event log by `ApplyFoul` once the foul's place is known.

1. **The side is named or chosen.** A pending restart (§7.8) names the side, with no draw. Otherwise the
   possession is chosen from the two sides' control, where a side's control is
   `BuildUp − opponent.DefensivePressure`. The home share is
   `5_000 + swing(4000 per 1000 differential) + 120`, clamped to **2_000…8_000** so neither side is ever
   shut out of a match.
2. **The clock advances** — its start was read first — and both sides pay the load (§7.5). Once a minute of
   play, both sides' unit ratings are refreshed together, so tiredness, morale, the scoreline, and a man
   down reach them (`MatchState.RefreshRatings`, `engine-v6`).
3. **The substitution planner runs** (§7.6).
4. **The passage is planned.** The possession starts at the restart's spot, or at `MatchState.Ball` — where
   the previous possession left it. `PassagePlanner` draws 3–8 touches that advance the ball toward the far
   goal with lateral drift, so the approach runs from the start to the possession's **pressure point** (the
   middle-to-attacking third), plus every point an outcome could need: the final-third entry point, the box,
   the corner, and the strike targets (§7.3).
5. **The defending side's foul** (`BaseFoulBasisPoints` = 780 per possession, ×13_500 aggressive /
   ×8_200 stay-on-feet, and ±15% at most for the side's Aggression and Tackling, §7.9). A foul ends the
   possession. 40 bp of fouls are **penalties**: the attack is played
   into the box, the defender brings the attacker down, and the ball is set on the spot (§7.3). A foul from
   `FreeKickShootingRangeX` (6_500) onwards becomes a **direct free kick** 4_500 bp of the time
   (`FreeKickAwardBasisPoints`); in range (`FreeKickAttemptBasisPoints` = 3_000) it is struck at goal, and
   otherwise crossed into the box. Any other foul is a quick free kick for the fouled side. The fouler and the
   fouled player are both recorded at the ball.
6. **A loose-ball scramble opens only a share of passages.** `ScrambleOpeningBasisPoints` = 1_500 of
   possessions begin with a genuine 50/50, contested by the players the scramble asks for (pace,
   acceleration, work rate). Winning it, the side carries on; losing it is a hard turnover — the approach is cut
   5–25% of the way along, the defender has the ball, and the defence clears it.
7. **Progression**: `6_200 ± swing(2400)` against the control differential, clamped to **3_400…9_000**. A
   failure cuts the approach 40–80% of the way along. It is an offside
   (`OffsideShareOfTurnoverBasisPoints` = 800) — a through ball to the offside line, ahead of the ball, and a
   free kick for the defending side — or a plain turnover, cleared towards the middle third.
8. **The carrier's 1v1 ground duel** (engine-v3), at the final-third entry point: the carrier is drawn by
   dribbling and the tackler by tackling, each weighted by his band (§7.9), and the winner buys (or loses)
   `DribbleCreationBonusBasisPoints` = 1_200 of creation. A lost duel does not end the passage unless the
   defender fouled (`engine-v6`): a foul brings a free kick, a penalty (`DuelFoulPenaltyBasisPoints` = 300), or
   a card, and ends the possession.
9. **Creation**: `2_400 ± swing(2800)` against
   `(Creation + Finishing/2) − (DefensiveShape + Goalkeeping/2)`, plus the duel bonus, clamped to
   **1_100…6_200**, then multiplied by the scoreline effect (§7.4). A failure is a corner
   (`CornerShareOfFailedCreationBasisPoints` = 1_200) — the ball goes out over the goal line, is set down at
   the flag, and is delivered into the box — or a turnover, cleared towards the middle third. A corner becomes a
   headed chance 3_400 bp of the time, moved by the corner taker's delivery (§7.9); the aerial duel then
   decides whether the attacker gets a shot, and a delivery that is not headed at goal is cleared.
10. **The chance** (§7.3). The ball is played on to the shot point and struck from there; the event is
    stamped where the shot was taken from.
11. **The possession ends.** The injury roll is taken, the ball is placed at the restart's spot when one is
    pending (§7.8), and the passage is closed with its outcome.

### 7.3 Shot resolution

The shooter is drawn weighted by the effective skill the chance asks for — `Finishing` in open play,
`Heading` from a corner — over the outfield players in slot order. The penalty taker is not drawn: it is the
best finisher on the pitch, ties broken by identity.

```text
zoneMultiplier  = central 15_000 | inside 10_000 | wide 8_000
base            = BaseShotGoalBasisPoints (865) × zoneMultiplier / 10_000
contest         = shooter's effective skill − (opponent Goalkeeping rating / AttributeRatingFactor)   (hundredths of an attribute point)
goalChance      = clamp(base + swing(contest, ShotQualitySwingBasisPoints = 1_900 per ShotContestReference = 150 points), 220, 5_600)
```

Through `engine-v5` the swing was applied per 1,000 — the rating-scale reference — to a gap of at most 19 on
the attribute scale, so the whole skill range moved a shot by about a third of a percentage point.
`ShotContestReference` is the shot's own reference (ADR-0055), shared by the save and free-kick contests and
the penalty.

One draw resolves the goal. If it does not score, one further draw splits the failure:

| Outcome | Chance | Event |
|---|---|---|
| Woodwork | `WoodworkShareBasisPoints` = 700 | `Woodwork` |
| Blocked | `BlockedShareBasisPoints` = 2_600 | `ShotBlocked` |
| Saved | `BaseSaveBasisPoints` = 5_000 ± swing, clamped to 2_500…7_500 | `ShotSaved` |
| Off target | the remainder | `ShotOffTarget` |

A penalty is `PenaltyGoalBasisPoints` = 7_600 bp for an average taker and keeper, moved by the taker's
Finishing and Composure against the goalkeeper's Reflexes (`PenaltyQualitySwingBasisPoints` = 3_000 per
reference), clamped to 5_500…9_400, and is its own event pair: `PenaltyAwarded`, then `PenaltyGoal` or `PenaltyMissed`. It
is a *placement* — the ball is set on the spot — and then a strike. The engine records a miss as
`PenaltyMissed` and no more; whether it is shown being saved or put wide is the geometry stream's choice
(`PenaltySavedShareBasisPoints` = 6_000) and cannot move a result.

**Where the strike goes (`engine-v5`).** The strike is a `Shot` waypoint from where it was taken to a target
its outcome decides, and the outcome event is positioned after that waypoint. Every target is a function of
the possession's geometry draws, so the outcome picks one and the draws place it.

| Outcome | The strike ends at |
|---|---|
| Goal | Inside the goal mouth: between the posts, 100 units in, under the bar (altitude < `ShotAltitude`) |
| Saved | The goalkeeper, 120–450 units off the line and between the posts — never behind where the shot was struck from; the keeper's `Save` touch is there |
| Off target | Out of play on the goal line: 150–1_250 beyond a post, or over the bar (`MissOverShareBasisPoints` = 3_500) |
| Woodwork | A post (`PostShareOfWoodworkBasisPoints` = 7_000) or the crossbar, then a rebound 600–1_400 out into the box |
| Blocked | 190–570 units — two to six metres — in front of the shooter |

A **direct free kick** in range is its own resolution (engine-v4). The taker is the best set-piece/finishing
player on the pitch; the ball is placed at the pressure point where the foul was committed, so
`attackingX >= FreeKickShootingRangeX` is genuinely reachable — a foul deep in the attacking third can now
produce a `free_kick_shot`. Its baseline is `FreeKickGoalBasisPoints` = 900, moved by the taker's Set pieces against the goalkeeper's
Reflexes on the shot reference (`FreeKickQualitySwingBasisPoints` = 1_400); a non-goal is saved
(`FreeKickSavedShareBasisPoints` = 4_500), blocked (`FreeKickBlockedShareBasisPoints` = 2_500), or hits the
woodwork (`FreeKickWoodworkShareBasisPoints` = 800). A free kick that is not struck directly is crossed
into the box. Measured on even sides it occurs about **1.5 times per match**, with the goal and shot bands
held (§12).

A side whose goalkeeper has been sent off has no player in the Goalkeeping unit, so `KeeperQuality` is 0
and every shot against them is close to a formality. That is the correct shape: `MAT-6` has no mechanism
for naming a new goalkeeper mid-match.

### 7.4 The scoreline effect

*(ADR-0013.)* A pure function of the current score, applied to the attacking side's creation chance. It
consumes no draw and cannot drift. From `GameStateMarginThresholdGoals` = 2 goals of margin, the leading
side's creation falls by 900 bp per goal of margin beyond the threshold and the trailing side's rises by
600 bp, each clamped at `MaxGameStateModifierBasisPoints` = 3_000.

It exists because independent goals give a Poisson tail: measured over 40,000 matches, removing it
produced **4.2%** of matches with seven or more goals against football's ~2.5%, with the same mean. The
cap is deliberate — a settled game should finish 3-1 rather than 6-1, not become a coin flip.

### 7.5 Fitness

Every possession, every player on the pitch loses condition and gains fatigue, scaled by their side's own
instructions (high tempo ×12_000, low ×8_500, high press ×11_500, low block ×8_000) and gains sharpness.
Half-time gives back 900 bp of condition and sheds 1_200 bp of fatigue. Morale drifts with the scoreline —
+90 bp per goal scored, −70 per goal conceded — bounded to ±600 bp from its kickoff value. The best
**Leadership** on the pitch scales the shift (`engine-v6`): 400 bp per point above `LeadershipReference` = 13,
bounded to 6_000…14_000, so a strong leader sharpens a lift and softens a blow.

Each outfield player's condition loss is also scaled by his own **Stamina** (`engine-v6`): 350 bp per point
from `StaminaReference` = 13, so a 20 tires about 25% slower and a 6 about 25% faster, bounded to 6_000…15_000.
Goalkeepers are exempt. What the lost condition costs is in §6.3.

Condition loss is calibrated so a player who stays on ends near 55–60% and the planner has somebody to
replace. Recovery and load are tuned together: a half-time recovery that undoes a whole half leaves a side
that never tires and a bench that is never used.

### 7.6 Substitutions and the bench

Human managers make no live changes (`MAT-6`), so every substitution is the engine's, and it is
deliberately conservative:

- **Injuries are always dealt with.** An injured player cannot continue; if a substitution is available
  they are replaced, and if not the side finishes a player short.
- **Fatigue changes happen at windows 46, 58, 68, 78, 84.** The most tired player below
  `ConditionSubstitutionThresholdBasisPoints` = 6_800 is replaced by the bench player with the highest
  `familiarity × condition`, provided the replacement is at least
  `MinimumConditionAdvantageBasisPoints` = 1_200 fresher. Without that guard a bench of equally exhausted
  players would burn all five substitutions on changes that help nobody.
- **A maximum of 5 substitutions** per side (`SQ-5`). A sent-off player is never replaced.
- A player is substituted at most once, and a substitute comes from the bench.

### 7.7 Discipline and injuries

A foul's card decision consumes exactly one draw: below `StraightRedPerFoulBasisPoints` = 20 it is a
straight red; below that plus the booking chance (1_600 bp, ×12_500 when tackling aggressively) it is a
booking, and a second booking is a dismissal (`DIS-3`). A dismissed player leaves the pitch for good.

An injury is rolled once per possession across both sides. The chance is
`BaseInjuryPerPossessionBasisPoints` = 12 scaled by the average fatigue multiplier (up to ×21_000 at full
fatigue), capped at `MaxInjuryProbabilityBasisPoints` = 90. The victim is drawn weighted by their own
fatigue, and their absence is 1–6 fixtures (`DIS-1`, `TRN-12`).

### 7.8 Restarts and the passage record (`engine-v5`)

**A dead ball belongs to somebody (`MAT-12`).** One `state.NextRestart` value — side, kind, spot — is set by
every outcome that stops play and **cleared by the very next possession**, which takes its side without a
possession draw:

| Outcome | Next possession | Taken by, from |
|---|---|---|
| Kick-off (start of a half) | `KickOff` | Home in the first half, away in the second; the centre spot |
| Goal (open play, penalty, free kick, corner header) | `KickOff` | The conceding side; the centre spot |
| Saved shot | `KeeperBall` | The defending side; its goal area (`GoalAreaXBasisPoints` = 1_200) |
| Shot off target | `GoalKick` | The defending side; its goal area |
| Missed penalty | `GoalKick` or `KeeperBall` | The defending side; its goal area |
| Foul with no shot | `FreeKick` | The fouled side; where the foul was committed |
| Offside | `FreeKick` | The defending side; where the offside was given |
| Blocked shot, woodwork rebound, cleared corner, crossed free kick, scramble lost, failed progression or creation | none | Contested (step 1), from where the ball is |

A restart never survives its possession: the old model left a goal-area flag pending until that side next had
the ball, so the ball could teleport to a goal area several possessions later. `state.Ball` ends where play
actually continues — the block point, the rebound point, the goal area, or the centre after a goal.

**The recorder** captures each possession as a `MatchPassageV1` and is still a by-product of the same run: it
reads no random draw, writes nothing to the match, and is never hashed, so the output hash is identical with
and without it (`PassageTests.Recording_the_film_never_changes_the_result`). Since `engine-v5` it records:

- `Period` (1 or 2) and a real `StartClockSeconds`/`EndClockSeconds` — the possessions tile each half;
- `Outcome` — scramble lost, progression failed, creation failed, offside, foul, penalty, free kick struck or
  crossed, corner cleared or headed, open-play shot — and `Restart`, the kind of restart it began with;
- `Events` as `(Sequence, FractionBasisPoints)`: an event takes the fraction of the waypoint or touch recorded
  last, so it sits where the ball was when it happened (`EventSequences` stays, derived);
- waypoints of kind `Carry`, `Pass`, `Cross`, `Shot`, `Clearance`, and the new `Restart` (a placement, not a
  movement); and touches for every participant the engine names — the carrier, the passer, the duel pair, the
  shooter, the keeper, the free-kick and penalty takers, both scramble contestants, the fouler and the fouled
  player, the winner and the loser of an aerial duel, and the player caught offside.

---

### 7.9 The skill model (`engine-v6`)

*(ADR-0055.)* Every duel and every rating reads **effective skill** (§6.3): sheet value × position fit × the
tiredness drop for its family × fatigue, morale, and sharpness.

- **Duels read effective skills.** Scores are built in hundredths of an attribute point and the reference is
  scaled to match, so a duel's curve is unchanged for fresh players in position. A player a man short is worth
  `DuelShortHandedPenaltyBasisPoints` = 9_800 per missing player — much milder than the ratings'. The duel's
  weights, the 15%–85% clamp, and the tackling nudge (+6 aggressive, −4 on feet) are rules constants.
- **Duels pick their players by band.** The carrier is drawn by Dribbling × a band weight (defence 1,
  midfield 3, attack 4) and the tackler by Tackling × (defence 4, midfield 3, attack 1), so a striker is rarely
  the tackler. A `DuelContender` carries the player and how many of his side are missing.
- **A lost duel can end in a foul.** The defender's foul chance is `DuelFoulBasisPoints` = 1_200 (×16_000
  aggressive, ×6_000 on feet), moved by his Aggression and Tackling. The possession ends as a penalty
  (`DuelFoulPenaltyBasisPoints` = 300), a free kick in range, or a quick restart, and a card is drawn after.
  The per-possession foul chance was lowered from 1_100 to 780 so a match still has about 21 fouls.
- **Aggression and Tackling set the foul rate.** A side's average Aggression above 13 raises its foul chance
  100 bp a point and its average Tackling above 13 lowers it 100 bp a point, bounded to ±1_500 bp.
- **Corners have a taker.** Drawn from the outfield by (Set pieces + Crossing) / 2. His delivery edge over
  `CornerDeliveryBaseline` = 13 adds to the header contest (`CornerDeliveryAerialWeight` = 4) and moves the
  chance a corner is headed at goal (60 bp a point, 1_500…6_000).
- **Time wasting follows the score and the clock.** Situational applies only while the side leads, on applies
  always (the rating penalty is applied at the minute refresh); while it applies the side's possessions run
  `TimeWastingPossessionSecondsMultiplierBasisPoints` = 12_500 longer, so it has fewer of them.

**What `engine-v6` retuned.** Removing the 1.05 condition ceiling made every rating about 5% smaller and so
every rating differential smaller; `RatingDifferentialReference` 1_000 → 950 restores the ability curve and
`HomeAdvantageBasisPoints` 10_380 → 10_420 the home edge. `BaseShotGoalBasisPoints` 845 → 865 puts goals back
at 2.90, and `ShortHandedPenaltyBasisPoints` 6_400 → 6_700 keeps a sending-off at about 1.4 goals.

## 8. Output

`MatchResultV1` carries the score, per-side statistics, the ordered event stream, per-player lines, the
minutes played, and both hashes.

**Statistics are counted from the event stream, never accumulated in parallel** — that is how `MAT-5`
("the final score equals the goal events; statistics reconcile exactly with events") holds by
construction rather than by discipline. `Shots = on target + off target + blocked + woodwork`, and
`ShotsOnTarget = goals + saves`.

`MatchPlayerLineV1` records who played, for how long, and what happened to them. Playing time is an input
to contract renewal (`CON-3`) and the discipline and injury stages apply suspensions and absences from
these lines, so the line is part of the output contract rather than a convenience.

### 8.1 Assists and the match rating (version 2)

Two facts a season's statistics read from the stored result.

**Assists.** A goal from open play or a corner credits exactly one teammate with the assist, drawn from the
outfield players on the pitch in slot order excluding the scorer, weighted by vision, passing, technique,
crossing, and dribbling. A penalty has no assister. The choice is drawn from a stream derived from the match
seed and the goal's own sequence number, **never from the play stream**: a draw taken from the play stream
would advance every decision after it and move the scoreline distributions the engine was calibrated
against. Crediting an assist therefore changes a player line and the output hash, and nothing else about the
match.

**The match rating** is arithmetic over facts the result already carries — minutes, goals, assists, saves,
cards, and the result — on the 0–10,000 basis-point scale (`TRN-8`), clamped to the rules' bounds. A player
who did not take the pitch is given no rating rather than a low one. The result's contribution is weighted
by playing time; every other term is absolute. The rating is a display value derived from public facts, not
a hidden player value (`MAT-11`), and it is produced once with the result so a season's average has one
definition.

### 8.2 Passes and take-ons (`engine-v7`)

Two more facts the player line carries, because no event carries them either (`ADR-0056`).

**Take-ons.** The 1v1 ground duel a carrier fights once a possession has progressed (§7.2) is a dribble
attempted by the carrier, and a dribble completed when he wins it; a duel the defender ends with a foul is
neither. No draw is added.

**Passes.** A possession is played as phases, so its passes follow from what the phases decided: every leg of
the recorded approach is a pass; a possession that fails to progress ends on a lost pass (the last leg played,
or the first when none was; a scramble lost is a lost 50/50, not a pass); the ball played on to the shot point
is a completed pass when the attack breaks through and a lost one when creation fails; a penalty's run into
the box is a completed pass. Set pieces, a goalkeeper's distribution, and the entry carry are not counted, and
a corner that sets a goal up is the assister's completed delivery. `PassTally` draws each leg's passer from a
stream derived from the match seed and the possession ordinal — **never the play stream** — weighted by
`Passing` for a completed pass and by the complement of `Passing` for the lost one, so the better passer has
the ball more and loses it less. The ball that creates a goal is the assister's, so `Assists <= PassesCompleted`.

Measured over 400 even matches: 328 passes a side at 79.6% completion; 47.9 take-ons a side, 52.6% won.
Counting them moves no event, scoreline, or assist; it changes the player lines, the canonical serialization,
and so the hashes.

Events carry **facts, never prose**. A shot event carries its `QualityBasisPoints` — the goal probability
it was resolved against — so highlight selection can tell a good chance from a bad one. That is a fact
about a shot, derived from attributes the owning manager can already see, and emphatically not a hidden
player value; but it is also not something a player-facing response may carry (`MAT-11`), which is why the
commentary tests hold an allowlist of parameter names and the data-classification test guards the contracts
assembly.

---

## 9. Commentary (`commentary-v3`)

`CommentaryTokenBuilder.Build` turns the event stream into tokens: a stable template key, the facts, a
variant key, and the current English text. The key and parameters are the durable part — storing them
rather than only a sentence is what makes the same match narratable in another language later without
re-simulating it.

Repetition is avoided deterministically: each template has three to five variants and the event's sequence
number chooses between them, so the same match always produces the same words and a long match does not
read as one sentence repeated. A random variant would make the commentary unreproducible while the result
stayed reproducible.

**Build-up beats (engine-v4).** Because a possession now records real touches, `BuildPassage` narrates the
build-up rather than three generic lines: the template families `match.build.pass`, `match.build.carry`,
`match.build.dribble`, `match.build.cross`, `match.build.header`, `match.build.tackle`,
`match.build.interception`, `match.build.save`, and `match.build.chance` describe progressive passes,
carries, dribbles, crosses, headers, tackles, interceptions, saves, and the chance itself. The policy emits
a beat for every meaningful touch and skips filler square passes, so a passage reads as a moving sequence
rather than a cut. The same lines build the full-match log; a passage's tokens are offset into playback
milliseconds, exactly as the events are, so the feed synchronises with the action.

Commentary is a pure function of input and result. It cannot change an outcome (`MAT-8`), and it cannot
reveal a hidden attribute (`MAT-11`): parameters are name/value fact pairs (player names, club, clock) and
the tests hold an allowlist of parameter names.

---

## 10. Replay: the film and the reel (`replay-v4`)

`ReplayDirector.Build(input, result, passages, options, liveMetrics)` re-derives the whole presentation
from the frozen snapshot, the result, the recorded passages (ADR-0051, ADR-0053), and the optional live metric
curve. It is a pure function of those inputs and consumes no draw; the presentation is **never stored**, which is
why a replay revision is a clean contract change rather than a migration (ADR-0052). `replay-v4` replaced the
time warp and the anchor tracks of `replay-v3` (ADR-0054). Every constant below is a field of
`HighlightOptionsV1`: the film's pace is a presentation decision, so none of it can move a result.

**One film at one pace.** `FilmScript` turns each possession into *beats* — carry, pass, lofted pass, cross,
header, shot, clearance, duel, save, placement, and dead-ball holds (restart, goal, card, substitution,
half-time card) — from the recorder's `Outcome`, `Restart`, waypoints, touches and events. An intermediate pass
goes to the teammate who can reach the reception point soonest in the current shape; the participants the engine
named take precedence at their beats; a ground move of 12 m or more becomes *receive → carry 3–10 m → pass*
(the carry's length weighted by Dribbling through a stable hash); a cross is drawn only from a wide final-third
position into the box, and anything else is a lofted pass. A possession that starts away from where the last one
ended without a restart gets a transition beat at physical speed.

`FilmTiming` gives each move its natural real-time length — its distance at the speed of its kind plus
`ControlSeconds` = 0.3 (pass 15 m/s, lofted 20, cross 21, clearance 24, shot 27, header 14, carry 5–7 by
Dribbling, placement 8) — and each hold a fixed film length (kick-off 1.2 s, goal kick 0.8, quick free kick
0.6, set piece 1.2, goal and celebration 4.0, card 1.0, substitution 1.0, half-time card 3.0), and solves **one
pace** for the whole film: `p = motionSeconds / (targetSeconds − holdSeconds)`.

- `targetSeconds = clamp(playedSeconds / 10, 9:30, 11:00)` (`FilmMatchSecondsPerFilmSecond` = 10). **11:00 is a
  hard ceiling** that includes the half-time card; a test enforces it, and a busy match raises the pace before
  it lengthens the film.
- The pace's band is 1.8–2.9× (`MinPaceMilli`, `MaxPaceMilli`). Above `CondensePaceMilli` = 2.3× the *quietest*
  possessions are condensed first — no event, ending outside the final third, not within two possessions of a
  chance — by merging consecutive ground moves by one side into one. Only then may the pace rise, to a 3.0×
  ceiling (`CeilingPaceMilli`), and only then are the holds shortened (to no less than half). Below the floor the
  pace stops at 1.8× and the holds lengthen (to at most twice) toward the target.
- The pace is solved again after the motion is simulated, because a move is lengthened when a player cannot
  reach a constraint at bounded speed; the settling is bounded and deterministic.

**Motion.** `FilmMotion` samples the ball along the beats — a ground pass eases out; a lofted pass, a cross and
a clearance get a parabolic height; a shot's height depends on its outcome — and simulates each player in
real-time units at a 0.1 s step under a speed cap (shape 5.5 m/s, sprint 8, a keeper's dive 10) and an
acceleration cap (4.5 m/s²). `FilmShape` gives each player a target from `TacticalFormationResolver.Orient` using
the side's **real instructions**, shifted toward the ball (about 40% along the pitch, 30% across), compact out of
possession, with one or two pressers on the carrier and the keeper on the line between the ball and the goal.
**Hard constraints override the shape**: the carrier is at the ball, the receiver at the reception point when the
ball arrives, the shooter, the header pair and the fouler and fouled player at their touches, the keeper at the
save point, and set-piece and celebration formations. If a constrained player cannot arrive in time, the
preceding move is lengthened; **no speed cap is ever exceeded**. The only discontinuities are *cuts* — the
kick-off after a goal and the half-time reset — listed in the presentation and played as a 300 ms crossfade.

**Passages and the clock.** The film is cut into passages of about 8–12 s of film (at most `MaxPassages` = 75), split
at personnel changes and at half-time, with boundary keyframes copied exactly. A passage carries its `Period`,
its `Cuts`, and `Clock` keyframes mapping film time to the match second on that half's own clock (second-half
seconds start at 45:00), so the displayed minute at an event is the event's stamped minute. The presentation
carries `PaceMilli`. Players are sampled every 200 ms and the ball every 100 ms while it is in the air.

**Commentary.** A line is read when its beat happens — the pass when it is played, the cross when it is delivered,
the shot when it is struck — and the outcome line 0.6 s after the strike. Build-up lines are at least 2.5 s of film
apart; events always get theirs. The templates are `commentary-v3`'s.

**One reel.** `ReelBuilder` selects the chance clips: **goals always**, plus the best chances by
`QualityBasisPoints` above `MinQualityForShotBasisPoints` = 700, count-capped at `MaxReelClips` = 12 with
goals excepted. Each clip's lead-in reaches back `ReelLeadInMatchSeconds` = 600 *match* seconds, never across
half-time, and is then clamped to 25–70 s of film; a clip ends 1.5 s after its outcome (a goal's clip runs through
its celebration), and overlapping clips merge. If the reel would exceed `MaxReelMilliseconds` = 12:00 the lead-ins
are shortened to their floor first, then the lowest-quality non-goal clips are dropped; **goals survive both**.

**Payload.** `EstimatedPayloadBytes` counts entities (×48), keyframes (×24), narration, commentary, schedule
segments (×48), both lineups, and the live metrics (×64). Each entity is compressed with one tolerance (the ball
20, the players on a ladder) and action keyframes are always kept. If the estimate exceeds `PayloadBudgetBytes` =
750 KB (ADR-0006) the director recompresses the players at widening tolerances (50 → 70 → 90 → 120 → 160) and
sampling intervals (200 → 300 → 400 → 500 → 600 ms) until it fits — deterministic, so the same match always lands
on the same rung.

Entities and tracks are ordered by identifier. The narration names the player and the clock, so the Canvas
is not the only way to follow it; the colours come from a fixed generated palette chosen by club identity,
so no real club's identity can leak (`WORLD-3`). Shirt numbers and sides are on every player entity, which
is what lets a client distinguish teams by more than colour.

**The viewer.** `FilmTimeline` (client) builds a presentation's passages into one continuous track per entity,
roster stints for substitutions and dismissals, a half-aware clock (`45+2'`, `HT`, `46'`, `90+N'`), and the feed,
cards, markers and cuts. One renderer draws global film time for the whole presentation; players follow a
monotone cubic Hermite (Fritsch–Carlson) spline, the ball a straight line, and nothing is blended across a cut.

---

## 11. Configuration reference

Every value below is a field on `EngineRulesV2`, covered by the rules hash. The validation rules are the
engine's own: a probability must lie in `0…10_000`, a multiplier in `5_000…25_000`, an ordered pair must
be ordered, and a rating scale must be able to hold a maximum-attribute player. The **Spatial play
(engine-v3)**, **Passage progression (engine-v4)**, and **Restarts and strikes (engine-v5)** blocks are the
constants the pitch model, the continuous passage, and the complete recorder added; they are re-validated by
their own shape checks (a bounded touch count, a pressure band that reaches the free-kick range, a shot band
wholly inside the final third, altitudes on the ball's 0–100 scale that straddle the crossbar, a miss that
stays on the pitch).

| Constant | Value | Meaning |
|---|---|---|
| `RegulationMinutes` | 90 | Regulation time. |
| `HalfTimeMinute` | 45 | Where the first half ends. |
| `SecondsPerMinute` | 60 | |
| `MinStoppageMinutes` / `MaxStoppageMinutes` | 1 / 10 | Stoppage bounds per half. |
| `StoppageBaseSeconds` | 150 | Base stoppage. |
| `StoppageJitterSeconds` | 60 | Seeded jitter above the base. |
| `StoppageSecondsPerGoal` | 20 | |
| `StoppageSecondsPerCard` | 25 | |
| `StoppageSecondsPerSubstitution` | 15 | |
| `StoppageSecondsPerInjury` | 60 | |
| `PossessionSecondsMin` / `Max` | 17 / 46 | Time one possession takes (16 / 44 before `engine-v5`; §12). |
| `HighTempoPossessionSecondsMultiplierBasisPoints` | 8_000 | High tempo shortens possessions. |
| `LowTempoPossessionSecondsMultiplierBasisPoints` | 12_000 | Low tempo lengthens them. |
| `MinEffectivePossessionSeconds` | 6 | Clock-advance floor. |
| `BasePossessionBasisPoints` | 5_000 | Even split. |
| `PossessionControlSwingBasisPoints` | 4_000 | Swing at a full reference differential (2_400 before `engine-v5`; §12). |
| `PossessionHomeBonusBasisPoints` | 120 | Possession's own home bonus. |
| `MinPossessionBasisPoints` / `Max` | 2_000 / 8_000 | Neither side is ever shut out. |
| `BaseProgressBasisPoints` | 6_200 | Baseline progression out of build-up. |
| `MinProgressBasisPoints` / `Max` | 3_400 / 9_000 | |
| `ProgressControlSwingBasisPoints` | 2_400 | |
| `BaseCreationBasisPoints` | 2_400 | Baseline chance creation. |
| `MinCreationBasisPoints` / `Max` | 1_100 / 6_200 | |
| `CreationSwingBasisPoints` | 2_800 | |
| `OffsideShareOfTurnoverBasisPoints` | 800 | Failed progressions caught offside. |
| `CornerShareOfFailedCreationBasisPoints` | 1_200 | |
| `CornerChanceBasisPoints` | 3_400 | A corner becomes a headed chance. |
| `PenaltyFromFoulBasisPoints` | 120 | A foul is in the box. |
| `GameStateMarginThresholdGoals` | 2 | Where the scoreline starts to matter. |
| `LeadingCreationStepBasisPoints` | 900 | Per goal of margin above the threshold. |
| `TrailingCreationStepBasisPoints` | 600 | |
| `MaxGameStateModifierBasisPoints` | 3_000 | The bound on the scoreline effect. |
| `MinShotGoalBasisPoints` / `Max` | 220 / 5_600 | No chance is impossible or a formality. |
| `BaseShotGoalBasisPoints` | 865 | An average chance, inside channel. |
| `ShotContestReference` | 150 | The attribute-scale gap at which a shot, save, free-kick, or penalty swing is applied in full. |
| `ShotQualitySwingBasisPoints` | 1_900 | Per full reference differential. |
| `CentralZoneMultiplierBasisPoints` | 15_000 | |
| `InsideZoneMultiplierBasisPoints` | 10_000 | The reference case. |
| `WideZoneMultiplierBasisPoints` | 8_000 | |
| `WoodworkShareBasisPoints` | 700 | Of non-goal shots. |
| `BlockedShareBasisPoints` | 2_600 | |
| `BaseSaveBasisPoints` | 5_000 | |
| `MinSaveBasisPoints` / `Max` | 2_500 / 7_500 | |
| `PenaltyGoalBasisPoints` | 7_600 | For an average taker and keeper. |
| `PenaltyQualitySwingBasisPoints` / `PenaltyMinGoalBasisPoints` / `Max` | 3_000 / 5_500 / 9_400 | The taker-versus-keeper swing and its bounds. |
| `CornerDeliveryBaseline` / `CornerDeliveryAerialWeight` / `CornerDeliveryChanceStepBasisPoints` | 13 / 4 / 60 | A corner taker's delivery edge. |
| `CornerChanceMinBasisPoints` / `Max` | 1_500 / 6_000 | Bounds on a corner becoming a headed chance. |
| `ShotZoneCentralPercent` / `InsidePercent` / `WidePercent` | 40 / 20 / 10 | The open-play shot zones: one central, two inside, two wide. |
| `MinPassageTouches` / `Max` | 3 / 8 | Touches a possession's passage is built from. |
| `MinTouchAdvanceBasisPoints` / `Max` | 350 / 1_700 | How far one touch advances the ball. |
| `MaxTouchLateralDriftBasisPoints` | 1_600 | How far a touch may drift across the pitch. |
| `PressurePointXMinBasisPoints` / `Max` | 4_200 / 8_800 | Where the defending side engages; the foul point. |
| `ShotFinalThirdXMinBasisPoints` / `Max` | 8_300 / 9_700 | Where an open-play shot is taken from. |
| `ShotCentralBandYMinBasisPoints` / `Max` | 3_050 / 3_950 | The central shooting band. |
| `ShotInsideBandYMinBasisPoints` / `Max` | 1_700 / 5_300 | An inside-channel shooting band. |
| `ShotWideBandYMinBasisPoints` / `Max` | 500 / 6_500 | A wide shooting band. |
| `OffsideLineXBasisPoints` | 7_400 | Where an offside is given. |
| `TurnoverMiddleThirdXBasisPoints` | 4_800 | Where a plain turnover leaves the ball. |
| `GoalAreaXBasisPoints` | 1_200 | The goal area a keeper's ball or goal kick is taken from. |
| `CrossShareOfPassageBasisPoints` | 2_800 | Share of the final approach that is crossed. |
| `CrossAltitude` / `HeaderAltitude` | 70 / 80 | Ball altitude at a cross and a header. |
| `ShotAltitude` / `ClearanceAltitude` | 30 / 55 | Ball altitude at a shot (and the height of the crossbar) and at a clearance. |
| `FreeKickShootingRangeX` | 6_500 | Distance beyond which a free kick is worth striking. |
| `FreeKickAwardBasisPoints` | 4_500 | Chance an attacking-half foul is a direct free kick. |
| `FreeKickAttemptBasisPoints` | 3_000 | Chance a free kick in range is struck at goal. |
| `FreeKickGoalBasisPoints` | 900 | A direct free kick's baseline goal probability. |
| `FreeKickQualitySwingBasisPoints` | 1_400 | Per set-piece-vs-goalkeeping differential. |
| `FreeKickSavedShareBasisPoints` | 4_500 | Of non-goal free kicks. |
| `FreeKickBlockedShareBasisPoints` | 2_500 | |
| `FreeKickWoodworkShareBasisPoints` | 800 | |
| `ScrambleCutMinBasisPoints` / `Max` | 500 / 2_500 | How far along its approach a lost scramble is cut (`engine-v5`). |
| `ProgressionCutMinBasisPoints` / `Max` | 4_000 / 8_000 | How far along its approach a failed progression is cut. |
| `EntryFractionMinBasisPoints` / `Max` | 3_000 / 7_000 | Where, between the pressure point and the shot point, the ball enters the final third. |
| `FinalThirdEntryXBasisPoints` | 6_700 | The least far up the pitch that entry is. |
| `MinClearanceDistanceBasisPoints` | 600 | The shortest clearance worth recording. |
| `BoxXMinBasisPoints` / `Max`, `BoxYMinBasisPoints` / `Max` | 8_700 / 9_400, 2_400 / 4_600 | The penalty-area band a box foul and a delivery are placed in. |
| `CornerOutOfPlayMinBasisPoints` / `Max` | 300 / 1_500 | How far from the flag, along the goal line, a corner goes out. |
| `SaveDepthMinBasisPoints` / `Max` | 120 / 450 | How far off the line the keeper gets to a shot. |
| `MissWideMinBasisPoints` / `Max` | 150 / 1_250 | How far beyond a post a wide miss crosses the line. |
| `MissOverShareBasisPoints` | 3_500 | Share of off-target strikes that go over the bar. |
| `PostShareOfWoodworkBasisPoints` | 7_000 | Share of woodwork hits that strike a post rather than the bar. |
| `PenaltySavedShareBasisPoints` | 6_000 | Share of missed penalties shown being saved (presentation only). |
| `BlockDistanceMinBasisPoints` / `Max` | 190 / 570 | How far in front of the shooter a block is made (two to six metres). |
| `ReboundDistanceMinBasisPoints` / `Max` | 600 / 1_400 | How far out from the line a ball rebounds off the woodwork. |
| `StrikeLowAltitudeMax` / `OverBarAltitudeMax` | 18 / 75 | Highest altitude of a strike under the bar, and of one over it. |
| `BaseGroundDuelBasisPoints` | 5_000 | A 1v1 ground duel, before attributes. |
| `GroundDuelSwingBasisPoints` | 3_500 | Per full dribble-vs-tackling differential. |
| `BaseAerialDuelBasisPoints` | 5_000 | An aerial contest, before attributes. |
| `AerialDuelSwingBasisPoints` | 3_200 | Per full aerial differential. |
| `ScrambleOpeningBasisPoints` | 1_500 | Share of possessions that open with a loose ball. |
| `BaseScrambleBasisPoints` | 5_000 | A loose-ball scramble, before attributes. |
| `ScrambleSwingBasisPoints` | 3_000 | |
| `DuelDifferentialReference` | 150 | The attribute-scale differential at which a duel swing is full. |
| `DribbleCreationBonusBasisPoints` | 1_200 | Creation bought by winning the carrier's duel. |
| `DuelHomeBonusBasisPoints` | 400 | The crowd's duel bonus, on top of home advantage. |
| `UnderdogVarianceBasisPoints` | 1_200 | The bounded stochastic variance around a duel. |
| `DuelFoulBasisPoints` | 1_200 | Chance a lost ground duel is a foul. |
| `AggressiveTacklingDuelFoulMultiplierBasisPoints` | 16_000 | |
| `StayOnFeetDuelFoulMultiplierBasisPoints` | 6_000 | |
| `DuelMinWinBasisPoints` / `Max` | 1_500 / 8_500 | The clamp on every duel. |
| `AggressiveTacklingDuelScoreBonus` / `StayOnFeetDuelScorePenalty` | 6 / 4 | The tackling nudge, in attribute points. |
| `GroundDuel*Weight`, `AerialDuel*Weight`, `Scramble*Weight` | 4/3/3, 4/3/3, 5/3/2, 3/3/2 | The skills each duel reads. |
| `DuelTackler*Weight` / `DuelCarrier*Weight` | 4/3/1, 1/3/4 | Band weights (defence/midfield/attack) in drawing the two players. |
| `DuelShortHandedPenaltyBasisPoints` | 9_800 | Per missing player, on a duel skill. |
| `DuelFoulPenaltyBasisPoints` | 300 | A duel foul that is a penalty. |
| `ShorthandedConditionLossMultiplierBasisPoints` | 12_500 | Per man short, as cover tires. |
| `BaseFoulBasisPoints` | 780 | Per possession, by the defending side. |
| `FoulSkillReference` / `AggressionFoulStepBasisPoints` / `TacklingFoulStepBasisPoints` / `MaxFoulSkillAdjustBasisPoints` | 13 / 100 / 100 / 1_500 | How Aggression and Tackling move the foul rate. |
| `TimeWastingPossessionSecondsMultiplierBasisPoints` | 12_500 | Longer possessions while time wasting applies. |
| `AggressiveTacklingFoulMultiplierBasisPoints` | 13_500 | |
| `StayOnFeetFoulMultiplierBasisPoints` | 8_200 | |
| `YellowCardPerFoulBasisPoints` | 1_600 | |
| `StraightRedPerFoulBasisPoints` | 20 | |
| `AggressiveTacklingCardMultiplierBasisPoints` | 12_500 | Bookings, separately from fouls. |
| `ConditionLossPerPossessionBasisPoints` | 18 | |
| `HighTempoConditionLossMultiplierBasisPoints` | 12_000 | |
| `LowTempoConditionLossMultiplierBasisPoints` | 8_500 | |
| `HighPressConditionLossMultiplierBasisPoints` | 11_500 | |
| `LowBlockConditionLossMultiplierBasisPoints` | 8_000 | |
| `FatigueGainPerPossessionBasisPoints` | 7 | |
| `HalfTimeConditionRecoveryBasisPoints` | 900 | |
| `HalfTimeFatigueRecoveryBasisPoints` | 1_200 | |
| `SharpnessGainPerPossessionBasisPoints` | 2 | |
| `MaxMoraleDriftBasisPoints` | 600 | How far the scoreline can move morale. |
| `MoraleGainPerGoalBasisPoints` | 90 | |
| `MoraleLossPerConcededGoalBasisPoints` | 70 | |
| `BaseInjuryPerPossessionBasisPoints` | 12 | |
| `FatigueInjuryMultiplierBasisPoints` | 21_000 | At full fatigue. |
| `MaxInjuryProbabilityBasisPoints` | 90 | The cap on the above product. |
| `MinInjuryAbsenceFixtures` / `Max` | 1 / 6 | `DIS-1`. |
| `MaxSubstitutions` | 5 | `SQ-5`. |
| `SubstitutionWindows` | 46, 58, 68, 78, 84 | |
| `ConditionSubstitutionThresholdBasisPoints` | 6_800 | |
| `MinimumConditionAdvantageBasisPoints` | 1_200 | |
| `AttributeRatingFactor` | 50 | Rating units per attribute point. |
| `TiredPhysicalDropBasisPoints` / `Technical` / `Mental` | 4_000 / 2_000 / 1_000 | The drop in a skill at zero condition. |
| `StaminaReference` / `StaminaConditionLossStepBasisPoints` | 13 / 350 | How Stamina scales condition loss, bounded 6_000…15_000. |
| `LeadershipReference` / `LeadershipMoraleStepBasisPoints` | 13 / 400 | How the best leader scales morale shifts, bounded 6_000…14_000. |
| `FatigueFactorFloorBasisPoints` / `Ceiling` | 8_750 / 10_000 | |
| `MoraleFactorFloorBasisPoints` / `Ceiling` | 9_500 / 10_000 | |
| `SharpnessFactorFloorBasisPoints` / `Ceiling` | 9_600 / 10_000 | |
| `MaxUnitRating` | 1_150 | |
| `RatingDifferentialReference` | 950 | The rating differential at which a swing is applied in full. |
| `MaxTacticalModifierBasisPoints` / `Min` | 11_500 / 8_800 | `INS-9`. |
| `OutOfPositionPenaltyBasisPoints` | 8_800 | `INS-10`. |
| `SecondaryPositionPenaltyBasisPoints` | 9_600 | |
| `UnfamiliarRolePenaltyBasisPoints` | 9_400 | |
| `ShortHandedPenaltyBasisPoints` | 6_700 | Per player below eleven. |
| `HomeAdvantageBasisPoints` | 10_420 | A ~4% multiplier on the home side's ratings. |
| `RatingBaseBasisPoints` | 6_000 | Where a player's match rating starts. |
| `RatingWinBonusBasisPoints` / `RatingDrawBonusBasisPoints` / `RatingLossPenaltyBasisPoints` | 600 / 120 / 350 | The result's contribution, weighted by minutes. |
| `RatingGoalBonusBasisPoints` / `RatingAssistBonusBasisPoints` | 1_000 / 450 | Per goal and per assist. |
| `RatingSaveBonusBasisPoints` / `RatingMaxSaveBonusBasisPoints` | 60 / 400 | Per save and its cap. |
| `RatingYellowPenaltyBasisPoints` / `RatingRedPenaltyBasisPoints` | 350 / 1_400 | Per booking and per sending-off. |
| `RatingMinBasisPoints` / `RatingMaxBasisPoints` | 1_000 / 10_000 | The clamp on a rating. |
| `LiveRatingBaseBasisPoints` | 6_000 | Where a player's live rating starts (`engine-v3`). |
| `LiveRatingTackleBonusBasisPoints` / `TackleLostPenaltyBasisPoints` | 120 / 80 | A tackle won and lost. |
| `LiveRatingAerialBonusBasisPoints` / `AerialLostPenaltyBasisPoints` | 80 / 50 | An aerial duel won and lost. |
| `LiveRatingShotBonusBasisPoints` / `ShotMissPenaltyBasisPoints` | 100 / 40 | A shot on and off target. |
| `LiveRatingGoalBonusBasisPoints` / `AssistBonusBasisPoints` | 800 / 450 | A goal and an assist. |
| `LiveRatingSaveBonusBasisPoints` / `GoalConcededPenaltyBasisPoints` | 250 / 250 | A save and a goal conceded. |
| `LiveRatingYellowPenaltyBasisPoints` / `RedPenaltyBasisPoints` | 200 / 1_200 | A booking and a sending-off. |
| `MinLiveRatingBasisPoints` / `MaxLiveRatingBasisPoints` | 3_000 / 10_000 | The live-rating clamp (pinned constants). |

Two constants are deliberately **not** fields on the rules, because they are contract values rather than
balance values: `Certain` (10_000) and `SlotCoordinateScale` (10_000, `TAC-9`).

---

## 12. Measured behaviour

The simulation laboratory (`tools/simulation-benchmarks`) is the tuning tool and the evidence.

```bash
dotnet run --project tools/simulation-benchmarks -c Release -- all 20000
```

Run over **20,000 matches** between evenly matched 13/20 sides, on a 16-logical-core Windows machine
(`engine-v6`, rules hash `e090db39…`):

| Measure | Measured | Target |
|---|---|---|
| Goals per match | 2.90 | 2.5 – 3.0 |
| Home / away goals | 1.58 / 1.32 | 1.3 – 1.9 / 1.0 – 1.5 |
| Home win / draw / away win | 43.6% / 24.7% / 31.8% | 40 – 50 / 20 – 30 / 25 – 35 |
| Shots per match | 27.2 | 20 – 32 |
| Home possession | 52.1% | 50 – 54 |
| Fouls per match | 21.4 | 18 – 26 |
| Yellows per match | 3.40 | 3.0 – 5.0 |
| Reds per match | 0.28 | 0.10 – 0.35 |
| Injuries per match | 0.43 | 0.20 – 0.60 |
| Penalties per match | 0.24 | 0.15 – 0.40 |
| Substitutions per match | 7.7 | 4.0 – 10.0 |
| p99 total goals | 7 | 6 – 8 |
| Matches with 7+ goals | 2.79% | < 3.0% |

Calibration invariants (10,000 fixtures): home advantage worth **+4.0 points** (target ~+4); a three-ability-
point favourite upset **17.2%** of the time (target ~15; `engine-v5` measured 16.4% on the same sample size,
a standard error of about 0.8 points); a side sent off early finishes **1.45 goals** worse (target ~1.2,
band 0.9 – 1.5, measured over 2,000 fixtures); a high-pressing side is measurably more tired by the 80th
minute (gap ~1,730 bp) and a fresh substitute measurably fresher than the tired defenders (~1,783 bp). Free
kicks in shooting range occur about **1.5 per match** and fouls in the final third now come from the duel.

The `engine-v6` run holds every `engine-v5` band, and the shape changed where the audit said it should: a
better finisher or goalkeeper counts at the shot, a tired player is worse, and a poor tackler fouls more.
`engine-v6` retuning is in §7.9.

**What `engine-v5` retuned, and why.** Giving the second half its true length, and letting the fouled side keep
the ball after a foul, raised every volume statistic about five per cent over `engine-v4` — goals 3.05 and
shots 28.8 per match, 3.7% of matches with seven or more goals — while goals per shot stayed at 10.6%: two bands
had moved. Dead-ball ownership also flattened the ability curve, because a saved shot now hands the ball to the
defender by rule instead of by a draw that favoured the stronger side (a three-point underdog won 18.7%). Two
constants restore the calibration without touching an outcome probability: `PossessionSecondsMin`/`Max`
16/44 → 17/46 keeps the number of possessions in a match where the calibration put it (about 193 now), and `PossessionControlSwingBasisPoints` 2,400 → 4,000 gives the stronger side its
possession edge back. Substitutions per match rise from 7.0 to 7.7, inside the band either way: the window minutes are the same,
but the first half's stoppage is now played before the second half's windows instead of being counted inside
them.

What the matches are made of (2,000 matches, `engine-v5`): about **193 possessions** a match, of which 78% begin
from play, 11% are free kicks (20.5 a match), 4.3% each goal kicks and keeper's balls (8.3 a match each), and 2.5%
kick-offs (4.9). By outcome: open-play shots 24.0 a match, corners 9.0 (1.5 headed), free kicks struck 1.5 and
crossed 3.5, penalties 0.3, offsides 4.8, quick free kicks 16.0, and turnovers (scramble, progression, creation)
134 a match.

The replay over **2,000 matches** (ADR-0054; `replay-v4` on `engine-v5`, `simulation-benchmarks -- replay 2000`):

| Measure | p05 | p50 | p95 | min / max |
|---|---|---|---|---|
| Passages per match | 60 | 63 | 66 | — / 69 |
| **Film minutes** | 9.90 | **10.12** | 10.41 | 9.74 / **10.74** |
| Reel minutes | 5.61 | 6.95 | 8.13 | — / 9.11 |
| **Pace** (× real time) | 2.28 | **2.53** | 2.83 | — / 3.00 |
| Real-time motion (min) | 20.6 | 22.5 | 24.8 | — |
| Holds (min of film) | — | 1.23 | 1.58 | — |
| Payload estimate (KB) | — | 722.0 | 746.8 | — / 750.0 |
| Payload JSON (KB, `System.Text.Json`) | — | 2,095 | 2,172 | — / 2,190 |

**100%** of films land inside 9:00–11:00 and **0.00%** exceed eleven minutes; **98.2%** are played inside the
1.8–2.9× band; the reel is always inside its 12:00 cap. Outside the cuts (3.8 a match) there are **0 teleports**
in 2,000 matches, the ball stands still for a median 2.5% of the film outside the holds (p95 3.3%), and no
player moves faster than the sprint cap times the pace (keepers at most 0.80 of the dive cap). Quiet play is
condensed in every match — 41.5% of possessions at the median (p95 49.2%) — and a move is lengthened to meet a
constraint by a median 18.4% (p95 21.4%). The ball moves at a median of 23 m/s of film in a pass (p95 39), 37 in a
lofted pass, 45 in a cross, 44 in a shot. The payload sits on the third rung of the ladder in 78% of matches and
the fourth in 19%; the estimate reaches the budget at the maximum and leaves little headroom. A match takes 96 ms
end to end (simulate, film, serialise) on the benchmark machine.

*Why the pace is 2.5× and not 2.2×.* A match's moves add up to about 26 minutes of real time before any is condensed
(22.5 after), and a ten-minute film has about 8.8 minutes left once its holds are paid for. Holding 2.2× would
make the film 11:26 even with the quiet play condensed. The film's length and its pace are one trade-off, set by
`FilmMatchSecondsPerFilmSecond` and `CondensePaceMilli` (ADR-0054).

Performance, 5,000 matches after a warm-up (`engine-v5`):

| Measure | Value |
|---|---|
| p50 per match | 0.76 ms |
| **p95 per match** | **4.07 ms** (budget: 100 ms) |
| p99 per match | 8.1 ms |
| Mean per match | 1.32 ms |
| Allocated | ~3.0 MB per match |
| Throughput | ~759 matches/sec, single-threaded |

A nine-fixture division matchday is therefore about 12 ms of simulation at the mean and about 37 ms at the
p95, well inside the per-match budget. Throughput comes from running *independent* fixtures concurrently;
one match is always simulated single-threaded (ADR-0004).

`DistributionTests` checks a smaller version of the same bands — two thousand matches, in a couple of
seconds — so a formula change fails in CI rather than at the next tuning session. Its bands are
deliberately wider than the targets above: a test that failed on sampling noise would be switched off, and
a test that is switched off catches nothing.

---

## 13. Test map

| Suite | What it pins |
|---|---|
| `Pcg32Tests` | The pinned sequence, bounded draws, uniformity, stream and seed independence. |
| `EngineRulesTests` | Every constant validated, and every constant covered by the hash. |
| `CanonicalSerializationTests` | Order-independence, content-vs-input hashing, output-hash sensitivity. |
| `MatchInputValidationTests` | Twenty malformed snapshots refused by name. |
| `EngineFuzzTests` | 150 randomised valid snapshots bounded; 12 isolated mutations refused. |
| `DeterminismTests` | **The golden hashes**, repeatability, seed sensitivity, attribute sensitivity. |
| `MatchInvariantTests` | Score equals goals, statistics reconcile, times ordered, substitutions legal, degraded paths. |
| `UnitRatingTests` | All 10,935 instruction combinations bounded; attributes dominate; freshness, home advantage, ten men, out of position. |
| `CommentaryTests` | One line per event, variant rotation, build-up families, no hidden value by allowlist. |
| `PassageTests` | One passage per possession; waypoints and touches on the pitch and in fraction order; every shot in the attacking third and free-kick shots in range; touches naming match participants; recorder determinism; the with/without-recorder hash equality. Since `engine-v5`: the possessions tile each half; events ordered and positioned; an outcome that tells the truth; a goal inside the goal mouth, a save at the keeper, a miss out of play, the woodwork and its rebound, a block two to six metres out, a penalty placement, a corner's path, a free kick's placement, the fouler and the fouled player, the scramble contestants, and the header pair. |
| `HalfTimeClockTests` | `MAT-3`: the second half kicks off at 46'; each half plays its own regulation and stoppage; event minutes are 1'…45'+N and 46'…90'+N; `TotalMinutesPlayed` counts only the stoppage the clock used; substitutions at the planner's windows; the live metrics cover every minute. |
| `RestartOwnershipTests` | `MAT-12`: the right side kicks each half off; every dead ball is taken by the side that owns it and by nobody else; a goal is followed by the conceding side's kick-off; a save is the keeper's ball and a miss a goal kick; a foul or offside gives the free kick to the right side; a loose ball is not a restart; a goal-area start only ever follows a keeper's ball or a goal kick; possessions join except at a placement. |
| `ReplayDirectorTests` | `replay-v4`: one contiguous schedule; the film between 9:00 and 11:00 and never longer, with a median near ten minutes; a short film is a faster one, not a longer one; one pace inside its band; the ball and the players never faster than their caps times the pace outside a cut; each half on its own clock, the second starting at 45:00; the displayed minute at each event is its stamped minute; boundary frames joined except at a cut; cuts only at a kick-off and the interval; on-pitch, in-passage keyframes; the eleven and the ball with a track each; `MAT-11`-safe commentary read when the beat happens; the reel carrying every goal; determinism; the payload budget. |
| `FilmScriptTests`, `FilmMotionTests` | Every possession scripted into contiguous beats that join except at a cut; a cross only from a wide position into the box; restarts taken by the owning side; the players the engine named at their beats; a goal followed by its celebration and a cut; no teleports; receivers at the ball when it arrives; a carrier at the ball; the keeper at a save; a goal ending in the goal mouth; the ball never left standing outside the holds; fixed hold lengths; quiet play condensed before the pace rises. |
| `BallPlayStatisticsTests` | `engine-v7` (§8.2): a completed count is a nonnegative subset of its attempted one; nobody who did not take the pitch passed or dribbled; an assist is a completed pass; counting is repeatable; a side's volumes and completion rates read like football; a better passer has the ball more and completes a higher share. |
| `HighlightTests` | Reel selection: goals always shown, the quality floor, the count cap and its goal exception. |
| `EnginePurityTests` | No clock, no `System.Random`, no IO; exactly one source of randomness. |
| `DistributionTests` | The statistical bands, over two thousand matches. |
