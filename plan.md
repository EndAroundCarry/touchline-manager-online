# Touchline Manager: 2D Match Engine & Replay Center Master Plan (`plan.md`)

This document outlines the complete specification, mathematical modeling, and staged execution plan to recreate the **Match Engine** and **Match Viewer** for Touchline Manager. The system is inspired by the iconic **Football Manager** (Sports Interactive) and **Championship Manager** engines.

---

## 1. Vision & Architecture Overview

### 1.1 Core Requirements
- **Match Simulation**: Full 90+ injury minute football match simulated deterministically on the backend.
- **Asynchronous Execution & Replay**: Matches are pre-calculated by a scheduled worker or trigger at the appointed time. Once complete, the user clicks **"Watch Match"** to play back the recording.
- **Playback Duration**: Condensed replay lasting **5–10 minutes** at 1x speed (with controls for 1x, 2x, 4x, 8x, pause, and skip to next highlight).
- **Match Engine Complexity**:
  - Spatial 2D coordinates on a FIFA-standard pitch ($10,000 \times 7,000$ normalized units).
  - 22-player positioning, formations, and off-the-ball runs.
  - Duels: 1v1 dribbles, tackles, aerial headers, loose ball scrambles, shots vs goalkeepers.
  - Dynamic tactical imbalances: Sent-off players leave tactical voids; fatigue degrades physical & mental attributes; injuries force immediate substitutions or hobbling play.
  - Home turf advantage factor (+4% duel success, crowd pressure, stamina resilience).
  - Controlled stochastic variance (~15% underdog factor) while tactics and player skills decide ~85% of outcomes.
  - Dynamic live player match ratings (6.0 baseline, fluctuating in real time based on on-pitch actions).
- **Match Viewer UI (FM / CM Look & Feel)**:
  - **Top**: Scoreboard with club kits, crests, digital match clock (00:00 to 90:00+), live event summary, and playback controls.
  - **Center**: 2D grass pitch with alternating cut stripes, 22 player tokens with shirt numbers & kit colors, dynamic ball with altitude scaling/shadow, and visual clash/shot/card effects.
  - **Left Panel**: Home team lineup with positions, live condition % bars, dynamic color-coded ratings, cards, and bench.
  - **Right Panel**: Away team lineup with identical live metrics.
  - **Bottom Ticker**: Authentic Championship Manager single-line commentary ticker that smoothly overwrites line by line as the action unfolds.
  - **Tabs**: Replay Pitch, Full Chronological Commentary Log, Team Match Statistics & Shot Map, and Player Performance Matrix.

---

## 2. Staged Implementation Roadmap

The implementation is broken down into **7 modular, self-contained stages**. You can pause development after any stage.

```
Stage 1: Versioned Contracts & Transport Schema
   ↓
Stage 2: Spatial 2D Match Engine (Math, Physics, Duels, Ratings)
   ↓
Stage 3: Replay Director & Semantic Keyframe Synthesizer
   ↓
Stage 4: Backend API, Persistence & Scheduled Worker Integration
   ↓
Stage 5: Retro FM/CM Match Center UI (Angular & TailwindCSS)
   ↓
Stage 6: High-Performance HTML5 2D Canvas Match Renderer (60 FPS)
   ↓
Stage 7: Calibration, Validation Suite & Final Polish
```

---

### Stage 1: Versioned Contracts & Transport Schema

**Goal**: Establish versioned data contracts for `engine-v3` and `replay-v2` across both backend (.NET 10) and frontend (Angular) while maintaining backward compatibility with past matches.

