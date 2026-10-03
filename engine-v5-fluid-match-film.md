# Fluid full-match 2D replay: engine-v5, the replay-v4 constant-pace film, and a continuous viewer timeline

## Context

When a full match plays in the 2D viewer, the dots move very fast for a moment and then crawl or stand still. The user wants a Football Manager / Championship Manager-style 2D match with these properties:

- the **whole match** is shown;
- it runs in about **10:00 at 1x** (up to 11:00 with stoppage, never longer);
- motion is **fluid**;
- the **build-up** is visible: passes, carries toward goal, crosses into the box;
- in Highlights mode, each chance comes with roughly **the previous 10 match-minutes** of lead-in, so a manager can see where the team fell short.

The last milestone (`engine-v4-continuous-match-replay.md`, ADR-0051/0052) built the right architecture but has defects. The defects below were verified by reading the code; the line numbers are from commit `4926ad4`.

**Decisions confirmed with the user**

1. **Engine first.** Fix the engine bugs that change results as engine-v5, including the dev DB archive and reseed (M1). Then build the replay-v4 film on top (M2), then the viewer (M3), then calibration and docs (M4). Stop and confirm after each milestone.
2. **One constant pace for the whole film.** Every touch plays at one global speed of about 2–2.5× real time. Quiet possessions are shortened only if the film would otherwise run past 11:00.
3. **Film length:** `target = clamp(totalMatchSeconds / 9, 9:30, 11:00)`, so 90' → 10:00. **11:00 is a hard ceiling** and includes the half-time card. The speed options stay 0.5/1/2/4/8.
4. **Highlights:** keep the reel. A clip's lead-in reaches back about 600 match-seconds, staying inside the same half (25–70 s of film). The reel stays ≤ 12:00 and always keeps the goals.

## Root causes

### A. Why the dots burst and then crawl (affects every match; presentation)

| # | Symptom | Cause | Where |
|---|---|---|---|
| 1 | **About every 9 s, a burst of motion followed by near-stillness** | **Every recorded possession has zero length.** The clock is advanced before `BeginPassage` stamps the start, and `EndPassage` stamps the same value. `ReplayDirector.Windows` then gives each possession about 1 s out of its group's span. All 2–4 possessions in a ~9 s passage therefore play their moves in the first ~0.3–0.7 s, and the ball holds still for the remaining ~8.5 s. Build-up commentary bursts the same way. | `PossessionSimulator.cs:76-78,96`; `MatchState.cs:164-165,181`; `ReplayDirector.cs:484-501` |
| 2 | Ball speed varies by about 10× | Waypoints are spaced by touch-tick count, not by distance. Passages are sized by a weight (×0.8…×2.0), so film-ms per match-second varies about 4× between passages. | `MatchState.cs:406-457`; `ReplayDirector.cs:168-258,328-349` |
| 3 | Players zip to the ball, then drift | Each player gets only 3 formation anchors per passage plus touch keyframes. A touch close in time to an anchor needs teleport speed. `Order` pushes keyframes with the same time 1 ms apart. | `ReplayDirector.cs:540-570,811-853` |
| 4 | Team shape ignores the play and the tactics | The resolver gets `new MatchInstructionsV1()` instead of the side's instructions. One possession side is used for a whole multi-possession passage. The block moves only 12% with the ball. | `ReplayDirector.cs:552,589`; `TacticalFormationResolver.cs:55` |
| 5 | The commentary and "GOAL!" appear seconds away from the ball | Event beats are placed at `(minute+stoppage)*60`. | `ReplayDirector.cs:668-699` |
| 6 | Flicker back at every passage boundary | `onFrame` advances into passage N+1, then draws the *old* renderer at N+1's local time (≈0) before the effect rebuilds it. | `match-viewer.ts:475-500,772-787` |
| 7 | Overshoot and wobble | Uniform Catmull-Rom is used over keyframes that are not evenly spaced in time. | `keyframe-interpolator.ts:103-122` |
| 8 | Jumps after a slow frame; per-frame cost | No frame-delta clamp. The pitch is redrawn, the canvas size is read, and a Map is allocated every frame. | `render-loop.ts:37`; `canvas-match-renderer.ts:110-118`; `keyframe-interpolator.ts:149` |

### B. Engine bugs that change results (engine-v5)

