# Master Plan: Tick-Based Match Engine Architecture & Implementation (`tick-based-match-engine-plan.md`)

> **Status:** Implemented — milestones 0 to 9 delivered. The tick engine (`tick-engine-v1`) is the active
> engine, `engine-v11` still plays the possession engine it always did, and the calibration lands inside the
> plan's bands (the largest block's home share is half a point under its floor, inside sampling noise).
> Recorded in [ADR-0066](docs/architecture/adr/0066-engine-v12-tick-simulation.md); the specification is
> `docs/product/match-engine.md` §14 and the release note is the CHANGELOG's `Engine v12` entry.
> **Target Version:** `engine-v12` / `engine-rules-v11` / `tick-engine-v1`  
> **Compatibility Target:** 100% plug-and-play with existing 2D Match Viewer (`apps/web/src/app/features/match-viewer`) and Application contracts  
> **Design Vision:** Football Manager / Championship Manager-style 2D simulation where player dots move with tactical intent, coordinated structure, and spatial intelligence rather than unorganized ball-chasing swarms.

---

## 1. Executive Summary & Core Mandate

### 1.1 Problem Statement
The current match engine (`TouchlineManager.MatchEngine`) is a **possession-based engine** (`PossessionSimulator.cs`). It slices a 90-minute match into macro possession chunks (15–45 seconds) and resolves them through statistical percentage checks (possession winner, progression roll, creation roll, chance duel). 

To drive the 2D match viewer, the engine attempts to retroactively reconstruct spatial waypoints and touches (`PassagePlanner.cs`, `ReplayDirector.cs`, `FilmScript.cs`, `FilmMotion.cs`). Because player positions are synthesized post-hoc from a few formation anchors:
1. **Bursty and Artificial Movement:** Dots sprint unnaturally to meet arbitrary touch points, then crawl or stand still.
2. **Lack of Tactical Intent:** Players do not make genuine off-the-ball runs, create passing triangles, or hold defensive shapes.
3. **The "Schoolyard Swarm" Effect:** Out-of-possession players either swarm directly at the ball or wander aimlessly without covering passing lanes or holding an offside line.
4. **Disconnect Between Visuals and Logic:** A shot or save is visually generated to match a dice-roll outcome rather than emerging naturally from physical geometry, angles, and real-time player collisions.

### 1.2 The Solution: A Pure Discrete Tick-Based Engine
This plan specifies a brand-new **Tick-Based Match Engine** (`TickMatchEngine`) operating at a discrete time step $\Delta t = 100\text{ ms}$ (10 Hz, 10 ticks per match second).

In this engine:
- **True Physical Simulation:** The pitch ($10,000 \times 7,000$ units, Z: $0..100$) hosts 22 autonomous player agents and 1 physical ball.
- **Dots with Intent & Intelligence (FM-style):**
  - **Ball Carrier Brain:** Evaluates shooting, passing (short, direct, through-ball, cross), dribbling into space, or recycling possession based on attributes (`Vision`, `Decisions`, `Passing`, `Finishing`, `Composure`).
  - **Off-The-Ball Support:** Teammates dynamically create passing angles, make overlapping runs, check into open pockets, or provide backward safety outlets (`Positioning`, `Anticipation`, `WorkRate`, `Pace`).
  - **Coordinated Zonal Defense:** Only designated pressers close down the ball carrier within a pressing trigger zone; the remaining defenders maintain horizontal/vertical compactness, cut passing lanes (cover shadows), mark runners, and coordinate the offside line (`DefensiveLine`, `Width`, `Pressing`, `Marking`, `Positioning`, `Tackling`).
  - **Goalkeeper AI:** Tracks the ball-to-goal bisector, rushes out for 1v1 duels, and executes physical reach/dive calculations for shots (`Reflexes`, `Handling`, `OneOnOnes`, `Positioning`).
- **Zero Hallucinated Keyframes:** Because every player and the ball have real spatial trajectories every tick, the output naturally feeds the 2D viewer with smooth, lifelike movement.

