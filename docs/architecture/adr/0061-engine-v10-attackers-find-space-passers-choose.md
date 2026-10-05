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
   statement that a cross does not change the chance that follows it (ADR-0059 now says so). Recalibrated to about
   2.90 goals.
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

### M4: The chain drives the outcome, and a cross is headed (built)

**The chain is played before the progression roll**, because how the ball was played up the pitch is now part of
what the roll weighs. It draws from its own stream, so playing it early moves no play draw, and a possession the
attack loses on the way is played again as far as it got, with the lost pass known (the legs before the lost one
come out the same). Three readings of it nudge the two probabilities the ratings used to decide alone:

- the openness of its **weakest** pass over the approach moves the chance the attack **progresses**
  (`ChainProgressSwingBasisPoints` = 700 either way, about `ChainWeakestOpennessReference` = 5,200);
- the openness of the **last receiver** (600 about 6,900) and the mean **score of the receivers the holders chose**
  (500 about 6,200) move the chance it **creates a shot**.

The references are the measured means of 49,000 possessions, so the nudges move who creates chances and not how
many. They are bounded well under the ratings' own swings (700 against 2,400 for progression; 1,100 against 2,800
for creation), and a test pins that.

**One leg further: the pass into the final third.** The first build named the ground duel's carrier from the chain
(the M3 finding said that was M4's). The holder then carried the ball from the pressure point to the entry point on
foot, and the film, which moves a carrier at running pace where it moves a pass at the speed of the ball, got 10%
more real-time motion: the pace went from 2.65x to 2.91x and only 48% of films stayed in the 1.8-2.9x band. The
fix is the natural one: the chain runs one pass past the approach, to the entry point, which only a progressing
attack plays. The holder chooses who gets it there like any other pass, and that player fights the duel, is the
favoured shooter, and puts the cross in. The pass is not counted in the tally, as before. When nobody is eligible
the holder carries it in, and the duel's carrier is drawn as it always was (a defender can be that carrier: 21.5%
of the duel's carries past halfway at M3, and this fallback is rare). The approach's own legs are unchanged by it.

**The shooter** is the same single weighted draw, with the weight of the player the ball was played in to
multiplied by `ShooterChainBonusBasisPoints` = 25,000 on top of Finishing and his Positioning edge. The ball that
created the shot is credited to the player who has it, or, if he takes the shot himself, to the man who played it
in to him (`PossessionPassing.CreatorFor`); `PassTally` credits the creating leg the same way for shots that do
not score. The bonus is not what makes Positioning matter so much (see below): trying it mid-milestone, the twin gap was 1.9x
at 15,000 and 2.1x at 25,000.

**A cross is a header.** When the approach ended in a cross and creation succeeded, the man the ball was played in
to puts it into the box (a `Cross` waypoint at `HeaderPoint`, at `HeaderAltitude`) and the chance is an aerial duel
between the best-placed attacker (Heading, his Positioning edge, and how near the formation puts him to the box;
never the crosser) and the defender the formation puts there. The duel is the corner's, with the attacker
`CrossHeaderAttackerBonus` = 13,000 ahead (the attacker wins 75.5% of them over 600 matches, 7.4 crossed chances a match) and the crosser's Crossing
above 13 adding to it. A won header is the shot, from the box, in the zone the possession was planned for, so the
shot-zone shares of ADR-0059 stand; a lost one creates nothing, the cross is cleared, and there is no corner from
it (a corner after a lost cross would put two header contests in one passage). A cross is a chance more readily than
a ground ball, so a crossed approach's creation chance is multiplied by `CrossCreationMultiplierBasisPoints` =
12,300, which keeps the shots from crosses at what they were: 26.9% of open-play chances come from a crossed
approach, 24.2% of all open-play chances are now headed.

**Finding: Finishing is worth less.** A header is decided by Heading, so the quarter of open-play chances that are
headers no longer read Finishing. Over 3,000 matches a side of 20-Finishing players scores 15.6% more than a side
of 6s, where it scored 23.5% more at M3; `Finishing_counts_when_the_shot_is_taken` asked for 15% on 200 matches and
now asks for 5% (the 200-match sample is worth about 9%).

**Finding: position-aware picks, because the formation moves the whole block with the ball.** The formation resolver
pushes defenders up with the ball, so by the formation alone a centre half is "near" a ball in the other box. Three
places picked a player with no regard to where he stood and put a defender at the ball in the attacking half: the
carrier who begins the possession (a rebound or block that stays with the same side starts it there), the two players
who contest a loose ball, and the man who heads a cross. They now weight by Dribbling, pace or Heading times how near
the formation puts him to the ball (`ReachWeightFloorBasisPoints` = 500 at the limit of his reach, all of it at the
ball), and a defender is held to the floor for any ball beyond `DefenderReceiveMaxPointX`. The draw count is the
same. The duel's carrier, drawn by the play stream before, is the receiver of the entry pass.

| Reading | engine-v9 | M3 | M4 |
|---|---|---|---|
| Goals per match (20,000) | 2.898 | 2.899 | 2.898 |
| Shots per match | 27.22 | 27.22 | 27.17 |
| Home possession | 52.10% | 52.10% | 52.11% |
| Fouls, yellows, reds per match | 21.37, 3.40, 0.28 | 21.37, 3.40, 0.28 | 21.38, 3.41, 0.29 |
| Penalties per match | 0.241 | 0.241 | 0.244 |
| Matches with 7+ goals | 2.79% | 2.80% | 2.97% |
| Passes attempted per match | | 622 | 626 |
| Passes completed, Defence / Midfield / Attack | | 90% / 77% / 77% | 92% / 77% / 77% |
| Share of passes by Defence / Midfield / Attack | | 14.4 / 56.0 / 29.6% | 9.8 / 58.2 / 32.0% |
| Open-play chances that are headed | 0% | 0% | 24.2% |
| Defender carries the ball past halfway (the engine's touches) | | 21.5% | 3.4% |
| Defender receives past halfway, as the film shows it | 31.7% | 9.0% | 3.5% |
| ... of which the chain's receivers | | 0.0% | 0.0% |
| Film length p05 / p50 / p95 (10,000) | 9.90 / 10.13 / 10.41 | 9.90 / 10.12 / 10.42 | 9.91 / 10.13 / 10.41, max 10.76 |
| Teleports outside cuts | 0 | 0 | 0 |
| Film pace p50, inside 1.8-2.9x | 2.54x, 97.9% | 2.64x, 92.8% | 2.65x, 90.1% |
| Moves lengthened for constraints | 18.4% | 19.7% | 20.4% |
| Golden match | 2-2 | 2-2 | 3-2 |

The 7+ goals share is the one reading that moved against its limit (3.0%): it is 2.82-3.05% over 10,000-match runs
and 2.97% over 20,000. The 3.5% of film receptions left to a defender past halfway are in the passages the film
fills in itself (the header at a corner, a free kick's delivery, a clearance): the film picks the receiver of a
ball the engine does not name, and that is the replay milestone's. The engine's own receivers are 0.0%.

Positioning and the mind, over 4,000 matches:

| Reading | engine-v9 | M1 | M3 | M4 |
|---|---|---|---|---|
| Twin strikers, Positioning 18 against 4: shots per match | 1.470 against 1.491 | 1.704 against 1.151 | 1.718 against 1.156 | 2.225 against 0.890 |
| ... goals per match | 0.144 against 0.150 | 0.167 against 0.117 | 0.172 against 0.120 | 0.235 against 0.095 |
| Whole home side Vision and Decisions 4 against an even side, goals | | | 1.348 - 1.467 | 1.329 - 1.446 |
| ... 13 | | | 1.585 - 1.348 | 1.586 - 1.301 |
| ... 18 | | | 1.759 - 1.293 | 1.765 - 1.222 |

Shots and goals by position family, with role-shaped squads (Finishing 16 up front, 11 in midfield, 6 at the back,
Heading 13, 11, 14), Defence / Midfield / Attack: 23.0 / 45.0 / 32.1% of shots at M3, 19.5 / 49.8 / 30.7% at M4. A
striker takes 15% of the shots, a midfielder 12% and a defender 5%, the defenders' mostly from set pieces, which still
draw from the whole side.

**Finding: Positioning now counts three times, and the gap at the extremes is 2.5x.** It lifts the shooter's weight
(M1), it makes a player likelier to be found (his openness and reach in the receiver's score, M2) and likelier to be
the one at the ball (the header and the opening carrier). The twin strikers at 18 and 4 are the extreme of the
scale; the gap in a realistic squad (say 14 against 8) would be about 1.5x, read off the same curve. The knob, if it is
too much, is the floor and ceiling of the edge (`PositioningFloorBasisPoints`, `PositioningCeilingBasisPoints`):
8,000 and 12,000 would make it about 1.8x at the extremes, by the same estimate. Vision and Decisions help the side by what they do to the receiver (the +0.54 goal
difference at 18 against +0.47 at M3, and the 4-side is no worse), and the effect stays bounded by the nudges.

## Consequences

To be completed at the later gates with the measured tables.
