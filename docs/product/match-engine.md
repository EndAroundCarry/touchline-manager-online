# Match engine version 1

> **Status:** Executable specification for `engine-v1` / `engine-rules-v1`, implemented in
> `src/TouchlineManager.MatchEngine`.
> **Applies to:** engine version `1`, engine rules version `1`, rating weights `engine-ratings-v1`,
> tactical modifiers `engine-tactical-v1`.
> **Behavioural rules:** [`game-rules.md`](game-rules.md) §15 (`MAT-*`) is normative for *what* a match
> must be. This document is normative for *how* version 1 computes it.
> **Decisions:** [ADR-0004](../architecture/adr/0004-deterministic-match-engine.md) (purity, versioning,
> reproducibility), [ADR-0013](../architecture/adr/0013-engine-arithmetic-and-scoreline-effect.md)
> (integer arithmetic, the scoreline effect).

Every constant named below lives in `EngineRulesV1` and is covered by the rules hash, so a result can
always be explained by the configuration that produced it. **Changing any value, formula, draw order, or
event semantic is an engine-version change** (`MAT-9`, ADR-0004): the golden hashes move, and the old
version must remain compiled and replayable rather than being edited in place.

---

## 1. Scope and purity

The engine is one public method:

```csharp
MatchResultV1 MatchSimulator.Simulate(MatchInputV1 input, EngineRulesV1 rules)
```

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

Nine ratings. Eight are weighted means of attributes over the players a weighting table says do that job;
`Cohesion` measures fit rather than quality and is computed from familiarity instead.

### 6.1 The weighting tables (`UnitRatingWeights`, `engine-ratings-v1`)

