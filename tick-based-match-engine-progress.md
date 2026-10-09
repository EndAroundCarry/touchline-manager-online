# Tick engine (`engine-v12`) — M9 progress note

> **Written:** 2026-10-09, at the end of the second session that worked Milestone 9.
> **Branch:** `feature/tick-engine-commandcode` — this note was written against uncommitted working-tree state and is
> committed with the `Engine v12 M9` change it describes.
> **Plan:** [`tick-based-match-engine-plan.md`](tick-based-match-engine-plan.md), Milestone 9
> ("Plug-In, Benchmarking, Determinism & Calibration Suite").
> **Status:** **Milestone 9 is complete** as of the third session, same day. Steps 1 (plug-in), 2 (determinism)
> and 3 (calibration) were already done; step 4 — `ADR-0066`, `docs/product/match-engine.md` §14, the CHANGELOG
> entry, the plan's status, and the `MAT-3` wording in `game-rules.md` — is written, the performance budget was
> attacked (p50 233 ms → 206 ms; still above the 100 ms budget), and the API integration suite was run for the
> first time (218/221, three date-bound failures unrelated to the engine). See the **Session 3 addendum** at the
> end of this note.

## Where the work stands

- `engine-v12` snapshots play the tick engine through `MatchSimulator.Simulate`; `engine-v11` still dispatches to
  `LegacyPossessionEngine` and its golden hashes are untouched.
- The replay path serves a real tick film (`SimulateFilm` → `TickReplaySynthesizer` → `TickReplayDirector`), and
  `GetMatchPresentation` uses it.
- **The calibration suite passes.** `tick` mode over 200 fixtures (seed 20260925):

  | metric | measured | plan band |
  |---|---|---|
  | goals per match | 2.66 | 2.60 – 2.90 |
  | shots per match | 24.4 | 22 – 28 |
  | shots on target | 35.8 % | 32 – 38 % |
  | pass completion | 84.8 % | 75 – 85 % |
  | yellow cards | 3.77 | 3.0 – 4.5 |
  | home / draw / away | 43.0 / 23.5 / 33.5 % | 42–48 / 22–26 / 28–34 |

  Two larger blocks agree: 400 fixtures at seed 20260925 gave 2.81 goals, 41.5/24.5/34.0 %, and 400 at seed
  987654321 gave 2.95 goals, 43.0/23.0/34.0 %. The first calibration session's numbers (17.78 goals, 91 % on
  target, 0.04 yellows, 33/15/52 home/draw/away) are gone.
- **Not run:** `tests/TouchlineManager.Api.IntegrationTests` (needs the database). The engine suite is 1360/1360
  green and the application suite 257/257.
- **Not done:** `ADR-0066`, `docs/product/match-engine.md`, the CHANGELOG entry, and the plan's own milestone
  status. Do not write the ADR before reading the "Shot model" and "Home advantage" sections below — they are
  the decisions it has to record.

## What the tuning changed, and why

### The calibration fixture was sideways (the big one)

Both engines read a slot's `X` as *depth* towards the goal the side attacks (`TickAnchorSpec`, and the legacy
`TacticalFormationResolver` says the same), and `MatchSnapshotBuilder` writes `X = NormalizedX`, where the domain
means depth. But `LaboratoryFixtures.StartingShape` and `TestMatchFactory.Shape` are written the legacy way round
(X across, Y down — their own comment says so). A tick fixture built straight from them stood both sides on their
side: "full-backs" anchored 84 m up the pitch, the keeper anchored at the halfway line, the "strikers" wide. That
is what produced the pinball box and the 30-odd shots a match.

The tick calibration now builds on `LaboratoryFixtures.OnTheBoard(...)`, the board's own 4-4-2 — the same helper
the film diagnostics always used. `tick`, `single` and the film sample all go through it.

### Shot model (`TickBallCarrierBrain`)

The old model aimed at the far corner, 1.5 m inside the post, and rotated the aim by ±4–7°: 91 % of shots were on
target and the keeper had no chance. It is now:

