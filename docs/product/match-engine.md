# Match engine version 4

> **Status:** Executable specification for `engine-v4` / `engine-rules-v4`, implemented in
> `src/TouchlineManager.MatchEngine`.
> **Applies to:** engine version `4`, engine rules version `4`, rating weights `engine-ratings-v1`,
> tactical modifiers `engine-tactical-v1`, commentary `commentary-v3`, replay `replay-v3`.
> **Version 2** added the assists and the per-player match rating to a result's player lines (§8.1).
> **Version 3** made the play spatial: a possession resolves a loose-ball scramble, a 1v1 ground duel,
> and set pieces against player attributes on a normalised pitch, and samples a live condition and rating
> curve. **Version 4** makes the passage the unit of movement — a possession is played as a real chain of
> touches that starts where the last one left the ball (or at a restart), progresses into the attacking
> third, and ends at an outcome-appropriate point, so event coordinates, shot maps, direct free kicks,
> and the continuous film are meaningful (§7.2, §7.3). The outcome formulas and their calibrated
> distributions are unchanged from version 3 and were re-validated, not re-invented (§12).
> **Behavioural rules:** [`game-rules.md`](game-rules.md) §15 (`MAT-*`) is normative for *what* a match
> must be. This document is normative for *how* version 4 computes it.
> **Decisions:** [ADR-0004](../architecture/adr/0004-deterministic-match-engine.md) (purity, versioning,
> reproducibility), [ADR-0013](../architecture/adr/0013-engine-arithmetic-and-scoreline-effect.md)
> (integer arithmetic, the scoreline effect), [ADR-0051](../architecture/adr/0051-engine-v4-continuous-passages.md)
> (continuous passages, the passage recorder), [ADR-0052](../architecture/adr/0052-replay-v3-film-and-reel.md)
> (the film and the reel).

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

Each step consumes its draws whether or not it is reached, and the order is the version. The outcome
decisions (the foul, the scramble, progression, the duel, creation, the chance) draw from the match's own
stream; the **geometry** — the ball's path and the participants of the generic touches — draws from a
per-possession derived stream, `new Pcg32(seed * 1_000_003 + ordinal)`, the same pattern `AssistPlanner`
uses. The geometry can therefore never advance an outcome draw or move a distribution (ADR-0051).

1. **Possession is chosen** from the two sides' control, where a side's control is
   `BuildUp − opponent.DefensivePressure`. The home share is
   `5_000 + swing(2400 per 1000 differential) + 120`, clamped to **2_000…8_000** so neither side is ever
   shut out of a match.
2. **The clock advances** and both sides pay the load (§7.5).
3. **The substitution planner runs** (§7.6).
4. **The passage is planned and the ball moves.** The possession starts at `MatchState.Ball` — where the
   previous possession left it — except at a restart: the centre spot after kick-off, half-time, and a
   goal; the goal area after a keeper claim or parry. `PassagePlanner` draws 3–8 touches that advance the
   ball toward the far goal with lateral drift, so the approach runs from the start to the possession's
   **pressure point** (the middle-to-attacking third), and `state.MoveBallAndRecord` plays them. The
   carrier who receives and the passer who plays the final ball are drawn from that same derived stream.
5. **The defending side's foul** (`BaseFoulBasisPoints` = 1_100 per possession, ×13_500 aggressive /
   ×8_200 stay-on-feet). A foul ends the possession, ball at the pressure point. 120 bp of fouls are
   penalties — the ball moves to the penalty spot (§7.3). A foul in the attacking half from
   `FreeKickShootingRangeX` (6_500) onwards becomes a **direct free kick** 4_500 bp of the time
   (`FreeKickAwardBasisPoints`); in range (`FreeKickAttemptBasisPoints` = 3_000) it is struck at goal, and
   otherwise crossed. Either way the discipline flow records the foul and its card.
6. **A loose-ball scramble opens only a share of passages.** `ScrambleOpeningBasisPoints` = 1_500 of
   possessions begin with a genuine 50/50, contested by the players the scramble asks for (pace,
   acceleration, work rate). Losing it is a hard turnover: the ball is cleared into the middle third.
7. **Progression**: `6_200 ± swing(2400)` against the control differential, clamped to **3_400…9_000**. A
   failure is an offside (`OffsideShareOfTurnoverBasisPoints` = 800) — the ball is placed on the offside
   line — or a plain turnover, cleared into the middle third.