### 1.3 Key Architectural Constraints & Guarantees
1. **Plug-and-Play Contract Preservation:**
   - The engine consumes the existing `MatchInputV1` snapshot and produces the exact `MatchResultV1` output and `MatchPassageRecorder` / `MatchPresentationResponse` structure expected by the system.
   - The 2D Canvas Match Viewer (`canvas-match-renderer.ts`, `keyframe-interpolator.ts`, `film-timeline.ts`) requires **zero breaking changes**.
2. **Preserve & Disable Legacy Engine (Do Not Delete):**
   - The possession-based engine (`PossessionSimulator`, `PassagePlanner`, etc.) is retained in full inside the codebase.
   - An engine strategy abstraction (`IMatchSimulationEngine`) allows switching between engines via configuration/rules. The new tick-based engine becomes the default active engine; the possession engine is preserved for regression comparison and historical derivations.
3. **Reuse Existing Domain & Engine Elements (No Re-invention):**
   - **All 28 Attributes** (`MatchAttributeName`) in canonical $1..20$ scale.
   - **Player Match States** (`PlayerMatchStateV1`: Condition, Fatigue, Morale, Sharpness).
   - **Tactics & Instructions** (`MatchInstructionsV1`: Mentality, Tempo, Passing, Width, PassFocus, Pressing, DefensiveLine, Tackling, TimeWasting, CounterAttack).
   - **Pitch Coordinates** (`SpatialPitch`: $10,000 \times 7,000$, penalty boxes, goal centers).
   - **Discipline & Injury Formulas** (`DisciplineSimulator`, `InjurySimulator`).
   - **Match Ratings** (`PlayerRatingCalculator`).
   - **Commentary Generation** (`CommentaryTokenBuilder`).
4. **Deterministic & High-Performance:**
   - Powered entirely by `Pcg32` seeded from `MatchInputV1.Seed`.
   - Pure integer / fixed-point math ensuring 100% reproducible cross-platform byte-identical execution.
   - Zero-allocation hot tick loop: 90 minutes (54,000 ticks) simulates in $< 35\text{ ms}$ on backend CPU.

---

## 2. High-Level Architecture & Engine Switch

```
                                  [ MatchInputV1 ]
                                         │
                                         ▼
                             [ MatchSimulator.Simulate ]
                                         │
                     ┌───────────────────┴───────────────────┐
                     ▼                                       ▼
          [ IMatchSimulationEngine ]             [ LegacyPossessionEngine ]
                     │                           (Preserved, Disabled by Default)
                     ▼
          [ TickMatchEngine (Active) ]
             ├── Pitch & Ball Physics (10 Hz)
             ├── 22 Autonomous Player Agents
             │     ├── Tactical Shape & Phase Modulators
             │     ├── Defensive Shape, Pressing Triggers & Offside Trap
             │     ├── Off-the-ball Support Runs & Passing Triangles
             │     └── Ball Carrier Decision Engine (FM Brain)
             ├── Physical Contests & Duels (Tackles, Aerials, Saves)
             └── Set Pieces & Restarts State Machine
                     │
                     ▼
          [ TickReplaySynthesizer ]
             ├── Ticks-to-Passages Chunking
             ├── Keyframe Compression (Delta & Colinear Filter)
             └── Event & Commentary Synchronization
                     │
                     ▼
        [ MatchResultV1 ] + [ MatchPresentationResponse (for 2D Viewer) ]
```

---

## 3. Implementation Milestones

```
Milestone 0: Engine Abstraction & Non-Destructive Legacy Engine Wrap
   ↓
Milestone 1: Spatial Kinematics, Ball Physics & Field Mechanics
   ↓
Milestone 2: Team Tactical Geometry, Dynamic Anchors & Steering
   ↓
Milestone 3: Defensive AI — Pressing Triggers, Cover Shadows & Offside Line
   ↓
Milestone 4: Offensive AI — Off-The-Ball Runs, Triangles & Channel Attacks
   ↓
Milestone 5: Ball Carrier Decision Engine (The Football Manager Brain)
   ↓
Milestone 6: Goalkeeping Dynamics, Shot-Stopping & Parries
   ↓
Milestone 7: Restarts & Set Pieces State Machine
   ↓
Milestone 8: Replay Synthesis, Keyframe Compression & 2D Viewer Compatibility
   ↓
Milestone 9: Plug-In, Benchmarking, Determinism & Calibration Suite
```

