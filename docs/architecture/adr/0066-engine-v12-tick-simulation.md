# ADR-0066: Engine-v12 plays the match as a discrete tick simulation, and the possession engine is kept for the matches it played

- **Status:** Accepted
- **Date:** 2026-10-09
- **Stage:** Engine roadmap, the tick engine (`tick-based-match-engine-plan.md`, Milestone 9)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0006](0006-semantic-highlight-keyframes.md), [ADR-0013](0013-engine-arithmetic-and-scoreline-effect.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0054](0054-replay-v4-constant-pace-film.md), game rules `MAT-3`, `MAT-4`, `MAT-5`, `MAT-9`, `MAT-12`

## Context

Through `engine-v11` a match was modelled as about 190 macro possessions, each resolved by percentage rolls, with
the movement reconstructed after the fact from formation anchors (`PassagePlanner`, `ReplayDirector`). The dots
sprinted to arbitrary touch points and then stood still; players off the ball either swarmed it or wandered; a
shot or a save was drawn to match a roll rather than falling out of angles and bodies. The request was a
Football Manager-style simulation where twenty-two autonomous players and a physical ball move with tactical
intent, so that the film *is* the match and not a costume over a dice roll.

The new engine had to keep the plug-and-play contract — `MatchInputV1` in, `MatchResultV1` and the
`MatchPresentationV1` shape out, the 2D canvas viewer unchanged — remain deterministic, integer, and versioned
(`MAT-9`, ADR-0004), and leave the possession engine compiled so that every stored `engine-v11` result stays
re-derivable.

## Decision

**1. `engine-v12` is a discrete tick engine, and it is the active engine.** `tick-engine-v1` plays the match at
a 100 ms time step (10 Hz, 54,000 ticks a half-less match), on the 10,000 × 7,000 fixed-point pitch, with
headings in binary angles off a quarter-wave table and an integer square root. There is no floating point in the
simulation (ADR-0013, plan §4). `MatchEngineRegistry` resolves `engine-v12` snapshots to `TickMatchEngine` and
leaves `engine-v11` snapshots to the untouched `LegacyPossessionEngine`, whose golden hashes do not move.
`MatchSimulator.Simulate` is still the whole public surface, and `MatchSimulator.SimulateFilm` plays a match and
derives its film in one pass.

**2. The tick is the unit of play.** Each tick, in order: both sides' dynamic anchors from the ball's place and
the phase; the attacking side's off-the-ball orders (triangles, runs, pockets, overlaps) and the defending
side's (one presser at most, markers, cover shadows, the line); the carrier's decision on his own interval; both
goalkeepers' arcs and rushes; one steering and physics step per player (arrival, separation, acceleration,
turning, braking, stamina); a challenge within reach; the ball's flight, its boundaries (goal, touchline, goal
line, woodwork) and the reception of a loose ball. Contests read effective skills weighted by attributes; a pass
is completed, intercepted or blocked by geometry, not by a roll (`MAT-4`).

**3. What a shot does is a placement draw, and the keeper is a blocker with a reach and a dive.** A shot spends
two draws: one decides whether it is on target (`OnTargetChance`: 43% at 16 m, ±0.2% a metre, moved by
`2 × Finishing + Technique`; halved under full pressure; clamped 10–80%), the second where it crosses — placed
shots to the middle of the goal, mishits up to 9 m wide of the post they were aimed at. The goalkeeper counts as
a blocker in the shot's value, so the brain no longer shoots whenever a chance exists at all. His reach is
`70 + 55 + 6 × Agility` pitch units and his hold is `45% + 2.5% × Handling` of it — he saves about 68% of the
shots on target. A dive accelerates at 200% of a run's rate and snaps the heading: that is what lets a dive
cover ground a run cannot. A tipped ball guards the keeper for a tick, so a tip can become the corner it looks
like.

**4. The challenge comes before the carrier's act.** A defender within contact reach (150 units, 1.6 m) puts a
foot in as the pass is played, not after the ball has gone; the win chance falls off past 90 units (arm's
length), so a lunge lands less of itself than a close challenge. A beaten shot keeps the shooter's identity
until somebody touches it, so a defender on the line is recorded as a `ShotBlocked` block rather than an
interception, and the result's shots reconcile with the shots taken (`MAT-5`).

**5. The tick engine pays its own foul rate.** Its foul share is the rules' `DuelFoulBasisPoints` times a
multiplier of its own (≈ ×3.1), which lands about 21 fouls and 3.8 yellows a match. The rules value itself was
deliberately not touched: the legacy engine's golden hashes depend on it, and a balance value shared by two
engines can only move with a version.

**6. Home advantage goes through the contests, not through the legs.** Boosting the home side's pace and
acceleration made it *worse* (a faster side presses past the ball and finishes worse: 123 home goals to 146
over 100 fixtures). The crowd's edge is now applied where a crowd is said to lift a side — the finishing draw,
the keeper's reach, and the tackler's win chance — which is what puts home/draw/away in the plan's band.

**7. A tick fixture must be built from the board's own shape.** A snapshot's `slot.X` is depth towards the goal
the side attacks (`MatchSnapshotBuilder` writes `NormalizedX`), so a tick calibration built straight from the
laboratory's legacy fixture — whose `X` runs across the pitch — stands both sides sideways and its numbers mean
nothing. The tick fixture is the board's own 4-4-2 (`LaboratoryFixtures.OnTheBoard`). This is a fixture rule,
not engine behaviour, and it is what turned the first calibration's 17.8 goals a match into football's.

