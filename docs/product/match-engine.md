# Match engine version 12 (the tick engine) and version 11 (the possession engine)

> **Version 12** is a different kind of engine: it plays the match tick by tick with twenty-two players and a ball, and it is the engine every new match
> is played on. It is specified in [§14](#14-engine-version-12-the-tick-engine) and decided in
> [ADR-0066](../architecture/adr/0066-engine-v12-discrete-tick-match-engine.md). Sections 1 to 13 describe the possession engine, `engine-v11` /
> `engine-rules-v10`, which stays compiled and is what a snapshot frozen against `engine-v11` is played by; the rules constants in §11 are shared
> (discipline, injuries, substitutions, ratings), the possession model in §7 is not used by version 12.

> **Status:** Executable specification for `engine-v11` / `engine-rules-v10`, implemented in
> `src/TouchlineManager.MatchEngine`.
> **Applies to:** engine version `11`, engine rules version `10`, commentary `commentary-v3`, replay `replay-v16`.
> The rating weights and tactical modifiers carry their own versions (§6.1, §6.5).
> **Engine versions 6 to 10** are described in the sections that name them: the skill model (§7.9, `engine-v6`),
> ball-play statistics (§8.2, `engine-v7`), the pass focus (§7.x, `engine-v8` and `engine-v9`), and positions and the
> receiver chain (§7.10, `engine-v10`), and the counter-attack (§7.11, `engine-v11`).
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

### 6.5 Tactical modifiers (`engine-tactical-v4`)

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
| Counter-attack | On: −120 build-up and −60 defensive shape (`engine-v11`); the benefit is in the counters themselves (§7.11). |
| Pass focus | Centre: +build-up, +finishing, +defensive shape, −creation. Wings: +creation, −build-up, −finishing, −defensive shape. Centre with a flank: a smaller +build-up, +creation and +finishing, and −defensive shape (the flank it leaves alone is thin). Left and right cost the same. |
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
7. **Progression**: `6_200 ± swing(2400)` against the control differential, plus the chain's nudge (§7.10,
   at most `ChainProgressSwingBasisPoints` = 700 either way), clamped to **3_400…9_000**. A
   failure cuts the approach 40–80% of the way along. It is an offside
   (`OffsideShareOfTurnoverBasisPoints` = 800) — a through ball to the offside line, ahead of the ball, and a
   free kick for the defending side — or a plain turnover, cleared towards the middle third.
   When the progression succeeds, the holder at the end of the approach may take a **shot from distance** instead
   (§7.10, `engine-v10`): then there is no step 8 or 9, and the shot is struck from where he stands (§7.3).
8. **The carrier's 1v1 ground duel** (engine-v3), at the final-third entry point: the carrier is the player
   the ball was played in to, or the holder who carried it in himself (§7.10), and the tackler is drawn by
   tackling, weighted by his band (§7.9), and the winner buys (or loses)
   `DribbleCreationBonusBasisPoints` = 1_200 of creation. A lost duel does not end the passage unless the
   defender fouled (`engine-v6`): a foul brings a free kick, a penalty (`DuelFoulPenaltyBasisPoints` = 300), or
   a card, and ends the possession.
9. **Creation**: `2_400 ± swing(2800)` against
   `(Creation + Finishing/2) − (DefensiveShape + Goalkeeping/2)`, plus the duel bonus and the chain's nudge
   (§7.10, at most 1_100 either way), clamped to **1_100…6_200**, then multiplied by the scoreline effect (§7.4)
   and, for an approach that ended in a cross, by `CrossCreationMultiplierBasisPoints` = 12_300. A failure is a corner
   (`CornerShareOfFailedCreationBasisPoints` = 1_200) — the ball goes out over the goal line, is set down at
   the flag, and is delivered into the box — or a turnover, cleared towards the middle third. A corner becomes a
   headed chance 3_400 bp of the time, moved by the corner taker's delivery (§7.9); the aerial duel then
   decides whether the attacker gets a shot, and a delivery that is not headed at goal is cleared.
10. **The chance** (§7.3). The ball is played on to the shot point and struck from there, or, for an
    approach that ended in a cross, delivered into the box and headed (§7.3); the event is stamped where the shot
    was taken from.
11. **The possession ends.** The injury roll is taken, the ball is placed at the restart's spot when one is
    pending (§7.8), and the passage is closed with its outcome.

### 7.3 Shot resolution

The shooter is drawn weighted by the effective skill the chance asks for — `Finishing` in open play,
`Heading` from a corner — times his **Positioning edge** (`engine-v10`, §7.9), over the outfield players in slot
order. In open play the player the ball was played in to (§7.10) has his weight multiplied again by
`ShooterChainBonusBasisPoints` = 25_000, because he has it, and the draw is still one. The penalty taker is not
drawn: it is the best finisher on the pitch, ties broken by identity.

**Crosses are headed** (`engine-v10`, ADR-0061; this supersedes the statement in ADR-0059 that a cross does not
change the chance that follows it). When the approach ended in a cross and creation succeeded, the player the ball
was played in to puts it into the box (a `Cross` waypoint at `HeaderPoint`, at `HeaderAltitude`) and an aerial duel
decides the chance:

- **Who goes up.** The attacker is drawn from the others (the crosser never heads his own ball), weighted by
  Heading × his Positioning edge × how near the formation puts him to the ball; the marker is drawn the same way
  from the defending side with the defender's edge (§7.9). How near is `ReachWeightFloorBasisPoints` = 500 at the
  limit of his reach to all of his weight at the ball, falling off in a straight line, and a defender is
  held to that floor for any ball beyond `DefenderReceiveMaxPointX`, however far the formation has pushed the block
  up, so a centre half is a twentieth as likely to be the man in the box.
- **The duel** is the corner's aerial duel (§7.9), with a bonus to the attacker of `CrossHeaderAttackerBonus`
  = 13_000 (the attacker wins 75.5% of the headers, measured), plus the crosser's Crossing above `CrossHeaderDeliveryBaseline` = 13 at
  `CrossHeaderDeliveryAerialWeight` = 3. Both jumpers' live ratings record it, and the winner is recorded with a
  `Header`, the other as having gone for it.
- **A won header is the shot**, from the box, in the zone the possession was planned for, with the header
  winner's Heading in the goal chance. The cross is the creating pass and the crosser has the assist. **A lost one
  creates nothing**: the cross was the pass that was stopped, and the defence clears it. There is no corner from
  it.
- A cross is a chance more readily than a ground ball, so the creation chance of a crossed approach is multiplied
  by 12_300 (`CrossCreationMultiplierBasisPoints`), which keeps the shots from crosses at what they were.

```text
zoneMultiplier  = central 15_000 | inside 10_000 | wide 8_000
base            = BaseShotGoalBasisPoints (865) × zoneMultiplier / 10_000
contest         = shooter's effective skill − (opponent Goalkeeping rating / AttributeRatingFactor)   (hundredths of an attribute point)
goalChance      = clamp(base + swing(contest, ShotQualitySwingBasisPoints = 1_900 per ShotContestReference = 150 points)
                       + finishing, 220, 5_600)
finishing       = swing(shooter's effective Finishing − FinishingGoalReference (11) points, FinishingGoalSwingBasisPoints = 3_500 per 150 points)
```

`finishing` is added to every shot, a header included (`engine-v10`): the contest above reads the skill the shot
asks for (Heading for a header), and Finishing is whether it goes in. It is read against 11, not the lab's sheet value of 13,
because effective skill is lower than the sheet (§6.3), so the lab's average side is untouched.
A side of 20-Finishing players scores about 46% more than a side of 6s, over 4,000 matches (the requirement is
at least 40%).

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

- **Positioning helps at the finish** (`engine-v10`, ADR-0061). The **Positioning edge** is a multiplier in basis
  points that rises linearly with effective Positioning from `PositioningFloorBasisPoints` = 7_000 (Positioning 1)
  to `PositioningCeilingBasisPoints` = 13_000 (20), neutral at 10.5. It multiplies the weight with which a player
  is drawn as the shooter or the corner header. The defender who marks a corner is drawn by Heading times an edge
  read from the mean of his Marking and Positioning. Positioning is also a fourth term of the aerial duel
  (`AerialDuelPositioningWeight` = 2) for both jumpers. No draw is added, and a side whose players are alike in
  Positioning shoots and scores as before; the edge moves who takes the chance within a side.

### 7.10 Positions and the receiver chain (`engine-v10`)

*(ADR-0061.)* Before `engine-v10` the ball's path was drawn first and the players were picked for it afterwards,
one weighted draw each, with nothing reading where anybody stood. Since it, each pass of the approach has a passer
and a receiver, chosen from where the formation puts the players and from the holder's Vision and Decisions.

- **Where players stand** (`OffBallModel`, integer-only, no draw). `TacticalFormationResolver` gives each player a
  spot for the ball's position, attackers with the ball and defenders without, resolved for each pass because the
  block shifts with the ball. The away side is resolved in the home frame and flipped back. A target's **openness**
  is the distance to the nearest defender (each defender's Marking and Positioning pulling him nearer or pushing
  him further, the receiver's own Positioning edge widening it), full at `OffBallOpennessFullDistance` = 1_200 and
  0 with a man on him. **Reach** is `OffBallReachDistance` = 4_600 times the receiver's Positioning edge.
  **Progress** is the forward gain, full at 2_000. A holder is **under pressure** with a defender within 600.
- **The depth rule.** Anyone may be given a ball back or across by up to `BackPassFreeDepth` = 500; a midfielder or
  attacker up to `BackPassMaxDepth` = 2_500 back when the holder is under pressure; nobody further. A defender is a
  receiver only while the holder is at or short of `DefenderReceiveMaxHolderX` = 4_500 and where he takes it at or
  short of `DefenderReceiveMaxPointX` = 5_000; the goalkeeper never is.
- **Choosing the receiver** (`ReceiverChooser`). For each leg the holder (the carrier at the start, then the previous
  receiver) is *shown* a teammate with a chance from `ReceiverSeeLowestBasisPoints` = 4_500 at Vision 1 to
  `ReceiverSeeHighestBasisPoints` = 9_900 at Vision 20, less up to 3_500 for a teammate 6_000 or more away. Of those
  he sees, the ones who can *reach* the point and pass the depth rule are *scored*: openness, progress, reach and
  the fit with the side's pass-focus lane, weighted 4/3/2/2. One is drawn with weight
  `100 + score² × gain / 100`, the gain running from `ReceiverChoiceGainLowest` = 2 (Decisions 1, nearly flat) to
  `ReceiverChoiceGainHighest` = 24 (Decisions 20, the best placed is about 25 times likelier than the worst). A
  receiver is also weighted by his Passing, and on a possession the play draws already ended with that pass lost,
  by what is left of Passing. The planned touch moves `ReceiverPullBasisPoints` = 2_500 of the way to him, never out
  of its lane, and the last touch of the approach never moves. When nobody is seen and eligible the holder keeps the
  ball for the leg, which is the dribble below.
- **One leg further.** The chain runs one pass past the approach, to the final-third entry point, which only a
  progressing attack plays. The player it goes to is the carrier of the ground duel (step 8 of §7.2) and, in open
  play, the favoured shooter and the crosser (§7.3). If the holder keeps it, he carries it in and is all of those
  himself.
- **Its own stream.** The chain draws from `new Pcg32(seed * 1_000_037 + ordinal)`, with a fixed number of draws per
  leg (one for every outfield player, then the choice, then the two that settle pass, dribble or shot), so it never
  moves a play draw. The opening carrier is drawn
  from the geometry stream, with one draw as before, weighted by Dribbling times how near the formation puts him to
  the ball.
- **The chain's quality drives the outcome.** The chain is played before the progression roll, because how it was
  played is part of what the roll weighs. Its **weakest** pass's openness, over the approach's legs, moves the
  chance the attack progresses by up to ±`ChainProgressSwingBasisPoints` = 700 about
  `ChainWeakestOpennessReference` = 5_200, the mean of the passes the engine plays; the openness of the **last**
  receiver (up to ±600 about 6_900) and the mean **score** of the receivers the holders chose (up to ±500 about
  6_200) move the chance it creates a shot. The references are the measured means, so the nudges move who creates
  chances, not how many. A possession lost on a pass is played again as far as it got.
- **Credit.** `PassTally` credits each pass of the approach to its passer, the pass that creates the chance to the
  man who has the ball (or, if he takes the shot, to the man who played it in to him) and the failed final pass to
  the passer of the lost leg. `AssistPlanner` credits an open-play goal the same way; a set piece keeps its
  weighted draw. The pass into the final third is not counted, as before.
- **Pass, dribble or shoot** (`SoloPlay`, `engine-v10`, M5). At each leg the holder has up to three options, each
  with a small integer utility on the receivers' 0..10_000 scale. The **pass** is worth the best score among the
  teammates he sees and can give it to, and nothing when there is nobody. The **dribble** is always open: his
  Dribbling and the space ahead of him (the openness of the point `SoloDribbleStep` = 800 on, weighted
  `SoloDribbleSkillWeight` 2 to `SoloDribbleSpaceWeight` 3), times `SoloDribbleUtilityBasisPoints` = 4_200. The
  **shot from distance** is open only on the ball played into the final third, and only from `LongShotMinX` = 7_800 to
  `LongShotMaxX` = 8_700 on the side's own scale (the edge of the box): his Finishing and Composure and how near the
  goal he is (3 to 2), times `LongShotUtilityBasisPoints` = 5_600. He takes the option worth most with a chance from
  `SoloBestChoiceLowestBasisPoints` = 9_700 at Decisions 1 to `SoloBestChoiceHighestBasisPoints` = 9_980 at Decisions
  20, and otherwise one of the others, in proportion to what each is worth. Ties go to the pass, then the dribble. A
  holder who finds nobody has only the dribble and the shot to choose between, which is "nobody in a good position" and
  "he did not see them" in one rule. A dribble is a `Carry` waypoint to the planned point, is not a pass, and the man
  who dribbled is the one who fights the ground duel, is favoured to shoot and crosses.
- **A shot from distance** replaces the whole of the chance: there is no ball into the final third, no duel and no
  creation roll. He shoots from where he stands with the zone read off where that is, the goal chance of an ordinary
  shot (§7.3) times `LongShotGoalMultiplierBasisPoints` = 6_000 after the shooter, the zone and the keeper have set it.
  It is no one's assist and no pass is counted for it; the ball he was given before is counted as it was.

### 7.x Pass focus (`engine-v8`; shots and crosses `engine-v9`)

`MatchInstructionsV1.PassFocus` asks for the centre alone, the centre and a flank, or both wings (`ADR-0058`).
`PassagePlanner.FocusLateral` remaps a uniform lateral position into three lanes in the rules' shares (centre
alone 30/40/30; centre and a flank 42 favoured, 27 centre, 31 other; wings 46/8/46) and is applied to the
pressure point and to each touch of the approach, so no draw is added and a `Balanced` side is unchanged. Left
is the low end of the attacking side's own scale. The shares are calibrated, not read off: every possession
starts where the last one ended, mostly in the middle, so the ball measures about 23/53/23 with no preference and
about 20/60/20 (centre), 39/22/39 (wings) and 37/44/20 (centre and left) with a focus. `PassTally` and the film's
receiver choice do not read it.

From `engine-v9` the focus also moves the shots and the crosses (`ADR-0059`), with no draw added:

- **Shot zones.** The roll that picks an open-play shot's zone is read against the focus's shares of the five zones
  (central, and an inside channel and a wide zone each side). No preference keeps 40 / 20 / 10 / 20 / 10; the centre
  is 54 central, 15 inside and 8 wide each side; the centre and left is 34 central with 28 inside and 15 wide on the
  left and 15 and 8 on the right; both wings is 12 central, 29 inside and 15 wide each side. Measured on events,
  which include the corners, free kicks and penalties taken from the middle, the shot lanes (left/centre/right) are
  about 26/47/26 with no preference, 20/60/19 (centre), 38/41/20 (centre and left) and 39/21/39 (wings).
- **Crosses.** The share of a possession's final approach that is crossed depends on the lane the ball arrives in,
  38.5% in a flank lane and 7% in the centre lane for a side with no preference or both wings: about 28% overall,
  almost all from the flanks. The focus decides the lane, so both wings cross about a quarter more often (22.1 a
  match against 17.5). A side that favours the centre crosses from the middle too, in shares set to the lanes
  asked for: the centre alone crosses 21% from a flank lane and 34% from the centre lane, which measures 24/52/24
  (left/centre/right); the centre and a flank crosses 37% from its flank, 33% from the centre and 14% from the
  other flank, which measures about 54/31/15. A cross is shown on the film and in the commentary; it does not
  change the chance that follows it.
- **Shot volume.** A zone is worth different amounts (central 1.5, inside 1.0, wide 0.8 times the base), so the
  focus also scales how often a progressed possession becomes a shot: the centre by 0.935, the centre and a flank
  by 1.03 and both wings by 1.16. Goals for stay within about 2% of a side with no preference; the centre takes
  about 7% fewer shots at a higher conversion, the wings about 13% more at a lower one.

What the focus costs beyond that is the tactical modifier above (§6.5): the centre builds, finishes and holds its
shape better and creates less, the wings the reverse. The modifiers are small, as they are for every instruction
(`INS-9`).

### 7.11 The counter-attack (`engine-v11`)

`MatchInstructionsV1.CounterAttack` (`ADR-0063`) is a switch, off by default. A possession is a counter-attack when it
begins from play, the one before it was the opponent's, and a roll from a stream of its own comes up
(`CounterStartBasisPoints` 2_000 off, `CounterStartWithInstructionBasisPoints` 5_000 on; the stream is the seed times
a stride plus the possession ordinal, so no play draw moves). The passage record's `Counter` says which.

A counter's progression chance gains `CounterProgressBasisPoints` (200) plus `CounterProgressPerPostureBasisPoints`
(1_150) a step, and its creation chance `CounterCreationBasisPoints` (150) plus `CounterCreationPerPostureBasisPoints`
(1_050) a step, where the opponent's posture is its mentality (−2 defensive … +2 attacking) plus its defensive line
(−1 deep … +1 high). The defenders' and midfielders' mean effective pace and acceleration against
`CounterRecoveryReference` (11) then moves each by `CounterRecoveryProgressStepBasisPoints` (150) and
`CounterRecoveryCreationStepBasisPoints` (120) per point, capped at 900 and 700: quick legs cut the counter down, slow
ones are caught out. When a counter ends in a lost ball, the cautious side that wins it back (posture below the
middle) is likelier to counter in its turn by `CounterBackfireStartBasisPoints` (2_000) a step, and its creation
chance gains `CounterBackfireCreationBasisPoints` (1_500) a step. A side that plays on the counter loses
`CounterAttackBuildUpCostBasisPoints` (120) of build-up and `CounterAttackShapeCostBasisPoints` (60) of defensive shape
(§6.5). Measured over 3,000 matches the instruction is worth about +0.24 goals a match against an attacking side with
a high line, nothing against a balanced one, and a goal difference of −0.165 against a defensive side with a deep line.

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

## 10. Replay: the film and the reel (`replay-v16`)

`ReplayDirector.Build(input, result, passages, options, liveMetrics)` re-derives the whole presentation
from the frozen snapshot, the result, the recorded passages (ADR-0051, ADR-0053), and the optional live metric
curve. It is a pure function of those inputs and consumes no draw; the presentation is **never stored**, which is
why a replay revision is a clean contract change rather than a migration (ADR-0052). `replay-v4` replaced the
time warp and the anchor tracks of `replay-v3` (ADR-0054); `replay-v6` changed where the players stand and how a
set piece is laid out (ADR-0064), `replay-v7` stops the ball being ringed (the clearance zone and the duel's
one challenger, below), `replay-v8` shows a corner as a ball played in and a touch short of the line, `replay-v9` keeps the keeper on his line (below), `replay-v10` lets nobody play a ball to himself, `replay-v11` holds a ball played in along the ground at the goal line while a defender closes the man down before it is won, `replay-v12` brings the keeper of the side with the ball up the pitch with it, `replay-v13` sets the set pieces, `replay-v14` sends defenders after a wing carrier, and `replay-v15` drives the corner in low and fast, and `replay-v16` shows throw-ins (below). Every constant below is a field of
`HighlightOptionsV1`: the film's pace is a presentation decision, so none of it can move a result.