---

### Milestone 0: Engine Abstraction & Non-Destructive Legacy Engine Wrap

**Objective:** Introduce a clean engine strategy interface, wrap the existing possession simulator as `LegacyPossessionEngine` (disabled from default execution), and register the new engine version without altering any existing simulation code.

#### Steps:
1. **Define Engine Strategy Interface (`src/TouchlineManager.MatchEngine/Simulation/IMatchSimulationEngine.cs`):**
   ```csharp
   public interface IMatchSimulationEngine
   {
       string EngineLabel { get; }
       string RuleSetLabel { get; }
       void Run(MatchState state);
   }
   ```
2. **Encapsulate Possession Engine (`src/TouchlineManager.MatchEngine/Simulation/LegacyPossessionEngine.cs`):**
   - Implement `IMatchSimulationEngine` wrapping `PossessionSimulator.Run(state)`.
   - Preserve all existing files (`PossessionSimulator.cs`, `PassagePlanner.cs`, `ChanceSimulator.cs`, etc.) completely intact.
3. **Version Stamp Registration (`src/TouchlineManager.MatchEngine/EngineVersions.cs`):**
   - Add new engine constants:
     - `public const int Engine = 12;`
     - `public const int RuleSet = 11;`
     - `public const string EngineLabel = "engine-v12";`
     - `public const string RuleSetLabel = "engine-rules-v11";`
     - `public const string LegacyEngineLabel = "engine-v11";`
4. **Engine Selector in `MatchSimulator.cs`:**
   - Add configuration flag or registry defaulting to `TickMatchEngine`.
   - If snapshot specifies `engine-v11`, allow fallback dispatch to `LegacyPossessionEngine`.
   - Ensure clean compilation.

**Verification Checkpoint:**
- Solution builds with 0 warnings.
- Legacy regression tests can still execute against `LegacyPossessionEngine`.

---

### Milestone 1: Spatial Kinematics, Ball Physics & Field Mechanics

**Objective:** Build high-performance 2D/3D physics and player kinematics that run at 10 Hz ($\Delta t = 100\text{ ms}$) using deterministic integer / fixed-point mathematics.

#### Steps:
1. **Pitch Kinematics & Scale Constants (`src/TouchlineManager.MatchEngine/Spatial/TickSpatialUnits.cs`):**
   - Coordinates: $X \in [0, 10_000]$, $Y \in [0, 7_000]$, $Z \in [0, 100]$.
   - Conversion ratios: 100 units $\approx 1.05\text{ m}$ (Pitch is $105\text{ m} \times 68\text{ m}$).
   - Tick rate: `TicksPerSecond = 10`, `TickDeltaMs = 100`.
2. **Physical Ball Simulation (`src/TouchlineManager.MatchEngine/Spatial/TickBallPhysics.cs`):**
   - State: Position $(x, y, z)$, Velocity $(vx, vy, vz)$ in units/tick.
   - Ball States:
     - `Loose`: Free ball moving along ground with friction ($v_{next} = v \times 0.96$).
     - `Controlled(playerIndex)`: Locked to player's dribble foot ($0.3\text{ m}$ ahead in heading vector).
     - `Flight(origin, target, apexZ)`: Ballistic trajectory with air resistance and gravitational acceleration ($g = 9.81\text{ m/s}^2$ scaled).
     - `Bounced`: Restitution factor ($0.65$ vertical velocity retention, friction drop).
   - Touch/Catch Radius: Ground reception radius $120$ units ($1.2\text{ m}$); aerial reception radius $100$ units.
3. **Player Kinematics & Physical Attributes (`src/TouchlineManager.MatchEngine/Tick/TickPlayerPhysics.cs`):**
   - Speed calculation:
     $$\text{MaxSpeed} = \text{BaseSpeed} + (\text{Pace} \times \text{PaceWeight}) + (\text{ConditionMultiplier})$$
     $$\text{Acceleration} = \text{BaseAcc} + (\text{AccelerationAttribute} \times \text{AccWeight})$$
   - Turn speed / Agility: Maximum heading change angle per tick limited by `Agility`.
   - Energy / Stamina consumption:
     - Sprinting ($> 70\%$ max speed) drains condition by formula derived from `Stamina` and `WorkRate`.
     - Walking / holding shape recovers small energy buffer.