#### Deliverables:
1. **Backend Contracts (`TouchlineManager.Contracts/Match`)**:
   - `HighlightKeyframeResponse`: Enhance with `int Z` (ball altitude 0–100), `int Speed`, and `string? Action` (e.g. tackle, shot, pass, header).
   - `HighlightEntityResponse`: Add `string? Name` and `string? Position` (e.g. GK, DC, MC, ST).
   - `MatchLineupPlayerResponse`: Full player record (ParticipantId, PlayerId, ShirtNumber, Name, Position, Family, IsStarter, SlotNumber, KickoffCondition, FinalCondition, FinalRating, Goals, Assists, YellowCards, SentOff, SubbedOutMinute, SubbedInMinute, IsInjured).
   - `MatchLineupResponse`: ClubName, ShortName, PrimaryColour, SecondaryColour, Formation, Starters, Bench.
   - `PlayerLiveMetricResponse`: Minute-by-minute condition and rating snapshots (`ParticipantId`, `Minute`, `ConditionBasisPoints`, `RatingBasisPoints`).
   - `MatchPresentationResponse`: Add optional `HomeLineup`, `AwayLineup`, and `LiveMetrics`.
2. **Frontend Models (`apps/web/src/app/core/match`)**:
   - Update `match.models.ts` with matching TypeScript interfaces (`MatchLineup`, `MatchLineupPlayer`, `PlayerLiveMetric`, `ReplayKeyframe`).
   - Add formatting helpers in `match-presentation.ts` for live ratings (`formatMatchRating`), condition percentage (`formatConditionPercent`), and color classes.

#### Pausing Checkpoint:
- C# builds cleanly with zero warnings (`dotnet build TouchlineManager.slnx`).
- Angular tests pass (`npm --prefix apps/web test:ci`).
- No gameplay logic changed; wire contracts ready.

---

### Stage 2: Spatial 2D Match Engine (`TouchlineManager.MatchEngine v3`)

**Goal**: Build the core spatial simulation engine that executes a 90+ minute match on a 2D pitch coordinate system ($10,000 \times 7,000$), calculating player movements, duels, passing decisions, set pieces, fatigue, and tactical adjustments.

#### Deliverables:
1. **Spatial Mathematics & Pitch Model**:
   - `SpatialPitch.cs`: Normalized pitch coordinates ($10,000 \times 7,000$), distance calculations, passing lane checks, goal geometry.
   - `TacticalFormationResolver.cs`: Computes dynamic attacking and defensive positions for all 11 slots based on ball location, team mentality, defensive line, and width.
2. **Duels & Physical Contests**:
   - `DuelResolver.cs`:
     - **1v1 Ground Duels**: Dribbling, Agility, Pace vs Tackling, Positioning, Strength.
     - **Aerial Duels**: Jumping Reach, Heading, Strength.
     - **Loose Ball Scrambles**: Pace, Acceleration, Work Rate.
     - **Home Turf Factor**: +4% bonus to home team duel success, stamina resilience, and 50/50 referee whistle bias.
     - **Underdog Variance**: Controlled stochastic factor ($\pm 12\%$) allowing upsets while skills and tactics govern ~85% of matches.
3. **Ball Aerodynamics & Trajectories**:
   - `BallPhysics.cs`: Trajectories for ground passes, lofted crosses, swerving shots, post deflections, and keeper parries.
4. **Dynamic Imbalances & Real-Time Recalculation**:
   - `TacticalImbalanceHandler.cs`: When a player is sent off or injured without a sub, their positional zone is left open. Teammates must cover extra distance (+25% stamina drain) and opponents exploit numerical overloads.
   - `FatigueManager.cs`: Continuous condition degradation based on Stamina and Match Tempo. Fatigue scales down Pace and Composure, triggering late-game defensive mistakes.
5. **Set Piece Engine**:
   - `SetPieceDirector.cs`: Penalties (box cleared, taker placement, keeper reaction dive), Direct Free Kicks (defensive wall at 9.15m, curler, dive), Corners (near/far post runners, zonal defending, contested header).
6. **Live Dynamic Player Ratings**:
   - `LiveRatingAccumulator.cs`: Starts every player at 6.0; updates dynamically on completed passes (+0.03), key passes (+0.30), tackles (+0.12), interceptions (+0.08), shots on target (+0.10), goals (+0.80), saves (+0.25), missed tackles (-0.08), errors (-0.60), and cards.