| # | Issue | Where |
|---|---|---|
| 9 | **The second half kicks off at ~48–49'** and its regulation time runs 3–5 min short, because the clock is never reset at half-time. `TotalMinutesPlayed` counts first-half stoppage that the clock already used. The viewer clock has no notion of halves. | `MatchState.cs:192-205,249-254,383-385`; `PossessionSimulator.cs:43-47`; `match-presentation.ts:47-61` |
| 10 | **Stale restart flag.** `GoalAreaRestartSide` is consumed only when that side next has the ball, so the ball teleports to a goal area several possessions later. The existing test accepts any goal-area start. | `PassagePlanner.cs:125-130`; `PassageTests.cs:78-99` |
| 11 | **Restarts don't belong to anyone.** The side is re-drawn for every possession, so the scoring side can kick off, the shooting side can take the goal kick, and the fouling side can take the free kick. | `PossessionSimulator.cs:72,566-589` |
| 12 | **Shots never reach the goal.** The move *into* the shot point is labelled as the strike, and nothing records where the shot went. The keeper's save is stamped at the shooter's position. A penalty is recorded as a "shot" to the spot. | `PossessionSimulator.cs:111,123,200`; `ChanceSimulator.cs:240` |
| 13 | Odd geometry | Every attack that progresses funnels through one fixed central point. A headed goal from a corner is followed by a clearance. The full approach is recorded before an early loss of the ball. | `PossessionSimulator.cs:97,169,545-555` |

## M0 — Housekeeping

Copy this plan into the repo root as `engine-v5-fluid-match-film.md`, and point `plan.md`'s status note at it, as the last milestone did. Repo conventions:

- leave work uncommitted unless asked;
- put the changelog entry and the ADR in the same change set as the code;
- name behavioural tests so they cite rule IDs;
- put XML/JSDoc on public types;
- keep the build at zero warnings, and pass `dotnet format --verify-no-changes` and prettier;
- when reseeding, archive the database; never drop it.

## M1 — engine-v5 and a complete passage recorder (changes results; reseed)

1. **Half-time clock (`MAT-3`).** At the second-half kick-off, set `ClockSeconds = HalfTimeMinute * SecondsPerMinute`, so the second half runs 46'…90'+ with its own stoppage. Check that `TotalMinutesPlayed`, player minutes, `CaptureLiveMetrics` minute keys, and the substitution windows (46/58/…) still hold.
2. **Restart ownership (new `MAT-12` in `game-rules.md`).** Replace `RestartFromCentre` and `GoalAreaRestartSide` with one `state.NextRestart` value (side, kind, spot). It is set by dead-ball outcomes and **always cleared by the very next possession**, which takes its side without a `ChoosePossession` draw.

   | Restart | Who takes it, and where |
   |---|---|
   | Kick-off | Home in the first half, away in the second, the conceding side after a goal |
   | Keeper's ball or goal kick (after a save, a miss off target, or a missed penalty) | The defending side, from its goal area |
   | Free kick after a foul with no shot | The fouled side, from the foul spot |
   | Offside | The defending side, from the offside point |

   Loose balls — a block, a woodwork rebound, a cleared corner, a turnover — keep the contested `ChoosePossession`.
3. **Ball continuity after a strike.** `state.Ball` ends where play actually continues: the block point, the rebound point, the keeper or goal area, or the centre after a goal.
4. **Geometry semantics.** Use the per-possession geometry stream, with new draws appended after the existing ones.
   - Open play: replace the fixed `HeaderPoint` with a final-third entry point drawn between the pressure point and the shot point.
   - Record the strike as a `Shot` waypoint from the shot point to an outcome target:

     | Outcome | Target |
     |---|---|
     | Goal | Inside the goal mouth |
     | Saved | The keeper, near the line |
     | Off target | Wide or over, out of play |
     | Woodwork | The post or bar, then a rebound |
     | Blocked | A point 2–6 m in front of the shooter |

   - Record the keeper's `Save` touch at the save target.
   - Penalty: a placement (new `PassageWaypointKind.Restart`), then the strike.
   - Free kick: a placement, then a strike or a cross into the box.
   - Corner: out of play → placement at the flag → delivery → header → outcome. There is no clearance after a goal.
   - Scramble lost or failed progression: cut the recorded approach at a drawn point — near the start for a scramble, 40–80% of the way for a failed progression.