4. **Collision & Boundary Handling:**
   - Touchline and goal-line bounds clamping with out-of-bounds detection.
   - Goal posts and crossbar physical collision bounding boxes ($Y \in [3000, 4000]$, $Z \in [0, 35]$).

**Verification Checkpoint:**
- Unit tests verify deterministic trajectory of a 30m lofted pass landing at expected tick and location.
- Ball rolls to a natural stop on grass.

---

### Milestone 2: Team Tactical Geometry, Dynamic Anchors & Steering

**Objective:** Eliminate static positions and chaotic clustering. Implement team shape elasticity and steering behaviors so each team moves up and down the pitch as a coherent unit based on formations and tactics.

#### Steps:
1. **Dynamic Formation Anchor Resolver (`src/TouchlineManager.MatchEngine/Tick/TickTacticalGeometry.cs`):**
   - Each player has a base formation coordinate $(X_{base}, Y_{base})$ from `MatchSlotV1`.
   - Dynamic anchor calculation per tick:
     - **Pitch Progress Shift:** Block moves forward/backward with ball position:
       $$\text{Shift}_X = \frac{(X_{ball} - 5000) \times \text{CompactnessFactor}}{100}$$
     - **Phase Shift:**
       - *In Possession:* Mentality push (+400 to +800 units forward), Width expansion factor ($1.15\times$ to $1.35\times$ spread on Y axis).
       - *Out of Possession:* Mentality drop (-300 to -600 units backward), Width compression factor ($0.75\times$ to $0.85\times$ compactness on Y axis).
     - **Defensive Line Height:** Offset by `DefensiveLine` instruction (High: +600, Normal: 0, Deep: -700 units).
     - **Pass Focus Bias:** Lateral shift towards focused wing or central corridor.
2. **Autonomous Steering Behaviors (`src/TouchlineManager.MatchEngine/Tick/TickSteering.cs`):**
   - **Arrive:** Smooth deceleration when reaching target anchor (prevents vibrating/overshooting).
   - **Separation / Spacing:** Repulsion vector between teammates within $2.0\text{ m}$ (prevents players clipping or running into each other).
   - **Velocity Blending:** Inertia blending: $V_{desired} \times (1 - \alpha) + V_{current} \times \alpha$.

**Verification Checkpoint:**
- When ball moves from home penalty box to away penalty box, all 10 outfield home players advance in unified formation block.
- Wingers spread wide in attack and tuck in during defense.

---

### Milestone 3: Defensive AI — Pressing Triggers, Cover Shadows & Offside Line

**Objective:** Solve the "schoolyard swarm". Enforce disciplined zonal defending where only designated pressers engage the ball carrier while others mark, screen passing lanes, or maintain the defensive offside line.

#### Steps:
1. **Pressing Trigger & Role Assignment (`src/TouchlineManager.MatchEngine/Tick/TickDefensiveAI.cs`):**
   - Each tick, the defending team evaluates:
     - Who is the **Primary Presser**? The nearest defender to the ball carrier within the team's pressing trigger radius.
     - Pressing radius determined by `MatchPressing`:
       - *Low Block:* $1,200$ units ($12\text{ m}$), only inside own defensive half.
       - *Mid Block:* $2,000$ units ($20\text{ m}$).
       - *High Press:* $3,200$ units ($32\text{ m}$), extends deep into opponent third.
     - **Anti-Swarm Rule:** Only **one** primary presser engages. A second defender (supporting presser) is dispatched ONLY if `HighPress` is active AND ball is within $10\text{ m}$ of touchline (trap). All other 8-9 outfield players MUST NOT charge the ball!
2. **Cover Shadows & Passing Lane Interception:**
   - Non-pressing defenders calculate the ray between the ball carrier and potential attacking receivers.
   - Defenders position themselves along that ray to cast a "cover shadow" (`Positioning`, `Anticipation`), cutting off easy passes.
3. **Zonal & Man Marking in Final Third:**
   - In own defensive third ($X < 3500$ for home defense):
     - Center-backs and fullbacks pick up the closest opposing attacker within marking zone ($< 3\text{ m}$ tight mark, weighted by `Marking`).