| Unit | Attributes (weight) | Bands (weight) |
|---|---|---|
| Build-up | Passing 6, Technique 5, First touch 5, Composure 3, Decisions 3, Vision 3 | GK 1, DEF 4, MID 6, ATT 2 |
| Creation | Vision 6, Passing 5, Technique 5, Dribbling 4, Crossing 3, Decisions 3 | DEF 1, MID 6, ATT 5 |
| Finishing | Finishing 7, Composure 5, Technique 4, Heading 3, Anticipation 3, Pace 2 | MID 3, ATT 7 |
| Defensive pressure | Tackling 6, Work rate 5, Aggression 4, Stamina 4, Anticipation 4, Pace 3 | DEF 5, MID 5, ATT 1 |
| Defensive shape | Marking 6, Positioning 6, Anticipation 4, Decisions 4, Tackling 3, Strength 2 | GK 1, DEF 6, MID 4 |
| Goalkeeping | Handling 6, Reflexes 6, One-on-ones 4, Aerial ability 4, Positioning 4, Composure 2 | GK 1 |
| Set pieces | Set pieces 7, Crossing 5, Heading 4, Jumping reach 4, Technique 3 | DEF 3, MID 4, ATT 4 |
| Fitness | Stamina 7, Work rate 6, Pace 4, Strength 3, Agility 3 | GK 1, DEF 4, MID 5, ATT 4 |

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
playerRating  = Σ(weight × attribute × AttributeRatingFactor) / Σ(weight)
effective     = playerRating × familiarity / 10_000 × stateMultiplier / 10_000
unitRating    = Σ(bandWeight × effective) / Σ(bandWeight)
unitRating    = unitRating × tacticalModifier / 10_000
unitRating    = unitRating × homeAdvantage? × shortHandedPenalty^missing
unitRating    = clamp(unitRating, 0, MaxUnitRating = 1_150)
```

### 6.3 The state multiplier

Each of the four state values is a linear interpolation between its floor and ceiling, and they compose
multiplicatively. At the worst possible state a player is worth about a quarter less — roughly five
attribute points: enough to prefer a fresh substitute, not enough to make a good player bad.

| Input | Floor | Ceiling |
|---|---|---|
| Condition | 8_500 | 10_500 |
| Fatigue (inverted: freshness) | 8_750 | 10_000 |
| Morale | 9_500 | 10_000 |
| Sharpness | 9_600 | 10_000 |

### 6.4 Cohesion

The mean familiarity of the players on the pitch, less `OutOfPositionCohesionPenaltyBasisPoints` = 1_400
for each makeshift player, expressed on the rating scale. A side where everyone fits is worth a
maximum-attribute player; a side of misfits is worth correspondingly less. It takes no tactical modifier,
because no instruction changes how well a player suits a job.

### 6.5 Tactical modifiers (`engine-tactical-v1`)

Every instruction has a cost as well as a benefit, and this is where that is enforced. Each unit's
modifier is the sum of the applicable deltas, then clamped to `MinTacticalModifierBasisPoints` = 8_800 …
`MaxTacticalModifierBasisPoints` = 11_500 — about ±15% across the whole instruction set, worth roughly
three attribute points.

| Instruction | Effect on units |
|---|---|
| Mentality | Attacking: +creation, +finishing, −defensive shape, −fitness. Defensive: the reverse. |
| Tempo | High: +creation, −fitness, shorter possessions, faster fatigue. Low: the reverse. |
| Passing | Short: +build-up. Direct: −build-up, slightly +creation. |
| Width | Wide: +creation, +set pieces, −defensive shape, −build-up. Narrow: the reverse. |
| Pressing | High press: +defensive pressure, +creation, −defensive shape, −fitness, faster fatigue. Low block: the reverse. |
| Defensive line | High: +build-up, +defensive pressure, −defensive shape. Deep: the reverse. |
| Tackling | Aggressive: +defensive pressure, −defensive shape, **and more fouls, more cards, more suspensions** (see §7.3). Stay on feet: the reverse. |
| Time wasting | Costs creation, buys defensive shape. |

Goalkeeping and cohesion take no tactical modifier. The bound is what keeps attributes dominant
(`INS-9`): the worst possible instruction set does not overturn a whole division of quality — a 16-ability
side under the most hampering instructions still out-rates a 9-ability side under the best ones
(`UnitRatingTests.The_bounds_leave_attributes_in_charge`).

---

## 7. Simulation

### 7.1 The clock

90 regulation minutes plus stoppage, played as a sequence of possessions (`MAT-3`). A possession consumes
`PossessionSecondsMin`…`Max` = 16…44 seconds, multiplied by 8_000 at a high tempo or 12_000 at a low one,
floored at `MinEffectivePossessionSeconds` = 6 so the clock always advances. A match is therefore roughly
180 possessions — about 90 per side.

Stoppage is drawn per half — `StoppageBaseSeconds` = 150 plus up to `StoppageJitterSeconds` = 60 — and
accumulates as the half is played: 20 s per goal, 25 s per card, 15 s per substitution, 60 s per injury.
It is clamped to 1–10 minutes and the half ends when the clock reaches regulation plus stoppage.

### 7.2 One possession, in order

Each step consumes its draws whether or not it is reached, and the order is the version.

1. **Possession is chosen** from the two sides' control, where a side's control is
   `BuildUp − opponent.DefensivePressure`. The home share is
   `5_000 + swing(2400 per 1000 differential) + 120`, clamped to **2_000…8_000** so neither side is ever
   shut out of a match.
2. **The clock advances** and both sides pay the load (§7.5).
3. **The substitution planner runs** (§7.6).
4. **The defending side's foul** (`BaseFoulBasisPoints` = 1_100 per possession, ×13_500 aggressive /
   ×8_200 stay-on-feet). A foul ends the possession. 120 bp of fouls are penalties; otherwise a card roll.
5. **Progression**: `6_200 ± swing(2400)` against the control differential, clamped to **3_400…9_000**. A
   failure is an offside (800 bp) or a plain turnover.
6. **Creation**: `2_400 ± swing(2800)` against
   `(Creation + Finishing/2) − (DefensiveShape + Goalkeeping/2)`, clamped to **1_100…6_200**, then
   multiplied by the scoreline effect (§7.4). A failure is a corner (1_200 bp) or a turnover; a corner
   becomes a headed chance 3_400 bp of the time.
7. **The chance** (§7.3).

### 7.3 Shot resolution

The shooter is drawn weighted by the attribute the chance asks for — `Finishing` in open play, `Heading`
from a corner — over the outfield players in slot order. The penalty taker is not drawn: it is the best
finisher on the pitch, ties broken by identity.

```text
zoneMultiplier  = central 15_000 | inside 10_000 | wide 8_000
base            = BaseShotGoalBasisPoints (760) × zoneMultiplier / 10_000
contest         = shooterAttribute − (opponent Goalkeeping rating / AttributeRatingFactor)
goalChance      = clamp(base + swing(contest, 1900 per 1000), 220, 5_600)
```

One draw resolves the goal. If it does not score, one further draw splits the failure:

| Outcome | Chance | Event |
|---|---|---|
| Woodwork | `WoodworkShareBasisPoints` = 700 | `Woodwork` |
| Blocked | `BlockedShareBasisPoints` = 2_600 | `ShotBlocked` |
| Saved | `BaseSaveBasisPoints` = 5_000 ± swing, clamped to 2_500…7_500 | `ShotSaved` |
| Off target | the remainder | `ShotOffTarget` |

A penalty is 7_600 bp and is its own event pair: `PenaltyAwarded`, then `PenaltyGoal` or `PenaltyMissed`.

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
+90 bp per goal scored, −70 per goal conceded — bounded to ±600 bp from its kickoff value.

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

---

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

Events carry **facts, never prose**. A shot event carries its `QualityBasisPoints` — the goal probability
it was resolved against — so highlight selection can tell a good chance from a bad one. That is a fact
about a shot, derived from attributes the owning manager can already see, and emphatically not a hidden
player value; but it is also not something a player-facing response may carry (`MAT-11`), which is why the
commentary tests hold an allowlist of parameter names and the data-classification test guards the contracts
assembly.

---

## 9. Commentary (`commentary-v1`)

`CommentaryTokenBuilder.Build` turns the event stream into tokens: a stable template key, the facts, a
variant key, and the current English text. The key and parameters are the durable part — storing them
rather than only a sentence is what makes the same match narratable in another language later without
re-simulating it.

Repetition is avoided deterministically: each template has three variants and the event's sequence number
chooses between them, so the same match always produces the same words and a long match does not read as
one sentence repeated. A random variant would make the commentary unreproducible while the result stayed
reproducible.

Commentary is a pure function of input and result. It cannot change an outcome (`MAT-8`), and it cannot
reveal a hidden attribute (`MAT-11`).

---

## 10. Highlights (`highlights-v1`)

Selection is a pure function of the event stream:

| Shown | Always / condition |
|---|---|
| Goals, penalty goals, penalty misses | Always |
| Woodwork | `IncludeWoodwork` |
| Saves, blocks, off-target shots | `QualityBasisPoints ≥ MinQualityForShotBasisPoints` = 1_600 |
| Penalty awards | Never on their own — the goal or miss that follows is the thing to watch |

Two caps apply in order. `MaxHighlights` = 24 trims the lowest-quality chances. The
`PayloadBudgetBytes` = 750 KB budget then trims again. **Goals survive both**, however large the payload: a
result a manager cannot watch is a worse failure than a large download.

Each highlight is `HighlightPresentationV1`: 22 player entities plus the ball, one track per entity (an
involved player's moving track *replaces* their stationary one), normalized 0–10_000 coordinates from the
same values the tactics board stores — mirrored for the away side — and a duration of 5–8 seconds. A goal
is worth the longest look.

Entities and tracks are ordered by identifier. The narration names the player and the clock, so the Canvas
is not the only way to follow it; the colours come from a fixed generated palette chosen by club identity,
so no real club's identity can leak (`WORLD-3`). Ship numbers and sides are on every player entity, which
is what lets a client distinguish teams by more than colour.

---

## 11. Configuration reference

Every value below is a field on `EngineRulesV1`, covered by the rules hash. The validation rules are the
engine's own: a probability must lie in `0…10_000`, a multiplier in `5_000…25_000`, an ordered pair must
be ordered, and a rating scale must be able to hold a maximum-attribute player.

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
| `PossessionSecondsMin` / `Max` | 16 / 44 | Time one possession takes. |
| `HighTempoPossessionSecondsMultiplierBasisPoints` | 8_000 | High tempo shortens possessions. |
| `LowTempoPossessionSecondsMultiplierBasisPoints` | 12_000 | Low tempo lengthens them. |
| `MinEffectivePossessionSeconds` | 6 | Clock-advance floor. |
| `BasePossessionBasisPoints` | 5_000 | Even split. |
| `PossessionControlSwingBasisPoints` | 2_400 | Swing at a full reference differential. |
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
| `BaseShotGoalBasisPoints` | 760 | An average chance, inside channel. |
| `ShotQualitySwingBasisPoints` | 1_900 | Per full reference differential. |
| `CentralZoneMultiplierBasisPoints` | 15_000 | |
| `InsideZoneMultiplierBasisPoints` | 10_000 | The reference case. |
| `WideZoneMultiplierBasisPoints` | 8_000 | |
| `WoodworkShareBasisPoints` | 700 | Of non-goal shots. |
| `BlockedShareBasisPoints` | 2_600 | |
| `BaseSaveBasisPoints` | 5_000 | |
| `MinSaveBasisPoints` / `Max` | 2_500 / 7_500 | |
| `PenaltyGoalBasisPoints` | 7_600 | |
| `BaseFoulBasisPoints` | 1_100 | Per possession, by the defending side. |
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
| `ConditionFactorFloorBasisPoints` / `Ceiling` | 8_500 / 10_500 | |
| `FatigueFactorFloorBasisPoints` / `Ceiling` | 8_750 / 10_000 | |
| `MoraleFactorFloorBasisPoints` / `Ceiling` | 9_500 / 10_000 | |
| `SharpnessFactorFloorBasisPoints` / `Ceiling` | 9_600 / 10_000 | |
| `MaxUnitRating` | 1_150 | |
| `RatingDifferentialReference` | 1_000 | The differential at which a swing is applied in full. |
| `MaxTacticalModifierBasisPoints` / `Min` | 11_500 / 8_800 | `INS-9`. |
| `OutOfPositionPenaltyBasisPoints` | 8_800 | `INS-10`. |
| `SecondaryPositionPenaltyBasisPoints` | 9_600 | |
| `UnfamiliarRolePenaltyBasisPoints` | 9_400 | |
| `OutOfPositionCohesionPenaltyBasisPoints` | 1_400 | Subtracted from cohesion, not a multiplier. |
| `ShortHandedPenaltyBasisPoints` | 8_600 | Per player below eleven. |
| `HomeAdvantageBasisPoints` | 10_300 | A 3% multiplier on the home side's ratings. |

Two constants are deliberately **not** fields on the rules, because they are contract values rather than
balance values: `Certain` (10_000) and `SlotCoordinateScale` (10_000, `TAC-9`).

---

## 12. Measured behaviour

The simulation laboratory (`tools/simulation-benchmarks`) is the tuning tool and the evidence.

```bash
dotnet run --project tools/simulation-benchmarks -c Release -- all 20000
```

Run over **40,000 matches** between evenly matched 13/20 sides, on a 16-core Windows machine:

| Measure | Measured | Target |
|---|---|---|
| Goals per match | 2.84 | 2.5 – 3.0 |
| Home / away goals | 1.51 / 1.33 | 1.3 – 1.9 / 1.0 – 1.5 |
| Home win / draw / away win | 41.9% / 25.2% / 33.0% | 40 – 50 / 20 – 30 / 25 – 35 |
| Shots per match | 29.1 | 20 – 32 |
| Home possession | 52.0% | 50 – 54 |
| Fouls per match | 21.2 | 18 – 26 |
| Yellows per match | 3.34 | 3.0 – 5.0 |
| Reds per match | 0.27 | 0.10 – 0.35 |
| Injuries per match | 0.43 | 0.20 – 0.60 |
| Penalties per match | 0.26 | 0.15 – 0.40 |
| Substitutions per match | 6.9 | 4.0 – 10.0 |
| p99 total goals | 7 | 6 – 8 |
| Matches with 7+ goals | 2.59% | < 3.0% |

Performance, 5,000 matches after a warm-up:

| Measure | Value |
|---|---|
| p50 per match | 0.65 ms |
| **p95 per match** | **3.47 ms** (budget: 100 ms) |
| p99 per match | 12.0 ms |
| Allocated | ~2.1 MB per match |
| Throughput | ~834 matches/sec, single-threaded |

A nine-fixture division matchday is therefore about 11 ms of simulation, single-threaded. Throughput comes
from running *independent* fixtures concurrently; one match is always simulated single-threaded
(ADR-0004).

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
| `CommentaryTests` | One line per event, variant rotation, no hidden value by allowlist. |
| `HighlightTests` | Goals and penalties always shown, 23 entities, one track each, budgets, caps. |
| `EnginePurityTests` | No clock, no `System.Random`, no IO; exactly one source of randomness. |
| `DistributionTests` | The statistical bands, over two thousand matches. |