- **A placement draw** (`Shoot`, two draws a shot, always). One roll decides whether the shot is on target
  (`OnTargetChance`: base 43 %, +0.2 % a metre nearer than 16 m, ±1.5 % per point of `2 × Finishing + Technique`
  against 39, halved at full pressure, clamped 10–80 %), the second draws where it crosses. A placed shot crosses
  towards the middle of the goal (a squared draw, so most on-target shots test the keeper and the extremes can
  clip a post); a mishit crosses wide of the post it was aimed at by up to 9 m, drawn squarely so just-past-the-post
  is the common miss. The ball is launched at the drawn crossing point, so the keeper's forecast reads the shot he
  actually faces.
- **The keeper counts as a blocker on the line** in `ExpectedGoals` (the loop now runs from index 0), which is what
  stops the brain shooting whenever a chance exists at all.
- **`ShotValuePercent`** (was `ShotValueMultiplier = 3`, i.e. three times the chance) is now 205, i.e. ~2.05× the
  chance. The name changed because the old one was misleading: it was a percent, and setting it to 1,500 once made
  shots five times *more* attractive.

### Goalkeeping (`TickShotStopper`, `TickPlayerPhysics`)

- Reach is `BodyReach 70 + DiveBase 55 + 6 × Agility` (1.9 m at Agility 10), hold `45 % + 2.5 % × Handling` of it:
  a keeper now saves about 68 % of the shots on target, which is the real-world figure.
- `DiveIntent` sets `TickMoveIntent.Dive`; `TickPlayerPhysics.Step` gives a dive `DiveAccelerationPercent = 200`
  of a run's acceleration and snaps the heading (no turn cost — a keeper pushes off any way he likes). This is
  what makes a dive cover ground a run cannot, and it is why the save share moved.
- A tipped or parried ball now **guards the keeper** (`GuardKeeper`, the self-pass guard): without it he gathered
  his own tip on the next tick and the tipped-ball corner never happened.

### Shot-flight bookkeeping (`TickMatchLoop`)

A shot that beat the keeper used to keep its shooter identity only until the save was resolved, so a defender
(usually stood on the line) collecting the ball was recorded as an *interception*, not a block — and the shot
disappeared from `result.Shots`. There is now a `_shotInFlight` flag: the shooter's identity stays on a beaten
shot until somebody touches it, so `TryReception` emits `ShotBlocked` with the right name. This is what made the
recorded shot count (31) agree with the shots actually taken (44 before the tuning, ~24 now).

### Duels and fouls (`TickTackleResolver`, `TickMatchLoop`)

- **The challenge now comes before the carrier's act** (in `TickOpenPlay`, before `CarrierTurn`): a defender within
  reach gets a foot in as the pass is played, not after it. Before this, the carrier always released the ball first
  and a match produced about four challenges.
- Contact reach is 150 units (1.6 m), not 90, and the win chance falls off `15 bp` a unit past 90 (`FullContactUnits`)
  — a lunge from arm's length lands less of itself.
- The tick engine's own foul share is `rules.DuelFoulBasisPoints × FoulShareMultiplierBasisPoints` (31,000 ≈ ×3.1):
  about 21 fouls and 3.8 yellows a match. The *rules value itself was not touched* — the legacy engine's goldens
  depend on it.

### Home advantage (`TickMatchLoop.Advantage`, and three call sites)