5. **Recorder fields (result-neutral; never draws from the play stream, never changes state).**
   - Take `StartClockSeconds` *before* the clock advances.
   - Add `Period` (1|2).
   - Add `Outcome` (a `PassageOutcome` enum: scramble lost / progression failed / creation failed / offside / foul / penalty / free kick struck or crossed / corner cleared or headed / open-play shot).
   - Add `Restart` (the kind of restart the possession began with).
   - Add `Events` as `(Sequence, FractionBasisPoints)` so each event has a position among the waypoints and touches. Keep `EventSequences` as a derived list.
   - Record touches for the scramble contestants and for the fouler and the fouled player.
   - The test that the result is identical with and without the recorder still guards all of this.
6. **Versions.** Set `EngineVersions.Engine = 5` and `RuleSet = 5` (`engine-v5` / `engine-rules-v5`), and re-pin the golden hashes in `DeterminismTests`.
7. **Keep replay-v3 working on the new data.** Make the minimal change to the half split in `ReplayDirector.Merge`: use `Period`, because match seconds are no longer monotonic across half-time. Make `ReplayDirectorTests.Passages_cover_windows_of_the_match_in_order` aware of the two halves for the same reason. Spans are now real, so the old film is already less bursty.
8. **New tests** (`tests/TouchlineManager.MatchEngine.Tests`):
   - the second half starts at 46' and its regulation lasts 45 min;
   - each restart is owned by the right side;
   - a goal-area start happens only immediately after a keeper ball or goal kick (no stale flags);
   - consecutive possessions join, except at restart placements;
   - `End > Start` for every possession, and the possessions tile each half;
   - events are ordered within a passage;
   - a goal's target is inside the goal mouth, a save is near the keeper, and a miss is out of play.

   Re-run `DistributionTests`, `CalibrationTests`, `SpatialPlayTests` and `TacticalInvariantTests`, and retune constants only if a band moves. **If a band cannot hold without distorting the model, stop and report.**
9. **Docs:** ADR-0053 (engine-v5: half-time clock, restart ownership, recorder timing); `docs/product/match-engine.md` §7; `game-rules.md` (MAT-3 wording, MAT-12); the changelog.
10. **Dev DB:** **stop and ask for confirmation first.** Then run `ALTER DATABASE touchline RENAME TO touchline_engine_v4_backup`, create a fresh DB, then `npm run migrate` and `npm run seed`. Note that engine-v4 matches cannot replay under v5 (`MatchSimulator.VerifyVersionAgreement`).

**Verify:** `dotnet build -c Release`, `dotnet format --verify-no-changes`, `dotnet test`, and `dotnet run --project tools/simulation-benchmarks -- all`. The goals, shots, cards, home-advantage and upset bands must hold, and stoppage minutes should look plausible.

## M2 — replay-v4: the constant-pace film (server; presentation only, results unchanged)

Split `Highlights/ReplayDirector.cs` into focused files under `Highlights/`. `Version = "replay-v4"`, and `ReplayDirector.Build` keeps the same signature.

1. **`FilmScript` — turn each possession into beats.**
   - Beat kinds: `Carry, Pass, LoftedPass, Cross, Header, Shot, Clearance, Duel, Save, Placement, Hold(kind)`, built from the recorder's `Outcome`, `Restart`, waypoints, touches and events.
   - Intermediate passes: the **receiver is the teammate who can reach the reception point soonest in the current shape**. Participants the engine named (duel carrier and tackler, shooter, header pair, keeper, free-kick or penalty taker, the player caught offside, the fouled player) take precedence at their beats.
   - Longer ground moves (≥ 12 m) become *receive → carry 3–10 m → pass*. A carry's length comes from a stable hash weighted by Dribbling, so the play shows "driving the ball".
   - A cross is only drawn from a wide final-third position into the box; anything else is a lofted pass.
   - **Continuity guard:** a possession that starts away from where the last one ended, without a restart, gets a transition beat at physical speed.
2. **`FilmTiming` — one pace for everything.**
   - Each beat's natural duration is `distance / speed(kind)` plus control time. Defaults: pass 15 m/s, lofted 20, cross 21, clearance 24, shot 27, header 14, carry 5–7 (by Dribbling), control 0.3 s.
   - Dead-ball holds have fixed film durations: restart 0.6–1.2 s, goal 4 s, card or sub 1 s, half-time card 3 s.
   - Solve **one global pace** `p = motionReal / (target − holds)` within the band **1.8–2.6×** (default about 2.2).
   - If `p` would exceed the band, condense the *quietest* possessions first: no event, ending outside the final third, and not within the two possessions before a chance. Condense by merging consecutive ground moves by the same side. Only then allow up to 3.0×, then shorten holds.
   - If `p` falls below 1.8, use 1.8 and lengthen holds toward the target. A film down to 9:00 is acceptable.
   - The film can never exceed 11:00. Put every constant in `HighlightOptionsV1`.
