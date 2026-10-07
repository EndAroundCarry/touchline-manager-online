# ADR-0064: Replay-v6 makes the players hold a shape, leaves the ball carrier room, and sets pieces for what they are

- **Status:** Accepted
- **Date:** 2026-10-07
- **Stage:** Engine roadmap, replay (film layer only)
- **Related:** [ADR-0054](0054-replay-v4-constant-pace-film.md), [ADR-0061](0061-engine-v10-attackers-find-space-passers-choose.md), [ADR-0063](0063-engine-v11-counter-attack.md), game rules `MAT-8`

## Context

The 2D viewer placed players badly around the ball. When a player ran with it, five or six dots stood within a metre
or two, teammates and opponents mixed, and neither side kept a recognisable formation. A corner flew out of play with
nobody touching the ball; every free kick had the same wall and three attackers whatever the distance or angle; and
a penalty put nine players inside the 9.15 m arc. The reference was fifteen Football Manager 2D screenshots (open
play, wing attacks, low and mid blocks, long balls); throw-ins are out of scope.

All of it is the film layer (`src/TouchlineManager.MatchEngine/Highlights/`), which turns a match that has already
been played into keyframes. Verified in code before the work began:

1. `FilmShape.FillOpen` pulled one centroid per side to the ball (0.40 along, 0.30 across), so the whole block
   collapsed onto it; there were no lines, no minimum spacing and no low block or mid block.
2. `FilmMotion` sent two pressers (1.5 m and 4 m from the ball) and one supporter 4 m ahead; the nearest free player of
   any position took each role.
3. `ArrangeCorner` was a fixed table, not mirrored for the flag, ordered by position family only.
   `ArrangeFreeKick` and `ArrangePenalty` ignored where the foul was, and the penalty stood everyone 8.5 m from the
   spot. `FormationMode.GoalKick` had no case.
4. A corner is only ever created by `PossessionSimulator.ResolveFailedCreation`, never from a saved or blocked shot,
   so the film had nothing to show as its cause.

## Decision

**1. Film only.** The simulation, the canonical output, the golden hashes and the calibration do not move. Corners
are not added or removed; only how one is shown changes. `EngineVersions` is unchanged. A real "corner after a save"
would need a new draw and a recalibration and is not done here; the film shows the cause of the corner the engine
already decided.

**2. The block stands as lines, by phase (`FilmShape.FillOpen`).** Each side is grouped into lines by the **depth of
its slot anchors** (back, midfield, front), not by position family: in the domain 4-4-2 the wingers are attackers
by family but stand 25 m behind the strikers. Each side takes a phase from the ball and possession: build-up,
attack, low block, mid block or high press. The ball moves the block by 0.15 of its offset along the pitch (it was
0.40) and 0.30 across, with the far side tucking in. A low block holds the back line at about the 18-yard line with
the forwards left high. A separation pass keeps outfield targets at least 3 m apart; set pieces skip it.

**3. One challenger, a cover, options ten metres off (`FilmMotion.AssignRoles`).** The side without the ball sends one
player to it, a second only in its final third, in a duel, or when told to press high, and a cover stands behind the
challenger from his own line. A back-line player leaves his line only when the ball is in his own third. The side
with the ball offers a wide, a forward and a way-back option at 10 m and more instead of one supporter at 4 m. A
flank attack sets an overlap, a striker pinning the back line, a trailing midfielder and a far-side wide player
held high. Roles keep the existing hysteresis (`RoleRadius`) and the pin, deadline and retry machinery is reused
unchanged; an elbow rule keeps teammates 2.5 m apart while they steer.

**4. A counter is the engine's, not the film's guess.** The plan proposed detecting a long ball or a break from the
passage. `engine-v11` (ADR-0063) now records which possessions are counters, so the film draws exactly those
(`FilmCounter`) and a long ball or a break after a turnover only sets the outlet roles in `IsTransition`.

**5. A delivery comes into a populated box (`FilmShape.SetBox`).** A cross has a run-up of up to three beats, a
flight and an aftermath of up to three beats. Near-post, far-post, penalty-spot and cutback runners are mirrored by
flank, defenders mark goal-side by proximity and not slot order, and two zones are covered. Open-play crosses
only; set-piece crosses are decisions 6 and 7.

