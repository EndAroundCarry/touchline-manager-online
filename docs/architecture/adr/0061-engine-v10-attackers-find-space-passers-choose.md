# ADR-0061: Engine-v10 lets attackers find space and passers choose who gets the ball

- **Status:** Proposed
- **Date:** 2026-10-05
- **Stage:** Engine roadmap, individual play
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0013](0013-engine-arithmetic-and-scoreline-effect.md), [ADR-0058](0058-engine-v8-pass-focus.md), [ADR-0059](0059-engine-v9-pass-focus-moves-shots-and-crosses.md)

## Context

The engine is a possession model. Team ratings decide whether a possession progresses, creates a chance or
breaks down, and the ball's path is drawn first (`PassagePlanner.Approach`). Players are decoration picked
afterwards by one weighted draw each: the carrier, one passer, the shooter. There is no pass with a named passer
and a named receiver, and nothing reads where a player stands.

Three things follow, and the first two are what a manager can see:

- **Positioning** helps a defender win a ground duel and feeds two team ratings. An attacker's Positioning never
  helps him get the ball, shoot or win a header.
- **Vision** and **Decisions** only feed team ratings (Vision also the assist credit). No individual choice reads
  them.
- The replay picks each receiver as the teammate who can reach the point soonest, with no role filter, so a
  defender can receive in the attacking half.

### Baseline, engine-v9 (measured before any change)

20,000 evenly matched fixtures; the off-ball probe (`tools/simulation-benchmarks -- offball 4000`) reads the
film's own "receive" marks over 1,000 matches.

| Reading | engine-v9 |
|---|---|
| Goals per match | 2.898 |
| Shots per match | 27.22 |
| Home possession | 52.10% |
| Fouls, yellows, reds per match | 21.37, 3.40, 0.28 |
| Penalties per match | 0.241 |
| Matches with 7+ goals | 2.79% |
| Film length (p05 / p50 / p95) | 9.90 / 10.13 / 10.41 min, never above 11:00 |
| Receptions per match in the film | 503.9 |
| Receptions past halfway taken by a Defence player | 31.7% |
| ... of those, beyond x 6,500 of 10,000 | 17.1% |
| Twin strikers, Positioning 18 against 4: shots per match | 1.470 against 1.491 |
| Twin strikers, Positioning 18 against 4: goals per match | 0.144 against 0.150 |

The twin strikers are the user's complaint in numbers: with Positioning 18 against 4 and everything else equal,
the two take the same shots and score the same goals. And nearly a third of what the film shows being received
in the attacking half goes to a defender.

## Decision (proposed, in milestones)

Each milestone ends in a gate at which the measured results are shown and the user decides whether to continue.
Engine 10 and rules set 9 span all of them; the golden hashes are re-pinned at each commit while v10 is unreleased.

1. **Positioning at the finish.** A bounded Positioning edge multiplies the weight of the shooter and the header
   winner, the corner marker, and joins the aerial duel as a fourth term.
