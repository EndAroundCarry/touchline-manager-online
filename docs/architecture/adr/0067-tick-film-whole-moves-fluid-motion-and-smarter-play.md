# ADR-0067: The tick film shows whole moves, the players keep moving and the ball is played with a purpose

- **Status:** Accepted
- **Date:** 2026-10-11
- **Stage:** Engine roadmap, `the-engine-and-the-partitioned-harbor.md` Milestones 0–8
- **Related:** [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0004](0004-deterministic-match-engine.md), [ADR-0052](0052-replay-v3-film-and-reel.md), game rules `MAT-*`

## Context

[ADR-0066](0066-engine-v12-discrete-tick-match-engine.md) made the match a recording of what twenty-two players did. Watching that film the manager found
five faults: a highlight showed only the 15 to 18 s before a chance, so how the move began and who lost the ball were cut off; the receiver of a pass
stood and waited for a ball that crawled in, and held it a moment before playing it; the players ran 5 m, stopped, and ran 5 m, all together, like pieces on
a board; tokens sat on top of one another; and the ball was played without regard for who was waiting for it.

Measuring first (Milestone 0) corrected part of that diagnosis. A token is about 2 m across (not 1.3 m); the median hold of a ball was already 3 ticks, so
"the ball sits at his feet" was a slow ball tail (55% of receptions followed a ball under 4 m/s for over a second) and a receiver standing at the spot where
the ball would stop, not a long hold; and the film already contained the regain in 94% of goals, so the longer film is for context and not for a missing start.

## Decision

**1. A highlight is the whole move (`tick-replay-v2`).** `TickMoveFinder` walks back from a goal or a good chance while the same side keeps the ball (loose-ball
gaps and deflections allowed) to the regain, plus 3 s, capped at 40 s and snapped to a pass. The least lead is 18 s for a goal and 15 s for a chance (the plan's
10 s would have made most highlights shorter than before). The film is 16 to 20 minutes at twice the pace; players are sampled every 50 to 300 ms. These are
`HighlightOptionsV1` options of the tick engine only (`TickMin/MaxFilmMilliseconds`, `TickMaxPassages`, `TickPlayerSampleIntervals/Tolerances`,
`TickPayloadBudgetBytes`), so the possession engine's film and its golden hashes do not move. The presentation response is Brotli or Gzip compressed (the
JSON is about 7 MB and about 1.1 MB compressed). Chances and set pieces alone make about 5:50 of film, so about ten minutes of the sixteen are spells of play
at the ends of the pitch.

**2. The film says who erred.** The loop stamps four blame tags in the recording only (`Beaten`, `Dispossessed`, `Misplaced`, `Bypassed`; no draw, no state);
the synthesizer ranks and narrates them and the viewer draws a ring or a chevron over the token for 1.5 s of film.

**3. The viewer draws a smooth, uncovered pitch.** Players follow a cubic Hermite spline with planar tangents (zero only at a real stop) in place of a per-axis
monotone spline, and the renderer pushes tokens at least 1.45 radii apart for display only (positions in the film are untouched).

**4. The ball flows.** A receiver goes to the point he can reach the ball (`TickInterception.EarliestReach`), not to where it will stop; he decides while the
ball is still in the air; the first touch is cushioned (the ball eases from where it arrived to a touch 1 m ahead, then to the dribbling offset, while the carrier
controls it throughout). Meeting the ball removed the slow tail without touching rolling friction (the plan's change to the deceleration was conditional on
the slow tail and was not needed). The faster game was brought back to the bands with `CurvePercent` 38, `FoulScalePercent` 250 and `HighPressLimit` 7 800.

**5. The players keep moving.** The team shape follows a smoothed, anticipating reference to the ball and blends between its in-possession and
out-of-possession forms over 1.5 s; a man holding his place walks about it; attackers search for room and defenders keep adjusting goal-side; and a man who is
not in a duel keeps 2.5 m from an opponent (twice the strength of the teammate push). Every rule is integer and a hash of tick and seat, never a new draw.

**6. The ball is played with a purpose.** A pass is judged on the pressure waiting for it when it arrives, goes to the safe side of the receiver, is a through
ball aimed at the meeting point of ball and runner, may switch the play when the near half is crowded, and a carry into ground the defence is giving up earns a
bonus (Milestone 6); a pressed man's one-two, the third man's run and the cut-back to the penalty-spot zone are played on purpose (Milestone 7,
`TickCombinations`).

**7. The label stays.** `engine-v12` has not been released, so it keeps its label and the golden hash in `TickEngineTests` was re-pinned at each
engine milestone. Matches stored under an earlier build of engine-v12 refuse to replay (MAT-9) and new matches must be played.

## Consequences

- The numbers over the stored snapshots (205 matches) and over 3 000 matches are in [`match-engine.md`](../../product/match-engine.md) §14.5 and §14.16:
  goals 2.68 and shots 27.6 a match (bands 2.6–2.9 and 22–28), 84% pass completion, 3.0 yellow cards; on target (31%) is just under its band.
- A match now costs more to play (the steering and the combination runs); see §14.16 for the time.
- The film is about twice the payload of the first one; the API compresses it, and the p95 estimate is within the 2 750 KB budget.
- Tuning remains a rules-version change: any constant in §14.4 moves the golden hash.

## Not decided here

- The target of 9.5–11.5 km per outfield player: after Milestone 5 it stands at 13.8 km (it was 13.6 km at the baseline, and 16.0 km when Milestone 5 began),
  so the lever has not been reached.
- Opponents under 1 m outside duels (5.4% of open-play frames) are men crossing, not men stacked; a shorter figure needs the presser's own overlap handled
  in the viewer.
- Backward balls (`Recycle`, about 1 100 a match) are not followed as passes by `AfterKick`.