**8. The film is the recording, sliced.** The loop records the continuous trace (`TickMatchRecorder`), the
synthesizer slices it into the presentation's passages (8–12 s of film each, at most 75, ten match seconds to
one film second) with delta-compressed keyframe tracks at a widening tolerance ladder, and `TickReplayDirector`
assembles the same `MatchPresentationV1` the possession film produces — playback schedule, lineups, live
metrics, the reel over the same film — so the client needs no change (ADR-0006, plan §1.3.1). `GetMatchPresentation`
re-simulates from the frozen snapshot and refuses to serve a film whose result does not reproduce the stored
output hash (`MAT-9`).

**9. Versions.** `EngineVersions.Engine = 12` (`engine-v12`), `RuleSet = 11` (`engine-rules-v11`), the model
`tick-engine-v1`; the presentation keeps the `replay-v16` shape the viewer already reads. `engine-v11` /
`engine-rules-v10` still resolve to the possession engine. `MAT-3`'s wording is updated in the same change set to
name both engine models; the rule's requirements — the 90 regulation minutes, each half on its own clock with its
own stoppage — are unchanged.

## Evidence

- **The calibration lands, on three independent seed blocks.** `tick` mode over 200 fixtures (seed 20260925):

  | Metric | Measured | Plan band |
  |---|---|---|
  | Goals per match | 2.655 | 2.60 – 2.90 |
  | Shots per match | 24.395 | 22 – 28 |
  | Shots on target | 35.807% | 32 – 38% |
  | Pass completion | 84.838% | 75 – 85% |
  | Yellow cards | 3.765 | 3.0 – 4.5 |
  | Home / draw / away | 43.0 / 23.5 / 33.5% | 42–48 / 22–26 / 28–34 |

  Two 400-fixture blocks agree: seed 20260925 gave 2.81 goals and 41.5/24.5/34.0% (its home share half
  a point under its floor, which is sampling noise on 400 fixtures), seed 987654321 gave 2.95 goals and
  43.0/23.0/34.0%. The first calibration session's numbers (17.78 goals, 91% on target, 0.04 yellows,
  33/15/52) are gone with the sideways fixture that produced them.

- **The film fits the contract.** Over ten tick matches: 60 passages at the median (cap 75), 9.9 film minutes
  (window 9.5–11.0), 26,039 keyframes at the median, a 692 KB payload estimate at the median (705 KB at p95,
  budget 750 KB), 0 of 10 over budget. `SimulateFilm` and `Simulate` produce the same output hash: recording is
  a by-product that consumes no draw (`MAT-8`).

- **Determinism and purity hold.** The tick engine's front door is pinned by property tests — the same snapshot
  produces the same output hash and byte-identical canonical output, a different seed produces a different
  match, the score equals the goal events, the statistics reconcile, and the film is identical between runs.
  The engine suite is 1361/1361 green; the legacy goldens did not move.

- **The performance budget is not met, and the cost is understood.** `bench` measures p50 205.9 ms and p95
  216.6 ms per match against the 100 ms budget (the plan's own target was 35 ms), and the film adds about 50 ms.
  Two causes were found and fixed on the way: the rules hash was being computed by reflection
  (`PropertyInfo.GetValue`) on every simulation, whose dynamic-method emit made the finalizer thread burn about
  26% of the process's sampled time (a cached typed reader removed it); and the physics took an unread square
  root per player per tick (computed only for a player told to arrive, and the arrival brake decided by
  comparison against the cap squared; a table-seeded integer root cut the remaining ones further). p50 fell from
  233 ms to 206 ms. What remains is the per-player per-tick cost of `TickPlayerPhysics.Step`,
  `TickSteering.Steer`, `SpatialMath.Sqrt` and `TickTrigonometry.AngleOf`, and further work there is open.

## Consequences

**Positive**

- The film is the match: every dot's track is where the simulation actually put him, so the viewer's movement
  is smooth, intentional and physical, and the replay needs no reconstruction.
- The result contract, the presentation contract and the viewer are untouched; the change is the engine behind
  them.
- Stored `engine-v11` matches still reproduce byte for byte, because the possession engine is quarantined, not
  deleted (`MAT-9`, ADR-0004).
- The engine is integer and allocation-light, so it stays machine-independent.

**Negative**

- A match costs about 2× the 100 ms budget and about 6× the plan's 35 ms target to simulate, and the film a
  further ~50 ms. Throughput comes from simulating independent fixtures concurrently, but a nine-fixture
  matchday is no longer trivially cheap.
- Substitutions, injuries, morale drift and fatigue accumulation are **not yet in the tick loop**: a tick match
  plays the same eleven to the end and its condition is the physical energy of the tick physics. The loop's own
  documentation says so; a follow-up must wire the match-day model in.
- Offsides are unit-tested but never occur on the calibration fixture: every player in it has
  `Decisions + Anticipation ≥ 12`, so the brain does not play the offside ball.
- `PassageV1.Commentary` is still empty on a tick film (the match-level commentary is complete).
- A snapshot frozen against `engine-v12` cannot be re-simulated after any formula change, as with every earlier
  version: the old build must be kept, or the result is a re-pin.

## Alternatives considered

- **Keep the possession engine and improve the reconstruction.** Rejected: the reconstruction *was* the
  problem — no amount of post-hoc anchor work makes a dot's movement emerge from geometry it never saw.
- **Make the tick engine a presentation layer over the possession outcomes.** Rejected: the plan's mandate is
  the opposite direction — the dice must fall out of the simulation, not the simulation be drawn from the dice.
- **Delete the possession engine once the tick engine landed.** Rejected: `engine-v11` results would lose their
  re-derivation path (`MAT-9`, ADR-0004).
- **Push home advantage through pace and acceleration.** Rejected on measurement: it made the home side worse.
- **Use `Math.Sqrt` for the geometry.** Rejected: a floating root is a platform's rounding, not the engine's
  (ADR-0013); the integer root is table-seeded instead.