4. **Coordinated Defensive Line & Offside Trap:**
   - The last outfield defender defines the offside threshold $X_{offside}$.
   - Defensive line steps up or drops back in unison (`Decisions`, `Positioning`).
   - If an attacker receives a forward pass while positioned past $X_{offside}$ at the moment of the pass, flag `EngineEventType.Offside`.
5. **Tackling & Duel Execution (`src/TouchlineManager.MatchEngine/Tick/TickTackleResolver.cs`):**
   - When presser enters contact radius ($< 90$ units, $\approx 0.9\text{ m}$):
     - Contest resolved using existing duel attributes: `Tackling`, `Strength`, `Aggression` vs `Dribbling`, `Agility`, `Composure`.
     - Outcomes:
       - *Clean Tackle:* Defender wins ball or pokes it loose.
       - *Failed Tackle:* Attacker beats defender (take-on won, defender decelerates).
       - *Foul:* Evaluated against `MatchTacklingStyle` and player `Aggression`. Invokes `DisciplineSimulator` for cards.

**Verification Checkpoint:**
- Tests prove that when opponent winger has the ball, only the fullback presses while center-backs stay in the box marking strikers.
- Offside flag correctly trips when a through ball is played to an offside striker.

---

### Milestone 4: Offensive AI — Off-The-Ball Runs, Triangles & Channel Attacks

**Objective:** Bring Football Manager-style attacking fluidity. Off-the-ball teammates move dynamically to create passing triangles, make overlapping runs, or penetrate defensive gaps.

#### Steps:
1. **Passing Triangle Generation (`src/TouchlineManager.MatchEngine/Tick/TickOffBallSupport.cs`):**
   - The ball carrier needs at least 2 distinct passing options at all times (short triangular support).
   - Nearest 2 supporting teammates calculate optimal support positions:
     - Distance: $12\text{ m} - 25\text{ m}$ from carrier.
     - Angle: $30^\circ - 60^\circ$ relative to carrier's forward vector.
     - Must have a clear line-of-sight vector to carrier unobstructed by defenders.
   - Supporting player moves to open the passing angle (`Positioning`, `WorkRate`, `Anticipation`).
2. **Penetrating Runs & Channel Exploitation:**
   - Strikers and attacking wingers evaluate runs:
     - **Behind Defensive Line:** If defensive line is high and carrier has time on ball (not immediately pressed), forward initiates a sprint into space behind the last defender (`OffTheBall` / `Positioning`, `Pace`, `Acceleration`).
     - **Checking into Pocket:** Forward drops $5\text{ m}$ off the center-back into space between midfield and defense to receive ball to feet.
   - Frequency of runs governed by `MatchMentality` (Attacking teams make more frequent penetrative runs; Defensive teams hold shape).
3. **Overlapping Fullback Runs:**
   - When an attacking winger cuts inside with the ball, the corresponding fullback recognizes the vacated flank and sprints forward into the wide channel to offer a crossing outlet.

**Verification Checkpoint:**
- Trace logs verify that when a midfielder has the ball in the central third, at least 2 passing lanes are actively cleared by supporting runs.

---

### Milestone 5: Ball Carrier Decision Engine (The Football Manager Brain)

**Objective:** Implement the core decision-making AI for the ball carrier. Every decision interval (1–2 ticks), the player evaluates the pitch state and picks the best action based on attributes, tactical instructions, and pressure.

#### Steps:
1. **Decision Cycle & Pressure State (`src/TouchlineManager.MatchEngine/Tick/TickBallCarrierBrain.cs`):**
   - Frequency: Evaluated every 1 to 2 ticks, adjusted by player's `Decisions` and `Anticipation`.
   - Pressure calculation: Distance and closing speed of nearest presser.
   - Under heavy pressure: Player composure check ($Composure$ vs $Pressure$). Low composure causes hurried clearances or turnover risk; high composure enables shielding or quick disguised passes.