#### Pausing Checkpoint:
- Determinism tests verify byte-for-byte identical output hashes across 20 simulation runs with the same seed.
- 1,000 match Monte Carlo test verifies realistic distributions (goals, cards, home win rate 44–48%).

---

### Stage 3: Replay Director & Semantic Keyframe Synthesizer

**Goal**: Transform raw simulation output into an efficient keyframe recording that drives the **5–10 minute condensed match replay** with coordinated 22-player animations.

#### Deliverables:
1. **Coordinated Team Movement**:
   - All 22 players shift dynamically with the play (defensive line pushing up or dropping off, fullbacks providing overlap, midfielders tracking runners, goalkeepers angling towards the ball).
2. **Condensed Playback Director**:
   - Highlights represent full 10–25 second passages of play (buildup -> progression -> duel -> cross/shot -> save/goal -> celebration).
   - Midfield recycling between major events is bridged smoothly, condensing the 90 minutes into a 5–10 minute viewing experience.
3. **Delta Compression & Payload Budget**:
   - Emit keyframes only on changes in velocity/direction ($dt \approx 300\text{ms} - 500\text{ms}$), keeping the total compressed match payload under **750 KB** (ADR-0006).
4. **Synchronized Commentary Tokens**:
   - Contextual commentary tokens generated with millisecond timestamps to synchronize with the on-pitch action and bottom ticker.

#### Pausing Checkpoint:
- Keyframe boundary tests verify all entity coordinates remain strictly within pitch boundaries.
- Payload budget tests verify serialized presentation is well within 750 KB.

---

### Stage 4: Backend API, Persistence & Scheduled Worker

**Goal**: Wire the new engine into the application pipeline, scheduled matchday worker, and REST API.

#### Deliverables:
1. **Matchday Worker Integration**:
   - Update `ResolveMatchday.cs` to execute the spatial simulator on schedule at kickoff time.
   - Staged results transition to `published` status once generated, enabling the "Watch Match" button for managers.
2. **Presentation Query & Caching**:
   - Update `GetMatchPresentation.cs` to return the new replay presentation with lineups and live metrics.
   - Cache immutable replays with strong HTTP ETags (`If-None-Match` -> 304 Not Modified).

#### Pausing Checkpoint:
- Integration tests confirm full division matchday (9 fixtures) simulates in under 2 seconds.
- REST API endpoint `GET /api/v1/matches/{id}/presentation` responds with full replay data and valid ETag.

---

### Stage 5: Retro FM/CM Match Center UI (Angular & TailwindCSS)

**Goal**: Redesign the match viewer in Angular with the authentic, immersive aesthetic of **Football Manager / Championship Manager**.

#### Deliverables:
1. **Layout Architecture (`lg:grid-cols-[280px_1fr_280px]`)**:
   - **Top Scoreboard**: Team crests/kits, digital clock (00:00 to 90:00+), goalscorers, playback toolbar (Play/Pause, Replay, 1x/2x/4x/8x speed, Condensed vs Highlights toggle).
   - **Center Stage**: 2D Pitch container ($105:68$ aspect ratio).
   - **Left Panel (Home)**: Lineup table with Shirt #, Position badge, Name, Condition bar, live colored rating badge (`6.8`, `7.4`, `8.2`), cards, and substitutes bench.
   - **Right Panel (Away)**: Symmetrical lineup panel for the away team.
   - **Bottom Ticker**: Single-line commentary ticker that smoothly overwrites with typewriter or fade transition as action progresses.
2. **Multi-Tab Views**:
   - **Tab 1: 2D Pitch Replay** (active pitch + live panels).
   - **Tab 2: Full Match Report** (complete chronological commentary log with filterable event tags).
   - **Tab 3: Match Statistics & Shot Map** (possession bars, shots on/off target, fouls, corners, xG).
   - **Tab 4: Player Performance Matrix** (passes, tackles won %, duels won, final rating).