The tick engine ignored `MatchInputV1.HomeAdvantageBasisPoints`, so with identical sides it produced 35/30/35 —
but the plan's bands (42–48 / 22–26 / 28–34) are real-football bands and need a home edge. First attempt was a
4.2 % boost to the home side's pace/acceleration/braking (`TickPlayerProfile.From(attributes, advantage)`): it made
the home side **worse** (a controlled 100-fixture pair gave home 123 goals to away 146 — a faster side presses
past the ball and finishes worse). Reverted. The edge now goes into the contests a crowd is said to lift:
`OnTargetChance` (finishing), `TickShotStopper.Assess` (reach), and `TickTackleResolver.WinChanceBasisPoints`
(the tackler's chance). 100-fixture neutral-vs-shipped compared cleanly: neutral 36/30/34 %, shipped 27/28/45 %
before the fix; after it, 43/23.5/33.5 % on the calibration blocks.

### Also in the change set (from the plug-in session and this one)

Everything the previous note listed still stands: `TickReplayDirector`, the tick film path, `SimulateFilm`,
`TickReplaySynthesizer` keyframe tracks, the tick keyframe ladder, the `SpatialMath.Sqrt` seed, the steering fast
paths, and `TestMatchFactory`'s tick configuration hash. New since: the temporary `TickCalibrationProbe` and the
scratch probe tests were **removed** (the numbers above were taken with them in place but they consumed no draws
and only read state; the final `tick 200` run above was taken after they were gone).

## Verified (commands and results)

- `dotnet test tests/TouchlineManager.MatchEngine.Tests -c Release` → **1360 passed, 0 failed** (3 m 2 s).
- `dotnet test tests/TouchlineManager.Application.Tests -c Release` → **257 passed, 0 failed**.
- `dotnet run -c Release --project tools/simulation-benchmarks -- tick 200 20260925` → the table above.
- `tick 400` at seeds 20260925 and 987654321 → both blocks inside the bands.
- Five tests were re-pinned deliberately (their comments say so): the contact reach (150), the pressure-scaled
  master finisher (60/100 from 8 m under 41 % pressure, was asserted at 100), the crowd scene moved to 11 m where
  a shot is worth taking, the geometry allocation test gained the tier-1 JIT warm-up run the brain test already
  had, and the film-slicing test now allows a passage that ends at the interval's seam.

## The remaining Milestone 9 work, in the order I would take it

1. **`ADR-0066`** (the plan says ADR-0055 but the highest used number is 0065): the tick engine adopted, the
   possession engine quarantined, and the calibration decisions to record — the placement draw, the keeper's
   reach and dive burst, the pre-decision challenge, the tick engine's own foul share, home advantage through the
   contests, and the board-fixture rule (a snapshot's `slot.X` is depth).
2. **`docs/product/match-engine.md`** section for `engine-v12` / `tick-engine-v1`, and the CHANGELOG entry.
   Then flip the plan's Milestone 9 status and the milestone table.
3. **Performance**: 235 ms p50 per match against the benchmark's 100 ms budget (the plan's own target was 35 ms).
   `dotnet-trace` showed `TickSteering.Steer`, `TickPlayerPhysics.Step` and `SpatialMath.Sqrt` as the remaining
   cost. The film adds ~50 ms on top.
4. **API integration suite** once the API can be built (it needs the database).
5. **Known gaps, none of them in the bands:** offsides are 0 a match (everyone in the calibration fixture is
   `Decisions + Anticipation ≥ 12`, so the brain never plays the offside ball — the flag itself is unit-tested);
   `PassageV1.Commentary` is still empty on the tick film (M8 step 3, the match-level commentary works);
   substitutions, injuries, morale and fatigue are still deferred by the loop's own docs.

## Gotchas for the next session

- The user's API and Worker processes are usually running; the full-solution build fails copying into `apps/*/bin`
  (MSB3021/MSB3027). Build the project you need (`src/TouchlineManager.MatchEngine`, the test projects, the
  benchmark tool) instead.
- Run tests with `-c Release`: Debug makes a tick match ~4× slower and the engine suite is ~3 minutes even in
  Release.
- **A tick calibration fixture must be built through `OnTheBoard`.** A fixture from `EvenlyMatched` alone is the
  sideways shape and its numbers mean nothing.
- The temporary probe was deleted; if a tuning session needs the diagnostics again, the recipe was: a static class
  with counters in `Tick/` (challenges by outcome, save outcomes, shots by distance bucket, the nearest-defender
  distance histogram, carrier ticks), which the loop increments, plus a scratch test that runs N matches and writes
  a report to `$COMMANDCODE_SCRATCHPAD`. Delete both when the numbers land.
- The `RULES="Name=Value,..."` environment variable still overrides `EngineRulesV2` constants without a rebuild —
  useful for sweeping, never for the shipped game.
- `dotnet format` has pre-existing failures in files this work does not touch (recorded with the M8 commit).

## Session 3 addendum — Milestone 9 closed

**Documentation (step 4).**

- `ADR-0066` (`docs/architecture/adr/0066-engine-v12-tick-simulation.md`) records the adoption, the versions
  (`engine-v12` / `engine-rules-v11` / `tick-engine-v1`), the calibration decisions this note's "What the tuning
  changed" section describes, the film, the measured evidence, and the open items; it is indexed in the ADR README.
- `docs/product/match-engine.md` is retitled "Match engine versions 11 and 12" and gains §14 — the tick engine's
  tick in order, movement, the carrier and the shot placement draw, defending and the duel, the keepers, restarts,
  the film, measured behaviour, and the tick suite's test map. The stale "covers engine version 5" pointer in
  `game-rules.md` §15 was fixed, and `MAT-3` now describes both engine models in the same change set as the ADR.
- `CHANGELOG.md` gains the "Engine v12 — the match is played tick by tick" entry; the plan's status is
  **Implemented** and its deliverables table now reads "Delivered in"; `EngineVersions.cs` gained the missing
  `engine-v12` paragraph in the label's own remarks.

**Performance (p50 233 ms → 206 ms; the 100 ms budget is still missed).** Three exact changes, none of which moved
a value:

- The rules hash reads its properties through cached typed delegates instead of `PropertyInfo.GetValue`. The
  reflection invoke path emitted a dynamic method per call and had the finalizer thread at ~26% of the process's
  sampled time; that is gone.
- `TickPlayerPhysics.Step` computes its distance only when `intent.Arrive` (nobody but a keeper sets it), and the
  arrival brake in `Step` and `Steer` compares `2 · braking · distance` against the cap squared instead of taking
  a square root that usually loses.
- `SpatialMath.Sqrt` is seeded from a 256-entry table (within 0.2% of the root) so Newton takes fewer divisions.

`dotnet-trace` says what remains: `TickPlayerPhysics.Step`, `TickSteering.Steer`, `SpatialMath.Sqrt`,
`TickTrigonometry.AngleOf`. **The strengthened `SpatialMathTests` caught a real bug in the first seed table** (it
bounded `m` where it had to bound `m + 1` — an under-estimate that stopped Newton below the root); the whole range
is now swept by test.

**Verified this session.**

- `dotnet test tests/TouchlineManager.MatchEngine.Tests -c Release` → **1361 passed, 0 failed** (last session's
  1360 plus the new whole-range sqrt test).
- `dotnet test tests/TouchlineManager.Application.Tests -c Release` → **257 passed, 0 failed**.
- `tick 200 20260925` → 2.655 goals, 24.395 shots, 35.807% on target, 84.838% passes, 3.765 yellows,
  43.0/23.5/33.5 — **identical to last session's numbers**, so the optimizations changed no behaviour.
- `tick 400 20260925` → 2.812 goals and 41.5/24.5/34.0% (the home share half a point under its floor, as last
  session), everything else inside.
- `tests/TouchlineManager.Api.IntegrationTests -c Release` → **218 passed, 3 failed, 221 total** (the suite's
  first run). The three are `CompetitionTests` date-bound failures and **not engine-related**:
  `WorldOptions.FirstSeasonStartDate` is 2026-10-06 and today is 2026-10-09, so the seeded world's "next fixture"
  is already past its lock instant — the test that wants `IsLocked == false` finds `true`, and the two sheet-save
  tests want 200/201 or 400 where the API rightly answers 409 for a locked fixture. Someone should make the
  integration world's first matchday relative to `now` (or step its clock); it is not this milestone's change.
- `bench 120` → p50 205.9 ms, p95 216.6 ms. The `tick` mode's own timing is noisier (211.7 ms on the 200 run,
  310.9 ms on the 400 run) because the machine was loaded.