**One film at one pace.** `FilmScript` turns each possession into *beats* — carry, pass, lofted pass, cross,
header, shot, clearance, duel, save, placement, and dead-ball holds (restart, goal, card, substitution,
half-time card) — from the recorder's `Outcome`, `Restart`, waypoints, touches and events. The participants the
engine named take precedence at their beats: since `engine-v10` (`replay-v5`) a `Receive` touch names the receiver
of each pass of the approach, and the station resolves to him. An intermediate pass the engine did not name goes to
the teammate who can reach the reception point soonest in the current shape; a ground move of 12 m or more becomes *receive → carry 3–10 m → pass*
(the carry's length weighted by Dribbling through a stable hash); a cross is drawn only from a wide final-third
position into the box, and anything else is a lofted pass. A possession that starts away from where the last one
ended without a restart gets a transition beat at physical speed.

`FilmTiming` gives each move its natural real-time length — its distance at the speed of its kind plus
`ControlSeconds` = 0.3 (pass 15 m/s, lofted 20, cross 21, clearance 24, shot 27, header 14, carry 5–7 by
Dribbling, placement 8) — and each hold a fixed film length (kick-off 1.2 s, goal kick 0.8, quick free kick
0.6, corner and penalty 1.4, free kick struck or crossed 1.5, goal and celebration 4.0, card 1.0, substitution 1.0, half-time card 3.0), and solves **one
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
the side's **real instructions**, and since `replay-v6` the block **stands as lines by phase** instead of collapsing on the ball: each
side is grouped into back, midfield and front lines by the depth of its slot anchors, takes a phase (build-up, attack,
low block, mid block, high press) from the ball and possession, is moved 0.15 along and 0.30 across by the ball's
offset, holds a low block's back line at about the 18-yard line with the forwards left high, and has its outfield
targets kept 3 m apart. The side without the ball sends **one challenger** to the carrier (a second only in its
final third or when told to press high; the defender a duel names is its challenger) with a cover behind him; the side with the ball offers a wide,
a forward and a way-back option 10 m and more from the ball, sets an overlap on a flank, and, on a counter the
engine marked (§7.11), sends outlets forward. Everybody the beat is not about keeps 6.5 m clear of the ball and of the places it goes over the next 6 s, and comes in only 2.5 s before he is due on it, so the ball is not ringed. The keeper stands on the line between the ball and the goal. A cross
comes into a box with near-post, far-post, spot and cutback runners and goal-side markers. **Set pieces are laid
out for what they are**: every corner is preceded by the ball being played towards the goal line by the attacking side and a defender's
touch a few metres short of it (a block or a header), or by a keeper's tip round the post, and the sides set for the
corner only once the ball is out, then by role and mirrored by the flag (taker, four runners, three outside the box, two held back, one of them on the outlet; two posts, three
zonal, markers, an outlet); a free kick struck at goal has a wall of 2 to 5 by distance, the keeper on the far
side and the rest of the defence a pack at the box with a marker on each of three attackers, one crossed into the box has no wall and the pack with five,
a quick one keeps open-play shape with the defence 9.5 m off; a
penalty has everybody outside the box and the arc; a goal kick spreads the kicking side and steps the other up.
**Hard constraints override the shape**: the carrier is at the ball, the receiver at the reception point when the
ball arrives, the shooter, the header pair and the fouler and fouled player at their touches, the keeper at the
save point, and set-piece and celebration formations. If a constrained player cannot arrive in time, the
preceding move is lengthened; **no speed cap is ever exceeded**. The only discontinuities are *cuts* — the
kick-off after a goal and the half-time reset — listed in the presentation and played as a 300 ms crossfade.
**The keeper stays on his line** (`replay-v9`): he stands between the ball and the goal, following it in steps of 1.5 m, and
is told nowhere ahead of time except by his own beat. He is not sent to where a shot will arrive, nor to the goal-kick
spot or the place a goal kick is played to, while the ball is in play; a goal kick is put down in the six-yard box
rather than fetched from where a wide shot stopped; and he reaches the ball only within 0.7 m on a save, by a longer beat
if he must. Measured: as a strike arrives he is 4.0 m from the goal mouth at p95 (11.5 m before) and no nearer than
3.0 m to a shot that goes wide at p05 (0.0 m before).

**The keeper comes up with the ball** (`replay-v12`): the keeper of the side with the ball stands on the line between the
ball and his goal at 3.1 m with the ball 35 m from his line (below that, `1 + 0.06 d` m), 13 m at 52.5 m, 20 m at 80 m and
28 m at 105 m, straight lines between (`KeeperLadder`); at a corner his side takes he stands 28 m out. The keeper of the
side without the ball stands `1.5 + 0.01 d` m off his line, at most 2.5 m. Kick-off, penalty, goal kick and celebration
keep him home. The reference has the attacking keeper 12 to 14 m out with the ball at halfway and 15 to 30 m inside 25 m
of goal, the defending one about 2 m. Measured (own / middle / final third of the ball): attacking 4.0 / 7.9 / 15.9 m
(4.4 / 3.8 / 4.5 before), defending 11.3 / 5.3 / 2.3 m (5.2 / 4.3 / 3.6, the first being the walk back after a turnover).
**Set pieces are set** (`replay-v13`): nobody runs in at the strike. A corner, a free kick struck at goal and one
delivered into the box are arranged from the moment they are known to be coming (a free kick from the foul, which is the
`Duel` beat before it), and while the hold lasts each pair of the pack drifts 1.6 to 3.0 m (`DriftLeast`, `DriftMost`) back from
the ball and up to 1 rad to either side (`DriftTurn`), setting off up to 0.3 of the hold in and taking 0.6 of it (`DriftStart`,
`DriftSpan`), a smooth step; the two of a pair share a key, the key and the possession pick the numbers (`Chance`, a
hash: no randomness), the wall, the taker and the keepers do not drift, and at the strike everybody is in his place
(`ShapeState.Waited` is the share of the hold gone, nought outside it). A free kick's pack (`FreeKickRunners`,
`FreeKickLine`, `FreeKickEdge`) is five pairs of an attacker and a marker 1.4 m goal-side of him at 13.5 to 17.5 m from the
goal line, a line of four, one pair at the top of the D at 25.5 m and two or three attackers at the circle, the best headers of
the attackers going in (three for a kick struck at goal, five for one delivered); from a spot closer than about 31 m the whole
pack is scaled towards the goal line by `(spot depth - 12) / 19.5`, at least 0.45, so that it stays clear of the wall and keeps
its shape. A corner has two held back (`CornerGuards`, the first goal-side of the outlet), four runners, three outside the
box (`CornerEdge`) and the posts, zones and markers as before. A pack running to its places keeps 0.9 of the speed it could
still stop from (`PackBraking`), rather than the smooth close-in of open play. A corner and a penalty are held 1.4 s of film
(`SetPieceHoldSeconds`) and a free kick that is struck or crossed 1.5 s (`FreeKickHoldSeconds`), up from 1.2. Measured over 300
matches (before): players within 25 m of the goal at a corner 13 (12) and at a free kick 11 (10; reference 13 to 14 and 11 to
15); running flat out as the ball is struck, a corner 11% (17%) and a free kick 29% (41%); settled in their places 41% (32%);
pace 2.68x (2.65x), inside the band 89.7% (92.3%), the film 10.1 min, the ball standing still outside holds 3.4% / 4.3%, moves
lengthened 16.9%, and crowding, the keepers and the delivery as before.

