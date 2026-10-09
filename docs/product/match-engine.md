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
> `src/TouchlineManager.MatchEngine/Tick` and `Spatial`. **Decision:** [ADR-0066](../architecture/adr/0066-engine-v12-discrete-tick-match-engine.md).

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
2. **Shape.** `TickTacticalGeometry` turns each slot of the team sheet into a point to hold: the block follows the ball up and down the pitch, pushes up
   with the ball and drops without it, is squeezed or opened by the width, the defensive line and the pass focus, and a defender is never nearer than
   8 m to his own goal line nor a player nearer than 7 m to the other.
3. **Orders.** The side without the ball (`TickDefensiveAI`): one presser, a second only on a touchline under a high press, markers in the own third,
   screens in the passing lanes, and a defensive line that steps up on a pressed carrier and drops off a free one; and the side with it
   (`TickOffBallSupport`): the carrier's support at 12 to 25 m, a runner behind the line, a pocket, an overlap. A goalkeeper takes his arc, rushes out
   to a lone attacker or dives (`TickGoalkeeperAI`, `TickShotStopper`).
4. **The man with the ball** decides every one to three ticks (`TickBallCarrierBrain`): shoot, pass, through ball, cross, dribble, shield, recycle or
   clear, whichever is worth most as the chance it comes off times its worth less the chance it fails times what a lost ball costs here, with no draw. A
   kick leaves rotated off its aim by one draw within the error angle.
5. **Bodies.** `TickSteering` and `TickPlayerPhysics` move every player: arrive, keep 2 m from a teammate, blend with the old velocity, turn at the rate
   Agility allows, accelerate and brake as Pace and Acceleration allow, and spend energy.
6. **Contest.** A defender in touch of the carrier may challenge (`TickTackleResolver`), a goalkeeper in reach may smother, a defender who reaches a
   pass or a shot rolls to cut it out or to block it, and the nearest man to a loose ball takes it.
7. **Boundary.** The ball steps (`TickBallPhysics`: roll, flight, bounce, post, bar) and a goal, a goal line, a touchline or a post is settled.

### 14.4 The calibration constants

All of these are `const` in the type that uses them, with the reason beside them. They were set by the calibration of §14.5 and are the engine's rules
for version 12; changing one moves the golden hash in `TickEngineTests`.

| Constant | Value | Meaning |
|---|---|---|
| `TickPlayerSkills.CurvePercent` | 60 | A player's attribute counts 60% of its distance from 13. |
| `TickBallCarrierBrain.ShotMinimumChance` | 1,850 bp | Below this heuristic chance a carrier does not shoot. |
| `TickBallCarrierBrain.ShotBaseError` | 210 | Base error angle of an open-play shot (a set piece: `SetPieceShotBaseError` 50). |
| `TickBallCarrierBrain.ErrorSkillWeight`, `MinimumErrorPercent` | 5, 25% | A kick's error is `Base × max(25%, 100 − (5 × skill + Technique) / 2)`. |
| `TickBallCarrierBrain.PassArrivalCentimetresPerSecond` | 1,000 | A ground pass arrives at 10 m/s. |
| `TickBallCarrierBrain.OffsideCarelessBase`, `…PerPoint` | 12%, 4 | Share of offside receivers a carrier misses, by Decisions plus Anticipation below 26. |
| `TickMatchLoop.InterceptMaximum`, `InterceptSkillPerPoint` | 3,500 bp, 4% | Chance a defender cuts out a pass he reaches, and the % it moves per point he is above the passer. |
| `TickMatchLoop.PassGraceTicks` | 3 | Ticks after a pass in which no opponent can reach it. |
| `TickMatchLoop.ChallengeCommitBase`, `…PerAggression` | 1,000, 100 bp | Chance a defender in touch of the carrier challenges on a tick. |
| `TickMatchLoop.PenaltyGivenBasisPoints` | 1,000 bp | Share of fouls in the area given as penalties. |
| `TickMatchLoop.BlockToCornerBasisPoints` | 4,500 bp | Share of blocked shots turned behind for a corner. |
| `TickTacticalGeometry.DefenceMinimumX`, `AttackMarginX` | 800, 700 | The nearest to a goal line a shape position goes. |
| `TickShotStopper` reach | `70 + 40 + 6 × (Agility + Reflexes) / 2` | A goalkeeper dives on his reflexes as well as his agility. |

Home advantage is the snapshot's `HomeAdvantageBasisPoints` less 10,000, added to the home side's man in every tackle and interception roll.

### 14.5 Measured behaviour

`tools/simulation-benchmarks` (`dotnet run -c Release --project tools/simulation-benchmarks -- <mode> …`):

| Mode | What it plays |
|---|---|
| `tick N` | N matches with the board's formations in turn, uniform players: the figures of the plan, the film's size and the time. |
| `tickmatrix N` | Every formation against every other, N matches each; `TICK_FORMS=0,1,5` picks formations, `TICK_ABILITY`, `TICK_INSTR=Pressing=2,Tempo=0`, `TICK_HOME_ATTR=16,Pace=9` change the sides. |
| `ticksnap N file` | The stored snapshots of a database export (id, goals, goals, document, tab separated): real formations, instructions and attributes. `TICK_UNIFORM`, `TICK_FORM`, `TICK_FLAT` take the real thing apart. |

The stored matches (205 snapshots of the development database, each played once):

| | Measured | Plan band |
|---|---|---|
| Goals a match | 2.83 | 2.60–2.90 |
| Shots a match | 28.3 | 22–28 |
| Shots on target | 38% | 32–38% |
| Pass completion | 76% | 75–85% |
| Yellow cards | 4.0 | 3.0–4.5 |
| Home / draw / away | 42 / 24 / 34 % | 42–48 / 22–26 / 28–34 % |
| Fouls, penalties, corners, offsides | 22, 0.4, 5.1, 3.9 | |

Every formation against every other with uniform players of 13 gives about 2.3 goals and 26 shots a match, 78% pass completion, 3.4 yellow cards; at an
ability of 8 and of 19 it gives 2.9 and 2.4 goals. A home side whose players are all 3 points better than the visitors wins 59% of the matches and loses
18%. Each instruction at its extreme (a high press, a high tempo, a high or deep line, an aggressive tackle, the most defensive and the most attacking
mentality) moves the goals between 1.9 and 3.1 and nothing runs away.

What is not matched: a throw-in is taken about 14 times a match (a real match has about 35) and a corner 5 (about 10), because balls leave the pitch
less often than they do in football; and the formations are not alike — a lone striker or three forwards shoots about twice as often as two strikers.

### 14.6 The film

With a `MatchPassageRecorder` attached the loop records each tick (`TickMatchRecording`: the 22 players and the ball as shorts, plus the actions, events,
rosters and stoppages), and `ReplayDirector.Build(input, result, recorder)` cuts a film from it (`TickFilmSelector`, `TickReplaySynthesizer`,
`tick-replay-v1`). The film is a selection at twice the pace — about ten minutes of kick-offs, every goal with 18 s of build-up, cards, the best chances and
the corners — in passages that share a boundary frame and are joined by `jump`, `kick_off` and `half_time` cuts; the viewer reads it unchanged.

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
