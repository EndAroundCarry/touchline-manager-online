# ADR-0066: Engine-v12 plays a match as a discrete tick simulation, and the possession engine is kept for the matches it played

- **Status:** Accepted
- **Date:** 2026-10-09
- **Stage:** Engine roadmap, `tick-based-match-engine-plan.md` Milestones 0–9
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0052](0052-replay-v3-film-and-reel.md), [ADR-0063](0063-engine-v11-counter-attack.md), [ADR-0064](0064-replay-v6-shape-and-set-pieces.md), game rules `MAT-*`

> The plan numbered this decision ADR-0055; that number was taken by [ADR-0055](0055-engine-v6-skills-where-the-design-says.md) before the work
> reached Milestone 9, so it is recorded as the next free number.

## Context

Engines v1 to v11 resolve a match in possessions: a possession is won, progresses, creates a chance and ends in a duel, each a probability read from
team ratings. To draw the film the replay then builds the movement afterwards (`PassagePlanner`, `ReplayDirector`, `FilmScript`, `FilmMotion`), so the
dots are put where the result says the ball went. The film is never what decided the match. Its movement is bursty (a dot sprints to a touch point and
waits), the players behind the ball do not defend or support, and a shot is drawn to match a dice roll, not found by the geometry.

The request was a match in which the dots are the match: twenty-two players and a ball moving every tenth of a second, with a defence that holds a shape
and a carrier who chooses, so that the film is a recording of the play and not a story told over a result.

## Decision

**1. A tick engine, `engine-v12` / `engine-rules-v11`.** `TickMatchEngine` plays the whole match at 10 Hz (100 ms a tick, about 54,000 ticks) with
`TickMatchLoop`. The players, the ball and the clock are fixed-point integers (a pitch unit is 1,000 fixed units, headings are 1,024 to the turn and the
sine is a literal quarter table), the only randomness is the snapshot's `Pcg32`, and nothing reads a clock, a database or `Random`, so a match is
reproducible byte for byte on any processor. The simulation types hold no `float`, `double` or `decimal` in a field, a signature or a local, and a test
says so (`TickEngineTests`).

**2. What a player is.** The loop is built from the parts of Milestones 1 to 8, each a pure function over spans so that a tick allocates nothing:
ball and body physics (`TickBallPhysics`, `TickPlayerPhysics`), shape and steering (`TickTacticalGeometry`, `TickSteering`), defence — one presser, cover
shadows, markers in the own third, a line that steps up and drops and an offside judge (`TickDefensiveAI`, `TickTackleResolver`), attack — support
triangles, runs behind the line, pockets and overlaps (`TickOffBallSupport`), the carrier's choice among shoot, pass, through ball, cross, dribble, shield,
recycle and clear by expected value (`TickBallCarrierBrain`), the goalkeeper's arc, rush and save (`TickGoalkeeperAI`, `TickShotStopper`), and every
restart (`TickMatchStateMachine`, `TickSetPieces`). Cards, injuries, substitutions, ratings and live metrics are the possession engine's own rules,
called from the loop.

**3. The film is a recording.** The loop records every tick when a `MatchPassageRecorder` is attached (`TickMatchRecording`), and
`TickFilmSelector` and `TickReplaySynthesizer` cut the recording into the same `MatchPresentationV1` the viewer already reads (`tick-replay-v1`): a
two-times-pace selection of about ten minutes (the kick-offs, every goal with its build-up, cards, the best chances, corners), joined by cuts. Recording
changes no draw and no state: the output hash is the same with and without it. The Angular viewer is not changed.

**4. The possession engine stays.** `LegacyPossessionEngine` wraps `PossessionSimulator` unchanged and the registry resolves `engine-v11` to it, so a
snapshot frozen against it is still re-derivable (ADR-0004). `engine-v12` resolves to the tick engine, which is now the default. A snapshot names the
engine it was frozen for and `MatchSimulator` refuses any other.

