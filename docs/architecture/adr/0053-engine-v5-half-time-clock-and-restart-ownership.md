# ADR-0053: Engine-v5 resets the half-time clock, gives every dead ball an owner, and completes the passage record

- **Status:** Accepted
- **Date:** 2026-10-03
- **Stage:** Engine roadmap, fluid match film milestone (M1 of `engine-v5-fluid-match-film.md`)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0013](0013-engine-arithmetic-and-scoreline-effect.md), [ADR-0019](0019-engine-v2-and-season-statistics.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0052](0052-replay-v3-film-and-reel.md), master plan §8.2–§8.5, §9.2–§9.3, game rules `MAT-3`, `MAT-8`, `MAT-9`, new `MAT-12`

## Context

`engine-v4` built the right architecture — a possession played along a real passage, recorded as a side
channel — but reading it for a constant-pace film (the next milestone) turned up defects that change
*results*, and so cannot be fixed in the presentation. Each was verified in code:

1. **The second half kicked off at about 48'.** `ClockSeconds` was never reset at half-time, so the first half's
   stoppage was carried into the second: the half started at about 48', its regulation ended three to five
   minutes early, and `TotalMinutesPlayed` counted the first half's stoppage a second time.
2. **A restart flag could go stale.** `GoalAreaRestartSide` was consumed only when that side next had the ball,
   so the ball could teleport to a goal area several possessions after the keeper's save.
3. **Dead balls belonged to nobody.** The side was re-drawn for every possession, so the scoring side could
   kick off, the shooting side could take the goal kick, and the fouling side could take the free kick.
4. **Shots never reached the goal.** The move *into* the shot point was labelled as the strike and nothing
   recorded where the shot went; the keeper's save was stamped at the shooter's position; a penalty was
   recorded as a "shot" to the spot.
5. **The geometry was odd.** Every attack funnelled through one fixed central point, a headed goal from a
   corner was followed by a clearance, and the whole approach was recorded before an early loss of the ball.
6. **Every recorded possession had zero length.** The start of a possession was read after the clock had
   advanced, so the start and end of a passage were the same second.

The product owner confirmed the order of work: engine first, so the film is built on facts that are true.

## Decision

**1. The clock is reset at half-time.** `MatchState.BeginHalf(firstHalf: false)` sets
`ClockSeconds = HalfTimeMinute × SecondsPerMinute`. The second half runs 46'…90' and then its own stoppage;
`TotalMinutesPlayed` is the minutes the clock really ran. A possession's start is read **before** the clock
advances, so the possessions tile each half and `End > Start` for every one. Match seconds therefore restart at
the second half, and a passage carries its `Period` (1 or 2).

**2. A dead ball belongs to somebody (`MAT-12`).** One `state.NextRestart` value — side, kind, spot — replaces
`RestartFromCentre` and `GoalAreaRestartSide`. A dead-ball outcome sets it; the very next possession reads and
**clears** it and takes the restart's side without a possession draw.

| Restart | Taken by, from |
|---|---|
| Kick-off | Home in the first half, away in the second, the conceding side after a goal; the centre spot |
| Keeper's ball or goal kick (after a save, a miss off target, or a missed penalty) | The defending side; its goal area |
| Free kick after a foul with no shot | The fouled side; where the foul was committed |
| Offside | The defending side; where the offside was given |

Loose balls — a block, a woodwork rebound, a cleared corner, a crossed free kick, a turnover — keep the
contested possession draw. `state.Ball` ends where play actually continues.

**3. A shot travels to a target its outcome decides.** The strike is a `Shot` waypoint from the shot point to a
goal-mouth point (goal), the goalkeeper a few metres off the line and ahead of the shooter (save), a point out
of play wide of a post or over the bar (miss), a post or the bar and then a rebound into the box (woodwork), or
a point two to six metres in front of the shooter (block). The keeper's `Save` touch is at the save target.
A penalty is a *placement* (new `PassageWaypointKind.Restart`) and then a strike; a free kick is a placement and
then a strike or a cross into the box; a corner goes out of play, is placed at the flag, is delivered, and is
headed, with no clearance after a goal. Open play reaches a final-third entry point drawn between the pressure
point and the shot point instead of one fixed point. A scramble lost, or a progression failed, cuts the recorded
approach where the ball was lost (5–25% and 40–80% of the way).