**6. A corner is won by somebody, set by role, and run into (`FilmScript.ScriptDeflection`/`ScriptTip`,
`FilmShape.ArrangeCorner`).** Every corner is preceded by a beat chosen from what came before, deterministically by
`FilmHash`: a defender blocks or heads it behind, or, for a ball that comes close to goal, the keeper shoots, saves
and tips it round the post. The layout is mirrored by the flag. Attackers: the taker (as `CornerTaker` already
picked), four box runners by Heading plus a share of how far forward they play, two at the edge, two or three guards
held near the halfway line. Defenders: two on the posts, three zonal, markers goal-side of the runners, one at the
edge, one outlet left high. Runners wait about 5 m short of their places and run in for the last 2 s of the hold.
Set-piece beats are arranged from the moment the set piece is known, take their side from the hold (a defender
winning the header no longer flips the layout), skip the open-play roles, and use a 1.5 m set-off.

*Amended in `replay-v8`.* As first built, the beat that put the ball behind was one move from wherever the last duel
was to where the engine had the ball leave play, a median of 31 m and up to 65 m, with the corner already set and the
taker waiting where it arrived; to a viewer it was a pass to the corner taker. Now the attacking side plays the ball
towards the goal line, and a defender touches it 2.5 to 4.5 m short of the line and puts it behind (a header when
the ball came in the air, a block when it did not); the sides do not set for the corner until it is out. A keeper's
tip goes round the post on the flag's side, a few metres, and the ball is put down at the flag from wherever it went
out. Only a ball that has just landed within 14 m of the line is headed from where it landed; after a duel the ball is
at somebody's feet, so it is played in. The taker is no longer pinned to the point the ball went out, so nothing before
the hold waits for him: he runs the length of the hold, and the ball is put down once he is within 14 m of the flag.
The engine's facts are unchanged: whether there is a corner, who takes it, and where the flag is.

*Amended in `replay-v11`.* Against a reference clip the defender's touch came almost as the ball arrived, 0.2 s after
it, where the reference has the man who is played the ball controlling it and being closed down for about a second
before he loses it. A ball played in along the ground is now received (by the attacker who can reach it soonest) and
held for 3.0 s of real time, about 1.1 s of film, in a `Contested` duel beat; the defender who can reach him soonest is
pinned there by the end of it and is the one who puts the ball behind. The two are found as the film is played, since
where people stand is not known when the script is written. A ball in the air, a header and a keeper's tip are as they
were.