**5. Calibration (Milestone 9).** The loop was first built against a single geometry: a 4-4-2 against a 4-4-2 of uniform players, which it scored
nearly right, while the games' stored matches (other formations, real attributes, random instructions) scored about 28 goals and 245 shots a match.
The calibration therefore uses three yardsticks and not one — the stored matches of the development database, every one of the thirteen formations
against every other with uniform players, and abilities from 8 to 19 and each instruction at its extremes — and changes only things that have a
football meaning:

- *Interception is a contest.* A defender who reaches a pass in flight no longer takes it by arriving; he rolls once per pass, on his Anticipation and
  Positioning against the passer's Passing and Vision, and no opponent can reach the ball in the first 0.3 s. A defender who fails the roll has had his chance and
  lets the ball go by. Passes travel at a firm 10 m/s on arrival.
- *A tackle is not offered on every tick of contact* (a commitment roll on Aggression), and a foul in the penalty area is given as a penalty one time in
  ten; the rest is play on. A goalkeeper's foul is judged the same way.
- *A shot is taken from a decent chance* (a heuristic of 18.5% or better), is aimed less finely, and a blocked shot goes behind for a corner nearly half the time. The goal is 7.32 m wide, as the viewer draws it; the possession engine's 10 m mouth is kept for the matches it played.
- *Skill has a curve.* A player's attributes count 60% of their distance from 13, so a side of stars is better than a side of journeymen by a margin and
  a side of specialists does not out-shoot the whole league. The goalkeeper moves and dives on his Reflexes as much as his Pace and Agility.
- *Players keep out of the six-yard box* (a defender's shape position is at least 8 m from his own goal line and no player's is nearer than 7 m to
  the opponents' goal line), and a ball that crosses the line off the attackers' last touch without a shot is a goal kick, not a goal.
- *A carrier misjudges the offside line* on a share of passes that falls as his Decisions plus Anticipation rise, by a fixed function of where the two
  men stand (no draw).
- *Home advantage* is the snapshot's ratio (+4.2%) added to the home side's chance in every tackle and interception.
- Set pieces are struck unhurried (a smaller error than an open-play shot).

**6. Cost.** A match plays in about 0.3 s on one core (0.4 s with the recording and the live metrics). The plan's 35 ms was a guess before the loop
existed; the figure is dominated by the 22 players' steering and the carrier's decisions. The integer square root, the hot spot, now starts at the power
of two above the root and takes four steps instead of thirty (the answers are the same to the last digit, so no result moved).

## Consequences

- Every new match is a tick match; matches already played keep their stored result and are replayed from their snapshot only while the snapshot names an
  engine this build can still run (`engine-v11` is kept for that).
- The live balance is the one measured in [`match-engine.md`](../../product/match-engine.md) §14. Over the stored matches the engine gives 2.8 goals and 28
  shots a match, 38% of the shots on target, 76% pass completion, 4.0 yellow cards, and home / draw / away of 42 / 24 / 34 %. Over all thirteen
  formations with uniform players it gives about 2.3 goals and 26 shots: the formations are not alike (a lone striker or three forwards shoots about
  twice as often as two strikers), which is a property of the shapes and not of the calibration, and the manager can see it.
- Tuning is a rules-version change from now on: any constant named above moves the golden hash pinned in `TickEngineTests`, which must be re-pinned with
  a new `engine-rules` label (ADR-0004).
- The film is shorter than the match and at twice its pace; a longer or live film is an option the recording already supports.

## Not decided here

- Whether a tick match should keep the possession engine's `engine-rules-v10` hash; the snapshot carries `engine-rules-v11` and the tick engine reads the
  same `EngineRulesV2` constants for discipline, injuries, substitutions and ratings.
- Condition and substitutions: the loop drains each player's energy tick by tick for his speed, but the match's condition, ratings and substitutions
  are still the possession engine's minute-by-minute rules. Joining the two is an engine-version change of its own.
