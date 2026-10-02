# Continuous 2D Match Replay — engine-v4 progression, replay-v3 film, viewer rebuild

Implementation plan for the next milestone on `feature/engine-improves-freebuff`.
Repo conventions apply throughout: stop after each milestone for confirmation, leave work uncommitted unless asked, changelog/ADR in the same change set as code, behavioural test names citing rule IDs, XML/JSDoc on public types, zero-warning build plus `dotnet format` gate, archive-don't-drop when reseeding.

---

## 1. Why the replay looks broken today (verified in code)

| Symptom | Root cause |
|---|---|
| "The ball moves randomly" | `PossessionSimulator.AdvanceBall` (`src/TouchlineManager.MatchEngine/Simulation/PossessionSimulator.cs:190`) draws an **absolute** random point in the possessing side's own half (x 2,600–4,200 of 10,000, y uniform 0–7,000); every event inherits it via `MatchState.Emit` (`Simulation/MatchState.cs:224`). The director then invents one arc from a fixed build-up X (`Highlights/HighlightDirector.cs:637`) to that point and drifts all 21 other players at it. No pass, carry, cross or duel exists in the model. |
| Shots logged in a team's own half; direct free kicks never happen | `AttackingX` is the own-half ball x, so `attackingX >= FreeKickShootingRangeX (6_500)` (`Configuration/EngineRulesV2.cs:344`) is unreachable — `FreeKickShot` is dead code. Shot maps built from event positions would be nonsense. |
| Cuts between chances; no build-up | The replay is ≤24 chance snippets (10–25 s each) plus 3-keyframe "bridges" (`HighlightDirector.PlanBridges`/`BuildBridge`). The 5–10 min window is real; coverage of the match is not. |
| Sterile commentary | `Commentary/CommentaryTokenBuilder.BuildPassage` emits exactly three generic lines per passage; there are no per-pass/per-dribble facts to narrate. |
| Away team is squares | `CanvasMatchRenderer.drawPlayer` (`apps/web/src/app/features/match-viewer/renderer/canvas-match-renderer.ts:372-385`): `arc` for home, `fillRect`/`strokeRect` for away (a deliberate a11y choice, now overridden by the product owner). |
| Ball shadow always on; no jumps or dives | `drawBall` (`canvas-match-renderer.ts:433-460`) draws the shadow at every altitude; player keyframes never carry `z`; the `save` action tag draws nothing. |

## 2. Decisions confirmed with the user