**4. Decide first, record second — without moving a draw.** The foul is split into `RollFoul` (the foul, the
fouler, and the card draw, in the order they have always been taken) and `ApplyFoul` (which puts them on the
event log). The possession knows how it ends — a penalty, a free kick, a quick restart — before the ball's
path is written down, so a foul that gives a penalty is recorded in the box and a possession that ends early
records an approach that ends early. All new geometry is drawn from the possession's derived stream, appended
after the existing draws, and is taken whether or not a recorder is attached.

**5. The recorder is complete.** A passage records its `Period`, its `Outcome` (scramble lost, progression
failed, creation failed, offside, foul, penalty, free kick struck or crossed, corner cleared or headed,
open-play shot), the `Restart` it began with, and its events as `(Sequence, FractionBasisPoints)` so each event
has a position among the waypoints and touches (`EventSequences` stays, derived). It also records the touches
a film needs to name: both scramble contestants, the fouler and the fouled player, both jumpers in an aerial
duel, and the player caught offside. None of it is hashed, and the with/without-recorder test still guards it.

**6. Replay-v3 keeps working on the new data.** The director reads the second half on a continuous clock — its
seconds carry on from where the first half's stoppage ended, which is what the film and the viewer have always
assumed — and splits film passages at half-time by `Period`. It is replaced wholesale by the next milestone.

**7. Two constants are retuned, and the evidence is recorded.** A true-length second half is about four per
cent more football than `engine-v4` simulated, and the fouled side keeping the ball adds a little more, so every
volume statistic rose about five per cent (goals ×1.054, shots ×1.055, fouls ×1.053; goals per shot unchanged
at 10.6%) and two bands moved (goals per match 3.05 against 2.5–3.0; matches with seven or more goals 3.7%
against < 3.0%). Dead-ball ownership also flattened the ability curve (a three-point underdog won 18.7% against
16.1%), because a saved shot now hands the ball to the defender by rule instead of by a draw that favoured the
stronger side. `PossessionSecondsMin`/`Max` 16/44 → **17/46** restores the number of possessions in a match,
and `PossessionControlSwingBasisPoints` 2,400 → **4,000** restores the possession edge. No band was widened and no
outcome probability was touched (§ measured behaviour of `match-engine.md`).

**8. Versions.** `EngineVersions.Engine = 5` and `RuleSet = 5` (`engine-v5` / `engine-rules-v5`); the golden
hashes are re-pinned. Stored `engine-v4` matches cannot be re-simulated (`MatchSimulator.VerifyVersionAgreement`),
so the dev database is archived (renamed, never dropped) and reseeded, as ADR-0019 and ADR-0051 did.

## Consequences

**Positive**

- The second half is 46'…90' with its own stoppage; player minutes, the live metric curve, and the substitution
  windows now all describe the same match.
- Every restart has the right owner and the right place, and none outlives the possession it was taken in.
- A film can show a shot reaching the goal, the keeper at the save, a penalty on the spot, and a corner taken
  from the flag, because the engine now says so.
- The calibrated distributions are unchanged: goals 2.90, shots 27.4, fouls 21.2, cards, injuries, penalties,
  home advantage +4.2 points, and a three-point underdog 16.4%.

**Negative**

- The output hash of every match changes, so a database seeded under `engine-v4` must be reseeded.
- Match seconds are no longer monotonic across half-time, so any consumer that orders possessions must use
  `Period` (replay-v3 hides this behind a continuous clock; the next presentation version exposes `Period`).
- A second half played to its true length shows a match of about 101 clock minutes (about ten minutes of
  stoppage between the halves, p05 8 to p95 13), which is more added time than football usually shows. It is
  the existing stoppage model, now no longer hidden by a truncated second half.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep the continuous clock and add a `Period` and a display offset | Leaves the second half three to five minutes short and `TotalMinutesPlayed` double-counting; the film would be built on a false clock. |
| Keep drawing the side for every possession | Lets the scoring side kick off and the fouling side take the free kick; a viewer sees it immediately. |
| Leave the restart flag pending until its side next has the ball | That is the defect: a goal-area restart several possessions late. |
| Restore the calibration by retuning goal or creation probabilities | Hides a longer match behind weaker chances; fouls and cards would stay five per cent high and the per-possession rates would stop meaning what they were calibrated to mean. |
| Reorder the card and penalty draws so the geometry can follow the outcome | Changes the draw order for no gain; splitting the foul into roll and apply keeps it. |
| Stamp shot events at the target the ball reached | Moves the event from where the shot was struck; the event keeps its origin and is *positioned* after the strike by the recorder. |
| Rework the replay director here | It is replaced wholesale by the next milestone; the minimal continuous-clock adapter keeps the shipped film correct meanwhile. |