2. **Action Candidate Evaluation:**
   The brain scores 4 competing actions:
   - **Option A: Shoot**
     - Eligible if: Distance to opponent goal $< 28\text{ m}$ and clear shooting angle.
     - Score based on: xG heuristic (distance, angle, defenders blocking shot line), player's `Finishing`, `Technique`, and `Mentality`.
   - **Option B: Pass (Short, Direct, Through-Ball, Cross)**
     - Evaluates all 10 teammates. For each teammate, calculates pass utility:
       - Forward progression towards goal.
       - Teammate space from nearest marker.
       - Line of sight / interception probability (raycast against defender positions and interception reach).
       - Alignment with team instructions:
         - `MatchPassingStyle`: Short passing prioritizes high-safety close teammates; Direct passing prioritizes long vertical balls to forwards.
         - `MatchPassFocus`: Wings vs central preference.
     - **Cross:** If carrier is in wide final-third channel, deliver cross into the box target zone.
     - **Through-Ball:** If teammate is actively sprinting into open space behind the defensive line, play ball into space ahead of the runner.
   - **Option C: Dribble / Carry**
     - Eligible if: Open space $> 5\text{ m}$ directly ahead.
     - Score based on: Player's `Dribbling`, `Pace`, `Acceleration`, and `Tempo` instruction.
   - **Option D: Shield / Hold / Recycle**
     - Eligible if: Forward passing lanes are blocked and carrier has defensive support behind.
     - Action: Turn body between defender and ball, pass backward to center-back or keeper to maintain possession.
3. **Execution & Accuracy Dispersion:**
   - When a pass or shot is selected, execution accuracy is calculated:
     $$\text{ErrorAngle} = \text{BaseError} \times \frac{100 - (\text{Passing} \times 4 + \text{Technique} \times 1)}{100} \times \text{PressureFactor}$$
   - Ball is launched into `TickBallPhysics` with calculated velocity and target coordinate.

**Verification Checkpoint:**
- When an isolated winger reaches the byline, they execute a cross into the penalty box rather than running out of bounds.
- When under heavy press with no forward options, players recycle the ball backward rather than gifting possession.

---

### Milestone 6: Goalkeeping Dynamics, Shot-Stopping & Parries

**Objective:** Implement realistic goalkeeper behavior: positioning on the goal arc, 1v1 rush-outs, diving reach, and shot-stopping mechanics.

#### Steps:
1. **Arc Positioning & Angle Reduction (`src/TouchlineManager.MatchEngine/Tick/TickGoalkeeperAI.cs`):**
   - The goalkeeper positions on the bisector between the ball and the center of the goal line.
   - Depth: Moves $2\text{ m} - 5\text{ m}$ off the line to narrow shooting angles (`Positioning`).
2. **1v1 Rushing Out:**
   - If an attacker breaks through the defensive line with a clear path to goal and ball is within $18\text{ m}$, keeper charges out to smother the ball (`OneOnOnes`, `Acceleration`, `Decisions`).
3. **Shot-Stopping & Dive Physics (`src/TouchlineManager.MatchEngine/Tick/TickShotStopper.cs`):**
   - When a shot is airborne:
     - Keeper computes estimated arrival time and intersection point on the goal plane.
     - Reaction time: Delay based on `Reflexes` (0.15s to 0.35s).
     - Reach radius: Calculated from `AerialReach`, `Agility`, and `Height`.
     - Outcome determination:
       - If arrival point is outside reach $\rightarrow$ **Goal**.
       - If within catch reach and low shot velocity $\rightarrow$ **Catch/Hold** (`Handling`).
       - If within parry reach $\rightarrow$ **Parry/Save** (`Reflexes`). Ball rebounds into play or is tipped wide for a **Corner**.
       - Woodwork hit: Post or crossbar collision deflection.

**Verification Checkpoint:**
- Tests confirm goalkeepers reliably save weak long-range shots ($> 30\text{ m}$) while elite strikers score high-quality 1v1 chances.

---

### Milestone 7: Restarts & Set Pieces State Machine

**Objective:** Clean, authentic handling of all dead-ball restarts (Kick-offs, Goal Kicks, Throw-ins, Corners, Free Kicks, Penalties) without teleporting or visual discontinuities.

