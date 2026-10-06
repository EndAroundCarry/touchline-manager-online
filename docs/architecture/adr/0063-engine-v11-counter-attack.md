# ADR-0063: Engine-v11 adds the counter-attack, a tactic that works against a side that has pushed forward

- **Status:** Accepted
- **Date:** 2026-10-06
- **Stage:** Engine roadmap, tactical instructions
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0058](0058-engine-v8-pass-focus.md), [ADR-0061](0061-engine-v10-attackers-find-space-passers-choose.md), game rules `INS-9`

## Context

A manager could ask for a high tempo and direct passing, but there was no counter-attack: nothing in the engine
treated a ball won back from the opponent differently from any other possession, and the replay drew every move
the same way. The request was a tactic for it. Without it a side breaks on about one in five of the balls it wins
back; with it, about one in two. It should work better against an opponent that has committed forward, whose
space can be run into, and backfire a little against one that sits deep, which is better placed to cut out the
long ball.

## Decision

**1. A tenth team instruction, `CounterAttack`.** A switch (default off), saved with the tactical plan
(`squad.tactical_plans.counter_attack`, `boolean not null default false`), carried by the save request (optional,
so a client that does not send it keeps it off) and the plan response, mapped to the engine in `EngineVocabulary`
and picked on the tactics board with a line saying what it does and costs. AI clubs choose it on one draw in four,
taken last, so nothing they drew before it moves.

**2. What a counter-attack is.** A possession is a counter-attack when it begins from play (no dead ball), the
possession before it was the opponent's, and a roll comes up: `CounterStartBasisPoints` (2,000) for a side that
has not asked for it, `CounterStartWithInstructionBasisPoints` (5,000) for a side that has. The roll is drawn from
a stream of its own, derived from the seed and the possession ordinal, as the geometry stream is, so it moves no
play draw. The passage record carries it (`MatchPassageV1.Counter`), which the replay reads and which never enters
the output hash.

**3. How a counter fares.** The opponent's posture is its mentality and its defensive line, each a step either side
of the middle (mentality −2…+2, line −1…+1), so −3 for a defensive side with a deep line and +3 for an attacking
one with a high line. A counter adds to the chance it progresses out of build-up
`CounterProgressBasisPoints` (200) plus `CounterProgressPerPostureBasisPoints` (1,150) a step, and to its creation
chance `CounterCreationBasisPoints` (150) plus `CounterCreationPerPostureBasisPoints` (1,050) a step. Against a
balanced opponent it is worth the base only; against a deep and defensive one it is worth less than an ordinary
attack, and the instruction backfires. Every roll is the one the engine always took and only the thresholds move.

**3a. The defenders' legs.** How fast the side being countered gets back decides how much of that is left. The mean
effective pace and acceleration of its defenders and midfielders, measured against `CounterRecoveryReference` (11,
which is what an ordinary back line and midfield of ability 13 has), moves a counter's progression chance by
`CounterRecoveryProgressStepBasisPoints` (150) and its creation chance by `CounterRecoveryCreationStepBasisPoints`
(120) per attribute point, either way, capped at 900 and 700. A quick side is in position before the ball arrives or
catches the runner from behind and tackles; a slow one is caught out. Tiredness is in the effective skill, so a
side that has run itself out defends a counter worse late on.

**3b. A failed counter leaves the other side stretched.** When a counter ends in a lost ball (`ScrambleLost`,
`ProgressionFailed`, `CreationFailed` or `CornerCleared`), the side that wins the ball back, if it is cautious, has
caught the first side out of shape. For each step of its own caution (mentality plus line, below the middle) its next
possession is likelier to be a counter, by `CounterBackfireStartBasisPoints` (2,000), and its creation chance
gains `CounterBackfireCreationBasisPoints` (1,500), counter or not: it either breaks in its turn or makes a
dangerous chance of an ordinary attack.

**4. A price, as `INS-9` requires.** A side that plays on the counter loses `CounterAttackBuildUpCostBasisPoints`
(120) of build-up, which is its patience in possession, and `CounterAttackShapeCostBasisPoints` (60) of defensive
shape, with players left forward (`engine-tactical-v4`).

**5. The replay draws it.** Exactly the possessions the engine marked, at the size the side's instruction gives
them (`FilmCounter`): without the instruction one forward holds as an outlet 24 m ahead of the ball and one
midfielder runs 10 m ahead, for the first three beats, and the defenders press as usual; with it two forwards hold
40 m ahead, two midfielders run 18 m ahead, for six beats, and the defenders drop and close up instead of chasing.

**6. Versions.** `EngineVersions.Engine = 11` (`engine-v11`), `RuleSet = 10` (`engine-rules-v10`). The canonical
serialization gains `counterAttack` per side and the rules gain fourteen constants, so the golden input and output
hashes, the rules hash and the events pin are re-pinned. The golden match is still 2-2.

## Evidence

- **The shares are the ones asked for.** Over 300 evenly matched matches, 20.3% of 11,613 regained balls were
  counters for a side that had not asked, and 50.2% of 11,638 for a side that had.
- **A balanced opponent sees no change.** Over 3,000 matches goals per match are the same with and without the
  instruction against a balanced side (+0.000), and calibration is unchanged: 2.894 goals a match over 1,500 fixtures
  (target 2.50 to 3.00), home advantage 3.47 points.
- **It works against an attacking side and backfires against a defensive one.** Over 3,000 matches a side that
  plays on the counter scores about 0.24 more a match against an attacking mentality with a high line (goal difference
  +0.20), and against a defensive mentality with a deep line it scores about 0.11 fewer and concedes about 0.06 more,
  a goal difference of −0.165. The first version of the rule was worth +0.07 and −0.09.
- **The suite.** The engine suite passes with the hashes re-pinned, and the new tests pin the shares, that only a
  regained ball is ever a counter, that the edge rises with the opponent's commitment, and the price.

## Consequences

- The effect is a clear one, as a tactic should be, and bounded (`INS-9`): about 0.24 goals a match for a side that
  breaks on an attacking opponent and a goal difference of about 0.17 against it when it breaks on a deep one. The
  posture steps, the recovery steps and the two backfire constants are the knobs.
- Stored `engine-v10` matches cannot be re-simulated, as with every earlier version.
- A counter is not yet different in where the ball goes or how long the possession lasts; it moves the thresholds
  and the replay shows it. Making it a faster, more direct passage is open.
