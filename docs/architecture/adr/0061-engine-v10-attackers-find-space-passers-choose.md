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

## Consequences

To be completed at the later gates with the measured tables.