3. **`FilmMotion` — ball and players.**
   - **Ball:** sampled from the beats. Ground passes ease out; lofted passes, crosses and clearances get a parabolic `z`; a shot's `z` depends on its outcome.
   - **Players:** simulated in real-time units (0.1 s step) with bounded speed (shape moves 5.5 m/s, sprints 8 m/s) and acceleration (~4.5 m/s²), plus a short keeper-dive burst.
   - Each player's target comes from a new `FilmShape`:
     - it uses the side's **real instructions**;
     - it shifts toward the ball, about 40% along the pitch and 30% across;
     - the block is compact out of possession;
     - one or two pressers go to the ball carrier;
     - the keeper stands on the line between the ball and the goal.

     Base it on `TacticalFormationResolver.Orient` and leave the resolver itself alone, because `SpatialPlayTests` covers it.
   - **Hard constraints override the shape:**
     - the passer or carrier is at the ball;
     - the receiver reaches the reception point when the ball arrives;
     - the shooter, the header pair and the fouler/fouled pair are at their touch points;
     - the keeper is at the save point;
     - celebration and set-piece formations.

     If a constrained player cannot arrive in time, **extend the preceding beat; never exceed the speed cap.**
   - **Cuts:** resets that cannot fit at bounded speed are explicit cuts, done as a 300 ms crossfade under an overlay. These are the kick-off after a goal and the half-time reset. Cuts are listed in the presentation, and the fluidity metrics exclude them.