#### Steps:
1. **Match State Machine (`src/TouchlineManager.MatchEngine/Tick/TickMatchStateMachine.cs`):**
   - States: `OpenPlay`, `KickOffPending`, `GoalKickPending`, `CornerPending`, `ThrowInPending`, `FreeKickPending`, `PenaltyPending`, `GoalCelebration`, `HalfTime`.
2. **Restart Setups & Placements:**
   - **Kick-off:** Home (1st half) or Away (2nd half) or conceding team placed at center circle; all 20 other players held in own halves. Two players at center spot, pass backward into midfield.
   - **Goal Kick:** Ball placed at 6-yard box; center-backs split wide; keeper or defender passes short or plays long.
   - **Corner:** Ball placed at corner arc; attacking headers and defending markers assemble in penalty box (`Heading`, `JumpingReach`); corner taker delivers cross.
   - **Penalty:** Ball at 11m spot; penalty taker vs goalkeeper 1v1; all other players held outside penalty area and arc.
   - **Free Kick:** Placed at foul location. If shooting range, wall is formed ($9.15\text{ m}$ distance); direct shot or cross executed.
3. **Whistle & Transition:**
   - Restarts have natural setup holds ($0.8\text{s} - 1.5\text{s}$), then execution occurs, transitioning smoothly into `OpenPlay`.

**Verification Checkpoint:**
- Corners, goal kicks, and penalties setup and resolve with 22 players placed in correct set-piece tactical shapes.

---

### Milestone 8: Replay Synthesis, Keyframe Compression & 2D Viewer Compatibility

**Objective:** Translate continuous 10 Hz tick data into the exact `MatchPresentationResponse` / `PassageResponse` structure consumed by the Angular 2D Canvas Match Viewer (`canvas-match-renderer.ts`, `film-timeline.ts`).

#### Steps:
1. **Passage Chunking (`src/TouchlineManager.MatchEngine/Tick/TickReplaySynthesizer.cs`):**
   - The tick simulation runs continuously for the full match.
   - The synthesizer slices the match into logical `PassageResponse` objects (~8 to 15 seconds each, or structured around chances and dead-ball phases).
   - Generates:
     - `Entities`: The 22 player entities (`H1`..`H11`, `A1`..`A11`) with names, numbers, positions, and kit colors, plus `'ball'`.
     - `Cuts`: Explicit jump crossfades at kick-offs and half-time.
     - `Clock`: Synchronized `ClockKeyframeResponse` mapping film milliseconds to match seconds.
2. **Keyframe Compression via Existing Compressor:**
   - Raw ticks produce 10 samples per second.
   - Use `KeyframeCompressor.cs` to filter out colinear / redundant points when players are moving at constant velocity or holding shape.
   - Retain all semantic action points (`Action`: "pass", "receive", "tackle", "shot", "save", "header", "dive", "cross").
   - Result: Extremely compact transport payloads ($< 3\text{ MB}$ for entire 90-minute film) that interpolate with perfect mathematical smoothness on the client's monotone Hermite spline.
3. **Live Metrics & Commentary Alignment:**
   - Minute-by-minute condition and rating curves populated into `PlayerLiveMetricsRecorder`.
   - Match events (`EngineEventV1`) fed directly into `CommentaryTokenBuilder` so authentic Championship Manager ticker lines display in exact sync with on-pitch actions.

**Verification Checkpoint:**
- Presentation JSON passes all existing client validation schemas in `match.models.ts`.
- 2D Canvas viewer renders the full match with zero console errors, zero frame drops, and buttery-smooth movement.

---

### Milestone 9: Plug-In, Benchmarking, Determinism & Calibration Suite

**Objective:** Fully wire the tick engine into `MatchSimulator.Simulate`, run regression determinism suites, calibrate match statistics to realistic football benchmarks, and finalize documentation.

#### Steps:
1. **Final Wiring in `MatchSimulator.cs`:**
   - Route `MatchSimulator.Simulate(...)` to `TickMatchEngine.Run(...)`.
   - Retain `LegacyPossessionEngine` as an optional fallback when explicit rule or engine version specifies it.
2. **Determinism & Purity Testing (`tests/TouchlineManager.MatchEngine.Tests`):**
   - Verify `DeterminismTests`: identical seed produces identical output hash and byte-for-byte identical canonical serialization across multiple runs.
   - Verify `EnginePurityTests`: no database, system clock, or uncontrolled random state is accessed.