8. **The carrier's 1v1 ground duel** (engine-v3): the carrier is drawn by dribbling and the tackler by
   tackling, and the winner buys (or loses) `DribbleCreationBonusBasisPoints` = 1_200 of creation. A lost
   duel does not end the passage; a tackled attack regrouping is the ordinary rhythm.
9. **Creation**: `2_400 ± swing(2800)` against
   `(Creation + Finishing/2) − (DefensiveShape + Goalkeeping/2)`, plus the duel bonus, clamped to
   **1_100…6_200**, then multiplied by the scoreline effect (§7.4). A failure is a corner
   (`CornerShareOfFailedCreationBasisPoints` = 1_200) — the ball is placed at the corner flag — or a
   turnover, cleared into the middle third; a corner becomes a headed chance 3_400 bp of the time.
10. **The chance** (§7.3). The ball is placed at the shot point before it is resolved, so the event's
    coordinates are the shot's real location in the final third.

The geometry ends every possession at a point its outcome names: the final third across the shot's zone
band for an open-play shot, the penalty spot, the pressure point for a foul, the corner flag, the offside
line, or the middle third for a turnover. `state.MoveBall` is called along the passage and **before every
`Emit`**, so an event's x/y is where it actually happened; crosses, headers, shots, and clearances carry an
altitude, ground passes and carries do not.

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

A **direct free kick** in range is its own resolution (engine-v4). The taker is the best set-piece/finishing
player on the pitch; the ball is placed at the pressure point where the foul was committed, so
`attackingX >= FreeKickShootingRangeX` is genuinely reachable — a foul deep in the attacking third can now
produce a `free_kick_shot`. Its baseline is `FreeKickGoalBasisPoints` = 900, moved by the set-piece-versus-
goalkeeping differential (`FreeKickQualitySwingBasisPoints` = 1_400); a non-goal is saved
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

## 10. Replay: the film and the reel (`replay-v3`)

`ReplayDirector.Build(input, result, passages, options, liveMetrics)` re-derives the whole presentation
from the frozen snapshot, the result, the recorded passages (ADR-0051), and the optional live metric curve.
It is a pure function of those inputs and consumes no draw; the presentation is **never stored**, which is
why a replay revision is a clean contract change rather than a migration (ADR-0052).

**One film.** The recorded possessions are merged into film passages of roughly equal playback length
(`TargetPassageMilliseconds` = 9 s, capped at `MaxPassages` = 75), split at substitutions, half-time, and
bookings so a passage's eleven is stable. A time warp fits the match into the nine-to-eleven-minute window —
`targetFilmMilliseconds = clamp(totalMatchSeconds * 1_000 / 9, 9:30, 11:00)` — weighting each passage (×2.0 a
goal, ×1.5 a shot, ×1.25 a final-third entry, ×0.8 a middle-third turnover) so chances are readable. The
ball's track is the recorded path; each player's track is the shape `TacticalFormationResolver` gives them
over that path, overwritten by their recorded touches. Boundary frames are copied exactly, so the film
joins rather than cuts. Every passage carries the current eleven and the ball, its `StartMatchSecond`/
`EndMatchSecond` window, its `OutcomeCode`, its event sequences, and synchronized commentary.

**One reel.** `ReelBuilder` selects the chance clips: **goals always**, plus the best chances by
`QualityBasisPoints` above `MinQualityForShotBasisPoints` = 700, count-capped at `MaxReelClips` = 12 with
goals excepted. Each clip reaches back over a lead-in of up to `ReelLeadInMatchSeconds` = 600 match-seconds
of film (~65 s, clamped to 25–70 s), and overlapping clips are merged. If the reel would exceed
`MaxReelMilliseconds` = 12:00 the lead-ins are shortened to their floor first, then the lowest-quality
non-goal clips are dropped; **goals survive both**, because a result a manager cannot watch is a worse
failure than a large download.

**Payload.** `EstimatedPayloadBytes` counts entities (×48), keyframes (×24), narration, commentary,
schedule segments (×48), both lineups, and the live metrics (×64). If it exceeds
`PayloadBudgetBytes` = 750 KB (ADR-0006) the director recompresses the tracks at widening tolerances
(32 → 40 → 48) and sampling intervals (400 → 600 → 800 ms) until it fits — deterministic, so the same match
always lands on the same rung.

Entities and tracks are ordered by identifier. The narration names the player and the clock, so the Canvas
is not the only way to follow it; the colours come from a fixed generated palette chosen by club identity,
so no real club's identity can leak (`WORLD-3`). Shirt numbers and sides are on every player entity, which
is what lets a client distinguish teams by more than colour.