4. **Chunking.** Group possessions into passages of about 8–12 s of film, split at personnel changes and at the half, at most 75. Copy boundary keyframes exactly.

   Each passage carries `Period` and `Clock` keyframes, which map film time to match second: the possession's start, the possession's end second reached at its first event (so the displayed minute equals the event's stamped minute), and the end of the possession.
5. **Output.**
   - Sample players every 200 ms, and the ball every 100 ms while it is in the air.
   - Compress with `KeyframeCompressor`, using one tolerance per entity: players 50, ball 20. Action keyframes are always kept.
   - The ladder becomes: players' tolerance 50 → 70 → 90, sampling 200 → 300 → 400 ms. The estimate must stay ≤ 750 KB.
6. **Commentary.**
   - A line appears when its beat happens: when the pass is played, the cross is delivered, the shot is struck. The outcome line follows 0.6 s later.
   - At most about one line per 2.5 s of film; events always get their line.
   - Optional restart lines: kick-off, goal kick, free kick, corner. If templates change, the version becomes `commentary-v4`.
7. **Reel (`ReelBuilder`).** The lead-in is measured in match time (≈600 s, inside the same half) and then clamped to 25–70 s of film. A clip ends after the outcome plus a reaction hold. Overlapping clips merge, and the 12:00 cap and goal rules are unchanged.
8. **Contract.** `PassageV1`/`PassageResponse` gain `Period`, `Clock` and `Cuts`. Update `MatchMapping.cs`, `PresentationResponses.cs` and the web `match.models.ts`. The ETag pattern `{OutputHash}:{PresentationVersion}` stays as it is.
9. **Tests.** Rewrite `ReplayDirectorTests` and add `FilmMotionTests`, over 20–30 seeds:
   - the film is ≤ 11:00 and ≥ 9:00;
   - the pace is one value for the whole film;
   - **no teleports:** outside cuts, the ball moves no more than `shot speed × p / 60` per 1/60 s at 1x;
   - player speed is ≤ cap × p;
   - the carrier is within 1.5 m of the ball during a carry;
   - the receiver is at the ball when it arrives;
   - the keeper is near their own goal at a save;
   - a goal ends inside the goal mouth;
   - the clock is monotonic within a half, and the displayed minute at each event equals the event's minute;
   - the reel covers every goal with at least 25 s of lead-in;
   - the output is deterministic;
   - the payload stays within budget.

   Update `GetMatchPresentationTests` and `MatchTests` for the new fields.
10. **Benchmark (`tools/simulation-benchmarks`, `replay` mode).** Print:
    - the pace (p05/p50/p95);
    - film and reel minutes;
    - how much was condensed;
    - ball speed p50/p95 by beat kind;
    - teleport count;
    - the share of film time with the ball still outside holds;
    - the payload estimate and the real JSON bytes (System.Text.Json).

    Add `--dump <file>` to write one presentation JSON for the harness in M3.

**Verify:** `dotnet test`; `dotnet run --project tools/simulation-benchmarks -- replay 2000`. Expect 0 teleports, a still-ball share of about 5% or less, and the pace inside the band for ≥ 95% of matches.

## M3 — viewer: one continuous timeline (client)

1. **New `core/match/film-timeline.ts`, built once per presentation.**
   - Continuous tracks per entity across passages, offset by the schedule, with duplicate boundary frames removed.
   - Roster segments: who occupies each slot over time. A substitution switches the label, and a red card hides the token.
   - `clockAt(filmMs)`, aware of the halves: `45+2'`, `HT`, `46'`, `90+N'`.
   - Feed rows, card times, goal and shot markers, and cuts.
2. **`match-viewer.ts`.**
   - **Create one renderer per presentation.** Rebuild it only when the presentation, canvas, text-only mode or kits change, never when the passage changes.
   - `onFrame` renders global film time.
   - The clock, feed, cards and live panels are all driven from film time.
   - A 200 ms fade covers reel window jumps and cuts.
   - `match-playback.ts` keeps its modes, windows and speeds, and exposes film time.
3. **Interpolator.**
   - Players: a time-aware monotone cubic Hermite (Fritsch–Carlson) instead of uniform Catmull-Rom, so there is no overshoot and velocity stays continuous.
   - Ball: linear, because the server pre-samples flights.
   - Use a cursor per entity rather than a per-frame scan or Map.
   - Never blend across a cut.
4. **Loop and renderer performance.**
   - Clamp the frame delta in `RenderLoop` to ≤ 100 ms.
   - Draw the pitch on an offscreen canvas, redrawn only on resize.
   - Track the canvas size with a `ResizeObserver` instead of reading layout every frame.
   - Avoid allocations in the hot path.
   - Keep the current visuals: dots, shadow policy, dive streak, trails, celebration.
5. **Tests (vitest).**
   - Timeline continuity and roster switching.
   - The interpolator never overshoots and joins smoothly.
   - The playback modes.
   - The viewer keeps a single renderer instance across a passage boundary.
   - Clock labels, including `45+2'` and `46'`.
   - The `RenderLoop` delta clamp.

   Update `tests/web-e2e/matchday/matchday.spec.ts`: the clock advances, feed rows accumulate, both modes switch, and 8x still works.
6. **Fluidity harness.** Point `apps/web/.preview` (`serve.mjs`, `capture.mjs`) at a dumped presentation. In headless Chromium, report FPS, the maximum ball and player movement per frame outside cuts, and the still-ball share.
7. **Optional.** Brotli/Gzip compression scoped to `GET /api/v1/matches/{id}/presentation` only, which is BREACH-safe because it is not applied to auth responses. Nothing compresses JSON today.

**Verify:** `npm --prefix apps/web run build`, `npm --prefix apps/web run lint`, `npm --prefix apps/web run test:ci`, `npm run test:e2e:matchday`, `npm run test:e2e:a11y`. The harness should show ≥ 58 FPS on desktop and 0 frame jumps above the threshold outside cuts.

## M4 — Calibration, docs, demo

1. Tune the pace, speeds, holds and shape constants by eye in the harness and with the benchmark, over 2,000 matches: film p50 ≈ 10:00, max ≤ 11:00; reel ≤ 12:00; payload ≤ 750 KB; pace p50 ≈ 2.2×. Record the numbers in `match-engine.md` §10/§12.
2. Write ADR-0054 (replay-v4 constant-pace film; supersedes ADR-0052 §2–§3, the time warp and anchor tracks). Update:
   - `game-rules.md` §18: `replay_version`, plus new `match_film_pace` and `match_film_seconds` rows;
   - `data-model.md`: `presentation_version` and the engine-v5 reseed note;
   - `CHANGELOG.md`, `README.md` status, and `plan.md`.
3. Demo. Run `npm run dev`, create a matchday with the non-production trigger (ADR-0016), and give the user a match URL to watch: the full match at 1x, a goal, half-time, Highlights mode and 8x.

## Key files

| Area | Files |
|---|---|
| Engine (M1) | `src/TouchlineManager.MatchEngine/Simulation/{PossessionSimulator,MatchState,PassagePlanner,ChanceSimulator}.cs`, `Model/MatchPassage.cs`, `Configuration/EngineRulesV2.cs`, `EngineVersions.cs` |
| Replay (M2) | `Highlights/ReplayDirector.cs` → new `FilmScript.cs`, `FilmTiming.cs`, `FilmMotion.cs`, `FilmShape.cs`; `Highlights/{HighlightPresentation,ReelBuilder,KeyframeCompressor}.cs`; `Commentary/CommentaryTokenBuilder.cs` |
| Contract | `src/TouchlineManager.Contracts/Match/PresentationResponses.cs`, `src/TouchlineManager.Application/Match/{MatchMapping,GetMatchPresentation}.cs` |
| Web (M3) | `apps/web/src/app/core/match/{match.models,match-playback,match-presentation}.ts`, new `film-timeline.ts`; `features/match-viewer/{match-viewer.ts,match-viewer.html}`, `renderer/{keyframe-interpolator,render-loop,canvas-match-renderer,renderer-effects,pitch-layout}.ts` |
| Tests/tools | `tests/TouchlineManager.MatchEngine.Tests/{PassageTests,ReplayDirectorTests,DeterminismTests,TestMatchFactory}.cs`, new `FilmMotionTests.cs`; `tests/TouchlineManager.Application.Tests/Match/GetMatchPresentationTests.cs`; `tests/TouchlineManager.Api.IntegrationTests/MatchTests.cs`; `apps/web/**/*.spec.ts`; `tests/web-e2e/matchday/matchday.spec.ts`; `tools/simulation-benchmarks/Program.cs`; `apps/web/.preview/*` |

## Reuse rather than rewrite

- `KeyframeCompressor.Sample` and `.Compress`: RDP compression that keeps action keyframes. Add a tolerance per entity.
- `TacticalFormationResolver.Orient`, as the base for slot positions in `FilmShape`.
- From `ReplayDirector`: `PersonnelChanges`, `OccupantsAt`, `Names`, `Colours`, `PositionCode`, `StableHash`/`Jitter`, `Estimate`, and the payload ladder loop.
- `CommentaryTokenBuilder.BuildPassageCommentary`, `KindOf`, and the existing `match.build.*` families.
- `ReelBuilder`'s selection, merge and cap logic. Only the lead-in changes.
- The `PlayerLiveMetricsRecorder` and `AssistPlanner` patterns for side channels and derived streams.
- `MatchPlayback`'s windows, modes and speeds; the renderer's drawing routines; `renderer-effects.ts`; `.preview/capture.mjs`.

## Guardrails

- **Result neutrality of the recorder.** The test that the hash is identical with and without the recorder must stay green. Presentation code never draws from the play stream.
- **Calibration (M1).** Re-validate the bands; never widen them. If a band cannot hold, stop and report.
- **The 11:00 ceiling** is enforced by a test. A pathological match (many goals) raises the pace before it ever lengthens the film.
- **Payload.** If the estimate exceeds 750 KB after the ladder, stop and present measured numbers for a superseding ADR.
- **Reseed** only after explicit confirmation. Archive, never drop.
- **Out of scope:** new dependencies, video or image generation, rewinding after a goal, changes to the persisted schema.

## End-to-end verification

1. Server: `dotnet build -c Release`, `dotnet format --verify-no-changes`, `dotnet test`. Benchmarks: `-- all` (engine bands) and `-- replay 2000` (pace, film/reel minutes, teleports, still share, payload).
2. Client: build, lint, `test:ci`, `test:e2e:matchday`, `test:e2e:a11y`. Run the `.preview` harness on a dumped presentation and check FPS and per-frame jumps.
3. Manual, on the dev stack after the reseed: watch a full match at 1x for 3–4 minutes and check that:
   - the motion is even and nothing flickers at passage boundaries;
   - passes go player to player, carries and crosses show up, shots reach the goal or the keeper, and goals end in the net with a celebration and a kick-off by the conceding side;
   - the second half starts at 46';
   - the whole match takes about 10–11 minutes.

   Then switch to Highlights: each chance should come with about a minute of lead-in. Check that 8x stays smooth.