2. **An off-ball model**, integer-only: where attackers and defenders stand for a given ball position, how open a
   point is, and a depth rule (a pass back to a midfielder is fine under pressure; a pass back to a defender while
   attacking near the opponent's box is not).
3. **A receiver for each pass of the approach**, chosen by the holder from what he sees (Vision) and how well he
   weighs it (Decisions), with the planned lane and the pass focus kept and the point pulled partway to the
   receiver. Passes, passers and assists are credited to the people who made them. Drawn from a separate
   stream, so no event moves.
4. **The chain drives the outcome**, and an open-play cross now gives a headed chance, which supersedes the v9
   statement that a cross does not change the chance that follows it. Recalibrated to about 2.90 goals.
5. **Solo play**: the holder may dribble on or shoot from distance, by Decisions.
6. **Replay polish** and the final documentation.

Rules the design holds to: no `double` in the simulation (ADR-0013); a fixed number of draws per decision; every
new constant lives in `EngineRulesV2`, validated and hashed; a shipped engine version is never edited in place.

## Milestone results

### M1: Positioning at the finish (built)

`PositioningEdge` (`Ratings/`) is linear in effective Positioning from `PositioningFloorBasisPoints` = 7,000
to `PositioningCeilingBasisPoints` = 13,000, neutral at 10.5. It multiplies the weight of the open-play shooter
(Finishing) and of the corner header (Heading), and of the defender marking the corner (Heading times an edge from
the mean of Marking and Positioning). Positioning is a fourth term of the aerial duel
(`AerialDuelPositioningWeight` = 2). No draw is added.

| Reading | engine-v9 | M1 |
|---|---|---|
| Goals per match (20,000) | 2.898 | 2.899 |
| Shots per match | 27.22 | 27.22 |
| Home possession | 52.10% | 52.10% |
| Fouls, yellows, reds per match | 21.37, 3.40, 0.28 | 21.37, 3.40, 0.28 |
| Penalties per match | 0.241 | 0.241 |
| Matches with 7+ goals | 2.79% | 2.80% |
| Twin strikers (Positioning 18 vs 4), shots per match | 1.470 vs 1.491 | 1.704 vs 1.151 |
| Twin strikers, goals per match | 0.144 vs 0.150 | 0.167 vs 0.117 |
| Goals per match in the twin fixture, both sides | 2.928 | 2.932 |

The golden match is still 2-2. The film is untouched, so the defender-receives reading (31.7%) is unchanged.
Not measured separately: corner headers by tier (they are inside the twins' shot counts).

### M2: The off-ball model (built, nothing calls it)

`OffBallModel` (`Simulation/`) is pure and integer-only, with `SpatialMath` (`Spatial/`) for the integer square
root and distances (ADR-0013). It places a side for a given ball position, and reads off a point:

- **Spots:** from `TacticalFormationResolver`, attackers with the ball and defenders without, per pass because
  the block shifts with the ball.
- **Openness** (0..10,000): the nearest defender's distance, each defender's Marking and Positioning (the M1
  defender edge) pulling him nearer or pushing him further, times the receiver's own Positioning edge; full at
  `OffBallOpennessFullDistance` = 1,200 (about 12 m). The goalkeeper marks nobody.
- **Reach**: `OffBallReachDistance` = 3,000 times the receiver's Positioning edge; and a reach score.
- **Progress**: forward gain on the side's own scale, full score at `OffBallProgressFullGain` = 2,000.
- **Pressure**: a defender within `OffBallPressureDistance` = 600.
- **Depth rule**: behind the holder by up to `BackPassFreeDepth` = 500, anyone; up to `BackPassMaxDepth` = 2,500,
  only a midfielder or attacker and only under pressure; beyond that nobody. A defender is a receiver only while
  the holder is at or short of `DefenderReceiveMaxHolderX` = 4,500. The goalkeeper is never a receiver here.

No behaviour moves: the golden match is 2-2 and the whole suite passes. The seven new constants change the rules
hash, and with it the input and output hashes, which are re-pinned.

**Finding: the formation resolver does not mirror its shifts for the away side.** `ResolvePosition` adds the ball,
mentality, line and phase shifts to X with the same sign for both sides, but the away side attacks towards the low
end of the pitch, so an attacking away block is pulled back instead of pushed up. The film reads it as it is, so
away shapes in the replay lean the wrong way. M2 does not depend on it (`OffBallModel.Place` resolves the away side
in the home frame and flips it back, and a test pins that both sides move up the pitch with the ball); fixing the
resolver itself changes the film and belongs with the replay changes of M3, which bump `ReplayDirector.Version`.

### M3: A receiver for each pass, credited to the people who made it (built)

`ReceiverChooser` (`Simulation/`) puts a named player at each end of every leg of a possession's approach. For each
leg the holder is the previous receiver (the carrier at the start):

- **Seen.** A teammate is seen when a draw is under `SeeChance`: the holder's effective Vision, from
  `ReceiverSeeLowestBasisPoints` = 3,500 to `ReceiverSeeHighestBasisPoints` = 9,800, less up to
  `ReceiverSeeDistancePenaltyBasisPoints` = 3,500 for a teammate `ReceiverSeeFullDistance` = 6,000 or more away.
- **Eligible.** He can reach the point he would take it at (`OffBallReachDistance`, now 4,000: measured over the
  point the ball lands at, which a pull has brought towards him) and the M2 depth rule allows him.
- **Score.** Openness, progress, reach and lane fit, weighted 4/3/2/2 (`Receiver*Weight`). The lane fit is the share of
  the pass focus's lane the receiver stands in, over the favourite lane's, so a manager's lanes still steer the ball.
- **Choice.** One weighted draw, the weight `100 + score² × gain`, where the gain runs from
  `ReceiverChoiceGainLowest` = 2 (nearly flat) at Decisions 1 to `ReceiverChoiceGainHighest` = 24 (the best
  placed is about 25 times likelier than the worst) at Decisions 20.
- **Pull.** The planned touch moves `ReceiverPullBasisPoints` = 2,500 of the way towards the receiver's spot, but never
  out of the lane it was planned in, so the v8/v9 lane shares are untouched. The last touch never moves, and neither
  does the point a ball is lost at, so the pressure point, free-kick range, turnover and offside logic stand where the
  plan put them.
- **Nobody to play it to.** The holder keeps the ball for the leg (a carry). That is rare (2.6% of legs, 0.10 a
  possession; 4.4% for a holder of Vision 1, 0.8% for one of Vision 20) and is only an artefact until M5 gives the
  choice its logic.
- **A defender is a receiver only** while the holder is at or short of `DefenderReceiveMaxHolderX` = 4,500 **and**
  where he takes it at or short of `DefenderReceiveMaxPointX` = 5,000 (new in M3; the M2 rule on the holder alone let
  a long ball from the back reach a centre half beyond halfway).

The draws come from a stream derived from the seed and the possession's ordinal with its own stride (1,000,037), with a
fixed number per leg (one for each outfield player, then the choice). It never touches the play stream. The recorded
possession is now written in the order it happens (the passer's `Pass` or `Cross`, the ball's waypoint, the receiver's
`Receive`); the film resolves a `Receive` to the holder of the station and shows the engine's receiver, and
`ReplayDirector.Version` is `replay-v5`.

**Credit.** `PassTally` credits each leg to its passer; a pass after the approach (the creating pass, the one the
defence stopped, the ball played in for a penalty) is the player on the ball at the end of the chain's. The lost pass
is the passer's of the lost leg. `AssistPlanner` credits an open-play goal to the player on the ball at the end of the
chain, or to the one who passed to him when he took the shot himself; a set piece keeps its weighted draw.

**Finding: the chain is the story of the outcome, not its cause.** The play draws decided whether a possession was
lost before the chain was written, so a better passer is not yet less likely to lose the ball. The credit the chain
replaced made that link (a lost pass was drawn against Passing), and a test pins it. The chain keeps it by
conditioning: the receivers are weighted by Passing, and on a lost possession the player who held the ball for the
lost pass is weighted by what is left of Passing instead, which is the same odds the old credit drew with. Passing
making a pass fail less is M4's.

| Reading | engine-v9 | M3 |
|---|---|---|
| Events and scorelines of 40 matches | | unchanged (pinned hash) |
| Goals, shots per match (20,000) | 2.898, 27.22 | 2.899, 27.22 (as M1) |
| Receptions past halfway by a Defence player, from the engine's own `Receive` touches | not recorded | 0.0% |
| Receptions past halfway by a Defence player, from the film's marks | 31.7% | 9.0% |
| Legs the holder kept | | 2.6% of legs, 0.10 per possession |
| Film length (p05 / p50 / p95) | 9.90 / 10.13 / 10.42 min | 9.90 / 10.12 / 10.42, never above 11:00 |
| Teleports outside cuts | 0 | 0 |
| Film pace p50, inside the 1.8-2.9x band | 2.54x, 97.9% | 2.64x, 92.8% |
| Moves lengthened for constraints | 18.4% | 19.7% |
| Passes attempted per match, completion by family (Defence / Midfield / Attack) | | 622; 90% / 77% / 77% |

Mean openness of the player the first ball goes to (0 to 10,000; 4,000 possessions a tier, the holder's other skill
at 20):

| Tier | 1 | 5 | 10 | 15 | 20 |
|---|---|---|---|---|---|
| By the holder's Decisions (Vision 20) | 7,319 | 7,420 | 7,495 | 7,516 | 7,538 |
| By the holder's Vision (Decisions 20) | 7,305 | 7,387 | 7,483 | 7,518 | 7,538 |

Both rise with the tier; the Vision effect is the smaller one in a possession's outcome, because it can only widen the
choice, and it is the Decisions that pick well from it. Vision also decides how often a holder keeps the ball because he
saw nobody (4.4% of legs at Vision 1, 0.8% at 20).

The 9.0% left in the film is not the chain: it is the carrier of the final-third ground duel, drawn by the play stream
(21.5% of those carries past halfway are a Defence player's), which the film shows receiving the ball at the entry
point. Naming him from the chain moves a play draw, so it is M4's.

The film pays a little for named receivers: the one the engine names is no longer always the one who can get there
soonest, so a few more beats are stretched and the pace settles about 0.1x higher. It stays inside its band for
93% of matches (the test asks for 90%). Quiet possessions the film condenses still merge their ground moves into one
pass by design, so their intermediate receivers are not shown; every possession with a chance in it, and the two before
it, keeps all of them.

## Consequences

To be completed at the later gates with the measured tables.