---

## 11. Configuration reference

Every value below is a field on `EngineRulesV2`, covered by the rules hash. The validation rules are the
engine's own: a probability must lie in `0…10_000`, a multiplier in `5_000…25_000`, an ordered pair must
be ordered, and a rating scale must be able to hold a maximum-attribute player. The **Spatial play
(engine-v3)** and **Passage progression (engine-v4)** blocks are the constants the pitch model and the
continuous passage added; they are re-validated by their own shape checks (a bounded touch count, a
pressure band that reaches the free-kick range, a shot band wholly inside the final third, altitudes on the
ball's 0–100 scale).

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
| `BaseShotGoalBasisPoints` | 845 | An average chance, inside channel. |
| `ShotQualitySwingBasisPoints` | 1_900 | Per full reference differential. |
| `CentralZoneMultiplierBasisPoints` | 15_000 | |
| `InsideZoneMultiplierBasisPoints` | 10_000 | The reference case. |
| `WideZoneMultiplierBasisPoints` | 8_000 | |
| `WoodworkShareBasisPoints` | 700 | Of non-goal shots. |
| `BlockedShareBasisPoints` | 2_600 | |
| `BaseSaveBasisPoints` | 5_000 | |
| `MinSaveBasisPoints` / `Max` | 2_500 / 7_500 | |
| `PenaltyGoalBasisPoints` | 7_600 | |
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
| `GoalAreaXBasisPoints` | 1_200 | A goal-area restart after a keeper claim. |
| `CrossShareOfPassageBasisPoints` | 2_800 | Share of the final approach that is crossed. |
| `CrossAltitude` / `HeaderAltitude` | 70 / 80 | Ball altitude at a cross and a header. |
| `ShotAltitude` / `ClearanceAltitude` | 30 / 55 | Ball altitude at a shot and a clearance. |
| `FreeKickShootingRangeX` | 6_500 | Distance beyond which a free kick is worth striking. |
| `FreeKickAwardBasisPoints` | 4_500 | Chance an attacking-half foul is a direct free kick. |
| `FreeKickAttemptBasisPoints` | 3_000 | Chance a free kick in range is struck at goal. |
| `FreeKickGoalBasisPoints` | 900 | A direct free kick's baseline goal probability. |
| `FreeKickQualitySwingBasisPoints` | 1_400 | Per set-piece-vs-goalkeeping differential. |
| `FreeKickSavedShareBasisPoints` | 4_500 | Of non-goal free kicks. |
| `FreeKickBlockedShareBasisPoints` | 2_500 | |
| `FreeKickWoodworkShareBasisPoints` | 800 | |
| `FreeKickFoulCardBasisPoints` | 2_200 | A free-kick foul is booked slightly more often. |
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
| `DuelYellowCardBasisPoints` | 1_800 | |
| `DuelRedCardBasisPoints` | 150 | |
| `ShorthandedConditionLossMultiplierBasisPoints` | 12_500 | Per man short, as cover tires. |
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
| `ShortHandedPenaltyBasisPoints` | 6_400 | Per player below eleven. |
| `HomeAdvantageBasisPoints` | 10_380 | A ~4% multiplier on the home side's ratings. |
| `RatingBaseBasisPoints` | 6_000 | Where a player's match rating starts. |
| `RatingWinBonusBasisPoints` / `RatingDrawBonusBasisPoints` / `RatingLossPenaltyBasisPoints` | 600 / 120 / 350 | The result's contribution, weighted by minutes. |
| `RatingGoalBonusBasisPoints` / `RatingAssistBonusBasisPoints` | 1_000 / 450 | Per goal and per assist. |
| `RatingSaveBonusBasisPoints` / `RatingMaxSaveBonusBasisPoints` | 60 / 400 | Per save and its cap. |
| `RatingYellowPenaltyBasisPoints` / `RatingRedPenaltyBasisPoints` | 350 / 1_400 | Per booking and per sending-off. |
| `RatingMinBasisPoints` / `RatingMaxBasisPoints` | 1_000 / 10_000 | The clamp on a rating. |
| `LiveRatingBaseBasisPoints` | 6_000 | Where a player's live rating starts (`engine-v3`). |
| `LiveRatingPassBonusBasisPoints` / `KeyPassBonusBasisPoints` | 30 / 300 | A completed pass and a chance-creating one. |
| `LiveRatingTackleBonusBasisPoints` / `TackleLostPenaltyBasisPoints` | 120 / 80 | A tackle won and lost. |
| `LiveRatingInterceptionBonusBasisPoints` | 80 | An interception. |
| `LiveRatingAerialBonusBasisPoints` / `AerialLostPenaltyBasisPoints` | 80 / 50 | An aerial duel won and lost. |
| `LiveRatingShotBonusBasisPoints` / `ShotMissPenaltyBasisPoints` | 100 / 40 | A shot on and off target. |
| `LiveRatingGoalBonusBasisPoints` / `AssistBonusBasisPoints` | 800 / 450 | A goal and an assist. |
| `LiveRatingSaveBonusBasisPoints` / `GoalConcededPenaltyBasisPoints` | 250 / 250 | A save and a goal conceded. |
| `LiveRatingYellowPenaltyBasisPoints` / `RedPenaltyBasisPoints` | 200 / 1_200 | A booking and a sending-off. |
| `LiveRatingErrorPenaltyBasisPoints` | 600 | An error leading to a goal. |
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
(`engine-v4`, rules hash `c0f6aaf3…`):

| Measure | Measured | Target |
|---|---|---|
| Goals per match | 2.89 | 2.5 – 3.0 |
| Home / away goals | 1.58 / 1.31 | 1.3 – 1.9 / 1.0 – 1.5 |
| Home win / draw / away win | 44.1% / 24.0% / 31.8% | 40 – 50 / 20 – 30 / 25 – 35 |
| Shots per match | 27.3 | 20 – 32 |
| Home possession | 52.3% | 50 – 54 |
| Fouls per match | 21.2 | 18 – 26 |
| Yellows per match | 3.40 | 3.0 – 5.0 |
| Reds per match | 0.28 | 0.10 – 0.35 |
| Injuries per match | 0.43 | 0.20 – 0.60 |
| Penalties per match | 0.25 | 0.15 – 0.40 |
| Substitutions per match | 7.0 | 4.0 – 10.0 |
| p99 total goals | 7 | 6 – 8 |
| Matches with 7+ goals | 2.76% | < 3.0% |

Calibration invariants: home advantage worth **+3.9 points** (target ~+4); a three-ability-point favourite
upset **16.1%** of the time (target ~15); a side sent off early finishes **1.16 goals** worse (target ~1.2);
a high-pressing side is measurably more tired by the 80th minute (gap ~1,678 bp) and a fresh substitute
measurably fresher than the tired defenders (~1,762 bp). Free kicks in shooting range — dead code before
engine-v4 — now occur about **1.5 per match**, with the goal and shot bands held.

The replay over **10,000 matches** (ADR-0052):

| Measure | p05 | p50 | p95 | min / max |
|---|---|---|---|---|
| Passages per match | 54 | 57 | 66 | — / 71 |
| Film minutes | 10.5 | 10.7 | 10.9 | 10.5 / 11.0 |
| Reel minutes | 6.6 | 8.3 | 9.6 | — / 10.9 |
| Payload (KB) | 500.0 | 518.6 | 539.6 | — / 573.9 |

**100%** of films land inside the 9:30–11:00 window and **0.00%** exceed eleven minutes; the reel is always
inside its 12:00 cap, and the payload estimate is comfortably inside the 750 KB budget.

Performance, 5,000 matches after a warm-up:

| Measure | Value |
|---|---|
| p50 per match | 1.00 ms |
| **p95 per match** | **5.73 ms** (budget: 100 ms) |
| p99 per match | 16.9 ms |
| Mean per match | 1.61 ms |
| Allocated | ~2.9 MB per match |
| Throughput | ~621 matches/sec, single-threaded |

A nine-fixture division matchday is therefore about 15 ms of simulation at the mean and about 52 ms at the
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
| `PassageTests` | One passage per possession; waypoints and touches on the pitch and in fraction order; continuity (or a restart) between possessions; every shot in the attacking third and free-kick shots in range; touches naming match participants; recorder determinism; the with/without-recorder hash equality. |
| `ReplayDirectorTests` | One passage per film segment and a contiguous schedule; the nine-to-eleven-minute film; passage windows in order; boundary-frame continuity; on-pitch, in-passage keyframes; the eleven and the ball with a track each; `MAT-11`-safe passage commentary; the reel carrying every goal; determinism; the payload budget. |
| `HighlightTests` | Reel selection: goals always shown, the quality floor, the count cap and its goal exception. |
| `EnginePurityTests` | No clock, no `System.Random`, no IO; exactly one source of randomness. |
| `DistributionTests` | The statistical bands, over two thousand matches. |