**7. Free kicks, penalties and goal kicks (`FilmShape`).** A free kick **struck** at goal has a wall of 5 / 4 / 3 / 2
by distance (under 20 / 25 / 30 m, else 2) on the line to the near post at 9.5 m, the keeper on the far side, and two
or three attackers waiting for the rebound. A free kick **crossed** (`FormationMode.FreeKickCross`, from
`PassageOutcome.FreeKickCrossed`) has no wall: six runners by heading, goal-side markers, a three-man line at the box
edge, an outlet and two guards. A **quick** free kick keeps open-play shape with every defender 9.5 m off
(`FreeKickQuick`, state only so the beat stays `Open`). The **penalty** puts everybody outside the box and the arc,
in rows of at most nine with the sides alternating. A **goal kick** (and a keeper's ball) now has its own shape: the
kicking side spreads with its back line at the box edge, the other side steps up to the halfway line.

**8. Measured, not just drawn.** `FilmMeasure` gains shape metrics sampled at every step of the simulated record in
open play (`FilmDiagnostics.ShapeMetrics`), and the benchmark's `replay` mode prints them. The laboratory and test
fixtures put X across the pitch and Y down it, the reverse of the tactics board the film reads, so the film drew
their formation on its side. They cannot change (calibration and the goldens are fitted to them), so the film
measures use `OnTheBoard`, which restands the slots with the domain 4-4-2.

**9. Version.** `ReplayDirector.Version` is `replay-v6`. ADR-0061 said `replay-v5` was unreleased and it could have
been kept, but this changes where nearly every dot stands, and bumping costs one refetch of each cached
presentation (the ETag is `{OutputHash}:{PresentationVersion}`) and rules out any doubt about stale film.
`commentary-v3` is unchanged. No schema, API or contract change: the renderer plays keyframes and needed nothing.

## Evidence

Benchmark `replay 300` (`OnTheBoard` fixtures, median over matches, except where a percentile is named). "Before" is
the `replay-v5` film measured in M0; "after" is `replay-v6`.

| Measure | Before | After | Plan target |
|---|---|---|---|
| Players within 5 m of the ball, p50 / p95 | 2 / 5 | 2 / 5 | p95 at most 3 |
| Nearest teammate, p5 | 2.2 m | 2.0 m | at least 2 m |
| Block depth with / without the ball | 48.9 / 44.0 m | 45.9 / 36.5 m | 20 to 30 m |
| Block width with / without the ball | 40.4 / 39.3 m | 38.4 / 36.2 m | 45 to 55 m |
| Back line / front line from own goal | 25.8 / 68.6 m | 26.8 / 58.1 m | |
| Ball in own third: players within 30 m of goal / back line | 4.2 / 20.6 m | 4.6 / 21.8 m | 8 to 9 / about 18 m |
| Ball in a box: players in box p50 / p95, six-yard p95 | 5 / 9, 2 | 6 / 11, 3 | six-yard at most 7 |
| Cross arrives: attackers / defenders in box, six-yard p95 | 2 / 1, 0 | 3 / 5, 2 | 3 to 5 / 4 to 6 |
| Corner delivered: attackers / defenders in box, six-yard p95 | not measured in M0 | 4 / 7, 6 | 4 / 6 to 7, at most 7 |
| Corner: held back near halfway | not measured in M0 | 3 | 2 to 3 |
| Free kick: players inside 9.15 m p95 / wall p50 | fixed 4 at 9.15 m | 2 / 2 | 0 / 2 to 5 |
| Penalty: players inside arc or box p95 | 8.5 m from spot | 0 | 0 |
| Film pace p50, share inside the 1.8 to 2.9x band | 2.63x, 92.3% | 2.64x, 91.3% | unchanged |
| Moves lengthened for constraints, p50 | 18.8% | 19.1% | not higher |
| Film length p05 / p50 / p95 | | 9.89 / 10.11 / 10.41 min | 9:00 to 11:00 |

Teleports outside cuts stay at 0, no speed cap is exceeded, and the payload ladder stays on rung 2 or 3.

**The 300 matches are the cap set for this work; none of it needed more.**

## Consequences

**Positive**

- The block is a recognisable shape with lines and gaps: the defending side without the ball is 7.5 m shallower, the
  front line is 10 m nearer its own goal, and the nearest-teammate floor holds at 2 m.
- A cross arrives into a box with 3 attackers and 5 defenders (it was 2 and 1) and nobody is stacked in the six-yard
  area.
- A corner has a cause, a taker, runners, a guard against the counter, and flips with the flag.
- A free kick is different for a shot, a delivery and a quick one, and a penalty is clean.
- The film's length, pace and payload did not move: the shape is a tighter target, so there are no more long runs
  than before.

**Negative: targets not reached**

- **A challenger on the ball is by design.** With roles switched off only 3 to 6% of carry and pass samples have four
  or more players within 5 m of the ball; with them on 15 to 19% do. The "p95 at most 3" target cannot be met with
  a challenger and a cover close to the carrier. It is unchanged at 5. The screenshots show it at 1 to 2 most of the
  time, which the p50 of 2 matches.
- **The block is shallower without the ball but not 20 to 30 m, and a low block holds 4.6 players within 30 m of goal,
  not 8 to 9.** The targets are what the ideal shape is; the players lag behind it. The cause is the walk-and-hold logic
  in `FilmMotion.Steer` (the 8 m `SetOffDistance`), not the roles (the figure is the same with roles off). Trying 4 m
  helped only slightly. The block width is narrower, not wider, than the screenshots.
- **More players in the box when the ball is in it** (p95 11, it was 9). The cause was not
  investigated; the six-yard area is still at most 3.
- **Penalties are about 0 a match in the film**, so the fix is measured on a handful and checked by test.
- A throw-in is out of scope and still takes the open-play shape.
- Every cached `replay-v5` presentation is refetched once.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Add a corner after a saved or blocked shot in the engine | A new draw and a changed corner count: every golden hash and the calibration would move, for a change that is about what the viewer sees. The film shows the cause of the corner already decided. |
| Cluster lines by position family | In the domain 4-4-2 the wingers are attackers by family and 25 m behind the strikers, so a family line would be a line 25 m deep. Anchor depth gives the lines the formation draws. |
| Remove the challenger so no more than three players are ever near the ball | Nobody would then press the carrier. The lone carrier in screenshots 3 and 7 is where no opponent is near, which the roles allow. |
| Fix the fixtures' transposed axes | Calibration and the goldens are fitted to them. The film measures restand the slots (`OnTheBoard`) instead. |
| Keep `replay-v5` because it is unreleased | A harmless version bump removes any doubt about a stale cached film. |

*Amended in `replay-v12`.* Counted against the reference clips, the keeper of the side with the ball stood about 4 m off his
line wherever the ball was, where the reference has him 12 to 14 m out at halfway and 15 to 30 m inside 25 m of goal, and the
keeper of the side without it stood 4 to 5 m off where the reference has about 2 m. The film now stands the first on a ladder
of the ball's depth (3 m while his side builds from the back, 13 m at halfway, 28 m at the far goal line, 28 m at a corner
his side takes) and the second 1.5 to 2.5 m off his line. Restarts whose focus is not the ball keep him home.