**Defenders trail a wing carrier** (`replay-v14`): when the ball is on a flank (16 m or more off the middle) and 50 m or more
up the attackers' pitch, in a carry, pass, lofted pass or duel that is not a counter-attack, three role slots
(`ChaserSlot`, `Chasers`; the box defenders' slots, unused here) ask for the free defenders (`RoleWants.Trailing`) within
`ChaseRecruit` = 25 m of the ball and no more than `ChaseAhead` = 8 m ahead of it. Their places are `ChaseBehind` = 5 m behind the
ball and `ChaseSpacing` = 2.2 m further back each, and `ChaseInside` = 1 m toward the middle and `ChaseInsideStep` = 1 m more each
(a diagonal line); as press roles they keep the ball's clear zone, so the nearest stands about 6.5 m off. Measured: three or more
within 10 m behind a wing carrier in 2.8% of the steps (0.4%); the reference has three in one carry and none in another.

**The corner is driven in** (`replay-v15`): the ball into the box from a corner (`FilmBeat.CornerKick`) arcs `CornerArc` = 18
(a cross is 55, the crossbar is 30) and is played at `CornerMetresPerSecond` = 14 of real time, which is 33.7 m/s of film at
the usual pace (the reference corner is about 35). The other balls keep their speeds on purpose: the film has a fixed length, so
a slower ball only raises the pace (cutting the four ball speeds by about a fifth moved the lofted ball from 44 to 40 m/s of
film and the pace from 2.68x to 2.89x, with 55% of the films inside the band).

**Throw-ins are derived by the film** (`replay-v16`): the engine records none. A possession that begins from play
(`Restart == None`) for the other side than the last, where the last one ended within `ThrowInBand` = 9 m of a touchline and
not within `ThrowInEnds` = 12 m of a goal line, begins with a throw-in: the ball runs out to the line (a `Placement` beat with
`FormationMode.ThrowIn`), play is held at `HoldKind.ThrowIn` for `ThrowInHoldSeconds` = 0.5 s of film, and the nearest player of
the side that won it throws it, a short arc (`ThrowZ` 22, `ThrowArc` 8) to the player the engine named, at least `ThrowInMin` =
3 m. `FilmShape.ArrangeThrowIn` stands the thrower on the line, four of his side in `ThrowInPocket` (6 to 15 m in front of him),
a defender 1.2 m goal-side and 0.8 m touchline-side of each, and the rest at least `ThrowInClear` = 4 m off him. About 7 a
match (a real match has about 40). Measured over 300 matches: 2 outfield players within 10 m of the ball as it is thrown and 7
within 25 m (reference 2 to 4 and 6 to 9), the thrower on the line, 81.0% of the films inside the pace band (87.7% before:
the film is a fixed length, and each throw-in adds about two seconds of motion).

**A held ball wins a corner** (`replay-v11`): a ball played in along the ground towards the goal line is received by the
attacker who can reach it soonest (a pending receiver, found as it is played), who is then *held*: a `Duel` beat with
`Contested` set, whose ball stays at his feet for `ContestSeconds` = 3.0 s of real time (about 1.1 s of film at 2.65x),
while the defender who can reach him soonest is pinned there by the end of it and the usual challenger and cover close in.
That defender is the receiver of the beat and the one who puts the ball behind (`ActorSource.PreviousReceiver`). Who the
two are is not known when the script is written, only when the film is played, so the beat names nobody. A ball in the
air, a header from a cross that has just landed and a keeper's tip are as they were. Measured over 300 matches: pace
2.65x (2.63x), inside the band 93.0% (94.3%), moves lengthened for constraints 16.8% (17.0%), the ball standing still
outside holds 3.4% at p50 and 4.4% at p95 (2.8% and 3.5%; the target is 5%), the touch that puts a corner behind 5.5 m at
p95 as before, no teleports, and every shape metric as it was.

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
750 KB (ADR-0006) the director recompresses the players at widening tolerances (50 → 70 → 90 → 120 → 160 → 220; the last
rung was added in `replay-v7`, when the players' extra movement left the fifth no room) and sampling intervals
(200 → 300 → 400 → 500 → 600 → 700 ms) until it fits — deterministic, so the same match always lands
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
| `ShotFocus…Percent` (eleven) | see §7.x | The shot zones of a side with a pass focus (`engine-v9`); each set sums to 100. |
| `CounterStartBasisPoints` / `WithInstruction` | 2_000 / 5_000 | How often a regained ball becomes a counter-attack, without and with the instruction (`engine-v11`). |
| `CounterProgressBasisPoints` / `PerPosture` | 200 / 1_150 | A counter's progression edge: the base, and a step per point of the opponent's posture. |
| `CounterCreationBasisPoints` / `PerPosture` | 150 / 1_050 | A counter's creation edge, likewise. |
| `CounterAttackBuildUpCost` / `ShapeCostBasisPoints` | 120 / 60 | What playing on the counter costs in build-up and defensive shape. |
| `CounterRecoveryReference` | 11 | The pace and acceleration, in attribute points, a back line and midfield are measured against. |
| `CounterRecoveryProgress` / `CreationStepBasisPoints` | 150 / 120 | What each point above or below it moves a counter's progression and creation chance. |
| `CounterRecoveryMaxProgress` / `MaxCreationBasisPoints` | 900 / 700 | The most it can move them. |
| `CounterBackfireStartBasisPoints` / `CreationBasisPoints` | 2_000 / 1_500 | What a cautious side gains, a step, when it wins the ball back from a failed counter. |
| `ChanceVolumeCentre` / `Pair` / `WingsBasisPoints` | 9_350 / 10_300 / 11_600 | How often a progressed possession becomes a shot, for a side with a pass focus (`engine-v9`). |
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
| `CrossShareFlankLaneBasisPoints` / `CrossShareCentreLaneBasisPoints` | 3_850 / 700 | Share of the final approach that is crossed, by the lane the ball arrives in (`engine-v9`); about 28% overall. |
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
| `GroundDuel*Weight`, `AerialDuel*Weight`, `Scramble*Weight` | 4/3/3, 4/3/3, 5/3/2/2, 3/3/2 | The skills each duel reads (the aerial duel's fourth term, Positioning, from `engine-v10`). |
| `PositioningFloorBasisPoints` / `PositioningCeilingBasisPoints` | 7_000 / 13_000 | The Positioning edge on a shot or header weight, at Positioning 1 and 20 (`engine-v10`). |
| `OffBall*`, `BackPass*Depth`, `DefenderReceiveMax*X` | 1_200, 4_600, 2_000, 600; 500, 2_500; 4_500, 5_000 | Openness, reach, progress, pressure, the depth rule (§7.10, `engine-v10`). |
| `Receiver*` | see §7.10 | Sight, pull, the four score weights and the choice gains (§7.10, `engine-v10`). |
| `ChainProgressSwingBasisPoints`, `ChainCreationOpennessSwingBasisPoints`, `ChainCreationChoiceSwingBasisPoints` | 700, 600, 500 | How far the chain's quality nudges progression and creation (`engine-v10`). |
| `ChainWeakestOpennessReference`, `ChainFinalOpennessReference`, `ChainChoiceReference` | 5_200, 6_900, 6_200 | The measured means the nudges centre on (`engine-v10`). |
| `FinishingGoalSwingBasisPoints` / `FinishingGoalReference` | 3_500 / 11 | How much the shooter's Finishing moves his chance of scoring, on every shot (§7.3, `engine-v10`). |
| `ShooterChainBonusBasisPoints` | 25_000 | The player the ball was played in to multiplies his weight for the shot by this (`engine-v10`). |
| `CrossHeaderAttackerBonus` / `CrossHeaderDeliveryBaseline` / `CrossHeaderDeliveryAerialWeight` | 13_000 / 13 / 3 | The aerial duel of an open-play cross (§7.3, `engine-v10`). |
| `ReachWeightFloorBasisPoints` | 500 | What a player out of reach keeps of his weight to go up for a cross or pick the ball up (`engine-v10`). |
| `CrossCreationMultiplierBasisPoints` | 12_300 | A crossed approach creates a chance this much more readily, so the header leaves the shots as they were (`engine-v10`). |
| `SoloBestChoice{Lowest,Highest}BasisPoints` | 9_700 / 9_980 | The chance the holder takes the option worth most, at Decisions 1 and 20 (§7.10, `engine-v10`). |
| `SoloDribbleUtilityBasisPoints`, `SoloDribble{Skill,Space}Weight`, `SoloDribbleStep` | 4_200; 2, 3; 800 | What a dribble is worth against a pass (§7.10, `engine-v10`). |
| `LongShotMin/MaxX`, `LongShotUtilityBasisPoints`, `LongShot{Skill,Range}Weight` | 7_800 / 8_700; 5_600; 3, 2 | Where a shot from distance is open and what it is worth against a pass (§7.10, `engine-v10`). |
| `LongShotGoalMultiplierBasisPoints` | 6_000 | A shot from distance scores this much as often as an ordinary one (§7.10, `engine-v10`). |
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

Run over **3,000 matches** between evenly matched 13/20 sides, on a 16-logical-core Windows machine
(`engine-v10`, rules hash `cb1a8aed…`; the 10,000-fixture calibration below is the larger sample, and
`engine-v9` measured 2.898 goals over 20,000):

| Measure | Measured | Target |
|---|---|---|
| Goals per match | 2.93 | 2.5 – 3.0 |
| Home / away goals | 1.62 / 1.32 | 1.3 – 1.9 / 1.0 – 1.5 |
| Home win / draw / away win | 44.8% / 23.9% / 31.3% | 40 – 50 / 20 – 30 / 25 – 35 |
| Shots per match | 27.9 | 20 – 32 |
| Home possession | 52.1% | 50 – 54 |
| Fouls per match | 21.4 | 18 – 26 |
| Yellows per match | 3.42 | 3.0 – 5.0 |
| Reds per match | 0.28 | 0.10 – 0.35 |
| Injuries per match | 0.44 | 0.20 – 0.60 |
| Penalties per match | 0.24 | 0.15 – 0.40 |
| Substitutions per match | 7.7 | 4.0 – 10.0 |
| p99 total goals | 8 | 6 – 8 |
| Matches with 7+ goals | 3.53% (2.97% over 20,000 at the end of M4) | < 3.0% |

The 7+ goals share is the one reading outside its band. Over 3,000 matches the standard error is about 0.3
points; it has run 2.8 – 3.5% across the `engine-v10` builds (ADR-0061), and the shots from distance of
`engine-v10` M5 (§7.10) added a little to it.

Calibration invariants (10,000 fixtures, `engine-v10`): goals per match **2.89**; home advantage worth
**+4.5 points** (target ~+4); a three-ability-point favourite upset **14.8%** of the time (target ~15); a side
sent off early finishes **1.40 goals** worse (target ~1.2, band 0.9 – 1.5, measured over 2,000 fixtures); a
high-pressing side is measurably more tired by the 80th minute (gap ~1,730 bp) and a fresh substitute
measurably fresher than the tired defenders (~1,782 bp). Free kicks in shooting range occur about **1.5 per
match** and fouls in the final third come from the duel.

What the individual play of `engine-v10` adds (ADR-0061, `simulation-benchmarks -- offball`): about **625 passes**
a match, completed 93% / 78% / 77% by Defence / Midfield / Attack players, who make 9.8% / 58.3% / 32.0% of them; a
defender is given the ball past halfway by the chain **0.00%** of the time, and appears to receive it in the film
**3.25%** of the time (`engine-v9`: 31.7%); none of it is the chain's receivers, and the probe finds 2.64% of the
engine's own carries past halfway a defender's (the carrier who starts a possession and the duel's fallback carrier,
held to a small share by `ReachWeightFloorBasisPoints` and not excluded); a holder keeps the ball on a leg that has nobody to pass to 3.9% of the time.
Twin strikers differing only in Positioning (18 against 4) take 2.49 against 0.89 shots a match and score 0.253
against 0.087 (`engine-v9`: 1.47 against 1.49). A side of 20-Finishing players scores about 46% more than a side of
6s.

The `engine-v6` run held every `engine-v5` band, and the shape changed where the audit said it should: a
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

The replay over **2,000 matches** (ADR-0054; `replay-v5` on `engine-v10`, `simulation-benchmarks -- replay 2000`; `replay-v6` on `engine-v11` over 300 matches: median 10.11 minutes, pace 2.64×,
91.3% inside the band, shape metrics in ADR-0064;
`replay-v4` on `engine-v5` measured a median 10.12 minutes and a pace of 2.53×, 98.2% inside the band):

| Measure | p05 | p50 | p95 | min / max |
|---|---|---|---|---|
| Passages per match | 61 | 63 | 66 | — / 71 |
| **Film minutes** | 9.91 | **10.13** | 10.41 | 9.71 / **10.71** |
| Reel minutes | 5.51 | 6.87 | 8.06 | — / 9.02 |
| **Pace** (× real time) | 2.34 | **2.64** | 2.97 | — / 3.08 |
| Real-time motion (min) | 21.1 | 23.5 | 25.9 | — |
| Holds (min of film) | — | 1.24 | 1.57 | — |
| Payload estimate (KB) | — | 723.1 | 747.3 | — / 750.0 |
| Payload JSON (KB, `System.Text.Json`) | — | 2,082 | 2,158 | — / 2,176 |

**100%** of films land inside 9:00–11:00 and **0.00%** exceed eleven minutes (the longest of 3,000 is 10.86);
**91.0%** are played inside the 1.8–2.9× band (97.9% before the named receivers of `engine-v10`: the player the
engine names is no longer always the one who can get there soonest, so more beats are lengthened and the pace
settles about 0.1× higher); the reel is always inside its 12:00 cap. Outside the cuts (3.9 a match) there are
**0 teleports** in 2,000 matches, the ball stands still for a median 2.6% of the film outside the holds (p95 3.4%),
and no player moves faster than the sprint cap times the pace (keepers at most 0.82 of the dive cap). Quiet play is
condensed in every match — 41.9% of possessions at the median (p95 49.5%) — and a move is lengthened to meet a
constraint by a median 19.9% (p95 23.0%). The ball moves at a median of 24 m/s of film in a pass (p95 41), 38 in a
lofted pass, 46 in a cross, 40 in a shot. The payload sits on the third rung of the ladder in 62% of matches and
the fourth in 36%; the estimate reaches the budget at the maximum and leaves little headroom. A match takes about
100–143 ms end to end (simulate, film, serialise) on the benchmark machine, which was loaded by other work.

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

`engine-v10` costs more per match, since every possession now plays a chain of named passes: over 3,000 matches
(`bench`, with other work running on the machine) p50 6.5 ms, **p95 12.5 ms**, p99 25.7 ms, mean 7.6 ms, about
6.5 MB allocated and ~132 matches/sec single-threaded; the budget is still 100 ms.

A nine-fixture division matchday was therefore (`engine-v5`) about 12 ms of simulation at the mean and about 37 ms at the
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
| `ReplayDirectorTests` | `replay-v16`: one contiguous schedule; the film between 9:00 and 11:00 and never longer, with a median near ten minutes; a short film is a faster one, not a longer one; one pace inside its band; the ball and the players never faster than their caps times the pace outside a cut; each half on its own clock, the second starting at 45:00; the displayed minute at each event is its stamped minute; boundary frames joined except at a cut; cuts only at a kick-off and the interval; on-pitch, in-passage keyframes; the eleven and the ball with a track each; `MAT-11`-safe commentary read when the beat happens; the reel carrying every goal; determinism; the payload budget. |
| `FilmThrowInTests` | `replay-v16`: a throw-in is a ball put out on a touchline, a hold there and a short throw by the side that won it, never near a goal line; the thrower on the line with four in front of him, each with a marker, nobody within 4 m of him; the measures are taken. |
| `FilmRolesTests`, `FilmCornerTests`, `FilmSetPieceTests`, `FilmMotionTests` | `replay-v6`: one challenger and a cover, options at 10 m and more, back-line eligibility; a cross arrives into a populated box; a corner is preceded by a ball played in and a defender or keeper touch a few metres from where it leaves play, the taker is at the ball when it is delivered, mirrored by the flag, with two held back and one of them on the outlet, the pack drifting a metre or two while it waits and in its places at the strike; a free kick differs for a shot, a delivery and a quick one, with the pack at the box and a wall that stands still; no penalty position inside 9.15 m of the spot or inside the box; the shape metrics (`FilmDiagnostics.ShapeMetrics`). |
| `FilmScriptTests`, `FilmMotionTests` | Every possession scripted into contiguous beats that join except at a cut; a cross only from a wide position into the box; restarts taken by the owning side; the players the engine named at their beats; a goal followed by its celebration and a cut; no teleports; receivers at the ball when it arrives; a carrier at the ball; the keeper at a save; a goal ending in the goal mouth; the ball never left standing outside the holds; fixed hold lengths; quiet play condensed before the pace rises. |
| `BallPlayStatisticsTests` | `engine-v7` (§8.2): a completed count is a nonnegative subset of its attempted one; nobody who did not take the pitch passed or dribbled; an assist is a completed pass; counting is repeatable; a side's volumes and completion rates read like football; a better passer has the ball more and completes a higher share. |
| `OffBallModelTests` | `engine-v10` (§7.10): home and away mirror, openness falls with a nearby defender, the depth rule's truth table, determinism. |
| `ReceiverChooserTests`, `NamedReceiverPassageTests` | `engine-v10` (§7.10): a better Vision sees more, a better Decisions chooses the better placed, the pull stays in its lane and the last touch never moves, every ball received is received by a man of the side that had it, nobody passes to himself, and no defender is given the ball beyond halfway. |
| `ChainDrivesOutcomeTests` | `engine-v10` (§7.10, §7.3): the chain's quality nudges are zero at the reference and bounded, the ball played into the final third goes to the man who fights the duel, the man it ended with shoots more than his share, a cross ends in a header from the box that the defence can win, and a header that scores is the crosser's assist. |
| `ReceiverChoiceRegressionTests` | A hash of every event of 40 matches, re-pinned at each `engine-v10` milestone that moves play. |
| `SoloPlayTests` | `engine-v10` (§7.10): a better Decisions takes the option worth most more often (exactly as the chance says over every draw), ties go to the pass then the dribble, an option that is not open is never taken, the others are taken in proportion to what they are worth, a better dribbler and more room make a dribble worth more, a shot is open only between the line and the box for either side and worth more to a better finisher nearer goal, only the last leg can end in a shot, a goal from distance is nobody's assist, and the man who shoots from distance is the man who had the ball.
| `PositioningEdgeTests` | `engine-v10` (§7.9): the edge runs from the floor to the ceiling and never leaves them, only rises with skill, is neutral in the middle of the scale, is lower for a tired player, reads a defender's Marking and Positioning, and the twin striker with the higher Positioning takes more of the shots. |
| `PassFocusShotsAndCrossesTests` | `engine-v9` (§7.x): each focus's shot zones sum to 100 and left and right mirror; no preference keeps the 40/20/10 zones and a volume of 10,000; the measured shot lanes follow each focus; crosses come from the flanks, both wings cross more and a left focus crosses more from the left; the wings shoot more and the centre less while goals stay within 15% and a shot is worth more from the middle; one side's focus does not move the other's shots. |
| `PassFocusTests` | `engine-v8` (§7.x): a `Balanced` draw is returned unchanged; a focused draw stays on the pitch and never moves backwards; a uniform draw lands in the rules' lane shares; the measured lane shares of the ball match each option's calibration, and both wings send more wide than a single flank; left and right mirror; the away side is steered to its own left; one side's focus does not steer the other's ball; a focus is part of the snapshot's identity. |
| `HighlightTests` | Reel selection: goals always shown, the quality floor, the count cap and its goal exception. |
| `EnginePurityTests` | No clock, no `System.Random`, no IO; exactly one source of randomness. |
| `DistributionTests` | The statistical bands, over two thousand matches. |

---

## 14. Engine version 12: the tick engine

> **Status:** Executable specification for `engine-v12` / `engine-rules-v11`, `tick-engine-v1`, implemented in
> `src/TouchlineManager.MatchEngine/Tick` and `Spatial`. **Decision:** [ADR-0066](../architecture/adr/0066-engine-v12-discrete-tick-match-engine.md) and, for the film and the movement of §14.9–14.16, [ADR-0067](../architecture/adr/0067-tick-film-whole-moves-fluid-motion-and-smarter-play.md).

### 14.1 What it is

A match is played at ten ticks a second, 100 ms a tick, 54,000 ticks and the stoppages. Twenty-two players and the ball have a position and a velocity
at every tick, and everything that happens in the match is something the players did: a pass is a ball in flight that somebody reaches or does not, a
save is a goalkeeper's body against the line of the shot, a corner is a ball that crossed the goal line off a defender. Nothing is drawn afterwards to
fit a result.

The engine is `TickMatchEngine` (the registry entry for `engine-v12`) running `TickMatchLoop.Play`. It takes the same `MatchInputV1` and returns the
same `MatchResultV1` as version 11; the events, the player lines, the ratings and the commentary are made the same way.

### 14.2 Numbers

Everything the simulation computes is an integer. A pitch unit (100 units are about 1.05 m) is 1,000 fixed units, a speed is fixed units a tick, a
heading is a binary angle (1,024 to the turn, 0 facing the away goal, 256 facing +Y), the sine is a literal quarter table (`TickTrigonometry`) and the
square root is the integer root (`SpatialMath.Sqrt`). The pitch is 10,000 by 7,000 units and the ball's height is a Z of 0 to 100 (the crossbar is 35).
The goal is 3,123 to 3,877 across the line: 7.32 m, as the viewer draws it (the possession engine's own mouth is 3,000 to 4,000 and is not touched).

The one random stream is the snapshot's `Pcg32`. A tick takes the draws the things that happened in it need, in a fixed order, so a match is a function
of its snapshot. The recording of the match for a film takes no draw and changes no state.

### 14.3 One tick, in order

1. **Play state.** Open play, or a dead ball (a restart waiting to be taken, a goal being celebrated, half-time). `TickMatchStateMachine` holds a restart
   for 0.8 to 1.5 s while `TickSetPieces` places the twenty-two players, then the taker plays it.
2. **Shape.** `TickTacticalGeometry` turns each slot of the team sheet into a point to hold: the block follows where the ball is going (a smoothed,
   anticipating reference, §14.13) up and down the pitch, pushes up with the ball and drops without it (the two shapes blended over 1.5 s after a turnover),
   is squeezed or opened by the width, the defensive line and the pass focus, and a defender is never nearer than 8 m to his own goal line nor a player
   nearer than 7 m to the other.
3. **Orders.** The side without the ball (`TickDefensiveAI`): one presser, a second only on a touchline under a high press, markers in the own third,
   screens in the passing lanes, and a defensive line that steps up on a pressed carrier and drops off a free one; and the side with it
   (`TickOffBallSupport`): the carrier's support at 12 to 25 m, a runner behind the line, a pocket, an overlap. A goalkeeper takes his arc, rushes out
   to a lone attacker or dives (`TickGoalkeeperAI`, `TickShotStopper`).
4. **The man with the ball** decides every one to three ticks (`TickBallCarrierBrain`): shoot, pass, through ball, cross, dribble, shield, recycle or
   clear, whichever is worth most as the chance it comes off times its worth less the chance it fails times what a lost ball costs here, with no draw. A
   kick leaves rotated off its aim by one draw within the error angle.
5. **Bodies.** `TickSteering` and `TickPlayerPhysics` move every player: arrive (a man holding his place eases in), walk about his place instead of standing on it,
   keep 2 m from a teammate and 2.5 m from an opponent he is not in a duel with, blend with the old velocity, turn at the rate Agility allows, accelerate and brake
   as Pace and Acceleration allow, and spend energy.
6. **Contest.** A defender in touch of the carrier may challenge (`TickTackleResolver`), a goalkeeper in reach may smother, a defender who reaches a
   pass or a shot rolls to cut it out or to block it, and the nearest man to a loose ball takes it.
7. **Boundary.** The ball steps (`TickBallPhysics`: roll, flight, bounce, post, bar) and a goal, a goal line, a touchline or a post is settled.

### 14.4 The calibration constants

All of these are `const` in the type that uses them, with the reason beside them. They were set by the calibration of §14.5 and are the engine's rules
for version 12; changing one moves the golden hash in `TickEngineTests`.

| Constant | Value | Meaning |
|---|---|---|
| `TickPlayerSkills.CurvePercent` | 38 | A player's attribute counts 38% of its distance from 13 (60 before the ball flowed, §14.12). |
| `TickBallCarrierBrain.ShotMinimumChance` | 1,850 bp | Below this heuristic chance a carrier does not shoot. |
| `TickBallCarrierBrain.ShotBaseError` | 200 | Base error angle of an open-play shot (a set piece: `SetPieceShotBaseError` 50). |
| `TickBallCarrierBrain.ErrorSkillWeight`, `MinimumErrorPercent` | 5, 25% | A kick's error is `Base × max(25%, 100 − (5 × skill + Technique) / 2)`. |
| `TickBallCarrierBrain.PassArrivalCentimetresPerSecond` | 1,000 | A ground pass arrives at 10 m/s. |
| `TickBallCarrierBrain.OffsideCarelessBase`, `…PerPoint` | 12%, 4 | Share of offside receivers a carrier misses, by Decisions plus Anticipation below 26. |
| `TickMatchLoop.InterceptMaximum`, `InterceptSkillPerPoint` | 3,500 bp, 4% | Chance a defender cuts out a pass he reaches, and the % it moves per point he is above the passer. |
| `TickMatchLoop.PassGraceTicks` | 3 | Ticks after a pass in which no opponent can reach it. |
| `TickMatchLoop.ChallengeCommitBase`, `…PerAggression` | 1,000, 100 bp | Chance a defender in touch of the carrier challenges on a tick. |
| `TickMatchLoop.PenaltyGivenBasisPoints` | 1,000 bp | Share of fouls in the area given as penalties. |
| `TickMatchLoop.BlockToCornerBasisPoints` | 4,500 bp | Share of blocked shots turned behind for a corner. |
| `TickTacticalGeometry.DefenceMinimumX`, `AttackMarginX` | 800, 700 | The nearest to a goal line a shape position goes. |
| `TickTackleResolver.FoulScalePercent` | 250 | A challenge's foul chance as a multiple of the possession engine's, in percent (§14.12). |
| `TickDefensiveAI.HighPressLimit` | 7,800 | The farthest up the pitch a high press presses (§14.12). |
| `TickMatchLoop` hold: `FirstTouchTicks`, `DwellTicks`, `DwellScale`, `MaximumDwellTicks`, `OneTouchPressure` | 3, 60, 8,000 bp, 40, 7,000 bp | How long a man keeps a ball he has received (§14.12). |
| `TickMatchLoop.ReceiverAdjustUnits` | 300 | How far from where a pass was aimed its receiver goes to meet it. |
| `TickMatchLoop.ReferencePercent`, `ReferenceStepUnits`, `PossessionBlendTicks` | 25%, 9 m/s, 15 | How fast the ball reference closes on its target, how fast it may move, and the ticks the shape takes to change between its two forms (§14.13). |
| `TickSteering.InertiaBasisPoints`, `ArriveBrakingPercent`, `WalkBasisPoints` | 4,500, 60%, 1,800 | The weight of the old velocity, the braking of a man holding his place, and the walk about it (§14.13). |
| `TickSteering.OpponentSpaceRadius`, `OpponentSpacePercent` | 2.5 m, 200% | How near an opponent pushes a man who is not engaged, and how hard, against a teammate's push (§14.13). |
| `TickOffBallSupport` space: `SpaceInterval`, `SpaceInnerRing`, `SpaceOuterRing`, `SpaceSwitchPercent`, `SpacePaceFloor/Step/Ceiling` | 10 ticks, 4 m, 8 m, 15%, 3,000 + 50 per WorkRate to 4,000 bp | The look for room (§14.13). |
| `TickDefensiveAI` adjust: `AdjustInterval`, `AdjustZone`, `AdjustLeash`, `AdjustStandOff`, `AdjustLineSlack`, `AdjustPaceBasisPoints` | 10 ticks, 12 m, 6 m, 2.5 m, 2.5 m, 3,000 bp | The defenders' steps (§14.13). |
| `TickShotStopper` reach | `70 + 40 + 6 × (Agility + Reflexes) / 2` | A goalkeeper dives on his reflexes as well as his agility. |

Home advantage is the snapshot's `HomeAdvantageBasisPoints` less 10,000, added to the home side's man in every tackle and interception roll.

### 14.5 Measured behaviour

`tools/simulation-benchmarks` (`dotnet run -c Release --project tools/simulation-benchmarks -- <mode> …`):

| Mode | What it plays |
|---|---|
| `tick N` | N matches with the board's formations in turn, uniform players: the figures of the plan, the film's size and the time. |
| `tickmotion N seed` | How the players and the ball move and how the ball is played, read from the recording (§14.8, §14.16). |
| `tickmatrix N` | Every formation against every other, N matches each; `TICK_FORMS=0,1,5` picks formations, `TICK_ABILITY`, `TICK_INSTR=Pressing=2,Tempo=0`, `TICK_HOME_ATTR=16,Pace=9` change the sides. |
| `ticksnap N file` | The stored snapshots of a database export (id, goals, goals, document, tab separated): real formations, instructions and attributes. `TICK_UNIFORM`, `TICK_FORM`, `TICK_FLAT` take the real thing apart. |

The stored matches (205 snapshots of the development database, each played once), as first calibrated (Milestone 9) and after the tick-film plan (§14.16):

| | M9 | Now | Plan band |
|---|---|---|---|
| Goals a match | 2.83 | 2.68 | 2.60–2.90 |
| Shots a match | 28.3 | 27.6 | 22–28 |
| Shots on target | 38% | 31.0% | 32–38% |
| Pass completion | 76% | 84.0% | 75–85% |
| Yellow cards | 4.0 | 3.04 | 3.0–4.5 |
| Home / draw / away | 42 / 24 / 34 % | 42.9 / 20.0 / 37.1 % | 42–48 / 22–26 / 28–34 % |
| Fouls, penalties, corners, offsides | 22, 0.4, 5.1, 3.9 | 17.4, 0.27, 11.0, 1.7 | |

At Milestone 9 every formation against every other with uniform players of 13 gave about 2.3 goals and 26 shots a match, 78% pass completion, 3.4 yellow cards; at an
ability of 8 and of 19 it gave 2.9 and 2.4 goals. After the tick-film plan the same yardstick gives 1.70 goals and 19.2 shots (§14.16): uniform sides are the
one yardstick that moved out of the band. A home side whose players are all 3 points better than the visitors wins 59% of the matches and loses
18%. Each instruction at its extreme (a high press, a high tempo, a high or deep line, an aggressive tackle, the most defensive and the most attacking
mentality) moves the goals between 1.9 and 3.1 and nothing runs away.

What is not matched: a throw-in is taken about 14 times a match (a real match has about 35), because balls leave the pitch
less often than they do in football (a corner is now taken 11 times a match on the stored squads, 8 on uniform sides); and the formations are not alike — a lone striker or three forwards shoots about twice as often as two strikers.

### 14.6 The film

With a `MatchPassageRecorder` attached the loop records each tick (`TickMatchRecording`: the 22 players and the ball as shorts, plus the actions, events,
rosters and stoppages), and `ReplayDirector.Build(input, result, recorder)` cuts a film from it (`TickFilmSelector`, `TickReplaySynthesizer`,
`tick-replay-v2`). The film is a selection at twice the pace — sixteen to twenty minutes of kick-offs, every goal and every good chance as the whole move
that made it, cards, the other chances and the set pieces, topped up with spells of play at the ends of the pitch — in passages that share a boundary frame
and are joined by `jump`, `kick_off` and `half_time` cuts; the viewer reads it unchanged (§14.10).

### 14.7 Tests

| Suite | What it pins |
|---|---|
| `TickEngineTests` | A v12 snapshot is played by the tick loop and a v11 one by the possession engine; the same snapshot plays the same match (hash, canonical text, across threads); recording changes nothing; **the golden hash**; no floating point in the simulation; the plan's bands, widened for 72 matches. |
| `TickBallPhysicsTests`, `TickPlayerPhysicsTests` | Trajectories, friction, bounce, posts and bar; speed, turning and energy. |
| `TickTacticalGeometryTests`, `TickDefensiveAITests`, `TickTackleResolverTests` | Anchors, steering, the press, the line and the offside judge, the duel. |
| `TickOffBallSupportTests`, `TickBallCarrierBrainTests` | Support, runs and the carrier's choice and error. |
| `TickGoalkeeperAITests`, `TickShotStopperTests` | The arc, the rush, the dive and the save. |
| `TickMatchStateMachineTests`, `TickSetPiecesTests` | Every restart. |
| `TickMatchLoopTests`, `TickTeamTests`, `TickMatchRecordingTests`, `TickFilmSelectorTests`, `TickReplaySynthesizerTests` | The loop, the sides, the recording and the film. |
| `TickMoveFinderTests` | Where a move began, on recordings built to have one shape: a tackle, a capped possession snapped to a pass, the interval, a restart, a deflection, a loose ball. |
| `TickBlameTests` | The four blame tags: a dispossessed carrier and his tackler, a beaten defender, a misplaced pass, a bypassed man not shown twice in 3 s; a blame is not a touch; the film carries and narrates them. |
| `TickInterceptionTests`, `TickMotionTests` | The earliest point a man can meet the ball; walking about a place, the search for room and the defenders' steps, personal space. |
| `TickCombinationsTests` | The return run and the third man's run: who is sent, where to, and when not. |

### 14.8 Motion and film baseline (the tick-film plan, Milestone 0)

`tickmotion N seed` (`TickMotionProbe`) reads how the players and the ball move, and how much of a move the film shows, from the recording alone, so it
changes no play. Speeds are the distance between two frames (a pitch unit is about a centimetre, so one unit a frame is 0.1 m/s). The figures below are
200 matches from seed 11 on the engine as committed at `5163b96`, and are what each later milestone is compared with.

| | Baseline |
|---|---|
| Standing (under 0.3 m/s), outfield, open play | 12.2% of player-ticks |
| Stop-and-go (under 0.5 m/s, then over 2 m/s within 1.5 s) | 7.5 per player-minute |
| 12 or more outfielders under 0.5 m/s at once | 8.6% of open-play frames |
| Distance per outfield player per 90 | 13.6 km (the plan's aim is 9.5–11.5) |
| Ball speed when a pass is received (p10 / p50 / p90) | 1.9 / 2.4 / 10.4 m/s |
| Ball under 4 m/s for over 1 s before it is received | 55% of receptions |
| Receiver moving toward the ball at reception | 2% (median 0.0 m/s: he is already standing there) |
| Kick to reception (p10 / p50 / p90) | 3 / 48 / 53 ticks |
| Reception to next release (p10 / p50 / p90); one-touch (2 ticks or fewer) | 3 / 3 / 10 ticks; 1.6% |
| Opposing pair closer than 1.0 m, neither near the ball (3 m) | 35% of open-play frames |
| Goal moves (first control of the spell to the strike; p10 / p50 / p90) | 0.0 / 6.3 / 16.1 s |
| Goals whose regain lies before the film window | 6.4% (shots 8.5%) |

The viewer harness (`apps/web/.preview/capture.mjs <film.json> --fluidity-only`) steps the film at 40 ms and counts a **stutter** (a token whose drawn speed
falls under 35% of its ±300 ms average and recovers within 400 ms) and a **covered pair** (drawn centres closer than 0.6 of a token radius). Over seeds 11,
12 and 13: 161–215 stutters a film minute, and a covered pair in 27% of the steps. A token's radius is `clamp(pitch height / 58, 5, 10)` pixels, about
1.04 m on the harness's 1400 × 900 page, so a token is 2 m across and two tokens touch at 2 m.

What the numbers say about the five complaints: the ball does crawl (55% of receptions follow a slow tail) and the receiver does wait; tokens do stack,
and not only in duels; and the film already shows the regain in 94% of goals, because most goals follow a short spell of possession, so the longer film
is for context rather than for a missing start. How much of the stutter is the viewer's spline and how much is the engine's stop-and-go is what Milestone 1
(the spline alone) separates.

### 14.9 The viewer's spline and de-overlap (the tick-film plan, Milestone 1)

Web only; no engine or calibration change. Players are drawn on a cubic Hermite spline with **planar** tangents: the time-weighted mean of the two
segment velocity vectors (worked out in metres, since the grid is not square), limited to 1.5 times the slower segment, zero only at a real stop (both
segments under 0.3 m/s), a cut or a track end, and the curve is held within 0.3 m of the line between its keyframes. A per-axis monotone spline zeroed an
axis at every bend; this one keeps at least 60% of its speed through a right-angled turn. Altitude and the ball are as before.

The renderer then pushes tokens apart for display only (`token-separation.ts`): three passes, any two centres closer than `TOKEN_MIN_GAP` = 1.45 token
radii are moved apart along their line, half each, no token by more than a radius; the ball follows the nearest player within 1.5 m when it is on the
ground. The film's positions are untouched, and hover and name tags use the drawn positions.

Measured on seed 11 with the harness: covered pairs 25% of steps to 0%. Stutters per film minute: 190 with the old spline, 164 with the new spline alone,
194 with the new spline and the de-overlap. The plan's aim of 70% fewer stutters is **not** met from the viewer: the slow-downs left are real in the
data (a token runs at 3–6 m/s, falls to under 1 m/s and sets off again over a second), which is the engine's stop-and-go, and the de-overlap adds a few
because a token pushed by one coming the other way is slowed. That is Milestone 5's work.

### 14.10 Whole moves in the film (the tick-film plan, Milestone 2)

The film used to show a fixed 18 s (goal) or 15 s (shot) before a chance, whatever the move was. It now opens on the move.

**The move finder** (`TickMoveFinder.StartOf(recording, eventFrame, side, minLead)`). From the strike (the side's `Shot`, `FreeKick` or `Penalty` stamp in
open play within 8 s before the event; failing that the last open-play frame) it walks back frame by frame for as long as the side keeps the ball. A side
has the ball when its man controls it, and while it is loose when it played it last. A tackle, an interception and a keeper's claim all leave the other
side's man in control, which is how the walk meets a regain. Two gaps are forgiven: a ball nobody holds for up to 1.5 s, and an opponent's control of up
to 0.5 s. A restart (play not open) ends the walk, and the move is then the restart's kick. The film opens 3 s before the regain (1 s before a restart's
kick). The lead is then held to a range: never more than 40 s, and when capped it opens on the side's last pass within 10 s before the cap, so the film
begins on a kick and not halfway through a carry; never less than the minimum the caller asks for; never before the half began. A lead that reaches back
over a restart shows it, and the film fades over the jump as it does anywhere the ball is put down.

**The selector.** Goals, penalties awarded and chances of at least `MinQualityForShotBasisPoints` (7%) use the move; the least lead for a goal is 18 s and
for a chance 15 s, the leads the film had, because a median move is only 6 s (§14.8) and a 3 s lead-in would have made most highlights shorter than before.
Weaker chances and direct free kick strikes keep a 15 s lead; corners, free kicks won and red cards keep theirs. A yellow card is never a reason to show a stretch: the foul is not filmed for its own sake, and the booking is handed to the next passage the film shows, where the viewer puts the card on the player's token. Stretches within 10 s are joined. The target is
`TickMinFilmMilliseconds` 16:00 to `TickMaxFilmMilliseconds` 20:00 (tick-only options; the possession engine's film is unchanged): everything above is
selected in order of worth until the middle of that range, and what is still short is filled with spells at the ends of the pitch (40 s of play each, then
shorter if they no longer fit). The selection aims 20 s over the floor, since the finished film loses a frame at every cut. Over the ceiling, the moves
behind chances are cut back to 15 s first, and only then are whole stretches dropped; goals and red cards stay.

**The sampling.** Players are sampled every tick first (`TickPlayerSampleIntervals` 50, 100, 200, 300 ms against `TickPlayerTolerances` 15, 25, 40, 60,
a rung of the ladder at a time until the film fits `TickPayloadBudgetBytes`), so a path is not cornered by the sampling. At most `TickMaxPassages` = 150
passages. The presentation version is `tick-replay-v2`, so a film cached under v1 is not served for the same match.

**The wire.** The film is about 3.3 times the old one: 6.7 MB of JSON, 1.0 MB with Brotli. The API compresses (Brotli, Gzip) the presentation response only,
over HTTPS as well: other responses are small, and some carry a token, which compression over HTTPS can leak when it shares a response with attacker-chosen
text (BREACH). The budget is on the estimate (24 bytes a keyframe), which is a third of the real JSON: p95 2 283 KB over 200 matches, plus a fifth,
2 750 KB.

Measured over 200 matches, seed 11, engine unchanged (so play and its calibration are as at the end of Milestone 9):

| | Milestone 0 | Milestone 2 |
|---|---|---|
| Film | 9:30, 74 passages | 16:02 to 16:39, 124 passages (cap 150), 81 cuts |
| Regain cut off, goals / shots | 6.4% / 8.5% | 0.2% / 0.3% (moves longer than the 40 s cap) |
| Film made of chances and set pieces, without filler | | about 5:50; the rest is filler |
| Payload estimate p50 / p95 | | 2 227 / 2 283 KB |
| Payload on the wire, JSON / Brotli | 2.0 MB (one match) | 6.8 MB / 1.1 MB |

Cutting a film takes about 60 ms.

The harness, seed 11: 60 fps with no dropped frame, 0 covered tokens; 205 stutters a film minute against 194 before (the denser sampling follows the engine's
stops more faithfully); 100 frame jumps outside the cuts against 111 on the same seed before this milestone.

### 14.11 Who erred and who was beaten (the tick-film plan, Milestone 3)

Recording and viewer only: play is unchanged (the golden hash and the calibration stand; `tickmotion` reproduces the Milestone 0 baseline to the digit).
Four new action tags (`PassageAction`, wire codes in `PassageVocabulary.Code`) say whom to blame. They are stamped on the man at fault, at the moment of
the error, through `Tag(...)`/`TagBlame(...)` in the loop, which only writes the recording and draws nothing:

| Tag | Stamped on | When |
|---|---|---|
| `dispossessed` | the carrier | a tackle is `Won` or `PokedLoose` (the tackler gets `tackle` on the same frame) |
| `misplaced` | the passer | his pass is cut out (`Interception`), or the ball leaves the pitch with the pass still in flight |
| `beaten` | the defender, or the goalkeeper | a tackle is `Beaten`, or a smother is `Beaten` (the carrier keeps the ball) |
| `bypassed` | a defender | he was on the goal side of the carrier within 3 m and, 1 to 3 s later, is 1 m or more behind him, the same man still on the ball and no challenge made (`TrackBypassed`, from the bodies) |

A man is not stamped `beaten` or `bypassed` twice within 3 s (a defender beaten tick after tick in one dribble is shown beaten once). Because a blame tag is
not a touch of the ball, `PassageVocabulary.IsBlame` lets a reader following who played the ball last step over it (`TickMoveFinder.TouchSide` does).

**In the film.** The synthesizer keeps the tags on keyframes (the compressor never drops an action keyframe) and ranks them in `ActionPriority` below a
save and above a pass. Two beat kinds narrate them, `PassageBeatKind.LosesBall` ("X loses it to Y", Y being the other side's tackler or interceptor at
the same frame, when there is one) and `Skipped` ("Y skips past X": Y is the man on the ball, X the man left behind), at priorities 45 and 35, so a
dispossession reads in place of the tackle line and a beaten man in place of the carry line. A keeper beaten in a smother loses his dive streak for that
segment: `beaten` outranks `dive` on the one keyframe.

**On the pitch.** `FilmTimeline.blames` lists the tags of every slot's track in film order; `blamesAt(t, window, out)` fills a caller-owned array with
the mistakes of the last 1.5 s of film, so a frame allocates nothing and a seek shows exactly what playing through would have. The renderer draws, over the
token (after the tokens have been pushed apart), a **red ring** for a ball lost (`dispossessed`, `misplaced`) and an **amber double chevron** for a man
beaten (`beaten`, `bypassed`), each with a small label under the token ("Dispossessed", "Misplaced pass", "Beaten", "Outrun"). The marker holds full for the
first quarter of `BLAME_MILLISECONDS` (1.5 s) and fades to nothing over the rest.

Measured over 200 matches, seed 11 (`tickmotion`):

| Per match | Stamped | In the film | A film minute |
|---|---|---|---|
| dispossessed | 208.0 | 57.3 | 3.47 |
| misplaced | 240.0 | 70.9 | 4.29 |
| beaten | 193.9 | 63.7 | 3.86 |
| bypassed | 3.7 | 1.6 | 0.09 |

That is a marker about every five seconds of film. `bypassed` is rare because a defender who is 3 m goal-side and then 1 m behind without a challenge is
rare in this engine: pressers close the carrier down. Both its window and the rule are constants (`BypassReach`, `BypassMinTicks`, `BypassMaxTicks`,
`BypassBehindUnits`) if the plan's reading is too strict.

### 14.12 The ball flows: meet the pass, decide early, first touch (the tick-film plan, Milestone 4)

Engine play changes; this is the first milestone to move the golden hash (re-pinned to `2984c8c4…`). Matches stored under the old build of engine-v12 refuse to
replay (MAT-9): play new ones.

**What changed.**

| Piece | What it does |
|---|---|
| `TickInterception` | Plays the ball forward 6 s on a scratch ball (`Trace`, the real `Step`) and finds, for a man, the first point he can be at as soon as the ball is (`EarliestReach`: speed now along the line, acceleration to top speed, tiredness, the turn). |
| `PlanBall`, `AssignChasers` | A pass used to send its receiver to where the ball *stops* (up to 25 m past him). Now the man it was played to goes to meet it, within `ReceiverAdjustUnits` (3 m) of where it was aimed, so a pass that goes wide of him is wide of him; the best man of each other side goes to the earliest point he can reach. Chasers run only as hard as they need (`MeetingPace`, a jog at least). |
| `PrepareReceiver` | Four ticks or fewer from the ball, the receiver runs the brain for himself from where he will be; that is his decision on the first tick he has it. |
| Cushioned touch | `TickBallPhysics.Attach(…, cushioned: true)`: the ball runs on a step, is eased over 3 ticks to a touch 1 m ahead along his heading, then drawn in over 3 ticks to the 0.3 m dribbling offset. Who controls the ball is the same, so duels are unchanged. |
| Hold (`HoldTicks`) | First touch 3 ticks; while nobody is on him a man adds a dwell of up to 40 ticks that falls to nothing as the pressure reaches 8,000 bp; with Technique 12 or more he plays first time (1 tick) from 7,000 bp or when he shoots. |
| Lane reach | A defender within 2.5 m of a pass on the ground (swept along the tick's path) gets the cut-out roll, not only one within 1.2 m of where the ball ends the tick. |

**What the faster ball did, and what was done about it.** A ball that arrives in a second and is met at pace roughly doubles the passes a match
(1,100 to 1,750) and shifts the balance of the whole game. Calibration moved these, one at a time against the 205 stored snapshots:

- *Fouls* collapsed (22 to 2 a match) because a carrier is challenged far less: `FoulScalePercent` 250 takes the fouls (and so the cards) from the challenges there are.
- *Scoring on real squads* ran away (7 to 8 goals) while uniform sides barely moved. Flattening each attribute group showed Finishing, Dribbling and Composure carry it; the skill curve went from 60% to 38%.
- *The high press* applied to the opponent's goal kick and, against a ball that now gets out cleanly, was worth +2.6 goals a match to the side pressed (4.4 against 1.7 at the default): `HighPressLimit` stops it at the edge of the opponent's box.
- *Shot error* 210 to 200, the shot floor unchanged (1,850 bp).

Measured over 200 to 300 matches, seed 11 (`tickmotion`, `tick`), and 205 stored snapshots (`ticksnap`):

| | Before (5163b96 + M3) | After |
|---|---|---|
| Ball speed at reception p50 | 2.4 m/s | 11.1 m/s |
| Kick to reception p50 | 48 ticks | 10 ticks |
| Slow tail before reception | 55% | 0.2% |
| Receiver moving toward the ball | 2% | 72% |
| Hold p50 / p90, first-time plays | 3 / 10 ticks, 1.6% | 6 / 21 ticks, 28% |
| Passes a match (engine count), completion | 1,104, 77% | 1,775, 82% |
| Opponents under 1 m outside duels | 35% | 13.5% |
| Stored snapshots: goals, shots, on target | 2.83, 28.3, 38% | 2.85, 29.7, 29% |
| Stored: completion, yellow cards, fouls | 76%, 4.0, 22 | 84%, 3.5, 20 |
| Stored: home / draw / away | 42 / 24 / 34 | 40 / 25 / 35 |
| Uniform sides, board formations (`tick`) | 2.4 goals, 26.5 shots | 2.0 goals, 20.7 shots |
| Play, ms a match (alone) | 296 | 533 |

**Where it is outside the plan, and why.** Shots are 6% over the band and on target is 29% (band 32–38): the shot floor was kept so uniform sides still shoot (a higher
floor starves them: 0.9 goals), and a lower shot error raises goals faster than it raises the on-target share. The hold median is 6 ticks, not 4: the passes a match
fall back to a plausible level only if a free man keeps the ball, and a shorter hold takes the challenges (and the fouls) away. Distance per outfielder is 16.0 km a
90 against 13.6 before and an aim of 9.5–11.5: the block follows a ball that travels twice as far, which M5 (a smoothed, anticipating ball reference) is for.
Equal-level sides are no longer level-invariant (ability 10 / 13 / 16 give 15 / 19.5 / 24.7 shots, the baseline 20 / 22 / 25): the attacker's skills now count for
more than the defender's. A play of 533 ms a match against 296 is the path trace and the extra decisions.

### 14.13 Continuous off-ball movement (the tick-film plan, Milestone 5)

Engine play changes (golden hash re-pinned to `c492e3ef…`; engine-v12 keeps its label). Matches stored under the earlier build refuse to replay (MAT-9): play new ones.

**Why it was chess.** The block followed the ball itself: every anchor was still while a man held the ball, all moved together on each pass and the whole team stopped dead
together inside a metre of the anchor. A possession flip switched the shape in one tick.

**What changed.**

| Piece | What it does |
|---|---|
| Ball reference (`TickTeam.RefX/RefY`, `TickMatchLoop.FollowBall`) | The shape follows not the ball but where it is going: the point the receiver will meet a pass at (M4), a second ahead of a man carrying it, otherwise where a loose ball stops. It closes 25% of the way a tick and never runs faster than 9 m/s. In a dead ball it sits on the ball. |
| Possession blend (`TickTeam.PossessionBlend`, `TickTacticalGeometry.Resolve/ResolveTeam` with a blend) | The shape with the ball and the one without it are mixed by how long ago the ball changed hands: 15 ticks to go from one to the other, not one. |
| Walking about (`TickSteering.Steer`, `TickSteerContext`) | A man in position with a micro-target within 10 m of his anchor does not stand: he walks to it at 18% of top speed (about 1.5 m/s, inside the walk band so he recovers energy) or at the pace his order gives. A man with no job and no square drifts to one of eight points 2 m from his place, a new one every 2 s and at a different tick for each seat (`TickMatchLoop.Drift`). |
| Easing in, inertia | A man holding his place (no pace in his order) brakes at 60% of what he has, so he eases onto his anchor; one sent at a pace brakes in full. Inertia 30% to 45%. |
| Looking for room (`TickOffBallSupport.FindSpace`) | A midfielder or forward with nothing to do scores eight squares (four at 4 m, four at 8 m) every 10 ticks, a seat at a time (`(tick + 3 × seat) % 10 = 0`): distance to the nearest defender (to 8 m), a clear lane from the carrier (+4 m), room from teammates (to 8 m, a quarter), less half the distance from his place. He keeps his square unless a new one beats it by 15%, stays onside (`ClampOnside`) and walks to it at 30–40% of top speed. A back-line man and the goalkeeper do not. |
| Adjusting (`TickDefensiveAI.AssignAdjustments`) | A defender who is only holding his place (`Holding`, `Line`) steps every 10 ticks, a seat at a time, to 2.5 m from the nearest attacker within 12 m of his place, goal-side of him and into the lane from the ball, at 30%; a back-line man stays within 2.5 m of the line's height and none goes more than 6 m from his place. The presser and the markers are not touched. |
| Personal space (`TickSteering`) | A man who is not engaged (not the carrier, the presser or second presser, the man sent to meet the ball, nor off balance) is pushed from an opponent within 2.5 m, at twice the strength of the teammate push (80% of top speed at contact). In a dead ball it is off. |

**What the measurements said.**

- A half-strength push at 1.5 m (the plan) left the overlap at 13.7%: the pairs were strikers held one metre short of the offside line beside the centre-backs they face, the pull to
  the place beating the push. 2.5 m at twice the strength takes it to 5.4%; a push of 3.5 m at four times takes it only to 4.2%, so what is left is two men crossing
  (a marker jogging alongside his man, runners passing), not two men sitting. The probe now also counts pairs where both move under 1.5 m/s: 0.7% before, 0.0% after.
- Easing every man in at 60% of his braking raised the fouls by a third (20 to 26 a match, yellow cards 3.5 to 4.6, reds 0.4 to 0.8): a presser who slows early stays in
  contact range and challenges again and again. Applying it only to a man with no pace in his order gave the fouls back (20.7, 3.7, 0.5).
- The walk is slower than the plan's "30% of top speed" (18%, search squares 30–40% instead of 45–60%, adjusting 30%): at the plan's values nobody stood, but the
  distance run was 15.8 km a 90, and the plan's aim is 9.5–11.5. Slowing every role's pace by a third, or the holding speeds, gained only a kilometre more and would have
  moved the defence; they were left.
- Stored squads are not moved by any of it (goals 2.85 to 2.89); uniform squads on the board formations score 11% fewer goals (1.97 to 1.75) and shoot 4% less. Switched off one at
  a time on the uniform harness: the push −4%, the search for room −6%, the adjusting −4%, the walking and drifting together −18%; the smoothed reference adds shots (+7%).
  None of it was bought back with another lever.

Measured over 200 matches from seed 11 (`tickmotion`), 205 stored snapshots (`ticksnap`), 1,000 matches (`tick`) and 20 matches of each pair of the 13 formations (`tickmatrix`):

| | Before (M4) | After |
|---|---|---|
| Standing (under 0.3 m/s), outfield, open play | 11.6% | 2.5% |
| Stop-and-go per player-minute | 8.05 | 3.17 |
| 12 or more outfielders under 0.5 m/s at once | 3.1% | 0.0% |
| Speed dips per player-minute (engine, 10 Hz) | 0.29 | 0.19 |
| Distance per outfield player per 90 | 16.0 km | 13.8 km |
| Opposing pair closer than 1.0 m, outside duels | 13.5% | 5.4% |
| … of which both under 1.5 m/s | 0.7% | 0.0% |
| Receiver moving toward the ball at reception | 72% | 82% |
| Viewer stutters a film minute (seed 11) | 275 | 203 |
| Stored snapshots: goals, shots, on target | 2.85, 29.7, 29.3% | 2.89, 28.8, 30.8% |
| Stored: pass completion, yellow cards, fouls | 83.7%, 3.50, 19.6 | 84.6%, 3.70, 20.7 |
| Stored: home / draw / away | 40.0 / 24.9 / 35.1 | 48.3 / 22.9 / 28.8 |
| Uniform sides, board formations (`tick`): goals, shots | 1.97, 20.5 | 1.75, 19.7 |
| All 169 formation pairs (`tickmatrix`): goals, shots | 1.93, 20.3 | 1.76, 19.4 |

**Where it is outside the plan, and why.** The standing, stop-and-go and synchronised-stop figures meet the plan (under 10%, down 50%, down 70%). The overlap does not reach 1%:
5.4% of frames still have a pair under a metre outside the ball's 3 m, almost all of them men crossing at speed, and the viewer's de-overlap (§14.9) covers those. Distance is 13.8 km, not
9.5–11.5. The viewer's stutters are down a quarter, not by 70%: the engine's own dips are rare (0.19 a player-minute is about 8 a film minute), so what the harness counts is the
sampling and the spline, which is the viewer's. The home / draw / away split of 205 matches moves by more than the 205-match noise (±3.5 points) in its home share and should be read with that.

### 14.14 Reading the receiver's situation (the tick-film plan, Milestone 6)

Engine play changes (golden hash re-pinned to `5286dc38…`; engine-v12 keeps its label). Matches stored under the earlier build refuse to replay (MAT-9): play new ones. All of it is in
`TickBallCarrierBrain` and is a pure function of the positions, so no random draw moved.

**What was missing.** A pass was judged by its lane (defenders within 3.5 m of the line) and the receiver's room *now*. It did not ask what would be waiting when the ball got there, played
every ball at the receiver's body wherever his marker stood, led a runner by a fixed 8 ticks whatever the length of the ball, and had no idea of a switch of play or of a defence backing off.

| Piece | What it does |
|---|---|
| Pressure on arrival (`ArrivalPressure`) | The ball's flight time (`TickBallPhysics.RollingTicks` for a rolled ball, `LoftedFlightTicks` for a lofted one, the same closed forms the launch uses) is set against the nearest defender's time to the receiver's spot at 6.5 m/s. Every tick by which he is there sooner (and two ticks for the first touch) takes 1.5% from the pass's chance, up to 15%. |
| The safe side (`SafeSide`) | A ball for a man who is standing or jogging, with a marker within 4 m of the spot, is moved 0.5 m (marker at 4 m) to 1 m (on top of him) across the ball's line to the side away from the marker. The length does not change. |
| Meeting a runner (`MeetingPoint`) | A through ball is aimed where the ball and the runner get there together: the lead is the ball's flight time, refined three times, between the old 8 ticks and 14. |
| Switch of play (`Congestion`, `IsSwitch`) | With four or more defenders within 20 m of the carrier on his half of the width, a ball to a teammate on the other half, 30 m or more across, with 10 m to his nearest marker, is worth 15 m more (`SwitchBonus` 1 500). |
| Driving into space | When the nearest defender is over 8 m away and moving away at 1 m/s or more, a carry is worth up to `DriveBonus` (1 200) more, in proportion to the ground taken. |

**What the measurements said.** Each piece was switched on alone against the M5 build (205 stored snapshots, 200 matches of `tickmotion`):

- *Arrival pressure at 2.5% a tick, up to 30%* (the first draft) was the whole of the effect and too strong: interceptions fell 24%, tackles 29%, fouls 27% and yellow cards to 2.6, while
  crosses rose 76% and goals to 3.3 (a pass into a defended area lost to a cross, which is not weighed this way). At 1.0% a tick it was nearly right (goals 2.94, yellow 3.46); 1.5% a tick up to 15% was kept.
- The safe side alone: the median room of the man receiving does not move (4.7 m against 4.8) and shots fall a little (27.3 against 28.8).
- The meeting point, the drive and the switch moved the totals by less than the 205-match noise.
- **The switch bonus barely fires.** It is worth more than a pass to a free man, but a ball of 35 m or more is lofted and the carrier's accuracy model gives an average passer a 30% chance of
  landing it within a receiver's catch, so only a skilled passer ever takes it. Raising the bonus to 4 000 left the switches at 69.1 against 68.6 (by the stamp count below). Left as it is: it shifts the choice for the
  skilled passer and costs nothing elsewhere.
- **Switches a match is already in the plan's aim.** Counted as balls a man touches that cross 30 m or more of the width, the M5 build plays 8.4 a match (aim 8–20) and this build 8.5.
  (Counted over every `pass` stamp it is 61 and 78, because the stamps include the backward balls below.)

**A finding that is not changed here.** About 1 100 of the 2 900 `pass` stamps in a match (a backward ball over 2 m, `TickCarrierAction.Recycle`) are not followed as passes:
`AfterKick` has no case for them, so they set no pending pass and no intended receiver, count in nobody's `PassesAttempted`, give the receiver no early decision, and show no `receive` stamp, `misplaced` blame
or completion. They are also why the stamp count (2 900) is nearly twice the engine's own (1 700). Counting them as passes (tried, `Recycle` joined to the pass case) moves the play: 2 400 attempts,
interceptions up from 214 to 294 a match, passes forward 63% instead of 91%. It needs its own decision and calibration.

Measured over 200 matches from seed 11 (`tickmotion`), 205 stored snapshots (`ticksnap`), 1 000 matches (`tick`) and 17 matches of each pair of the 13 formations (`tickmatrix`):

| | Before (M5) | After |
|---|---|---|
| Passes a man touches a match, share intercepted | 1 665, 14.4% | 1 627, 13.2% |
| Interceptions a match | 249 | 225 |
| Median room of the man receiving (to the nearest opponent) | 4.8 m | 5.2 m |
| Final-third receptions a match | 403 | 375 |
| Switches of play a match (30 m across) | 8.4 | 8.5 |
| Forward share of passes a man touches | 92.7% | 91.3% |
| Stored snapshots: goals, shots, on target | 2.89, 28.8, 30.8% | 2.70, 27.9, 30.4% |
| Stored: pass completion, yellow cards, fouls | 84.6%, 3.70, 20.7 | 85.6%, 3.45, 18.4 |
| Stored: home / draw / away | 48.3 / 22.9 / 28.8 | 40.0 / 24.4 / 35.6 |
| Uniform sides, board formations (`tick`): goals, shots, corners | 1.75, 19.7, 6.8 | 1.80, 19.4, 8.4 |
| All 169 formation pairs (`tickmatrix`): goals, shots | 1.76, 19.4 | 1.72, 18.9 |
| Standing, stop-and-go, distance (unchanged) | 2.5%, 3.17, 13.8 km | 2.6%, 3.22, 13.8 km |

**Where it is outside the plan, and why.** The plan's aim for switches was reached before the milestone began, so the bonus adds nothing to the count. The home share of 205 matches is below its band
(40.0 against 42–48, the away share 35.6 against 28–34) but moves by 4 to 8 points between builds that differ in one tiny lever (43 to 48 with the switch alone, the drive alone, the meeting point alone), which is the noise of 205 chaotic
matches, not a trend. The stored-snapshot pass completion is 0.6 points over the band's 85%, inside the 3% accepted. Corners rise by a quarter (more crosses, fewer interceptions).

### 14.15 Combination play: the wall pass, the third man and the cut-back (the tick-film plan, Milestone 7)

Engine play changes (golden hash re-pinned to `19800ed9…`; engine-v12 keeps its label). Matches stored under the earlier build refuse to replay (MAT-9): play new ones. No random draw moved: the runs are
chosen from positions, and the brain is still a pure function of the scene.

| Piece | What it does |
|---|---|
| Return run (`TickCombinations.TryReturnRun`, role `Return`) | When a midfielder or forward plays a pass of under 15 m to a team-mate with a defender within about 5.5 m of him (raw pressure 3 000 or more), the passer runs for 20 ticks to a point 7 m beyond that defender and 4 m to the side the passer is on, at 90% of his top speed. Not made if the run gains under 3 m or is over 20 m long. A defender never makes it. |
| Wall pass (`WallPassBonus` 1 500) | A ball to the returning passer is aimed where the ball and he meet (the through-ball rule) and earns the bonus when he has 4 m to the nearest defender (`FreeRunnerReach`). |
| Third man (`TryThirdMan`, role `ThirdMan`) | A pass of 5 m or more forward into a forward who is pressed and has a defender at his shoulder on the goal side (6 m) sends the nearest midfielder (within 30 m, neither the passer nor the receiver, level with or behind the forward) to a point 10 m beyond the forward and 4 m away from his marker, for 25 ticks at 95%. |
| Lay-off (`LayOffBonus` 1 500) | The pressed forward's ball to that runner, when he is free, earns the bonus. |
| Cut-back (`IsCutBack`, `CutBackWorthPercent` 35) | A ground ball from within 10 m of the goal line and 20 m of the middle, back by 3 m or more to the zone 7 to 22 m out and within 16 m of the middle, is worth 35% of the receiver's own `ExpectedGoals` there (his blockers, his finishing), valued as a shot is (x3, by mentality), in place of the progress a ball back would lose. |

Both runs are orders (`TickAttackingRole.Return`, `ThirdMan`) given by `TickOffBallSupport.Assign` before every other job, **also while the ball is in the air** (the passer is already moving when it is
played), and clamped onside of the line as it stands. The loop holds them per side by slot number (a sending-off closes the seats up, a slot keeps its name) and ends them when the ball reaches the man,
when their ticks are up, at a change of possession and at a dead ball. A ball to a man on a combination run, and a cut-back, are passes whichever way they go: they are not `Recycle` balls, which the loop
does not follow (§14.14), so they get a pending pass, an early decision for the receiver, a `receive` or `interception` stamp and a completion.

**What the measurements said.**

- The counters in `tickmotion` first showed that "ball back to the passer within 6 s" counted 93 a match before this milestone existed (ping-pong between two men), so a one-two now needs a pressed first
  receiver, a return within 2 s, and the passer 4 m farther up the pitch than he kicked from. A third-man ball needs the same pressed receiver, 5 m gained into him and 8 m gained out of him to someone else.
- Counted in the loop with temporary counters (60 matches, before the last change below): the runs are started about 197 (return) and 203 (third man) times a match, both sides; 96 balls go to the returning
  man (66 completed, 28 intercepted) and 121 to the third man (only 45 of them followed as passes then; the rest were backward balls, which is why the last change was made).
- **Not changed, but worth knowing:** the third-man count in `tickmotion` does not move (61.4 to 60.4 a match). The run gives the forward a free man to play to and the loop uses it (about 120 balls a
  match go to him), but those balls replace passes that would have gone forward to a midfielder anyway, so a chain of "pressed forward, ball on, 8 m gained" is as common as it was. The run is
  visible in the film; the count of such chains is not a measure of it.
- Joining the combination balls to the pass case lifts the passes the loop follows from 1 701 to 1 776 a match, interceptions from 241 to 249, and yellow cards from 3.19 to 3.04, and makes the lay-offs
  real passes. It was kept; goals, shots and on target stayed in the band.

Measured over 200 matches from seed 11 (`tickmotion`), 205 stored snapshots (`ticksnap`), 1 000 matches (`tick`) and 17 matches of each pair of the 13 formations (`tickmatrix`):

| | Before (M6) | After |
|---|---|---|
| One-twos a match, both sides (pressed man, back within 2 s, 4 m farther up) | 8.2 | 24.6 |
| Third-man balls a match (pressed man, on within 2 s, 8 m farther up) | 61.4 | 60.4 |
| Cut-backs completed a match, followed by a shot within 3 s | 0.1, 14% | 5.3, 10% |
| Goals by the last ball a match: cross / cut-back / one-two / third man / other pass | 0.12 / 0.00 / 0.00 / 0.03 / 0.67 | 0.09 / 0.02 / 0.06 / 0.02 / 0.64 |
| Passes a man touches a match, forward share, share intercepted | 1 627, 91.3%, 13.2% | 1 776, 86.1%, 14.0% |
| Final-third receptions a match, median room on receiving | 375, 5.2 m | 469, 4.9 m |
| Crosses (stamps) a match | 36 | 40 |
| Stored snapshots: goals, shots, on target | 2.70, 27.9, 30.4% | 2.68, 27.6, 31.0% |
| Stored: pass completion, yellow cards, fouls | 85.6%, 3.45, 18.4 | 84.0%, 3.04, 17.4 |
| Stored: home / draw / away | 40.0 / 24.4 / 35.6 | 42.9 / 20.0 / 37.1 |
| Uniform sides, board formations (`tick`): goals, shots, corners | 1.80, 19.4, 8.4 | 1.77, 19.9, 8.2 |
| All 169 formation pairs (`tickmatrix`): goals, shots | 1.72, 18.9 | 1.74, 19.6 |
| Standing, stop-and-go, distance | 2.6%, 3.22, 13.8 km | 2.6%, 3.15, 13.9 km |
| Opponents under 1 m outside duels | 5.1% | 5.3% |

**Where it is outside the plan, and why.** The stored snapshots' on-target share (31.0%) is a point under the band's 32%, and the yellow cards (3.04) are at its edge: both inside the 3% accepted and within what a
lever this small moves them by. The home share swings by several points between builds that differ by a tiny lever (the noise of 205 chaotic matches). The pressed man's ball to the wall-runner is intercepted 29%
of the time, against 13% for passes in general, because the runner is moving toward a defender; that is the risk of the pass, and the film shows who lost it (`misplaced`). The runs add a little running (13.8 to 13.9 km),
which was already above the 9.5 to 11.5 km aim.

### 14.16 The tick-film plan, finished (Milestone 8)

Nothing changes in play here: this is the plan's measurement at its end, against its start. The engine is as committed at `b68777e` (Milestone 7); the baseline is `5163b96`.
Decision: [ADR-0067](../architecture/adr/0067-tick-film-whole-moves-fluid-motion-and-smarter-play.md).

**The five complaints, before and after** (`tickmotion 200 11`; the film from `tick 1 11 --dump`):

| | Baseline (`5163b96`) | Now |
|---|---|---|
| Film length, passages, payload estimate | 9:30, 74, 704 KB | 15:49 to 16:42 (mean 16:20), 139 a film (p95 estimate 2 608 KB of 2 750), 1.2 MB Brotli on the wire |
| Goals whose regain is before the film window | 6.4% | 0.0% (shots 8.5% to 0.2%) |
| Ball speed at reception p50; slow tail | 2.4 m/s; 55% | 11.1 m/s; 0.3% |
| Kick to reception p50 | 48 ticks | 9 ticks |
| Receiver moving toward the ball | 2% | 81% |
| Hold p50; first-time plays | 3 ticks; 1.6% | 6 ticks; 28% |
| Standing (under 0.3 m/s) | 12.2% | 2.6% |
| Stop-and-go per player-minute | 7.5 | 3.15 |
| 12 or more outfielders under 0.5 m/s at once | 8.6% of frames | 0.0% |
| Distance per outfield player per 90 | 13.6 km | 13.9 km |
| Opponents under 1 m outside duels | 35% | 5.3% (both slower than 1.5 m/s: 0.0%) |
| Drawn tokens with centres under 0.6 radius | 27% of steps | 0.0% |
| Viewer stutters a film minute (seed 11) | 194 on the same viewer | 213 |

The five complaints, in turn. **Short highlights:** answered (whole moves, and about 16 minutes), though about 10 of the 16 are spells of play and not chances (§14.10).
**Stiff ball and receiver:** answered (the ball arrives at pace, the receiver meets it and plays first time more than a quarter of the time). **Chess motion:** answered in the engine
(standing and stop-and-go fall by 79% and 58%, and the block no longer stops together), but **not in the viewer's stutter count**, which is no lower than before (213 against 194 on
the same viewer: the dips are in the 100 to 300 ms keyframe sampling of players, and a softer push made it worse, §14.9). **Stacked tokens:** answered on screen (no covered pair); the engine
still has opponents under 1 m in 5.3% of frames, men crossing, which the viewer pushes apart. **Smarter play:** the pressure on arrival, safe side, switch and drive are in but small in the totals
(§14.14); the one-two (8 to 25 a match) and the cut-back (0 to 5) are visible, the third man's count is not (§14.15).

**The balance, final.**

| | Stored snapshots (205) | `tick` 3 000, uniform, board formations | `tickmatrix` 15 a pair (2 535) | Band |
|---|---|---|---|---|
| Goals a match | 2.68 | 1.81 | 1.70 | 2.60–2.90 |
| Shots a match | 27.6 | 19.8 | 19.2 | 22–28 |
| On target | 31.0% | 28.8% | 28.4% | 32–38% |
| Pass completion | 84.0% | 83.1% | 83.6% | 75–85% |
| Yellow cards | 3.04 | 1.73 | 1.79 | 3.0–4.5 |
| Home / draw / away | 42.9 / 20.0 / 37.1 | 38.9 / 34.1 / 27.0 | 39.6 / 33.6 / 26.7 | 42–48 / 22–26 / 28–34 |

**Outside the bands, and why.** The stored squads, the game's real matches, are in the band for goals, shots, completion and yellow cards; on target is a point under, and home / draw / away is
within the noise of 205 matches. **Uniform sides are not**: they score 1.7 to 1.8 goals from 19 to 20 shots and get 1.7 yellow cards, a third under the band, and draw 34% of matches. That is the cost of
the skill curve (`CurvePercent` 60 to 38, §14.12), which was set to stop real squads running away on the faster ball and so takes away the edge a *uniform* side gets from its attributes being at 13. The
plan's bands were written for uniform 4-4-2 sides at Milestone 9; the game plays squads, so the stored matches are the yardstick that was kept. Of the 169 formation pairs 31 are inside 2.2 to 3.4 goals; 3-4-3
against itself is the worst (3.87 goals, 30.9 shots) and 4-4-1-1 the weakest scorer (0.43 a side). These spreads were present at Milestone 9 (§14.5) and are not new. Play costs 657 ms a match with the live
recording (alone, M4 measured 533 ms), 49 ms of it the film.

**Not reached.** Distance per outfield player (13.9 km against the aim of 9.5 to 11.5, and 13.6 at the start) and viewer stutter (no lower). Matches stored under any earlier build of engine-v12 refuse to
replay (MAT-9); new matches must be played, and a browser may need a hard reload to show a film re-made for an old match (a presentation is cached `immutable` for each URL).

### 14.17 No hoofing out of the opponent's half (the keeper-gift plan, Milestone 1)

The first step of the plan to cut goals that come straight off a goalkeeper's kick (26% of goals on the stored snapshots, 4 of the 5 in three films watched). The `goalorigin` probe
(`tools/simulation-benchmarks/GoalOriginProbe.cs`) found 72 goal kicks a match, 41 of them an attacker's `Clear` from the opponent's half that rolled over the byline, and the keeper's short
kick that followed went to a centre-back with an opponent on him.

**The change** (`TickBallCarrierBrain.ScoreClearance`): a clearance is considered only while the carrier is in his own half (`ClearZoneLimit`, 50 m); beyond it a pressed carrier passes, recycles,
shields or dribbles, as he already could. A clearance from the own half aims `X + 30 m` as before but never past `ClearMaximumX` (75 m), so it cannot land in the byline zone.

| Stored snapshots (205) | Before | Now |
|---|---|---|
| Goals a match | 2.68 | 2.87 |
| Goals straight off a keeper's kick | 144 of 549 (26.2%) | 57 of 588 (9.7%) |
| Goal kicks a match | 72 | 30.1 |
| Goal kicks put out by the keeper's own pass a match | 11.6 | 4.5 |
| Shots a match; on target | 27.6; 31.0% | 32.5; 29.6% |
| Yellow cards; fouls | 3.04; 17.4 | 3.80; 21.0 |

The ball now stays in play, so there are more turnovers in the final third and more shots (32.5, over the band's 22 to 28; M5 of the plan re-balances shot volume). Goals are inside the plan's
2.4 to 3.0. `tickmotion` is unchanged in kind (standing 2.6%, distance 13.9 km, one-touch 29.8%). Short goal kicks still turn over 21% of the time (Milestone 3) and the keeper's long balls still
roll out (Milestone 2).

### 14.18 Keeper long balls stay in play (the keeper-gift plan, Milestone 2)

A lofted ball was struck to land on its target and then kept going: it lost 15% of its horizontal speed at the first bounce and rolled on at 96% a tick, so a 35 m ball ran on about 66 m and a
60 m one off the pitch. 4.5 goal kicks a match were the keeper's own long ball rolling out over the far line.

**The change.** A lofted pass or long kick (a pass of 35 m or more, which includes every long goal kick) is launched with a *soft landing* (`TickBallPhysics.LaunchLofted(..., softLanding: true)`):
the first bounce keeps 10% of the horizontal speed (`SoftLandingKeptBasisPoints`) and 45% of the vertical (`SoftLandingRestitutionBasisPoints`), so the ball checks on the turf, a 60 m ball stops
within about 7 to 13 m of where it came down, and the receiver has it to play. Crosses, clearances and free kicks do not take the flag and land as they did. The decision also caps where such a
ball is aimed: no deeper than `LongBallDeepestX` (88 m, 12 m from the goal line), in the pass scoring (`ScorePasses`) and in `TickSetPieces.DecideGoalKick`. A long goal kick was already aimed at
the receiver's position, not past it.

| Stored snapshots (205) | Before (M1) | Now |
|---|---|---|
| Goal kicks put out by the keeper's own pass a match | 4.5 | 0 |
| Keeper's long goal kicks (45 m and over) a match; completed; turned over | 11.2; 55.3%; 44.7% | 13.5; 65.2%; 34.8% |
| Keeper's goal kicks that leave the pitch a match | 5.8 | 0.2 |
| Goal kicks a match | 30.1 | 26.0 |
| Goals straight off a keeper's kick | 57 of 588 (9.7%) | 59 of 655 (9.0%) |
| Pass completion; shots a match | 84.0%; 32.5 | 84.3%; 32.9 |

Goals read 2.87 and 3.20 on the same 205 matches, which is noise: four draws of the squads (`TICK_REDRAW=n ticksnap`, a different seed on the same inputs) average 2.82 before and 2.91 now, and
the 1,014 uniform matches of `tickmatrix 6` are 2.06 and 2.08. `tickmotion` is unchanged in kind (standing 2.8%, distance 13.87 km, one-touch 29.9%).

**A film fix that this exposed.** Longer completed moves made a goal's lead long enough to be cut into ten-second passages, and the cutter took the goal's own frame (the stoppage that starts the
celebration) as a natural place to cut. The goal then fell in the passage that held only the celebration, and the strike in the one before. `TickReplaySynthesizer.Naturals` no longer offers the
goal-celebration stoppage as a cut. Film only; it changes no play.

### 14.19 Cautious goal kicks (the keeper-gift plan, Milestone 3)

A short goal kick went to the better-passing centre-back whatever stood round him. The layout only kept the opposition out of the penalty area, so a pressing side's forwards waited on the
edge of it, a few metres from the man the keeper was about to play to, and every style but Direct played short regardless.

**The layout.** The opposition keep at least 10 m (`GoalKickReceiverClearance`) from each split centre-back's spot. A player the push would leave in the area is moved up the pitch to the
radius instead (`PushAwayUpfield`), so clearing the area never brings him back within it. A pressing side can still mark a full-back or a midfielder.

**The decision.** Short is conditional for every style. The candidates are the defenders within 30 m of the ball (the two centre-backs, and a full-back who has dropped in); one counts only if
no opponent is within 8 m of him (`ShortGoalKickSpace`) and none stands within the engine's intercept reach of the lane from the keeper (`TickOffBallSupport.InterceptReach`, the segment test
`LaneClear` uses). The freest wins, the layout's better passer getting 3 m of head start (`ShortGoalKickPreference`). ShortPassing plays short to him, or goes long if nobody is safe; Mixed keeps
its `pressed < 2` gate and also needs a safe man; Direct is unchanged.

| Stored snapshots (205) | Before (M2) | Now |
|---|---|---|
| Goals straight off a keeper's kick | 59 of 655 (9.0%) | 35 of 544 (6.4%) |
| Of which from goal kicks | not split | 10 (0.05 a match) |
| Goal kicks a match; short (under 25 m); long (45 m and over) | 26.0; not split; 13.5 | 25.3; 9.2; 14.7 |
| Short goal kicks turned over | 21% (the plan's baseline) | 15.3% |
| Goals a match; shots | 2.9 (noise, see 14.18); 32.9 | 2.65; 32.1 |

**Not reached.** The plan expected short goal kicks to be lost about 5% of the time. They are lost 15%: a receiver 8 to 10 m clear is reached by the press while the ball is still travelling 19 m,
and a lost one still becomes a shot 41% of the time. A wider margin (15 m clearance, 12 m space) was tried: the loss was 13% on the same matches, within noise, and it cut the short kicks from
9.2 to 7.1 a match, so the plan's margins stay. The goals that matter did fall: the goals from short goal kicks are 10 a 205 matches, from 124 for all goal kicks at the start of the plan. `tickmotion` is unchanged in kind (standing 2.6%, distance 13.89 km, one-touch 30.0%).

### 14.20 A risk-averse keeper in open play (the keeper-gift plan, Milestone 4)

A keeper with the ball in his hands scored his passes like any other carrier, so he rolled the ball to a defender with a forward standing a few metres from him, and 49% of the balls he lost
became shots (25 goals in 205 matches).

**The filter.** In `ScorePasses` a goalkeeper considers a receiver only when the nearest opponent to the aim is at least 9 m away (`KeeperReceiverClearance`) and the pass has a 90% chance of
coming off (`KeeperSafePassChance`; lane survival, accuracy and arrival pressure as for anyone). Failing that, a lofted ball to a free midfielder or forward is a fallback that competes only when
no safe pass exists (it still goes through the 88 m cap of 14.18). Failing that, the keeper clears (`ScoreClearance` now ignores the panic threshold for a keeper with nothing else on): the clearance
is aimed at the wing away from the nearest opponent and no deeper than 75 m, as in 14.17. The plan's 85% and 6 m were tried first and left 12 open-play goals; the keeper-gift track was already
under its 10% aim, so the stricter pair is the one kept.

| Stored snapshots (205) | Before (M3) | Now |
|---|---|---|
| Goals straight off a keeper's kick | 35 of 544 (6.4%) | 20 of 544 (3.7%) |
| Of which from the keeper's open-play pass | 25 | 5 |
| Keeper short (under 25 m) open-play balls a match; turned over | 31.5; 5.1% | 20.0; 2.1% |
| Keeper open-play balls of 25 m and over a match | 0.3 | 8.7 (medium 19.7% turned over, long 12.1%) |
| Goal kicks a match | 25.3 | 26.1 |
| Goals a match; shots | 2.65; 32.1 | 2.65; 32.2 |

**Not as planned.** The plan expected the keeper's open-play turnovers to fall from 5% to 2%. Those on short balls did (2.1%), but the keeper now kicks 8.7 balls a match that are not short, and a
fifth of the medium ones are lost, so the keeper's overall turnover rate is 6.5%. These are lost 30 m or more from his goal, where 6 to 7% of them become a shot (49% of the short ones did). 1.6 a match of his
clearances end without a receiver (out of play or loose). `tickmotion` is unchanged in kind.

### 14.21 Shots from farther out, and a goalkeeper who meets a ball played back to him (the keeper-gift plan, Milestone 5)

**Shots.** Only 1 open-play shot in 100 came from beyond 12 m: the threshold (`ShotMinimumChance`, 1,850) is on the heuristic chance, and in the final third nothing competes with a shot above it (a pass
there has little forward gain left to earn, so its utility is small). The levers the plan names do almost nothing for that reason: `ShotValueMultiplier` 3 to 2 to 1 moved shots from 54 to 50 a match, and allowing
one blocker instead of three, or widening the blocking lane to 4.5 m, changed nothing much (a third of the close shots are blocked in flight, not on the line at the decision). Paying a pass to a man in a shooting
position a share of his own chance (as the cut-back already is) was tried at 30% to 150% and dropped: the move that ends in a pass to the man ends in his shot, so it raised the volume instead of lowering it. What works:

- `ShotMinimumChance` 1,850 to 1,400: shots from 12 to 18 m go from 1.0 to 8.0 a match.
- Calibration, since more shots from farther out doubled the goals: `TickShotStopper.ReactionBaseMilliseconds` 350 to 250 (the check is by whole 100 ms ticks, so only the tens digit of the base matters: 250 is a tick quicker for a goalkeeper of
  reflexes 15, 260 and 290 are alike) and `ShotBaseError` 200 to 110 (the far shots had left on target at 22%).
- `GoalOriginProbe` gets a shot-by-distance table and a "what put the ball behind for a corner" table.

**A fault the change exposed.** With the threshold at 1,100 corners went from 10 to 24 a match. 84% were given by a defender's last touch: a defender pressed in his own area plays the ball back to his goalkeeper
(`Recycle`) and the goalkeeper, who stood on his guard arc and treated a ball faster than 4 m/s as a pass or a shot rather than something to collect, let it run through to the line, or to an attacker. (7.6 corners a match were
this even before the change, and 65 goals in 205 matches were "a pass by an opposing outfielder reached us, then a first-time shot"; about 15 are now.) The goalkeeper now steps into the line of a ball played back to him
(`TickKeeperSituation.PassedToHim`, `TryMeetPass`: a ground ball running to his goal, met at the nearest point of its line, inside 25 m of the line, `MeetPassDepth`). `Recycle` is not a pass in the match loop (no
pending pass, no intended receiver), so its receiver is kept in `_recycledTo`, cleared by any touch. That takes about 0.3 goals a match out of the old calibration, which is part of why the finishing is re-balanced above.

| Stored snapshots (205) | Before (M4) | Now |
|---|---|---|
| Goals a match | 2.65 | 2.60 (three re-drawn sets: 2.55 to 2.68) |
| Shots a match; on target | 32.2; 28.8% | 40.4; 39.4% |
| Open-play shots a match under 6 m; 6 to 12 m; 12 to 18 m | 3.8; 17.6; 1.0 | 3.6; 18.4; 8.0 |
| Mean distance of a built-up shot | 9.0 m | 9.6 m |
| Goals from a move of 3 or more passes | 34% | 41% |
| Passes a match (engine count); completion | 1,989; 84.2% | 1,990; 84.3% |
| Corners; fouls; penalties a match | 10.1; 21.8; 0.37 | 4.3; 21.6; 0.36 |
| Goal kicks a match | 26.1 | 25.2 |
| Goals straight off a goalkeeper's kick | 20 of 544 (3.7%) | 53 of 534 (9.9%) |
| Home / draw / away | 43 / 22 / 34% | 38.5 / 25 / 36.6% |

**Not as planned.**
- The plan wanted 25 to 30 shots a match at a mean distance of 14 to 16 m. The close shots (about 22 a match from under 12 m) do not depend on the threshold, so the shots from farther out are added to them: 40 shots (the band is 22 to 28), 39% on target (band 32 to 38%). Reaching 28 shots would
  take fewer shots from inside the area, which no lever tried here does; that is a decision for the user. Nothing is shot from beyond 18 m (the heuristic range is 26.5 m) and the mean distance is 9.6 m: the far shots are a fifth of the total.
- Goals off a goalkeeper's kick rose from 20 to 53 (9.9%, on the plan's aim) because the keeper now holds the ball when he is played back to, where before those back-passes ran past him; 65 goals of that kind used to be filed under "opposing outfielder".
- Corners fell below the real figure (about 10): the old ones were mostly missed back-passes. A blocked or saved shot turned behind for a corner is rare in this engine (0.25 a match for the goalkeeper).
- `TickPlay`'s default seed moved from 20,260,925 to 20,260,926: the old one is now a 0-0 draw and two film tests need a goal.

### 14.22 Through balls that are played (the keeper-gift plan, Milestone 6)

**Diagnosis.** A temporary counter (removed again) over the 205 stored matches: a carrier with a runner in sight chose the through ball in about 3,100 decisions, and in 900 separate streaks of them (4.4 a match), but only 20 were kicked (0.1 a match).
The cause was the hold, not the offside check, the runners or the lane. A through ball was held like any pass: a free carrier takes his first touch and then looks up for up to 4 s (`HoldTicks`: 3 plus up to 40, 39 on average), and he re-decides every one or two ticks
while he waits. 98% of the streaks flipped to something else after ten ticks on average (72% to an ordinary pass, 18% to a dribble, 8% to a recycle), by which time the run had gone. The rest of the suspects counted for little: the receiver was offside at the flip in 14% of the flips,
the counter saw none lost to a tackle, and the meeting point was beyond the `LongBallDeepestX` cap in 16% of the evaluations.

**Changes.**
- A through ball is hit on the first tick however free the carrier is (`ThroughBallHoldTicks`, 1; was 3 to 43). That alone gave 3.9 kicked a match.
- The brain judged the ball to a runner at 29% (`Accuracy` takes the ball to land within `CatchWidth`, 2.5 m, of a man standing), where 70% came off once they were kicked: a runner is running and adjusts his line to the ball. A ball to a runner now has `RunnerCatchWidth`, 3 m. The volume is
  very sensitive to this (2.5 m 3.9 a match, 3.0 m 5.5, 3.5 m 7.3, 5.0 m 14.5) and nearly deaf to `ThroughBonus` (2,500, left as it was: 1,500 and 800 at 5.0 m gave 12.5 and 10.5), so the width is the knob.
- Lane survival already uses the meeting point (it is the aim of the ball), so it was left alone.

| Stored snapshots (205) | Before (M5) | Now |
|---|---|---|
| Through balls kicked a match | 0.1 | 5.5 |
| ...reaching a team-mate; intercepted | n/a | 67%; 31% |
| ...called offside | n/a | 0.005 a match (offsides in all 2.4 a match, as before) |
| ...followed by a shot of the side within 10 s | n/a | 0.9 a match (24% of those that arrived) |
| Goals a match | 2.60 | 2.76 (noise is about +-0.1 on 205 matches) |
| Shots a match; on target | 40.4; 39.4% | 41.5; 39.3% |
| Goals from a move of 3 or more passes | 41% | 42% |
| Passes a match (engine count); completion | 1,990; 84.3% | 1,994; 84.3% |
| Corners; fouls; yellow cards a match | 4.3; 21.6; 4.0 | 4.2; 21.7; 4.0 |
| Goals straight off a goalkeeper's kick | 53 of 534 (9.9%) | 46 of 565 (8.1%) |

**Not as planned.** The plan wanted through balls to score on par with a good forward pass by raising `ThroughBonus`; the bonus has almost no pull (a utility is the chance of success times a value, and the chance is what decides), so the lever is the estimate of the chance. 30% of the balls are still
intercepted (a ball behind the line is a gamble). Offsides did not need watching: the receiver is judged at the moment of the kick, and the careless-reader rule already lets the odd one through.
The reception-gap test (`A_received_ball_is_eased_to_the_foot...`) now has a 99th-percentile bound of 3 m and a maximum of 5 m, where it had a maximum of 4 m: two receptions in 600 are a runner taking a through ball at a sprint and pushing it ahead. The shots band of
`Over_many_matches_between_the_board_formations...` goes to 18 to 40 (the committed engine read 35.0 against the old 36, and this milestone adds about 2 shots).

### 14.23 Fewer balls given back in the final third, and long shots by name (the keeper-gift plan, Milestone 7)

**Diagnosis.** A temporary counter (removed again) over the 205 stored matches: of about 2,800 kicks a match, 824 were recycles (a ball played back), and 355 of those were in the final third, against 309 forward passes there. A carrier pressed in the final third gave the ball back more often than he went on with it.
Turnovers were 710 a match. The other levers the plan names did nothing to them: the cost of losing the ball in the final third (900 to 1,400) and the middle third (1,800 to 2,300), a cost on sideways passes (300), and the shield gate (1,200 to 2,000) all left them at 708 to 718. Only the recycle moved play, and it moves
it into shots: every way of making a carrier keep the ball (the recycle bonus cut to 40% and 70%, a floor of 55% and 70% on the chance of a pass) raised shots and goals together, because the chances he creates are close ones (goals 3.98 against 2.76 at 40%).

**Changes.**
- The recycle bonus in the final third is 70% of what it is elsewhere (`FinalThirdRecyclePercent`) and a ball played back there costs 150 (`FinalThirdBackwardCost`). Recycles in the final third fell from 355 to about 275 a match.
- To keep the shots where they were, `ShotMinimumChance` rises from 1,400 to 1,700 and `ShotBaseError` from 110 to 130 (on target had been over its band at 39%).
- That threshold alone ended the shots from beyond 16 m (0.1 a match), so a shot from beyond `LongShotRange` (16 m) is judged against its own, lower threshold, `LongShotMinimumChance` 1,150: a man with room tries his luck. The volume is steep in this number (1,200: 3.7 a match, 1,150: 6.7, 1,100: 9.8, 1,000: 15.6, 850: 23.7)
  and the shots sit at the near edge of the range (16 to 18 m), where the chance is highest; the chance is lower with distance and a worse finisher, so very few come from beyond 20 m.
- `GoalOriginProbe`: the shot-distance bands are < 6, 6-12, 12-16, 16-25 and 25+ m, and the distance is read from the frame before the shot's stamp: the stamp lands a frame after the kick, when the ball has flown 2 to 3 m, so every earlier distance table read shots 2 to 3 m too near (the old "12-18 m" row was mostly shots from 14 to 20 m).

| Stored snapshots (205) | Before (M6) | Now |
|---|---|---|
| Recycles in the final third a match | 355 | about 275 |
| Turnovers a match | 710 | 687 |
| Goals a match | 2.76 | 2.91 |
| Shots; on target | 41.5; 39.3% | 46.9; 35.9% |
| Open-play shots from 16 to 25 m a match (probe; counted at the kick) | n/a (0.1 at the old threshold) | 5.4; 6.7 |
| Goals from a move of 3 or more passes | 41.8% | 37.1% |
| Passes a match (engine count); completion | 1,994; 84.3% | 2,002; 84.0% |
| Corners; fouls; yellow cards a match | 4.2; 21.7; 4.0 | 4.6; 22.8; 4.1 |

**Not as planned.** The plan wanted turnovers to fall sharply and moves of five or more passes to convert to shots more than today. Neither came: turnovers fell 3%, and the shots per spell of five to seven passes stayed at about 5%. Most of the plan's other targets were already met before this milestone (passes completed 84%, goals from a long move 42%), and the pinball in
the turnovers is the physics of a pass (65% of final-third attacker passes arrive), not a choice the brain makes: the loss weights could not move it. The recycle cut is what shows in play (fewer balls given back in the final third); the price was shots, which the thresholds hold at 47 a match (41.5 before).
Tests: the shots band of `Over_many_matches_between_the_board_formations...` goes to 18 to 44 (it read 41.1), and the standard fixture's seed moves from 20_260_927 to 20_260_928 because the old one now draws a sending-off, which `Every_passage_has_the_ball_and_the_players_on_the_pitch_in_the_viewers_slots` (22 men in every passage) does not allow for.

### 14.24 The tactical-balance yardstick and its baseline (the weaker-side plan, Milestone 0)

**What was added.** `tickbalance [matches] [seed] [quick]` in `tools/simulation-benchmarks` (`TickShowcase.Balance`) plays a fixed table of 44 cells, 300 matches each by default (capped at 500), and prints one row per cell: results, points for both sides, goals, shots, shots on target, open-play shots inside 16 m, goals per shot on target, and **breaks**. A regain is a change of the controlling side in open play (no stoppage since the other side last had the ball, the winner is not the keeper, the ball is in the winner own half); a break is a regain followed by a shot by the same side within 12 s (120 frames). Regains are about 240 a side a match because a loose ball that changes hands counts, so the breaks figure is the one to read. Every cell fixes both keepers (`gk=`), so the keeper cliff never enters. `quick` plays S1 and the S4 cells with COUNTER. The engine is untouched: the golden hash does not move.

Setups: DEF (default 4-4-2), POS (`ment=3,press=2,line=2,tempo=2`), ATT (`ment=4,...`), COUNTER (`form=12,ment=1,press=0,line=0,pass=2,counter=1`), BUS (`form=12,ment=0,press=0,line=0,counter=1,waste=1`). Scenarios, the stronger side at home: S1 17-20 v 15-18, S2 17-20 v 13-15, S3 17-20 v 6, S4 16 v 16 in the full 5 x 5 matrix.

**Baseline (seed 90000, 300 matches a cell; standard error on points about 0.06).** Points of the weaker (away) side, by its setup:

| Scenario, home setup | DEF | POS | COUNTER | BUS |
|---|---|---|---|---|
| S1 17-20 v 15-18, home DEF | 0.45 | 1.06 | 0.45 | 0.17 |
| S1, home POS | 0.07 | 0.15 | 0.30 | 0.17 |
| S2 17-20 v 13-15, home DEF | 0.12 | 0.26 | 0.18 | 0.18 |
| S2, home POS | 0.00 | 0.05 | 0.11 | 0.06 |
| S3 17-20 v 6, home DEF (wins + draws) | 0% | n/a | 0% | 0% |

S4 (16 v 16), points of the away side against home DEF: DEF 0.91, POS 1.86, ATT 2.18, COUNTER 0.90, BUS 0.60. Away COUNTER earns less against a committed home side than against DEF: 0.63 against POS and 0.64 against ATT (home POS and ATT take 2.22 points each), so the counter does not yet punish committing. With the home side on COUNTER the picture is even: it takes 1.46 points against away POS (1.36) and 1.31 against ATT (1.51).

**What the numbers say.**
- COUNTER is a DEF side with fewer shots: S1 away COUNTER takes 13.3 shots against 15.1 and gets 2.89 breaks a match against 2.10; the opponent still takes 31.6 shots (36.2 against DEF). BUS holds the opponent to 34 shots and 3.12 goals (3.60 against DEF) and has 6.9 shots of its own, 0.70 goals and 0.17 points.
- Committing forward is free: S1 home POS against away DEF takes 63 shots to 16 and 2.90 points (DEF 2.43); committing costs almost nothing in goals against: the away DEF side scores 1.40 a match against home DEF and 1.34 against home POS.
- The weaker side best option in S1 is all-out attack (1.06 points), as the plan found. In S2 and S3 nothing helps.
- S3 holds at baseline: the 6-skill side wins no match in any setup, and the guard for later milestones is that wins plus draws stay at or under 2%.
- The S4 target "away COUNTER against DEF at least 0.85" is already met at baseline (0.90): that target is a floor to keep, not a gain to chase.

The full table is in the weaker-side plan progress memory. It is the baseline that M5 compares against.

### 14.25 A counter-attack in the tick engine (the weaker-side plan, Milestone 1)

**What changed.** `CounterAttack` is read by the tick engine for the first time. A side that has it is **on a break** for 7 s (`BreakTicks`) after it cuts out a pass in flight or wins a tackle in its own 60% of the pitch (`OpenBreak`, `TickMatchLoop.OpenPlay.cs`). The break ends at the first shot, ball played back (`Recycle`) or hoof (`Clear`), at a dead ball, or when the other side takes the ball (`TurnOver`). A loose ball picked up does not open a break: this engine changes hands about 250 times a side a match, and opening on every one put the side "on a break" for about 13 minutes of every match with no gain.

While a side is breaking:
- **Off the ball** (`TickOffBallSupport`): the run budget is at least 2 whatever the mentality (`BreakRunBudget`), wide midfielders count as runners, and runs start at once: the "nobody within 12 m" and "carrier past his own third" gates are skipped.
- **On the ball** (`TickBallCarrierBrain`, `HoldTicks`): progress is worth 80 points more (`BreakProgressPercent`), a ball into a runner's path earns the direct side's long-ball value (`BreakRunnerBonus`, 900) whatever the passing style, the mind is 4 sharper (`BreakSharpness`: a decision every tick, not every other) and the dwell before a pass is 30% of its length (`BreakDwellPercent`). A through ball still needs the carrier's Vision, so a poor carrier does not become a good one.

A side without `CounterAttack` is bit-for-bit what it was: every DEF, POS and ATT cell of the table is identical to the baseline, and the golden hash does not move.

**Tried and dropped: the out-ball.** The plan had the side's most advanced forward stay up the pitch without the ball (no out-of-possession depth shift, no share of the ball shift). Measured at 400 matches on three cells (S1 COUNTER, S4 away COUNTER against DEF, S1 BUS) it lowered the weaker side's points in every form: full, phase shift only, ball shift only. Points of the weaker side in S1 / S4: no out-ball 0.56 / 1.14, full out-ball 0.47 / 0.93, phase only 0.40 / 1.02, ball shift only 0.42 / 1.02 (standard error about 0.05). A lone striker 10 m higher does not receive more balls, and the side scores less. It was removed, not left switched off. The break progress bonus is the lever that matters: at 30 the side's shots stay at 13 a match, at 80 they rise to 15-16, at 150 to 19 (but the opponent's goals do not fall, which is Milestone 2's job). The second runner of the budget never starts in a 5-4-1 (one forward, the wide midfielders sit behind the ball): the budget is not a lever here.

**Result (seed 90000, 300 matches a cell; standard error on points about 0.06).** Points of the weaker (away) side, baseline (M0) to M1:

| Scenario, home setup | COUNTER | BUS |
|---|---|---|
| S1 17-20 v 15-18, home DEF | 0.45 to 0.59 | 0.17 to 0.30 |
| S1, home POS | 0.30 to 0.23 | 0.17 to 0.21 |
| S2 17-20 v 13-15, home DEF | 0.18 to 0.28 | 0.18 to 0.19 |
| S2, home POS | 0.11 to 0.10 | 0.06 to 0.05 |
| S4 16 v 16, home DEF | 0.90 to 0.99 | 0.60 to 0.77 |
| S4, home POS | 0.63 to 0.88 | 0.39 to 0.53 |
| S4, home ATT | 0.64 to 0.70 | 0.36 to 0.51 |

S3 (17-20 v 6): wins plus draws stay 0% in every setup (guard 2%). Breaks of the COUNTER side a match: S1 against DEF 2.89 to 6.37, against POS 2.67 to 6.72; S4 against DEF 2.96 to 7.14. The side's shots rise 13.3 to 16.1 in S1, and the stronger side's shots stay at 34-36, so the gain is small: **the targets of the plan (S1 COUNTER at least 0.95) are not reached by M1 alone.** The weaker side concedes 3.0 goals a match in S1 whatever it does: the low block of Milestone 2 is the other half.

**Film check.** `tickshow 17-20 15-18 <seed> "gk=17" "form=12,ment=1,press=0,line=0,pass=2,counter=1,gk=15" --dump file` (`tickshow` now takes a home and an away setup after the seed).

### 14.26 The counter depends on the opponent (the weaker-side plan, Milestone 1b)

**What changed.** The break of §14.25 is no longer the same against everyone. Its size is worked out at the regain (`OpenBreak`, `TickBreakGain.Plan`) from two things: how the opponent plays, and how quick the two sides are.

- **Posture** (`TickTeamStyle.Posture`): mentality Defensive -2 to Attacking +2, defensive line Deep -1 to High +1, pressing LowBlock -1 to HighPress +1, clamped to -4..+4. DEF is 0, POS +3, ATT +4, BUS and a deep 5-4-1 -4. The formation does not count.
- **Gain** (percent) = `GainAtBalancedPercent` (100) + `GainPerPosturePercent` (50) per point of posture, never below 0, times the **speed factor**. It sets the break's length (70 ticks per 100%), its runners (one per 50%, at most 3), whether they start at once (from 75%) and how much more the carrier values a ball forward (120 points per 100%, counted up to 100% so that it cannot run away: scaling it to 250% gave the side +0.75 points against POS; and times the speed factor twice over, so a break a quicker side can catch loses its hurry faster than its length).
- **Speed** (`GroupSpeed`, `SpeedFactorPercent`): the breakers are the side's Attack and Midfield families, the recoverers the opponent's Defence and Midfield families, over the men on the pitch at the regain. A man's speed is `(Pace + Acceleration) / 2` read back from the physics profile (raw attributes: the `Skills` are pulled towards the average by the skill curve, which shrank a 2-point gap to under 1) and scaled by his energy as the physics scales his top speed. The factor is 100% at no edge, falls 15 points for each point the recoverers are quicker (floor 30%) and rises 15 for each point the breakers are quicker (ceiling 115%). It is relative: 13 against 15 counts as 18 against 20.
- **The cost, paid against everyone** (`TickCarrierSituation.Waiting`, `WaitProgressPercent` 28): a side that counter-attacks and is not on a break does not force the play, so its carrier values going forward 28 points less. Against a side that attacks, the breaks make that up; against a side that sits deep, nothing does. This replaces the out-ball of the plan (dropped in §14.25) and the "hurry" of a minimum-length window: with a 3 s minimum window the flag was a *gain* of +0.26 points against DEEP, because the quick release and the forward bias only help against a side that does not attack. A deep opponent now gives no window at all (gain 0).

A side without `CounterAttack` is bit-for-bit what it was; the golden hash does not move.

**Result (500 matches a cell, seeds 90000 and 95000; the difference of two cells has a standard error of about 0.09, so about 0.065 for the mean of two seeds).** The flag's effect is the away side's points on COUNTER minus COUNTER0 (the same setup, `counter=0`). "Before" is §14.25; the two columns after it are the final constants (speed step 15, waiting cost 28, progress 120 capped at 100% and following the speed factor twice, ceiling 115%):

| Cell | Before | Seed 90000 | Seed 95000 | Target |
|---|---|---|---|---|
| S5 16 v 16, home DEF | +0.08 | -0.04 | -0.05 | within 0.07 |
| S5, home POS | +0.18 | +0.26 | +0.21 | at least +0.20 |
| S5, home ATT | +0.17 | +0.28 | +0.27 | at least +0.20 |
| S5, home DEEP | +0.26 | -0.37 | -0.43 | -0.10 or less |
| S6a (opponent quicker by 4), home DEF | +0.03 | -0.10 | -0.14 | -0.05 to -0.15 |
| S6a, home POS | 0.00 | -0.02 | -0.06 | -0.05 to -0.15 |
| S6b (breakers quicker by 3), home DEF | +0.14 | +0.25 | +0.16 | (larger than at no edge) |
| S6b, home POS | +0.07 | +0.32 | +0.43 | S5 POS +0.05 |
| S6c (14 v 14, opponent quicker by 2), home DEF | +0.18 | -0.13 | -0.31 | a small minus |
| S6c, home POS | +0.05 | -0.05 | -0.03 | a small minus |
| S6n (17-20 v 15-18, no pace override), home DEF | n/a | -0.09 | -0.17 | (no target: the weaker side is about 2 points slower) |
| S6n, home POS | n/a | -0.01 | +0.02 | (no target) |

**How the step was settled.** The plan asks for a step of about 15% a point, and at 15% the gain against an attacking side barely moves with speed: the runners (at most 3) and the progress cap are already saturated, so a 15% loss left the flag at about zero against POS in S6a and S6c, whatever the cost. What turned it into a small minus was a larger cost and a larger gain together (waiting 14 to 28, progress 80 to 120) with the progress following the speed factor twice over and capped at the balanced gain. Seven variants were swept in two rounds (seed 90000, COUNTER cells only, the COUNTER0 cells reused as they cannot change), and the last one was confirmed on a second seed. **S6b** stays inside the caps with a lower ceiling (115%): the better side wins 63-68% and the weaker one 20-22%, which the plan allows up to about 25%. S6c against DEF is a larger minus on the second seed (-0.31) than the target's range: the two-seed mean is -0.22.

Not reached: **DEEP at least as many points as COUNTER in S6a against DEF** (0.20 against 0.32). That target compares two setups, not the flag: COUNTER0 is Cautious and direct, DEEP is Defensive, and COUNTER0 alone has 0.42 against DEEP's 0.20. It belongs to Milestone 2 (the Defensive outlet).

**Other guards (final constants).** S4 away COUNTER against home DEF is the S5 cell: 0.90 and 0.89 points on the two seeds (target 0.85). S3 (17-20 v 6, 300 matches): wins plus draws 0% for COUNTER and 1% for BUS (guard 2%). **S1 17-20 v 15-18, 300 matches:** the weaker side on COUNTER against DEF 0.34 points (0.25 with the old constants, 0.45 at baseline), against POS 0.26; BUS against DEF 0.15, against POS 0.18. The weaker side is about 2 points slower, so its speed factor is about 70% and the break is a little under a full break. Decided 2026-10-12: a counter works for a weaker side only if its forwards and midfielders are fast, because the real question is whether the opponent can catch up, reorganise and defend; the flag has no S1 target (its effect against POS in S1 was -0.01 and +0.02 on two seeds, against DEF -0.09 and -0.17). Lifting the weaker side in S1 is the work of the low block and a Defensive outlet (Milestone 2) and the size of the bonus (2b). `tickbalance` S1 and S2 now also play COUNTER0 and DEEP.

**Global checks.** The full MatchEngine suite passes (1,585 tests, 3 min 54 s); the golden hash did not move. `tickmotion 200 1` runs and its film windows are healthy (goals and shots cut off 0.3% and 0.2%). `tick 500` gives 3.82 goals and 40 shots a match, outside the plan's 2.6-2.9 and 22-28 bands: that fixture has no counter flag, so nothing in this milestone changes it; it is the shot volume left by the keeper-gift work (the stored snapshots gave 2.9 goals and 47 shots). `ticksnap 205` was not run: it needs the database export and repeats what the golden hash already shows for a side without the flag.

**Film check.** `tickshow 16 16 90003 "gk=16,ment=3,press=2,line=2,tempo=2" "gk=16,form=12,ment=1,press=0,line=0,pass=2,counter=1" --dump file` (against POS) and `... "gk=16,form=12,ment=0,press=0,line=0" ...` (against a deep side).

### 14.27 A low block that packs the box, and an outlet for a Defensive side (the weaker-side plan, Milestone 2)

**What changed.** Two things, both for a side that defends deep. Neither touches a side that does not: the golden hash does not move.

- **The low block** (`TickTeamStyle.IsLowBlock`, `TickTeamStyle.LowBlock`): LowBlock pressing, or a Deep line under a Defensive or Cautious mentality. While the ball is in the last 45% of the pitch the side defends (`LowBlockBallLimit`), it packs its box:
  - **Shape** (`TickTacticalGeometry.Resolve`): out-of-possession compactness +6 points (`LowBlockCompactnessGain`, so a low block follows the ball 30% and a mid block with a deep line 36%), and a midfielder takes 85% of a deep line instead of 60% (`LowBlockMidfieldLineGain`). No outfield player of a low block stands closer than 8 m to his goal line (the old 8 m floor applied only to the back line).
  - **Marking** (`TickDefensiveAI`): after the back line has picked, up to 2 midfielders (`LowBlockMidfieldMarkers`) mark runners in the defensive third that nobody is left for.
  - **Screens**: 2 more lanes are screened (`LowBlockExtraScreens`, 3 to 5).
- **The Defensive outlet** (`TickBreakGain.WithOutlet`, `OpenBreak`): a Defensive side that wins the ball in its own 60% opens a 5 s window with 1 runner, starting at once, and its carrier values going forward 15 points more (`OutletTicks`, `OutletRunners`, `OutletProgressPercent`). It needs no counter flag. With the flag the window is the larger of the counter's and the outlet, so a counter against a side as deep as BUS (gain 0) keeps the outlet and the flag's cost stays as it was.

**Result (S1 17-20 v 15-18, home DEF, 500 matches a cell, seed 90000; weaker side's points, the standard error of a cell about 0.06).**

| Weaker side | Before (M1b, 250) | Now | W/D/L of the stronger side |
|---|---|---|---|
| COUNTER | 0.34 | 0.22 | 86/10/4 |
| COUNTER0 | n/a | 0.41 | 76/16/8 |
| DEEP | 0.18 | 0.25 | 86/8/6 |
| BUS | 0.14 | 0.23 | 87/8/5 |

The levers were swept at 250 matches a cell (`TickDiag`, removed) at the plan's sizes and at 2 to 3 times them (compactness +20 and +40, midfield line +40 and +100, 3 and 4 midfield markers, 3 and 4 extra screens, a press radius of 18 m and 25 m inside the box). **None of them cuts the stronger side's shots or goals.** Its shots stay at 34-37 a match, those inside 16 m at 24-26, and its goals at 3.0-3.6, whatever the size; the 40% compactness only lowered the weaker side's own goals (0.92 to 0.59), because its forwards drop with the block. The plan's expected 25-35% fewer shots inside 16 m is not reached, and the weaker side's gain from the low block is within the noise. The levers are shipped at the plan's sizes because the single scale of M2b needs something to scale; **whether they are worth keeping is the open question for the user.**

The outlet did more. It was swept at 0, 15, 30 and 45 points of progress and at 5 s and 10 s (250 matches a cell): DEEP against DEF 0.18 without, 0.26-0.29 with (the size hardly matters, so a mild 15), BUS 0.14 without and about 0.36 with. DEEP is now within 0.1 of COUNTER against DEF (0.25 against 0.22) and, in S6a, level with it (0.25 against 0.27 against DEF, 0.08 against 0.14 against POS: not reached against POS).

**Guards (500 matches a cell).**

| Check | Result | Target |
|---|---|---|
| S5 flag effect, home DEF | 0.00 | within 0.07 |
| S5, home POS / ATT | +0.18 / +0.29 | at least +0.20 |
| S5, home DEEP | -0.37 | -0.10 or less |
| S6a flag effect, home DEF / POS | -0.10 / -0.08 | -0.05 to -0.15 |
| S3 (17-20 v 6) wins plus draws, COUNTER / BUS | 1% / 1% | 2% or less |
| S1 stronger side's wins against COUNTER / DEEP / BUS | 86% / 86% / 87% | at least 60% |

Not reached: **the weaker side's S1 points against the all-out attack of the stronger side's DEF are 0.22-0.25, not the 0.6-0.8 the plan hopes for.** The weaker side's own all-out attack (POS) is still its best option at 1.00 points (57/15/28), which is M3's first aim. `ticksnap 205` was not run (it needs a database export; the golden hash does not move).

**Tests.** `IsLowBlock` on the setups, the exact anchor of a midfielder inside and outside the box, no outfield player of a low block within 8 m of his goal line, midfield markers and extra screens only when the low block packs the box, and the outlet against a bigger and a smaller counter.

### 14.28 The size of the bonus (the weaker-side plan, Milestone 2b)

**What changed.** Nothing new is built: the user chose how big the mechanisms of Milestones 1b and 2 are, **125%** of the sizes they were tuned at, and the constants now carry that size. The temporary scale (`TickDiag`, an environment variable `TD_BONUS`) that the sweep ran on is removed. The golden hash does not move: no fixture has the counter flag, a low block or a Defensive mentality.

| Constant | Before | Now |
|---|---|---|
| `TickBreakGain.GainAtBalancedPercent` | 100 | 125 |
| `TickBreakGain.GainPerPosturePercent` | 50 | 63 (62.5 rounded) |
| `TickBreakGain.MaximumProgressGainPercent` | 100 | 125 |
| `TickBallCarrierBrain.WaitProgressPercent` (the counter's cost) | 28 | 35 |
| `TickTacticalGeometry.LowBlockCompactnessGain` | 6 | 7 |
| `TickTacticalGeometry.LowBlockMidfieldLineGain` | 25 | 31 |
| `TickDefensiveAI.LowBlockMidfieldMarkers` | 2 | 3 |
| `TickDefensiveAI.LowBlockExtraScreens` | 2 | 3 |
| `TickBreakGain.OutletTicks` | 50 (5 s) | 62 (6.2 s) |
| `TickBreakGain.OutletProgressPercent` | 15 | 18 |

The speed factor (step 15, floor 30%, ceiling 115%) was not scaled. The outlet's single runner stays at 1.

**The sweep.** Six sizes (0%, 50%, 75%, 100%, 125%, 150%), 500 matches a cell, seed 90000, one scale on the break's gain and cost, the low-block levers and the outlet together (cells a size cannot change were played once). A cell's standard error is about 0.045 points, a flag effect (two cells) about 0.09.

Weaker side's points, S1 (17-20 at home v 15-18, the stronger side plays its default; the weaker side's own default scores 0.43):

| Size | Sits deep | Sits deep and counters | Parks the bus |
|---|---|---|---|
| 0% | 0.20 | 0.46 (flag does nothing) | 0.20 |
| 50% | 0.24 | 0.28 | 0.21 |
| 75% | 0.23 | 0.28 | 0.19 |
| 100% | 0.25 | 0.22 | 0.23 |
| **125%** | 0.23 | 0.31 | 0.15 |
| 150% | 0.19 | 0.33 | 0.30 |

The size does not lift a weaker side that defends deep: it stays under its own default (0.43) at every size, and S2 (17-20 v 13-15) is flat too (0.08-0.20 against 0.12). **The plan's 0.6-0.8 points is not reached at any size**; the low block and the outlet barely move the result, and the size mostly scales the counter flag. The stronger side wins 83-91% in every S1 cell and the weaker side at most 7% (cap 15%).

The counter flag's effect (COUNTER minus COUNTER0, points):

| Situation | 75% | 100% | **125%** | 150% |
|---|---|---|---|---|
| 16 v 16, opponent plays its default (neutral) | -0.09 | 0.00 | +0.04 | +0.20 |
| 16 v 16, opponent attacks all-out (at least +0.20) | +0.10 | +0.18 | +0.38 | +0.42 |
| 17-20 v 15-18 (no target), default / all-out | -0.18 / +0.03 | -0.19 / +0.04 | -0.14 / +0.07 | -0.07 / +0.08 |
| Opponent 4 points quicker, default / all-out (small minus) | -0.16 / -0.09 | -0.10 / -0.08 | -0.24 / -0.05 | -0.14 / 0.00 |
| Breakers 3 points quicker, default / all-out | +0.03 / +0.28 | +0.16 / +0.29 | +0.26 / +0.50 | +0.62 / +0.64 |

With the breakers quicker (S6b, home default) the stronger side wins 71 / 66 / 61 / **50**% and the weaker side 15 / 19 / 22 / **33**% at 75 / 100 / 125 / 150%: the 150% row breaks the caps (60% and about 25%), so 125% is the largest size that stays inside them. S3 (17-20 v 6), wins plus draws of the 6-skill side in 500 matches, COUNTER / BUS: 1 / 1, 1 / 1, 6 / 0 and 8 / 2 at 75 / 100 / 125 / 150%: all within the 2% guard.

**Check of the shipped constants (300 matches a cell, seed 90000, the first 300 matches of the sweep).** Flag effect, 16 v 16, home default / all-out: +0.15 / +0.34 (500 at 125%: +0.04 / +0.38); breakers quicker, home default / all-out: +0.28 / +0.46 (+0.26 / +0.50), the stronger side winning 57% / 65% (61% / 64%) and the weaker side 22% / 20%; S3 COUNTER 7 of 300 (2.3%, against 6 of 500), BUS 0; S1 weaker side on COUNTER / DEEP / BUS 0.28 / 0.21 / 0.18. The differences from the sweep are the smaller sample (a flag effect at 300 matches has an error of about 0.11); the two readings that sit outside a target, the neutral cell (+0.15 against 0.07) and the S3 leak (2.3% against 2%), are inside that error and were not tuned.

**Open.** The S1 target is still unmet. The next steps are Milestone 3 (what committing forward costs, whose first aim is to bring the weaker side's own all-out attack inside the cap) and then a look at whether the low block can be made to cut the stronger side's shots at all. Later sweeps play 300 matches a cell and the sizes 75%, 100% and 125% only.

### 14.29 What committing forward costs: tired legs (the weaker-side plan, Milestone 3)

**Why.** Against a default stronger side (17-20 at home to 15-18) the weaker side's own all-out attack (Positive, high press, high line, high tempo; "POS") won 27% of 1,000 matches (stronger side 60%), more than any defensive setup. Committing forward cost almost nothing: the stronger side's shots rose from 36 to 39.5 while the weaker side's doubled (15 to 31). The tick engine did tire players, but a side that pressed and ran ended 85 minutes with the same condition as one that did not (7,090 against 7,106), and a spent player lost only 30% of his pace and none of his acceleration.

**What changed** (`TickPlayerPhysics`; the golden hash moves):

| Constant | Before | Now |
|---|---|---|
| Slowest a spent player runs, share of fresh top speed (`TiredSpeedFloorBasisPoints`) | 70% | 60% |
| Acceleration | not tired | scaled by the same factor as speed (60% + 40% x energy) |
| `HighPressDrainPercent` (extra energy drain of a side that presses high) | - | +130% |
| `HighTempoDrainPercent` | - | +100% |
| `MentalityDrainPercent` (a step above Balanced: Positive one, Attacking two) | - | +35% |

`DrainPercent(instructions)` is a pure function of the side's instructions; the loop passes it to `Step`, which multiplies the energy a tick costs. A side with none of the three drains as before (a default side's condition at 85 minutes is unchanged at about 7,250-7,400), so the cost lands only on the side that commits. POS ends 85 minutes at about 3,700 condition (default 7,300), Attacking lower.

**Rest defence needed no change.** The plan's second lever, one full-back overlapping at a time, already holds: `AssignOverlap` sends one defender per tick, and only Attack-family players and wide midfielders make runs behind the line (`AssignRuns`), so the back line never goes forward in numbers. The space a counter attacks comes from the high line and the shape, as before.

**Method.** Variants swept with a temporary `TickDiag` environment-variable class (removed), on the cap cell (S1, stronger side default at home, weaker side POS), 500 matches on two seeds (90000 / 95000). Weaker side's wins:

| Variant (drain press / tempo / step, floor) | Stronger / draw / weaker wins % | Notes |
|---|---|---|
| Baseline | 57/15/28, 62/13/25 | the stronger side's cap is 60% |
| Drain 50/40/10, floor 70% | 64/15/21, 64/14/22 | condition 5,500 |
| Same, plus tired acceleration | 61/11/27, 64/12/24 | acceleration alone does nothing |
| Drain 80/60/20, acceleration, floor 55% | 67/14/20, 67/12/21 | S3 COUNTER draws 6% to 12%, stronger side's shots 24 to 18 |
| Drain 80/60/20, acceleration, floor 45% | 69/12/19, 68/14/18 | S3 COUNTER draws 17%, stronger side's shots 15 |
| **Drain 130/100/35, acceleration, floor 60% (shipped)** | **72/13/15, 70/11/19** | S3 COUNTER 90/9/1 (baseline 93/6/1) |

The floor is the lever that matters, and it is also a global one: a lower floor makes every side slower late, which thins out the stronger side's play against a weak defensive side (S3), so the shipped floor is the highest that lets the drain do the work. The drain alone, at any size tried, gave only 5 points of wins.

**Result** (300 matches a cell, seed 90000, and the 500-match cap cell above):

- Cap cell: the weaker side's all-out attack wins about 17% (was about 27%); the stronger side wins about 71% (was about 59%); the stronger side's shots 39.5 to 42.8, the weaker side's 31 to 27. **The cap of 15% for the weaker side is not quite met** (15% and 19% on the two seeds); the stronger-side cap of 60% is.
- The weaker side's defensive setups are where they were (S1 home default: COUNTER 83/9/7, DEEP 87/8/5, BUS 89/9/2; baseline 82/13/5, 87/9/4, 89/8/3).
- The counter flag, 16 v 16 (COUNTER minus COUNTER0): against the default **+0.04** (target within 0.07; baseline +0.15), against POS +0.34 and ATT +0.53 (at least +0.20), against DEEP -0.51 (at most -0.10).
- S3 (17-20 v 6): the 6-skill side wins 0-1% in every setup; its draws are 9% on COUNTER (baseline 6%) and 1% on BUS.
- `tick 500`: goals 3.85 (3.82 before; above the 2.6-2.9 band before this milestone), shots 39.3, on target 34.2%, pass completion 84.1%. `tickmotion 200 1` film health unchanged. `ticksnap 205` was not run (needs the DB export); nothing in the stored squads sets a different instruction mix than the one `tick 500` plays.
- Test changes: the board-formation test's upper goal band 3.8 to 4.3 (the true mean is 3.85; 72 matches scatter by 0.3); the replay test allows a slot to be missing for a player sent off (the fixed film match now has a red card); the spent-player pace floor in the physics test 60% to 40%.

### 14.30 Time-wasting in the tick engine (the weaker-side plan, Milestone 4)

**Why.** `TimeWasting` (Off, Situational, On) was read only by the possession engine's ratings and the film's counter logic, so a tick match ignored it. A side that was ahead could not run down the clock.

**What changed** (`TickMatchStateMachine`, `TickMatchLoop`; no existing fixture sets the instruction, so the golden hash is unchanged and every cell without time-wasting is bit-identical to Milestone 3):

| Where | Extra hold for a side that wastes time |
|---|---|
| Its own goal kick (`WastedGoalKickTicks`) | +5 s |
| Its own throw-in (`WastedThrowInTicks`) | +4 s |
| Its own free kick (`WastedFreeKickTicks`) | +3 s |
| The goalkeeper holding a ball he has caught or picked up (`KeeperWastedTicks`) | +4 s (6.5 s in all) |
| Corner, penalty, kick-off | none |

`SideRuntime.WastingTime` decides whether the side wastes time: always for On, and for Situational only while the side leads (the lead is read at the end of each minute, as the ratings do). The clock runs through the longer hold, so the wasted seconds are taken from open play. The hold is still cut off at `MaximumSetupTicks` (10 s). The cost named in the plan, that the side attacks less, comes from the same seconds: there is less play for both sides. Open-play moves, the break window and the counter flag are not touched.

**Result** (300 matches a cell, seed 90000; `tickbalance` got an open-play-seconds column `live` and a `led70` column, the matches in which a side led after 70 minutes and the number it then won; scenarios S7, S7h and S7e):

| Cell | W/D/L % | Points | Open play | Led at 70' (won) |
|---|---|---|---|---|
| 16 v 16, away DEF (default) | 58/17/25 | 1.92-0.92 | 5,718 s | 82 (61) |
| 16 v 16, away On | 50/22/28 | 1.72-1.06 | 5,580 s | 77 (65) |
| 17-20 v 15-18, home DEF | 76/13/11 | 2.41-0.47 | 5,714 s | 203 (196) |
| same, home Situational | 74/14/12 | 2.37-0.50 | 5,642 s | 206 (196) |
| same, home On | 78/14/8 | 2.49-0.37 | 5,590 s | 223 (209) |

- **Effective playing time falls by about 2.2% when it is On** (5,718 to 5,580 s of open play in a match, the clock's 5,400 s plus stoppages), about 1.3% when it is Situational for the stronger side that leads most of the match. Both sides' shots fall by about 5%.
- **Between equal sides** the side that wastes time gains about 0.14 points and 5 points of draws, and holds more of its leads (65 of 77 against 61 of 82). Read these as the size of the effect, not as precise figures: a flag effect at 300 matches has an error of about 0.1 points.
- **Caps (BUS, the only setup with `waste=1`, Situational):** S1 home DEF v BUS 87/9/4 (BUS with the flag off 89/9/2), home POS v BUS 89/8/3; stronger side wins 87-92% in all three BUS variants, weaker side at most 4%. S3 BUS 99/1/0 (limit 2% for wins and draws together). A BUS that wastes time while trailing (On) concedes more (3.44 to 3.96 goals, probably through the longer goal kicks and keeper holds that let the press set up), which is what the instruction should cost.
- **The counter flag is unchanged:** S5 home DEF v COUNTER 57/20/23 (1.92-0.88), identical to Milestone 3.
- Test changes: `TickMatchStateMachineTests` (the longer hold per restart kind, no hold above the ceiling), `TickMatchLoopTests` (a side on On leaves less open play, Off equals the default).

### 14.31 Calibration, guard and wrap-up: the bonus is set at 100% (the weaker-side plan, Milestone 5)

**Why the size moved.** Milestone 2b shipped the break, low-block and outlet mechanisms at 125% of the sizes they were tuned at. Milestones 3 (tired legs) and 4 (time-wasting) then changed the play they sit in, so the plan asked for the table to be re-run at 125%, 100% and 75% (300 matches a cell, seed 90000; a temporary `TickDiag` scale of the shipped constants by value / 125, removed again). At 125% two caps no longer held, at 100% all of them do, and **the user chose 100%** (2026-10-13). The constants are back at the Milestone 2 / 1b sizes:

| Constant | 125% | Now (100%) |
|---|---|---|
| `TickBreakGain.GainAtBalancedPercent` | 125 | 100 |
| `TickBreakGain.GainPerPosturePercent` | 63 | 50 |
| `TickBreakGain.MaximumProgressGainPercent` | 125 | 100 |
| `TickBallCarrierBrain.WaitProgressPercent` | 35 | 28 |
| `TickTacticalGeometry.LowBlockCompactnessGain` | 7 | 6 |
| `TickTacticalGeometry.LowBlockMidfieldLineGain` | 31 | 25 |
| `TickDefensiveAI.LowBlockMidfieldMarkers` / `LowBlockExtraScreens` | 3 / 3 | 2 / 2 |
| `TickBreakGain.OutletTicks` | 62 | 50 |
| `TickBreakGain.OutletProgressPercent` | 18 | 14 (the Milestone 2 value was 15; 14 is what the 100% cells ran) |

The golden hash does not move (the fixture sets no counter flag, low block or Defensive mentality).

**What broke at 125%, and what each size does** (300 matches a cell, seed 90000; the stronger side's W/D/L, the weaker side's points):

| Check | Target | 75% | 100% | 125% |
|---|---|---|---|---|
| S3 (17-20 v 6), weaker side's wins + draws on COUNTER | at most 2% | 1% | **2%** | **10%** (the stronger side shoots 19 times, not 38) |
| S6b (breakers 3 points quicker), home default v COUNTER | stronger side at least 60%, weaker at most about 25% | 68 / 14 / 18 | 66 / 14 / 21 | 59 / 16 / 25 |
| S6b, home all-out v COUNTER | the same | 68 / 15 / 16 | 62 / 17 / 21 | **54 / 18 / 28** |
| Counter flag against a balanced side (S5 DEF) | within 0.07 | **-0.18** | +0.06 | +0.04 |
| Counter flag against all-out attack (S5 POS / ATT) | at least +0.20 | +0.20 / +0.29 | +0.26 / +0.45 | +0.34 / +0.53 |
| Counter flag against a deep side (S5 DEEP) | at most -0.10 | -0.31 | -0.51 | -0.51 |
| S6a (opponent 4 quicker), flag vs default / all-out | -0.05 to -0.15 | -0.07 / -0.13 | -0.14 / -0.07 | -0.14 / -0.06 |
| S6b, flag vs all-out | at least S5 + 0.05 | +0.15 (**fails**) | +0.37 | +0.59 |
| S6c (14 v 14, 2 points quicker), flag vs default / all-out | a small minus | -0.35 / -0.12 | -0.30 / +0.08 | -0.21 / -0.07 |

- **S3 at 125%.** What is measured: a 6-skill side on COUNTER draws 9% at 125% and about 1% at 100%; the stronger side then makes 19 shots a match (38 against BUS, 62 against the default) and the two sides change the ball only 44 and 86 times a match (142 and 191 against the default); the same 6-skill side with `counter=0` loses 99 / 1 / 0 with 45 shots. So the flag itself, not the low block or the formation, is what slows the match. The likely cause (not isolated) is the waiting cost: at 35 points a Cautious carrier values going forward at 70 where it would at 105, and it keeps the ball in its own half where the stronger side's default press does not take it. At 100% (cost 28, value 77) it is 99 / 1 / 1. Nothing gates the waiting cost by skill, so the cap is met by the size, not by design.
- **75%** is not neutral against a balanced side (-0.18) and fails the fast-breaker comparison, so 100% is the largest size that keeps every cap.
- **S6c** (the flag against the default at low level, a 2-point edge) is more negative than the -0.05 to -0.15 target at every size (-0.30 at 100%), and against all-out attack the sign is noise (-0.12, +0.08, -0.07). The error of a flag effect at 300 matches is about 0.11.

**The table, baseline (Milestone 0) against final** (300 matches a cell, seed 90000, weaker side's points; the stronger side is at home; new columns in the final row only):

| Cell | Weaker side DEF | POS | COUNTER | DEEP | BUS |
|---|---|---|---|---|---|
| S1, home DEF: baseline | 0.45 | 1.06 | 0.45 | n/a | 0.17 |
| S1, home DEF: final | 0.47 | 0.59 | 0.31 | 0.21 | 0.22 |
| S1, home POS: baseline | 0.07 | 0.15 | 0.30 | n/a | 0.17 |
| S1, home POS: final | 0.15 | 0.23 | 0.30 | 0.20 | 0.12 |
| S2, home DEF: baseline | 0.12 | 0.26 | 0.18 | n/a | 0.18 |
| S2, home DEF: final | 0.09 | 0.23 | 0.16 | 0.12 | 0.10 |
| S2, home POS: baseline | 0.00 | 0.05 | 0.11 | n/a | 0.06 |
| S2, home POS: final | 0.05 | 0.02 | 0.13 | 0.08 | 0.08 |

The stronger side's results in the final S1 cells (W / D / L %): home DEF v POS 71 / 13 / 15 (baseline 55 / 14 / 31), v COUNTER 83 / 11 / 7, v DEEP 87 / 10 / 4, v BUS 88 / 6 / 5; home POS v POS 90 / 3 / 7. S3 (17-20 v 6): wins plus draws for the 6-skill side 0% on DEF, 2% on COUNTER, 1% on BUS.

S4 (16 v 16), points of the home side - the away side, by setup (rows home, columns away):

| | DEF | POS | ATT | COUNTER | BUS |
|---|---|---|---|---|---|
| DEF | 1.92-0.92 | 1.26-1.58 | 1.19-1.61 | 1.96-0.84 | 2.29-0.54 |
| POS | 2.28-0.58 | 2.03-0.85 | 1.82-1.08 | 1.80-1.00 | 2.17-0.68 |
| ATT | 2.42-0.48 | 2.03-0.87 | 2.01-0.87 | 1.85-0.98 | 2.20-0.63 |
| COUNTER | 1.38-1.35 | 1.92-0.96 | 1.80-1.00 | 1.45-1.08 | 1.48-1.09 |
| BUS | 0.98-1.77 | 1.29-1.48 | 1.31-1.49 | 1.20-1.33 | 1.39-1.06 |

Baseline away points against a home default: DEF 0.91, POS 1.86, ATT 2.18, COUNTER 0.90, BUS 0.60; final 0.92, 1.58, 1.61, 0.84, 0.54. Mean points over all ten cells a setup plays in: ATT 1.66, POS 1.58, DEF 1.37, COUNTER 1.33, BUS 1.02.

**Targets met, not met.**
- **Met:** the S1 caps (the stronger side at least 60%, the weaker at most 15% in every cell, 71 / 13 / 15 at the limit); S2 stronger side at least 75% and the best defensive setup at 1.5 times the default's points (COUNTER 0.16 against 0.09); S3 at 2%; S5 (+0.06 / +0.26 / +0.45 / -0.51); S6a; S6b with both caps; ticksnap goals 2.80.
- **Not met, and not tuned further:**
  - **S1 weaker side's best defensive setup 0.6-0.8 points.** It is 0.31 (COUNTER), 0.21 (DEEP), 0.22 (BUS), under the weaker side's own default (0.47). The size of the bonus did not move it in Milestone 2b, the low-block levers did not cut the stronger side's shots (Milestone 2), and the tired-legs floor that would is global (Milestone 3). DEEP is 0.10 under COUNTER against the default side (target: within 0.1), and S2 draws are 8% (target at least 10%).
  - **S4: all-out attack still beats the counter.** Away against a default home side POS takes 1.58 points and ATT 1.61, COUNTER 0.84 (target at least 0.85: missed by 0.01), and ATT is the best setup on average. Milestone 3's legs cut the gap (baseline 1.86 and 2.18) but did not close it.
  - **S6c** is noise-limited and more negative than its band against the default.
- **Global checks.** `ticksnap 300` on the 1,501 stored squads: goals **2.80** (band 2.6-2.9; the plan's 2.4-3.0), shots 42.6 (band 22-28; the shot volume the keeper-gift plan left open), on target 36.8%, pass completion 84.2%, yellow cards 3.9. `tick 500` (default instructions, so unchanged from Milestone 3): goals 3.85, shots 39.3, on target 34.2%, pass completion 84.1%. `tickmotion 200 1`: standing share 2.8%, no synchronised stops, 83.7% forward passes, 8.0 switches a match, distance 13.42 km a player (aim 9.5-11.5; 13.8 before the tired legs), film health in line with Milestone 3.

**The statistical guard** (`TickEngineTests`): 300 matches of a 17-skill side at home in the default 4-4-2 against a 16-skill side in a low block that counters (Cautious, Deep line, Direct, `CounterAttack`). It measures 75 / 18 / 7% (stronger side / draws / weaker side), 0.40 points for the weaker side, and asserts the stronger side wins at least 60%, the weaker side at most 20%, at least 8% draws, and the weaker side's points in 0.25-0.80. It catches a bonus that has vanished (no draws, no points) as well as one that has grown into a superpower. The break window's opening, closing and posture rules, the low block's marking and screens, and the 8 m guard on outfield anchors were already covered by `TickCounterAttackTests`, `TickDefensiveAITests` and `TickTacticalGeometryTests`; the low-block anchor test is back to 2,625.

The full MatchEngine suite passes (1,603 tests; the allocation test in `TickSetPiecesTests` failed once under the load of the benchmarks running beside it and passes alone). Logs: `.run-logs/m5-*.txt`.

### 14.32 The counter flag no longer holds the ball (the weaker-side v2 plan, Milestone 1)

**Why.** `WaitProgressPercent` (28) took 28 points off the value of going forward for a side with the counter flag that was not on a break. It made the side keep the ball in its own half (60% of the ball at 13-14 against 18-19, 2 shots) and lose it there. A real counter-attacking side gives the ball up high or plays it forward early; its cost is fewer men in a settled attack.

**What changed** (`TickBallCarrierBrain`, `TickOffBallSupport`, `TickMatchLoop.OpenPlay`):

| Before | Now |
|---|---|
| `TickCarrierSituation.Waiting` and `WaitProgressPercent` lowered `ProgressPercent` by 28 | removed; the carrier of a counter side values going forward as any other carrier of its mentality and tempo does |
| nothing in the shape of the attack | `TickAttackingSituation.Holding` (the flag is set and the side is not on a break): `RunBudget` is `HoldingRunnerCut` (1) lower, never below the break's runners and never below 0, and `AssignOverlap` returns early as it does for Defensive |

The golden hash does not move (the fixture sets no counter flag).

**Result** (150 matches a cell, seed 7000; the weaker side's points, before then after):

| Weaker side setup | 16 v 18-19 | 13-14 v 18-19 |
|---|---|---|
| 4-4-2 counter-attack | 0.12 to 0.16 | 0.09 to 0.11 |
| 5-4-1 counter-attack | 0.32 to 0.38 | 0.15 to 0.16 |
| every other setup (no flag) | bit-identical | bit-identical |

- **Possession** of a counter-attack side at 13-14 falls from 60% to 50% (4-4-2) and from 58% to 48% (5-4-1); shots per 100 s of possession rise from 0.07 to 0.18 and from 0.15 to 0.35. The prototype that only removed the cost (`TD_WAIT=0`) gave 0.27 for the 4-4-2 counter at 16 v 18-19; the rest defence takes about 0.1 of that back, and the cell stays far below the plan target for the 5-4-1 counter (0.7), which the later milestones carry.
- **Guards** (300 matches a cell, seed 90000): a 6-skill side takes no points in any setup (S3: 100/0/0 against default, counter and bus); between equal sides the away counter-attack against a home default takes 0.96 points (0.84 before; target at least 0.85), against a home all-out attack 1.27 (0.98), and the bus setup, which also holds the flag, takes 0.74 and 0.78 away (0.54 and 0.63); no setup beats every other. `ticksnap 150`: goals 2.73 a match (2.51 before; band 2.4-3.0).
- `HoldingRunnerCut = 2` was not tried: S3 did not leak.
- Test changes: `TickOffBallSupportTests` (the run budget with `holding`, a holding side runs one man fewer in play and sends no overlap).

### 14.33 A side that commits forward leaves space behind it: the exposed break (the weaker-side v2 plan, Milestone 2)

**Why.** Attacking had no cost. Winning the ball off a committed side gave the winner nothing extra, because a break opened only for a side with the counter flag or a Defensive mentality (`OpenBreak`). The stronger side scored 3.55 goals against an all-out attack and 3.37 against a default 4-4-2.

**What changed** (`TickMatchLoop.OpenPlay.OpenBreak`, `TickBreakGain`):

| Before | Now |
|---|---|
| a regain opened a break only for the counter flag or a Defensive mentality | a side **without** the flag that wins the ball in its own 60% off a side with posture of at least `ExposedPostureMinimum` (1) opens an **exposed break**: `TickBreakGain.Exposed(Plan(opponent posture, speed factor), ExposedBreakPercent)` |
| nothing | the exposed break is `ExposedBreakPercent` (40) of the counter break against that opponent: ticks, runners and progress scale by it; the runners start at once only at 100 |
| a flagged side's plan | unchanged; a Defensive side takes the larger of each field of the exposed break and its outlet (`WithOutlet`) |

`OpensBreak`'s first parameter is now `opens`. `TickBreakGain.IsExposed(counterAttack, opponentPosture)` and `TickBreakGain.Exposed(full, percent)` are pure and unit-tested. No new random draws. The golden hash does not move (the fixture's sides are balanced).

**Sweep** (150 matches a cell, seed 7000, 16 v 18-19; the weaker side's points, then the stronger side's goals; baseline in the first column):

| Setup | Baseline | 20% | 30% | 40% | 50% | 60% |
|---|---|---|---|---|---|---|
| 4-4-2 all-out attack | 1.24, 3.55 | 1.08, 3.95 | 0.90, 4.21 | 0.51, 4.92 | 0.47, 5.38 | 0.45, 5.39 |
| 4-4-2 Positive | 0.70, 3.95 | 0.63, 4.16 | 0.58, 4.52 | 0.53, 4.65 | 0.25, 5.03 | 0.45, 5.29 |
| 5-4-1 default | 0.91 | | | 0.91 | 0.91 | 0.91 |

The cells with no committed side (4-4-2 default, 5-4-1 default, Cautious, counter-attack, deep Defensive) are bit-identical at every value. Against the 13-14 side the attacking cells fall from 0.27 to 0.05-0.08 and the stronger side scores 6.3-7.1 goals (baseline 5.25).

- **The scoreline guard fails.** The plan limits the stronger side to 4.5 goals against the all-out attack. The fall from 0.90 points (30%) to 0.51 (40%) comes with 4.21 and 4.92 goals: no value meets both the points target (at most 0.55) and the guard. The extra goals come from shots (39 to 48-52 a match) rather than from better shots. The user picked 40%, which is baked.
- **Guards at 40%** (seed 90000): a 6-skill side takes no points (S3: 100/0/0 in all three setups); between equal sides no setup beats every other, and the away counter-attack against a home default takes 0.96 points (target at least 0.85); all-out attack against all-out attack is 6.46-4.76 goals (5.75-4.30 before). `ticksnap 150`: 3.05 goals a match (2.73 after Milestone 1), 0.05 over the plan band 2.4-3.0.
- Test changes: `TickCounterAttackTests` (the exposed break opens only without the flag and against a posture above 0; it is a part of the counter break that grows with posture and speed).