#### Pausing Checkpoint:
- Visual inspection confirms dark tactical FM styling, clean responsive layout on desktop and mobile.
- Reactive Angular signals update clock, scoreboard, and lineups without unnecessary re-renders.

---

### Stage 6: High-Performance 2D Canvas Match Renderer & Motion Engine

**Goal**: Build the 60+ FPS HTML5 Canvas renderer with smooth spline interpolation, ball altitude physics, and visual action effects.

#### Deliverables:
1. **Pitch & Player Graphics**:
   - Grass pitch with alternating cut stripes, penalty boxes, center circle, penalty spot, corner arcs, and goal nets.
   - Player circular tokens with kit colors, contrasting borders, and centered shirt numbers.
   - Distinct goalkeeper kit color.
   - Player name tags on hover or ball possession.
2. **Ball Mechanics & 3D Flight**:
   - White/black dual-tone ball with ground shadow.
   - Dynamic altitude scaling: ball expands and rises above shadow during crosses, high shots, and aerial clearances.
   - Motion trail during high-velocity shots.
3. **Visual Action Indicators**:
   - **Tackling**: Subtle clash ring ripple when two players contest a duel.
   - **Shot on Goal**: Dynamic projectile line.
   - **Referee Cards**: Yellow/red card badge floating above offending player token.
   - **Goal Celebration**: Pulsating flash and "GOAL!" badge overlay.
4. **Playback & Interpolation Engine**:
   - Smooth Catmull-Rom spline interpolation between keyframes.
   - `RenderLoop` decoupled from Angular change detection, running on `requestAnimationFrame` and auto-pausing when tab is hidden.
   - Accessibility: Text-only mode and `prefers-reduced-motion` compliance.

#### Pausing Checkpoint:
- 60 FPS verified via Chrome DevTools with zero memory leaks.
- Playback controls (speed 1x/2x/4x/8x, seek, skip) work smoothly.

---

### Stage 7: Tuning, Validation Suite & Final Polish

**Goal**: Calibrate gameplay parameters, verify tactical integrity, run statistical benchmarks, and execute end-to-end tests.

#### Deliverables:
1. **Monte Carlo Calibration (10,000 matches)**:
   - Verify realistic scorelines, upset frequency (~15%), and home advantage (+4% win rate boost).
2. **Tactical Invariant Tests**:
   - Red card teams before minute 30 suffer an average goal differential drop of $-1.2$.
   - High-tempo pressing teams experience higher fatigue by minute 80.
   - Fresh substitutes demonstrate measurable pace advantages over fatigued defenders.
3. **E2E Playwright Tests**:
   - Complete browser flow: navigate to match -> click "Watch Match" -> canvas renders -> clock advances -> ratings fluctuate -> commentary updates -> tabs switch.

#### Pausing Checkpoint:
- All unit, integration, architecture, and E2E test suites green.
- Feature ready for production deployment.

---

## 3. Work Breakdown & Safety Matrix

| Stage | Focus Area | Primary Tech Stack | Complexity | Pausing Safety |
|---|---|---|---|---|
| **Stage 1** | Contracts, DTOs & Models | C# (.NET 10), TypeScript | Low | 🟢 Safe (Non-breaking additive) |
| **Stage 2** | Spatial Pitch Engine & Duels | C# (Math, Physics, PRNG) | High | 🟢 Safe (Pure library with tests) |
| **Stage 3** | Replay Director & Keyframes | C# (Algorithms, Compression) | Medium | 🟢 Safe (Isolated director) |
| **Stage 4** | API, Persistence & Worker | C#, EF Core, PostgreSQL | Medium | 🟢 Safe (Backend integration) |
| **Stage 5** | FM Match Viewer UI Shell | Angular 22, TailwindCSS v4 | Medium | 🟢 Safe (UI shell & state) |
| **Stage 6** | 2D Canvas Match Renderer | TypeScript, Canvas 2D API | High | 🟢 Safe (Canvas renderer) |
| **Stage 7** | Tuning & E2E Validation | Playwright, xUnit, Benchmarks | Medium | 🟢 Complete Release |