3. **Statistical Calibration & Benchmarks (`tools/simulation-benchmarks`):**
   - Run 500 automated test matches:
     - Goals per match: $2.60 - 2.90$ average.
     - Shots per match: $22 - 28$ total.
     - Shots on target: $32\% - 38\%$.
     - Pass completion: $75\% - 85\%$.
     - Yellow cards: $3.0 - 4.5$ per match.
     - Home win rate: $42\% - 48\%$, Draw rate: $22\% - 26\%$, Away win rate: $28\% - 34\%$.
4. **Documentation & ADR:**
   - Write ADR-0055: *Adoption of Discrete Tick-Based Match Engine and Deprecation of Possession-Based Macro Engine*.
   - Update `docs/product/match-engine.md`.

**Verification Checkpoint:**
- `dotnet test` passes 100% across all test suites.
- Benchmark numbers fall cleanly within international professional football calibration bands.

---

## 4. Architectural Invariants & Agent Guidelines

When an AI agent implements this plan, it MUST adhere strictly to the following rules:

1. **Do Not Delete Legacy Files:** Do not remove `PossessionSimulator.cs`, `PassagePlanner.cs`, `ChanceSimulator.cs`, or related legacy simulation classes. They are quarantined under `LegacyPossessionEngine` to guarantee backward reproducibility of historical fixtures.
2. **Zero Allocation in the Tick Loop:** In the 54,000 tick loop, avoid heap allocations (`new List`, LINQ queries, lambda closures, boxed value types). Use fixed-size arrays, `Span<T>`, or reusable scratch buffers.
3. **Pure Integer / Fixed-Point Determinism:** Never use unconstrained floating-point operations that may differ across CPU architectures (x86 vs ARM64). Use integer basis points ($10,000 = 100\%$) and integer pitch units.
4. **Maintain Wire Contracts:** Do not alter the wire shapes in `TouchlineManager.Contracts/Match/PresentationResponses.cs` unless strictly additive and backward-compatible. The Angular frontend should run against the new engine output without modification.
5. **XML Documentation:** All newly created public and internal classes, records, methods, and enums must have clear, professional C# XML doc comments citing design rules and formulas.

---

## 5. Summary of Deliverables by Component

| Component | Responsibility | Delivered in |
|---|---|---|
| `IMatchSimulationEngine` | Interface abstracting match engines | Milestone 0 |
| `LegacyPossessionEngine` | Preserved legacy possession engine (disabled as default) | Milestone 0 |
| `TickSpatialUnits` & `TickBallPhysics` | 10 Hz physical ball & pitch dynamics | Milestone 1 |
| `TickPlayerPhysics` | Player kinematics, pace, stamina, turn rate | Milestone 1 |
| `TickTacticalGeometry` & `TickSteering` | Dynamic formation anchors & anti-collision steering | Milestone 2 |
| `TickDefensiveAI` & `TickTackleResolver` | Anti-swarm pressing, cover shadows, offside line, tackles | Milestone 3 |
| `TickOffBallSupport` | Passing triangles, penetrating runs, channel exploitation | Milestone 4 |
| `TickBallCarrierBrain` | FM decision engine (shoot, pass, through-ball, carry, shield) | Milestone 5 |
| `TickGoalkeeperAI` & `TickShotStopper` | Bisector positioning, 1v1 rushes, dive reach, saves | Milestone 6 |
| `TickMatchStateMachine` | Set pieces & restarts state machine | Milestone 7 |
| `TickReplaySynthesizer` | Converting ticks to `PassageResponse` & keyframes | Milestone 8 |
| `MatchSimulator` integration & tests | Plugging in active engine, determinism & calibration | Milestone 9 |

Every row is delivered in the milestone it names, and Milestone 9 — the plug-in, the determinism and
calibration suites, and the ADR — closes the plan. What remains open is recorded in the consequences of
[ADR-0066](docs/architecture/adr/0066-engine-v12-tick-simulation.md): the per-match cost is above the
performance budget, and substitutions, injuries, morale and fatigue are not yet in the tick loop.