1. **Two viewing modes over one dataset:**
   - **Full Match** (default): one continuous condensed film of the whole match. Target ≈10:00 at 1x; for example 90 min → 10:00, 95 min → ~10:35, hard ceiling 11:00 plus a ~3 s half-time card. Speed controls (0.5x / 1x / 2x / 4x / 8x) scale playback.
   - **Highlights**: selected chances, each preceded by a genuine lead-in from the immediately preceding play — up to the previous ~10 match-minutes of the film (≈65 s at the film's normal rate), played at 1x with the same speed controls, then cut to the next clip. This is the "understand where the team fell short" view. No FM-style rewind after goals; the lead-in is the mechanism.
2. **The build-up is simulated (engine-v4).** Each possession becomes a real pass/carry chain that starts where the last passage ended and ends in the final third. Accepted costs: engine/rules version bump, re-pinned golden hashes, recalibration, dev DB archive + reseed.
3. **Commentary as rows of text** (build-up beats), replacing both the chip mini-timeline and the single-line ticker.
4. **Renderer**: both teams are dots; FM/CM-era ball physics (short shadow only when airborne; short passes have none); headers show a jumping player with a ground shadow; keeper dives are fast lateral interceptions without a shadow.

## 3. Target architecture

### 3.1 Engine-v4 — possessions become passages (`src/TouchlineManager.MatchEngine`)

The simulation stays possession-based (no 22-player physics), but the ball finally moves like football:

- **Continuity.** Each possession starts where the previous one ended (`MatchState.Ball`), except restarts: centre after kick-off/HT/goals, corner flag after a corner, goal area after a goal kick/keeper claim. `AdvanceBall` and `Min/MaxPossessionAdvanceBasisPoints` are deleted.
- **Progression geometry.** A per-possession derived stream (same pattern as `AssistPlanner`: `seed * 1_000_003 + ordinal`) generates 3–8 touches that start deep, progress forward with lateral drift, and end at an outcome-appropriate final point:
  - open-play shot → final third by `ShotZone` (central / inside-wide / wide bands),
  - penalty → the penalty spot,
  - foul → the pressure point; when the foul is in the attacking half and in range, `FreeKickShot` now genuinely happens (retune `FreeKickAwardBasisPoints` / `FreeKickGoalBasisPoints` so the distribution bands hold),
  - corner → corner flag, offside → the offside line, plain turnover → middle third.
- `state.MoveBall(point[, altitude])` is called along the passage and before every `Emit`, so **event x/y become meaningful** (final-third shots, real foul/corner positions).
- **Derived stream only.** Outcome decisions keep their existing draw order and probabilities on the main `Pcg32`; geometry draws from the derived stream so calibration statistics stay comparable (bands are re-validated, not re-invented).
- **Recording.** New side-channel sink in the proven `PlayerLiveMetricsRecorder` style (`Model/PlayerLiveMetric.cs:37`): `MatchPassageRecorder` captures, per possession:
  - ordinal, side, `StartClockSeconds`/`EndClockSeconds`, emitted event sequence (if any),
  - ball waypoints `(fraction, x, y, z, kind)` with kinds `carry | pass | cross | shot | clearance`,
  - touches `(fraction, participantId, action, x, y, z)` using the participants the simulation actually picked (duel carrier/tackler, shooter, keeper, corner header winner, etc.),
  - altitude for crosses/shots/headers.
- `MatchSimulator.Simulate(input, rules, liveMetrics, passages)` — recorders stay optional, consume no draws, and never change the result (test: hashes identical with and without).
- Version bump: `EngineVersions.Engine = 4`, `EngineLabel = "engine-v4"`, `RuleSet = 4`, `RuleSetLabel = "engine-rules-v4"`; golden hashes re-pinned in `DeterminismTests`.

### 3.2 Replay-v3 — one film, two modes (`Highlights/ReplayDirector.cs`)

Rename `HighlightDirector` → `ReplayDirector` (matches the existing `ReplayDirectorTests`), `Version = "replay-v3"`.

- **Passages.** Merge recorded possessions into film passages with ~8–15 s of playback each (target ~55–75 passages per match) so per-passage entity overhead fits the budget; split at substitutions, half-time and bookings of personnel. Each passage is self-contained: entity list (current XI + ball, so substitutes appear), tracks, commentary tokens, `StartMatchSecond`/`EndMatchSecond`, `EventSequences`, `OutcomeCode` (`goal` if it contains one, else the shot outcome, else `play`).
- **Pacing (time warp).** `targetFilmMs = clamp(totalMatchSeconds * 1000 / 9, 9:30, 11:00)`. Weight each passage: base 1.0, ×2.0 goal, ×1.5 shot, ×1.25 final-third entry, ×0.8 middle-third turnover; normalise to the target with a ~1.2 s floor. This is what makes chances readable while dull spells fly by.
- **Tracks.** Off-ball shape sampled from `TacticalFormationResolver.ResolvePosition(slot, isHome, hasPossession, ballPosition, instructions, rules)` over the **real ball path** (plus small deterministic jitter), so the block moves up and down the pitch with the play. Involved players overwrite with their recorded touch paths (carrier runs, receivers meet passes, shooter's run, keeper angle). Boundary frames are copied exactly so passages join seamlessly — the "bridge" concept disappears.
- **Actions** (keyframe-only strings; free vocabulary, nothing hashed): `run | carry | pass | receive | cross | header | tackle | interception | shot | penalty | free_kick | save | dive | celebrate`.
- **Highlights reel.** Server-side clip list: goals always, plus the best chances by `QualityBasisPoints` (reuse the 700 bp floor), count-capped (~12). Clip = [chancePassage.start − leadIn, chancePassage.end], leadIn covers up to 600 match-seconds of film (~65 s), clamped 25–70 s; overlaps merged; if the reel exceeds 12:00, shorten lead-ins to the floor first, then drop the lowest-quality non-goal clips. Deterministic.
- **Commentary (`commentary-v3`).** Extend `CommentaryTokenBuilder` with build-up families — `match.build.pass`, `match.build.carry`, `match.build.dribble`, `match.build.cross`, `match.build.header`, `match.build.tackle`, `match.build.interception`, `match.build.save`, `match.build.chance` — 3–5 variants each, deterministic variant selection, parameters stay name/value fact pairs (player names, club, clock; never hidden values — `MAT-11`). Policy: emit build-up beats (every meaningful touch: progressive passes, carries, dribbles, crosses, tackles, shots, saves), skip filler square passes; roughly a row every few seconds of film. Full-match log gets the same lines; passage tokens are offset into playback ms as today.
- **Payload.** Estimate accounting stays (entities×48 + keyframes×24 + commentary + schedule + lineups + metrics; keep the 750 KB ADR-0006 budget). Add an adaptive ladder: if the estimate exceeds budget, recompress tracks at tolerance 32 → 40 → 48 and sample interval 400 → 600 → 800 ms until it fits (deterministic, chance passages compressed last). Update `tools/simulation-benchmarks` to print film minutes, reel minutes and payload percentiles.
- **Contract (replay-v3).** Presentation is re-derived, never stored, so this is a clean change:
  - `MatchPresentationResponse`: `Highlights` → `Passages: IReadOnlyList<PassageResponse>`; new `Reel: IReadOnlyList<ReelClipResponse>`; `Bridges` removed; `Playback` is one segment per passage (`kind: "passage"`).
  - `PassageResponse` = today's `HighlightResponse` + `StartMatchSecond`, `EndMatchSecond`, `EventSequences` (`HighlightKeyframeResponse`/`HighlightTrackResponse`/`HighlightEntityResponse` keep their names to limit churn).
  - `ReelClipResponse`: `SourceEventSequence`, `OutcomeCode`, `Minute`, `StoppageMinute`, `StartMilliseconds`, `EndMilliseconds`.
  - ETag (`apps/api/.../Match/MatchEndpoints.cs:83`, `Application/Match/GetMatchPresentation.cs:81`) becomes `{OutputHash}:{PresentationVersion}` so future replay revisions invalidate cached payloads; update `GetMatchPresentationTests` and `MatchTests` accordingly.

### 3.3 Viewer — timeline playback, feed, renderer (`apps/web`)

- **Playback core (`core/match/match-playback.ts`).** Replace passage-stepping with a global film timeline: segments from `presentation.passages` (+ `presentation.playback` offsets), `position` in film ms, active passage lookup, `mode: 'full' | 'reel'`, reel playlist assembled from `reel` clips (clip = ordered passage range, overlaps merged), speeds `[0.5, 1, 2, 4, 8]`, seek by event or passage, `totalPlaybackMilliseconds`. Keep `advance(delta)` semantics and all existing unit tests re-expressed for the timeline.
- **Clock (`match-viewer.ts`).** Continuous from the active passage's match window — `67'`, becoming `90+2'` in stoppage; publish only when the label changes (signals stay off the 60 Hz path).
- **Commentary feed** (replaces chips at `match-viewer.html:448-475` and ticker at `:605-629`): scrolling rows under the pitch — minute label + text, goals highlighted, side accent — appended as the playhead passes each row's film time; auto-scroll while pinned to the bottom, "jump to live" control when scrolled up; `role="log"` with polite announcements. Report tab keeps the full log and its "Watch" buttons (now seek into the film/reel).
- **Progress bar:** slim scrubber with goal and shot markers replaces the chip timeline (seek by click/drag/keyboard), showing elapsed and total film time.
- **Modes:** single control "Full match | Highlights" replaces "Condensed | Highlights"; no empty state ("no highlights") — every match is watchable.
- **Renderer (`renderer/`):**
  - both teams drawn as circles with shirt numbers; distinguish by kit colour plus border treatment (home white border, away dark ink) and the existing name tag;
  - `pitch-layout.ts`: `altitudeLift` drops from 0.3 × height to ~0.12 × height, `altitudeScale` tops out ~1.35;
  - ball shadow only when airborne (`z` above ~10) — a small ellipse that shrinks/fades with height; **none for ground passes**; trail only for shots/crosses (raise the `trailStrength` gate);
  - player `z`: jumping headers lift the token and draw a ground shadow; keeper `dive`/`save` moves fast laterally with no shadow (a short streak is enough);
  - action vocabulary wired through `renderer-effects.ts` (clash ring now fires on real `tackle`/`duel` actions).
- Accessibility preserved: text-only mode, `prefers-reduced-motion`, narration outside the canvas, keyboard-operable feed/scrubber; the axe route list already covers the match route.

## 4. Milestones (stop and confirm after each)

### M1 — engine-v4 progression + passage recording

**Work**
1. `Configuration/EngineRulesV2.cs`: replace the advance band with progression rules (pass count, touch distances, lateral drift, per-zone final-third bands, cross/header share, foul-zone distribution); keep `FreeKickShootingRangeX` meaningful; validate ranges.
2. New `Model/MatchPassage.cs`: `PassageWaypointV1`, `PassageTouchV1`, `MatchPassageV1`, public `MatchPassageRecorder` (sink pattern).
3. `Simulation/PossessionSimulator.cs`: delete `AdvanceBall`; add passage construction from a derived per-possession stream; restarts; `MoveBall` at each touch and before each `Emit`; feed the recorder.
4. `Simulation/MatchState.cs` + `MatchSimulator.cs`: recorder plumbing, version agreement unaffected.
5. `EngineVersions.cs`: engine-v4 / engine-rules-v4.
6. Tests (`tests/TouchlineManager.MatchEngine.Tests/`): re-pin `DeterminismTests` golden/config hashes; new invariants — ball continuity between possessions, shots and free-kick shots in the attacking half/final third, passage points inside the pitch, touches reference on-pitch participants, recorder determinism, hashes identical with/without recorder; re-run `DistributionTests` / `CalibrationTests` / `SpatialPlayTests` and retune the FK constants if the bands move.
7. Archive + reseed the dev database (stop and confirm; `ALTER DATABASE touchline RENAME TO touchline_engine_v3_backup`, fresh DB, migrate, seed). Note clearly: stored engine-v3 matches cannot be re-simulated and will not present after this change.

**Verify**: `dotnet build -c Release`, `dotnet format --verify-no-changes`, `dotnet test tests/TouchlineManager.MatchEngine.Tests`, benchmark printout (goals/shots bands unchanged; FK shots now occur).

### M2 — replay-v3 film, reel and commentary (server)

**Work**
1. `Highlights/HighlightDirector.cs` → `Highlights/ReplayDirector.cs`, `replay-v3`: passage merging, pacing/time warp, track construction (shape + touches + keeper + headers/crosses with `z`), actions, boundary continuity.
2. `Commentary/CommentaryTokenBuilder.cs` → `commentary-v3`: build-up template families, beat-selection policy, full log + passage tokens.
3. Reel builder (selection, lead-in windows, clipping, bounds, determinism).
4. `Highlights/HighlightPresentation.cs` and payload estimation/adaptive compression ladder.
5. `Contracts/Match/PresentationResponses.cs`, `Application/Match/MatchMapping.cs`, `Application/Match/GetMatchPresentation.cs` (recorder wiring, ETag with presentation version), API/integration/application tests updated (`MatchTests`, `GetMatchPresentationTests` — realistic payload bounds, 11-minute ceiling, passage continuity, reel bounds, commentary determinism).

**Verify**: `dotnet test` full suite; benchmark payload/film/reel percentiles within budget; inspect one generated match's JSON (film flows, comments readable).

### M3 — viewer and renderer (client)

**Work**
1. `core/match/match.models.ts`: `Passage`, `ReelClip`, renamed fields.
2. `core/match/match-playback.ts` rewrite (global timeline, modes, speeds, seek, reel playlist) + specs.
3. `match-viewer.ts` + `match-viewer.html`: modes control, continuous clock, commentary feed, scrubber with markers, no empty state, report-tab seek, text-only/reduced-motion, `metricMinute` from the timeline.
4. Renderer: dots for both teams, ball lift/shadow policy, player jump shadows, keeper dive, action vocabulary, `pitch-layout.ts` altitude tuning; renderer/effects specs updated (`canvas-match-renderer.spec.ts`, `pitch-layout.spec.ts`, `renderer-effects.spec.ts`).
5. E2E (`tests/web-e2e/…/matchday.spec.ts` and match-viewer journey): feed rows accumulate, clock advances, both modes switch, 8x still fast; a11y gate green.

**Verify**: `npm --prefix apps/web run build`, `npm --prefix apps/web run lint`, `npm --prefix apps/web run test:ci`, `npm run test:e2e:matchday`, `npm run test:e2e:a11y`; manual 60 FPS check via Chrome DevTools.

### M4 — calibration, docs, hands-on demo

**Work**
1. Monte Carlo benchmark: goals/shots/cards bands, film/reel durations (≤11:00 full, ≤12:00 reel), payload ≤ 750 KB estimate; record numbers in the docs.
2. New ADRs (next free numbers): engine-v4 progression/passage recording; replay-v3 film/reel/budgets. Update `docs/product/match-engine.md` (still describes engine-v2), `game-rules.md` config table, `data-model.md` if needed, `CHANGELOG.md` new stage section, `README.md` status, and root `plan.md`.
3. Bring the dev stack up, manufacture a demo matchday through the real diagnostics trigger, and hand over URL + credentials with a walkthrough (full match, highlights, speed controls, feed), then undo state per repo practice.

## 5. Key files

| Area | Files |
|---|---|
| Engine | `src/TouchlineManager.MatchEngine/Simulation/PossessionSimulator.cs`, `Simulation/MatchState.cs`, `MatchSimulator.cs`, `Configuration/EngineRulesV2.cs`, `EngineVersions.cs`, new `Model/MatchPassage.cs` |
| Replay | `Highlights/HighlightDirector.cs` → `ReplayDirector.cs`, `Highlights/HighlightPresentation.cs`, `Highlights/KeyframeCompressor.cs`, `Highlights/MatchLineupBuilder.cs`, `Commentary/CommentaryTokenBuilder.cs` |
| API | `src/TouchlineManager.Contracts/Match/PresentationResponses.cs`, `src/TouchlineManager.Application/Match/{GetMatchPresentation,MatchMapping}.cs`, `apps/api/TouchlineManager.Api/Match/MatchEndpoints.cs` |
| Web | `apps/web/src/app/core/match/*`, `apps/web/src/app/features/match-viewer/{match-viewer.ts,match-viewer.html,renderer/*}` |
| Tests | `tests/TouchlineManager.MatchEngine.Tests/*`, `tests/TouchlineManager.Application.Tests/Match/GetMatchPresentationTests.cs`, `tests/TouchlineManager.Api.IntegrationTests/MatchTests.cs`, `apps/web/src/app/**/*.spec.ts`, `tests/web-e2e/*` |
| Docs/tools | `docs/architecture/adr/*`, `docs/product/match-engine.md`, `docs/product/game-rules.md`, `CHANGELOG.md`, `README.md`, `plan.md`, `tools/simulation-benchmarks/Program.cs` |

## 6. Risks and guardrails

- **Calibration drift (M1)** — enabling real free-kick shots and final-third shot locations can move distribution bands. Retune the affected constants against `DistributionTests`/`CalibrationTests`; if a band cannot hold without distorting the model, stop and report rather than widening the band.
- **Payload (M2)** — a full-match film is close to the 750 KB estimate limit. The adaptive ladder is the mechanism; if the estimate still cannot fit, stop and present the option of a superseding ADR that raises `match_presentation_payload_budget_kb` with measured numbers.
- **Determinism** — recorders must never consume main-stream draws; the with/without-recorder hash equality test is the guard.
- **History** — engine-v4 invalidates stored matches (dev only, no production). Archive + reseed with explicit confirmation; never drop.
- **"Only 11 minutes"** — a hard ceiling test on the full-mode film and a hard cap on the reel.

## 7. Explicitly out of scope

No persisted schema or migration changes; no new dependencies; no video/image generation; no post-goal rewind; no changes to match outcome logic beyond the spatial progression and the constants the FK fix forces.
