# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16, with engine milestones named by their
engine version.

## Engine-v4 replay — one continuous film, and a reel over it

`replay-v2` turned the engine's events into at most two dozen chance snippets and bridged the gaps, so the
replay cut between chances and never showed a build-up. `engine-v4` gave the simulation a real ball path;
`replay-v3` now turns it into one continuous condensed film of the whole match, plus a highlights reel over
the same data. Presentation is re-derived, never stored, so this is a clean change.

The director merges the recorded possessions into roughly fifty to seventy-five film passages — split at
substitutions, half-time, and bookings of personnel so a passage's eleven is stable — and warps their time so
a ninety-minute match becomes a ten-minute film, weighted so chances are readable and dull spells fly by. The
ball's track is the recorded path; the eleven's tracks are the shape the tactical resolver gives them over
that real path, overwritten by the recorded touches of the carrier, passer, shooter, and keeper. Boundary
frames are copied exactly, so the film joins rather than cuts. A server-side reel selects every goal and the
best chances, each clip carrying a genuine lead-in of up to ten match-minutes, with overlaps merged. Payload
is guarded by an adaptive compression ladder (tolerance 32 → 40 → 48, sample interval 400 → 600 → 800 ms).

### Changed

- **`HighlightDirector` → `ReplayDirector`** (`Highlights/ReplayDirector.cs`, `replay-v3`), with a new
  `Highlights/ReelBuilder.cs` beside it; the highlights model becomes `PassageV1`/`ReelClipV1` and the bridge
  concept is deleted.
- **Presentation contract:** `MatchPresentationResponse.Highlights` → `Passages` (each a passage plus
  `StartMatchSecond`, `EndMatchSecond`, `EventSequences`), a new `Reel` of `ReelClipResponse`, `Bridges`
  removed, and `Playback` is one `passage` segment per passage. The presentation ETag becomes
  `{OutputHash}:{PresentationVersion}`, so a future replay revision invalidates cached payloads.
- **Commentary (`commentary-v3`):** `CommentaryTokenBuilder` gains the build-up families — `match.build.pass`,
  `carry`, `dribble`, `cross`, `header`, `tackle`, `interception`, `save`, `chance` — and narrates film
  passages from their recorded touches and events; a goal keeps the full log's own wording.

### Notes

- The film is a by-product of the ordinary simulation: the passage recorder is optional, consumes no draw,
  and the output hash is identical with and without it.
- The web viewer still reads `replay-v2`; the engine roadmap's M3 updates it to `replay-v3`.
- The dev database seeded under `engine-v3` was archived (`touchline_engine_v3_backup`) and reseeded, because
  a version-4 match cannot replay a version-3 result.

### Tests

- New `ReplayDirectorTests`: contiguous one-segment-per-passage schedule, the nine-to-eleven-minute film,
  passage windows in order, boundary-frame continuity, on-pitch and in-passage keyframes, the eleven and the
  ball with a track each, synchronized and `MAT-11`-safe passage commentary, the reel carrying every goal,
  goal narration naming the scorer, determinism, and the payload budget. `HighlightTests` re-expressed for
  reel selection. `GetMatchPresentationTests` and `MatchTests` updated for the new contract and ETag.

## Engine-v4 — a possession is played along a real passage

`engine-v3` located a possession on the pitch with a single absolute draw: every possession was assigned a
random point in the possessing side's **own half**, and every event inherited it. The ball therefore moved
randomly, no pass, carry, or cross existed in the model, and `attackingX >= FreeKickShootingRangeX` was
unreachable — direct free kicks were dead code and any shot map built from event positions was nonsense.

Engine-v4 makes the passage the unit of movement. A possession begins where the last one left the ball — or at
a restart (centre after a goal, the goal area after a keeper claim) — progresses into the attacking third
through three to eight touches with lateral drift, and ends at a point its outcome names: the final third for
an open-play shot, the penalty spot, the corner flag, the offside line, the middle third, or the pressure
point where a foul was committed. `state.MoveBall` is called along the passage and before every `Emit`, so
event coordinates finally mean something. A per-possession derived stream (`seed * 1_000_003 + ordinal`, the
`AssistPlanner` pattern) draws all of the geometry, so the outcome formulas and their calibrated
distributions are untouched. `AdvanceBall` and `Min/MaxPossessionAdvanceBasisPoints` are deleted.

The change is rounded off by `MatchPassageRecorder`: an optional side channel, in the
`PlayerLiveMetricsRecorder` style, that captures each possession's ball waypoints and the touches of the
players the simulation actually picked (carrier, passer, shooter, keeper, header winner, free-kick taker).
The engine and rules versions become `engine-v4` / `engine-rules-v4` (ADR-0051).

### Added

- **Continuous passages** (`engine-v4`): `Model/MatchPassage.cs` holds `MatchPassageV1`, `PassageWaypointV1`,
  `PassageTouchV1`, the waypoint/action vocabulary and codes, and the public `MatchPassageRecorder`;
  `Simulation/PassagePlanner.cs` builds the path from the possession's own derived stream.
- **Progression rules**: `EngineRulesV2` gains the passage constants — touch count, per-touch advance, lateral
  drift, the pressure-point band, the per-zone final-third shot bands, the offside line, the turnover and
  goal-area anchors, the cross share, and the cross/shot/header/clearance altitudes — replacing the advance
  band, with validation for every one of them.
- **`MatchSimulator.Simulate(input, rules, liveMetrics, passages)`**: the passage recorder is optional and
  consumes no draw, so the output hash is identical with and without it.

### Notes

- **The ball is now a simulation fact, and the free kick is real.** `FreeKickShot` — unreachable in version 3
  — is produced by a foul deep in the attacking third; measured on even sides it is about 1.5 per match,
  with the goal and shot bands held (2.88 goals and 27.0 shots per match over a thousand fixtures). The
  measured bands are unchanged, so the free-kick constants did not need retuning (ADR-0051).
- **The golden hashes move, and that is the point.** The main stream advances differently because the
  absolute positional draw is gone, so every stored match would replay differently; `engine-v4` is a new
  version and the input, output, and rules hashes are re-pinned. A database seeded before this change must be
  archived and reseeded.
- **A substitute who takes a chance is still not carried as a presentation entity.** The replay director's
  entity list is the starting eleven; making it the current XI is the next engine-roadmap milestone, and two
  director tests skip a substitute shooter with that noted.
- **`ADR-0051`** records the decision; the ADR index is updated with it.

### Tests

- New `PassageTests`: one passage per possession, every waypoint and touch on the pitch and in fraction
  order, continuity between possessions (or a restart), every shot in the attacking third and free-kick shots
  in range, touches naming match participants, recorder determinism, the with/without-recorder hash equality,
  and the post-goal centre restart.
- Re-pinned `DeterminismTests` golden input/output/rules hashes and the version-label tests; the
  `HighlightTests` and `ReplayDirectorTests` updates are the two noted above.

## Stage 15 — Player-facing rules, privacy, terms, status, and support pages

Stage 15 asks for "player-facing rules/privacy/terms/status/support pages", and the gap was concrete rather
than cosmetic: the registration screen asked a manager to accept "the terms of service and the privacy policy"
with **no page to link to**, no footer link, and no public endpoint a signed-out client could call. All five
pages now exist. Four are authored as data and reachable signed out; the fifth is a live service status behind
the product's **first anonymous read**, `GET /api/v1/status` (`F-55`, ADR-0050).

### Added

- **Four player-facing information pages** (`F-55`): `/rules`, `/privacy`, `/terms` and `/support`, one
  data-driven component (`features/info/`) selected by the route's `data.document`, so a page reads nothing from
  the server and a new one is a route plus a document. They are unguarded and shell-wrapped, because the
  register consent — and the footer — must reach them while signed out.
- **A live service status** (`F-55`, `F-51`): `/status` reads `GET /api/v1/status` and shows whether the game is
  operational or in read-only maintenance (with the operator's reason), the running season, and the next
  matchday, polling while the page is open (`core/status/`, `features/status/`).
- **The product's first anonymous read** (`F-55`, `LGL-1`): `StatusEndpoints` maps `GET /api/v1/status` with no
  authorization policy. `GetPublicStatus` composes the incident reader (`F-51`), the world's running season, the
  same next-round read the stepped clock's status uses (ADR-0049), and `AuthOptions` for the terms and privacy
  versions. It adds no table and no repository.
- **The published document versions on the pages** (`LGL-1`): the terms and privacy pages name the version the
  server records on consent, read from the status endpoint rather than authored beside the copy, so a page
  cannot disagree with the consent.
- **Footer and register-consent links**: the shell footer gains an "Information" navigation to all five pages,
  and the registration form's consent line links the terms of service and the privacy policy it names.

### Fixed

- **Two order-dependent API integration tests.** Adding a test class perturbs xUnit's non-deterministic
  ordering within the shared `api` world, and two long-standing assumptions did not survive it:
  `CompetitionTests.Another_clubs_fixture_is_refused_by_name` could pick the "other club's" earliest fixture
  when that fixture was the one it played against the manager's own club — so it read the manager's own side
  and got `200` rather than the expected `403`; and `MatchTests.A_replay_carries_commentary_...` required at
  least one goal in a match that can legitimately end goalless. Both are made robust (the fixture is chosen
  from a match the other club plays against somebody else; a goal event is required only when the score has
  one, and the score-to-highlight reconciliation is asserted unconditionally). Neither is a product change.

### Notes

- **One endpoint and four documents, deliberately.** One small public read serves both the live status and the
  versioned documents; a second endpoint for four short strings would be surface without a caller.
- **The retention values are fixed here and stated on the page.** `data-classification.md` §3 sets security/IP
  and device hashes at 90 days and support correspondence at 24 months, the privacy page states the same values,
  and `features/info/info.spec.ts` fails if they drift.
- **No rate limit on the anonymous read, recorded rather than overlooked.** It is a small read over a
  five-second-cached flag and existing tables, and it is the page a person opens when something is wrong
  (ADR-0050).
- **No migration, no new table.** The milestone is a contract, a query, one endpoint, the web pages, and their
  tests; the schema and existing endpoints are untouched.
- **Support names no address.** There is no support channel in the product, so the support page explains how to
  describe a problem and to quote the reference the footer already shows; the submission tool is the separate
  feedback/report deliverable of this stage.
- **`docs/architecture/adr/0050-...`** records the decision; the ADR index, `mvp-traceability.md` (`F-55` and
  its §2 row), `content-and-fictional-data-policy.md` §5's legal-page gate, `data-classification.md` §3 and §7,
  and `features/README.md` are updated with it.

### Tests

- New `PublicStatusTests` (API integration): the status is readable with no token and carries no address, a
  seeded world yields a season and a next matchday, the configured document versions are served, and driving the
  read-only flag command flips the state with the operator's reason — over the real database.
- New `features/info/info.spec.ts`: all four documents present in order, every document has sections with
  content, every link is an absolute in-app route, the disclosure-boundary tokens are absent, the terms and
  privacy documents are versioned, and the privacy page states the retention values fixed in the policy.
- New `core/status/status-store.spec.ts` and `features/status/status.spec.ts`: the store reads on start and each
  interval and stops when the page goes away, a failed read is remembered without blanking the last status, and
  the page maps operational, maintenance, unseeded and unreachable states without relying on colour.
- New `journeys/info.spec.ts`: a signed-out visitor reads every page from the footer, the register consent leads
  to the terms and privacy pages, a versioned page names the published version, and the status page reports the
  service operational; the five routes join `journeys/accessibility.spec.ts`'s signed-out axe scan.

## Stage 15 — A stepped clock and a test toolbar, so a season plays a press at a time

Stage 15's first exit criterion is "at least one complete closed-beta season and rollover succeeds under human
activity". The compressed clock (ADR-0015) accelerates a season but cannot be stepped: it is chosen at
composition, needs a restart to change, and runs whether or not anyone is watching. A tester needed the
opposite — a button that takes the world on one day, or to the next kickoff, and lets everything downstream
happen through the real worker. `Clock:Mode=Stepped` freezes game time at a stored instant and moves it only
when an operator advances it, and an app-shell toolbar presses the button (ADR-0049).

### Added

- **A stepped clock mode**: `Clock:Mode=Stepped` with `Clock:InitialNowUtc`. Game time is one stored instant in
  the new `ops.game_clock` row, read through `IGameClockStore` and kept fresh in each host by a one-second
  poller. It is refused in Production exactly as the compressed clock is, and the API and the worker must be
  given the same values (`TIME-6`).
- **An advance is a real worker job**: `POST /api/v1/ops/diagnostics/advance-game-clock` resolves the target —
  the next game day, or the next unpublished round's kickoff — and enqueues `ops.advance-game-clock` due now.
  The worker writes the instant and asks every scheduler's extracted materialisation for the day's jobs, so a
  step is indistinguishable from the calendar reaching the same moment (`TIME-7`, ADR-0049).
- **A test-clock toolbar**: a slim, server-gated bar under the header with the game date, the next round, and
  "Next day" / "Next matchday". It appears only where the API reports a stepped clock, waits for the step to
  land and the day to settle, then reloads so every screen shows the new day.
- **`GET /api/v1/ops/diagnostics/game-clock`**, and `Diagnostics:EnableGameClockControl` to map the pair — off
  everywhere by default, and mapped only when the clock is stepped (§17.12).

### Changed

- **The schedulers' materialisation is extracted behind `IJobMaterializer`.** Each scheduler still runs it on
  its interval; a step invokes the same code with the stepped instant, so the two paths cannot drift.
- **Under a frozen clock, transient failures retry at once.** A backoff measured from a frozen "now" would
  never become due, so the queue retries a transient failure immediately when the clock is stepped, and the
  attempt budget is what bounds it (ADR-0049).

### Notes

- **One migration, one table.** `Stage15OpsGameClock` creates `ops.game_clock` — a single row fixed by identity
  and constrained to it. No existing table is touched.
- **A frozen clock reaches no future deadline.** Leases do not expire and a retry scheduled ahead would not
  run; both are recorded in ADR-0049 rather than hidden, and both are acceptable for a world an operator is
  stepping by hand.
- **This supersedes ADR-0015 decision 4** — the clause that forbade an HTTP control surface over time. The
  compressed clock remains for unattended acceleration; the stepped clock is for a person with a button.

### Tests

- `SteppedClockTests`: Production refuses a stepped clock, a non-production host wires one, and real time is
  the default.
- `AdvanceGameClockTests`: the advance handler writes the target, never moves the clock backwards, invokes
  every materialiser, and dead-letters a payload with no target.
- `GameClockControlsTests`: the controls are unmapped when the flag is off and when the host is not stepped,
  the status reads the stored instant, and a day step targets the next midnight and is idempotent.
- `SteppedSeasonTests`: a seeded world plays three rounds across all six divisions by pressing "next
  matchday", through the real worker and engine — every round publishes and every fixture carries a result.
- `game-clock-store.spec.ts`: the toolbar hides when the host has no stepped clock, reports a step done only
  once the clock has reached the target, and gives up on a step that never lands.

## Stage 14 — Telemetry dashboards and SLO alerting, over the instruments that exist

The stage's last open half: §14.1's dashboards, and its "alerts must be actionable and linked to runbooks".
Both hosts have exported OpenTelemetry over OTLP since Stage 13, and nothing had ever received it — no metric
backend, no scrape endpoint, no dashboard, and no alert rule existed in the repository. There is now a local
stack that receives the export, three dashboards, and four alert rules, each naming the runbook section that
answers it. Only the instruments that exist are shown; the ones that do not are recorded rather than implied
(`F-48`, ADR-0048).

### Added

- **A local telemetry stack** (`F-48`): `infra/observability/` is its own compose project
  (`touchline-observability`, `npm run obs:up`), so it can never tear down the development stack. An
  OpenTelemetry collector receives the hosts' OTLP and re-exposes the metrics for Prometheus to scrape while
  forwarding the traces to Tempo; Alertmanager holds the alerts and Grafana reads both. Every published port
  is bound to `127.0.0.1`, because nothing in the stack is authenticated.
- **Three provisioned dashboards**: an SLO overview (availability, the error ratio behind it, and p95 read
  and command against their 500 ms and 800 ms objectives), an API RED view broken down by route template, and
  the two `F-54` product funnels — the same numbers `GET /ops/analytics/funnels` returns.
- **Four alert rules, each linked to a runbook**: availability fast burn, read p95, command p95, and a guard
  on the telemetry pipeline itself — which is what stops a dead collector from looking like a healthy game.
- **`npm run obs:check`**, which validates what would otherwise fail quietly: that every dashboard is valid
  JSON and names a provisioned datasource, that every metric named in a rule or a panel is one the
  application emits, and that every alert's `runbook` annotation resolves to a real section of
  `docs/operations/runbook.md`. The rules, the routing, the collector, Tempo, and the compose file go through
  their own validators.
- **Three runbook procedures** — the API is failing requests, the API is slow, and telemetry is not
  arriving — plus an alerts table, each following the seven fields §13 requires, and the new
  `docs/operations/observability.md`.

### Notes

- **No application code, no migration.** The milestone is configuration, dashboards, rules, docs, and one
  Node script; the .NET build, its tests, and the web build are untouched.
- **Reads and commands are approximated from the HTTP method.** The load suite tags the two exactly; a
  dashboard can only infer them, so reads are `GET`/`HEAD`/`OPTIONS` and commands are everything else. The
  inference is in ADR-0048 rather than hidden in a query.
- **The publication objective cannot be alarmed on.** "99% of matchdays published within five minutes" needs
  a delay instrument that does not exist, and neither does §7.1's queue telemetry, so those panels and alerts
  are deferred along with the rest of §14.1's metric list. The `F-48` traceability row now states exactly
  which half landed.
- **The stack is local and never deployed.** It has no authentication beyond Grafana's admin account, so it
  binds to loopback only and must not be exposed; the threat model records that as `I-12`.

### Tests

- `npm run obs:check` passes end to end: `promtool` validates the rules, `amtool` the routing, the collector
  and Tempo their own configurations, and `docker compose config` the stack file.
- Verified live against the running stack: the collector's target is up in Prometheus, `service_name`
  distinguishes the API from the worker, and the OTLP-to-Prometheus name translation was confirmed against
  real series — `http_server_request_duration_seconds_*`, `http_server_active_requests`, and
  `touchline_analytics_onboarding_total{step="registered"}`. Those names are what the allow-list check now
  pins, so the translation cannot drift silently on an OpenTelemetry upgrade.
- Tempo received real traces for `touchline-api`, and all three dashboards and both datasources provisioned.
- `TelemetryPipelineDown` was driven for real by stopping the collector: the rule went `pending`, then
  reached Alertmanager with its runbook annotation resolved, and resolved again when the collector returned.

## Stage 14 — Read-only incident mode

The stage's last self-contained control: master plan §13's "emergency read-only mode that blocks manager
writes while keeping published content available". An operator flips one world-scoped feature flag, and every
manager command is refused while reads, sign-in, and the operator console keep working and the worker keeps
advancing deadlines. No migration — it reads the `ops.feature_flags` store ADR-0045 already built
(`F-51`, ADR-0047).

### Added

- **A request-edge read-only gate** (`F-51`): `ReadOnlyModeMiddleware` runs after authorization and refuses
  any mutating manager request with `503 READ_ONLY_MODE`, carrying the operator's stated reason. Safe methods
  always pass; the admin and operational-analytics policies, sign-in, and the health probes are exempt, so an
  operator can always lift the switch. One registration covers every manager module, because those endpoints
  share no route group.
- **`incident.read_only`, read through the flag store** (`IReadOnlyMode`, `ReadOnlyModeReader`): the value is
  `{"enabled":true,"message":"..."}`, interpreted leniently — anything but an explicit `enabled: true` means
  "not read-only". The read is cached for five seconds and the admin command invalidates it, so a toggle takes
  effect at once in the API that serves it.
- **A client surface** (`MaintenanceStore`, `SystemNotices`): the shell reads the state from the existing
  `/sync` poll, shows a maintenance banner naming the operator's reason, and disables commands through one
  derived `canMutate` (online and not read-only). The server gate remains the backstop for a client that has
  not polled yet.
- **The incident state on the operator's first read**: `GET /api/v1/admin/health/game` now carries `readOnly`
  and `readOnlyMessage`, and the runbook gains a procedure to take the game read-only and give it back.

### Notes

- **Worker deadlines are not paused.** §13's wording keeps published content available; a full freeze remains
  the worker's `Enable*` configuration (ADR-0012). No migration was needed: `ops.feature_flags` already exists.

### Tests

- `ReadOnlyModeTests` (API) drives the real flag command and asserts a manager command is `503 READ_ONLY_MODE`
  while a read, `/sync`, and an admin mutation all still work, and that the command is allowed again after the
  flag is cleared; `ReadOnlyModeReaderTests` covers the interpretation and the cache; `SetFeatureFlagTests`
  asserts the command invalidates the cached read; the web specs cover the store, the banner, and the poll.

## Stage 14 — Load, supply-chain scans, and the point-in-time restore drill

The stage's operational proof: test-strategy **layer 10**. §16 requires load tests at three times the
projected launch population, dependency and container scans, and a backup/PITR restore with integrity
checks; `SC-2`/`SC-3` require the scans and §14 fixes the SLOs the load must meet. None of it existed.
All of it now runs **locally and by hand** — no application code changed, no schema moved, and nothing
is wired into CI yet (`F-49`, `SC-2`, `SC-3`, ADR-0046).

### Added

- **A k6 load suite at three times the projected launch population** (`F-49`): `tests/load/` holds five
  scenarios — a sign-in burst, dashboard reads, the matchday polling cadence, auction contention, and
  matchday publication. The population model is the world itself: six countries of eighteen clubs is 108
  clubs, so 108 concurrent managers and 324 virtual users at the required headroom. Each request is
  tagged `read` or `command`, so the p95 read (500 ms) and command (800 ms) SLOs are asserted per
  endpoint; a losing bid's expected `409` is declared, so `http_req_failed` still means "a `5xx`". k6
  runs through its pinned image, and the runner arranges the network per platform.
- **A load fixture built through the public API** (`tests/load/seed.mjs`): it brings the stack up,
  migrates and seeds the world, then registers, confirms, signs in, onboards, and claims one club per
  manager through the real endpoints — and opens a few listings for the auction scenario.
- **A supply-chain scan suite** (`SC-2`, `SC-3`): `infra/scan/scan.mjs` runs a .NET and npm
  vulnerability scan, a licence inventory for both read from the lockfiles and nuspecs a restore already
  wrote, a gitleaks secret scan, and a trivy scan of the base images. `.gitleaks.toml` allowlists by
  value, never by whole file, so a real secret in a test file is still caught.
- **A real point-in-time restore drill** (`F-49`): `infra/restore-drill/` stands up an archive-enabled
  PostgreSQL, seeds a world, marks a point in time, takes a `pg_basebackup`, writes data after that
  point, then recovers a fresh cluster to the point with `recovery.signal` and `recovery_target_time`,
  promotes it, and proves the outcome. `checks.sql` fails loudly unless the ledger replays to every
  stored balance, publication stays atomic, the job queue is sound, leases are cleared (ADR-0008), the
  post-target marker is absent, and the world is intact. `sanitize.sql` is the restored-environment step.
- **New npm scripts and three operations documents**: `scan`, `scan:*`, `load:*`, and `drill:restore`,
  with `docs/operations/supply-chain.md`, `load-testing.md`, and `backup-and-restore.md`.

### Notes

- **No migration, no application change.** The whole milestone is scripts, SQL, compose, npm wiring, and
  documentation; the .NET build, tests, and web build are untouched.
- **The base-image scan is advisory.** No image is shipped yet and production runs managed PostgreSQL
  (ADR-0008), so a trivy finding is a `WARN`; `scan:image:strict` becomes the gate when the deployment
  milestone builds images. The current `postgres:17-alpine` reports fixable Go `stdlib` advisories in its
  bundled `gosu` — the same set is in `postgres:18-alpine`, so it is upstream.
- **The matchday-publication scenario needs a due round.** On a real-time stack the next kickoff is days
  away and the scheduler has already materialised the round's jobs, so the diagnostics trigger is a
  no-op; the scenario says so and fails fast rather than polling for nothing. It runs against the
  compressed-clock matchday stack (ADR-0015).
- **Deferred to the deployment milestone** (recorded in ADR-0046): production Dockerfiles and image
  build/scan/publish; CI wiring and gating; deployment rollback and API/PWA compatibility (`F-50`);
  maintenance/read-only mode (`F-51`); telemetry dashboards and SLO alerting (`F-48`); and acting on the
  load evidence.

### Tests

- **Load**: the four pure-load scenarios were run against a live stack at 324 virtual users in smoke and
  full form — reads held a p95 well inside 500 ms with zero failures; the auction scenario placed bids
  under contention with no `5xx`. The publication scenario's guard was verified to fail fast and explain
  itself when no round is due.
- **Scans**: `npm run scan` was run end to end — dependency, licence, and secret checks pass; the
  base-image check reports its advisories as a warning. A deliberately introduced finding was confirmed
  to fail the run.
- **Restore**: `npm run drill:restore` recovered a seeded world to a chosen instant and passed every
  `checks.sql` invariant, with the post-target marker absent — the point-in-time property that makes the
  drill meaningful.

## Stage 14 — Administrative repairs and broadcasts: AI assignment, finance repair, announcements, and flags

The fourth Stage 14 milestone completes §10.8. §13 requires "account suspension/restoration and AI takeover",
"compensating finance entries rather than balance edits", and "feature flags and maintenance banners"; the
operator console could read and recover, but it could not yet hand a club to the AI, correct a ledger, tell
managers anything, or flip a switch. All four now exist on the frozen `AdminMutate` gate and cross the
`F-46`/`F-47` line (`F-46`, `F-47`, §13, ADR-0045).

### Added

- **Club assignment to the AI** (`F-46`, `OCC-3`, `OCC-6`, ADR-0045): `POST /api/v1/admin/clubs/{id}/assign-ai`
  closes the club's human tenure with `ClubTenureEndReasons.AdministratorClosed`, returning it fully to the
  AI, freeing its pyramid occupancy, and leaving it claimable — with **no** takeover cooldown and no change to
  the club's squad, contracts, cash, fixtures, or commitments (`OCC-5`). A club that already has no manager
  is a `409 CLUB_ALREADY_AI`.
- **Compensating finance entries** (`F-46`, `FIN-12`, ADR-0045): `POST /api/v1/admin/finance/compensating-entry`
  posts an append-only correction that moves a club's cash by a signed delta and names the line it corrects.
  The operator's `Idempotency-Key` becomes the entry's correlation key, so the ledger's own
  `unique (correlation_id, category)` index is the idempotency guarantee and no dedup store is added.
- **A reversal link on the ledger** (`F-46`, ADR-0045): `finance.ledger_entries.reverses_entry_id` (nullable,
  self-FK `Restrict`), so §13's "preserved original record" is a fact in the ledger and not only a sentence in
  an audit reason. `ck_ledger_entries_reverses` keeps the link exclusive to a `compensation` entry.
- **Operator announcements** (`F-46`, `COM-6`, ADR-0045): `POST /api/v1/admin/announcements` publishes a
  world-, country-, or division-scoped notice, optionally with an expiry, as a `comms.news_items` row under
  the new `announcement` category. Managers read it on the existing `GET /api/v1/news` feed.
- **The feature-flag store** (`F-46`, §6.9, ADR-0045): `ops.feature_flags` (`scope`, `key`, `value` jsonb,
  `rollout_metadata` jsonb, `version`; unique `(scope, key)`) and `POST /api/v1/admin/feature-flags/{key}`,
  which upserts a world-scoped flag to an opaque JSON value. Reading a flag to gate behaviour is the
  incident-control milestone; this makes the switch settable and audited.
- **New audit vocabulary** (`F-47`, ADR-0045): `world.club_tenure.assigned_ai`,
  `finance.compensating_entry.posted` (existing constant, first caller), `admin.announcement.published`, and
  `admin.feature_flag.set`, with target types `ledger_entry`, `news_item`, and `feature_flag`. Every command
  commits its audit row in the same unit of work as the change.

### Notes

- **Three migrations, one per command that touches storage.** `Stage14CompensatingEntry` adds the reversal
  column, its index, its self-FK, and its check constraint. `Stage14NewsAnnouncements` widens
  `comms.news_items.category` from `varchar(10)` to `varchar(12)` and adds `announcement` to
  `ck_news_items_category` — the only `AlterColumn`, and safe because the column only widens.
  `Stage14FeatureFlags` creates `ops.feature_flags`. `assign-ai` needs no migration.
- **`assign-ai` starts no cooldown.** `OCC-4`'s cooldown stops a manager resigning to shop for clubs; a repair
  the game makes on its own authority is not that, so applying it would lock a manager out of a repair.
- **The API never writes a squad decision.** Closing the tenure hands the club to the AI, and the worker's
  daily pass (ADR-0018) sets the tactics and training it now lacks.
- **Announcements store operator prose, the one such place.** The row still carries a template key and a
  parameter document, but the parameters hold the operator's title and body — a deliberate exception to
  `COM-2`, which exists so engine-written messages stay translatable.
- **The Angular operator console is the last `F-46` item.** The four commands, like the ones before them, are
  a bearer-token surface until the web milestone consumes them (`docs/architecture/adr/0045-...`).

### Tests

- New `tests/TouchlineManager.Infrastructure.Tests/World/AssignClubToAiTests.cs`: the tenure closes with
  `administrator_closed`, no cooldown starts, the club's cash is unchanged, the club is free again, and the
  audit row carries the reason; plus `AlreadyAi`, `ClubNotFound`, and a blank reason.
- Extended `tests/TouchlineManager.Domain.Tests/Finance/LedgerEntryTests.cs` and
  `tests/.../Application.Tests/Finance/LedgerPostingsTests.cs`: a compensating entry names the line it
  corrects, and only a compensation may carry the link.
- Extended `tests/.../Infrastructure.Tests/Finance/LedgerPersistenceTests.cs`: the reversal is stored, and
  the check constraint refuses a linked non-compensation row.
- New `tests/.../Application.Tests/Finance/PostCompensatingEntryTests.cs` and
  `tests/.../Api.IntegrationTests/AdminFinanceRepairTests.cs`: outcome mapping and audit; the gate from every
  side; a posted correction that names its target; an overdraw conflict; and an unknown club.
- New `tests/.../Application.Tests/Comms/PublishAnnouncementTests.cs` and
  `tests/.../Api.IntegrationTests/AdminAnnouncementTests.cs`: validation, scope, and audit; the gate matrix; a
  published notice that renders its title and body; and `ANNOUNCEMENT_INVALID` / `ANNOUNCEMENT_SCOPE_NOT_FOUND`.
  Extended `NewsMessageTextTests` for the announcement render.
- New `tests/.../Application.Tests/Ops/SetFeatureFlagTests.cs`,
  `tests/.../Infrastructure.Tests/Ops/FeatureFlagPersistenceTests.cs`, and
  `tests/.../Api.IntegrationTests/AdminFeatureFlagTests.cs`: create-then-update with a version bump, the
  `(scope, key)` uniqueness, jsonb round-trip, invalid key/value refusal, and the audit row.
- New `tests/.../Api.IntegrationTests/AdminClubTests.cs`: the gate matrix, a managed club handed to the AI
  with its audit row, `CLUB_ALREADY_AI`, and `CLUB_NOT_FOUND`.

## Stage 14 — Operator recovery commands: job retry and cancel, and matchday resume

The third Stage 14 milestone gives the operator the act that the console could only diagnose. §13 requires
"safe retry/resume of rollover and matchday workflows" and §10.8 lists `POST /admin/jobs/{id}/retry`,
`/cancel`, and `/admin/matchdays/{id}/resume`; §16's exit criterion is that "an on-call operator can diagnose
and resume failed matchday, auction, provisioning, and rollover workflows". The reads landed in the previous
milestone (`F-46`, `F-47`, ADR-0043); this one is the actions behind them, all on the `AdminMutate` gate
(`F-46`, §7, §10.8, ADR-0044).

### Added

- **Job retry** (`F-46`, §7, ADR-0044): `POST /api/v1/admin/jobs/{id}/retry` returns a dead-lettered job to
  the queue with a fresh attempt budget through `IJobQueue.RequeueAsync`, the primitive ADR-0034 recorded for
  the admin UI. Only a dead-lettered job is retried; a pending, leased, or completed job is reported
  unchanged with a `409`.
- **Job cancel** (`F-46`, §7, ADR-0044): `POST /api/v1/admin/jobs/{id}/cancel` moves a pending, leased, or
  dead-lettered job to the new terminal `cancelled` status and clears its lease. A completed or
  already-cancelled job is a `409`.
- **Matchday resume** (`F-46`, §13, ADR-0044): `POST /api/v1/admin/matchdays/{id}/resume` requeues a stuck
  round's governing job — resolution while the round is `pending`, publication while it is `staged` — and
  falls back to an enqueue under the same business key, exactly like the rollover resume. A published round,
  or one whose job the queue still owns, is a `409`.
- **The `cancelled` job status** (`F-46`, ADR-0003, ADR-0044): `JobStatus.Cancelled` and
  `JobStatuses.CancelledCode` join the four existing codes, and `IJobQueue` gains `FindByIdAsync` and
  `CancelAsync` so `ops.jobs` access stays in the queue port.
- **New audit vocabulary** (`F-47`, ADR-0044): `AdminAuditActions.JobRetried`, `JobCancelled`, and
  `MatchdayResumed`, with `AuditTargetTypes.Job` and `Matchday`. Every command commits its audit row in the
  same unit of work as the change.

### Notes

- **One migration, one check constraint, no web-client change.** `Stage14JobCancellation` drops and re-adds
  `ck_jobs_status` to admit `cancelled`; no column and no index moves. The three commands are a bearer-token
  surface until the Angular console consumes them in the next milestone.
- **Cancelling a leased job is safe by the queue's own guards.** Every worker terminal statement
  (`CompleteSql`, `DeadLetterSql`, `RescheduleSql`) is guarded by `status = 'leased'`, so a cancelled job can
  never also complete. Cancellation does not roll back work a handler already committed; that is inherent to
  the at-least-once, per-step-commit workflow design.
- **Matchday resume adds no domain transition.** Resolution already re-runs the fixtures that never staged —
  a fixture left `simulating` is one of them — and `MatchSnapshotFactory.FreezeAsync` returns the frozen
  snapshot untouched, so a resume re-reads rather than rebuilds (`MAT-9`, ADR-0014).
- **The Idempotency-Key is presence-checked, as for suspend and restore.** A replayed command finds the row
  no longer actionable and returns a `409`, matching the existing admin contract; no dedup store is added.
- **`docs/operations/runbook.md` now exists** (the file `docs/README.md` reserved for this stage): the
  diagnosis-to-action procedures for a stuck matchday, a dead-lettered job, a stuck job, and a failed
  rollover, each with who may act, prechecks, the exact command, validation, notification, compensation, and
  evidence. The rollover exception and the auction/provisioning limitations are documented there.
- **The remaining §10.8 mutations and the web console are the next milestone.** So are maintenance/read-only
  mode, telemetry dashboards, SLO alerts, and the load/restore drills; the recovery contract is frozen here
  (`docs/architecture/adr/0044-operator-recovery-commands.md`).

### Tests

- Extended `tests/TouchlineManager.Infrastructure.Tests/PostgresJobQueueTests.cs`: find by id, and the cancel
  edges — a pending job becomes terminal and unclaimable, a leased job's lease is cleared (so
  `ck_jobs_lease_consistency` holds), a dead letter reaches `cancelled`, and a completed or unknown job is a
  no-op.
- New `tests/TouchlineManager.Application.Tests/Ops/JobAdministrationTests.cs`: retry and cancel outcome
  mapping, the audit entry (action, reason, actor, target), and the race where the row moves between the read
  and the write.
- New `tests/TouchlineManager.Api.IntegrationTests/AdminRecoveryTests.cs`: the gate from every side
  (anonymous, plain manager, no fresh code, no idempotency key), a retried dead letter returning to
  `pending` with its audit row, a `JOB_NOT_RETRYABLE` and a `JOB_NOT_CANCELLABLE` conflict, a cancelled job,
  and a resumed round whose resolution job is back on the queue — with the `MATCHDAY_NOT_RESUMABLE` conflict.

## Stage 14 — The operator read console: jobs, matchdays, and audit

The second Stage 14 milestone gives the operator the diagnosis the first one could only promise. §13
requires the console to show matchday and worker status, queue depth and dead jobs, and a read-only audit
search; §16's exit criterion is that "an on-call operator can diagnose and resume failed matchday,
auction, provisioning, and rollover workflows". The first milestone built the gate — roles, the second
factor, and one aggregate health read (`F-46`, `F-47`, ADR-0042). This one builds the reads behind it: the
durable job queue, a single round with its failed simulation attempts, and the append-only audit trail,
each a projection over rows the game already holds (`F-46`, `F-47`, ADR-0043).

### Added

- **The job-queue read** (`F-46`, §13, ADR-0043): `GET /api/v1/admin/jobs` returns a keyset-paged page of
  `ops.jobs`, filterable by status and job type, with each job's attempts, lease, due instant, last error,
  and business key. The handler payload is not serialized — the business key is the correlation an
  operator needs.
- **The matchday read** (`F-46`, §13, ADR-0043): `GET /api/v1/admin/matchdays/{id}` returns the round's
  publication state and its division/country/season context, the nine fixtures with their club names and
  each fixture's latest simulation attempt (number, status, error category and message), and the lock,
  resolution, and publication job rows read by their deterministic business keys. It is the answer to
  "why is this round stuck?".
- **The audit search** (`F-47`, §13, ADR-0043): `GET /api/v1/admin/audit` returns a keyset-paged page of
  `ops.audit_log`, filterable by action prefix, actor, and target. It carries the actor, action, target,
  correlation ID, instant, and reason; the hashed client IP and the before/after metadata columns are
  withheld.
- **Stable job-status codes** (`F-46`, ADR-0043): the domain gains `JobStatuses` — `pending`, `leased`,
  `completed`, `dead_letter` — so the query and the filter validation share one vocabulary, mirroring the
  other status-code classes.

### Notes

- **No schema change and no web-client change.** Every read is a projection over `ops.jobs`,
  `competition.matchdays`/`fixtures`, `match.simulation_attempts`, and `ops.audit_log`: no column, index,
  or constraint moves. The reads are a bearer-token surface until the Angular console consumes them in the
  next milestone.
- **The reads reuse the inbox's keyset pagination** (§10): a page of 25, one extra row fetched to prove a
  successor exists, and an opaque base64url cursor (`AdminJobCursor`, `AdminAuditCursor`). A cursor this
  build did not produce is a `400` (`INVALID_CURSOR`), and an unrecognised status filter is a `400`
  (`INVALID_FILTER`).
- **All three reads sit behind `AdminRead`** — support, operator, or admin with a completed second factor
  — and are `Cache-Control: no-store`. They are reads, so they carry no `X-MFA-Code`, reason, or
  idempotency key; that is the mutation contract (ADR-0042).
- **The remaining §10.8 commands and the web console are the next milestone.** So is a `cancelled` job
  status, which needs a migration, and a global `audit_log(occurred_at)` page index; the read contract is
  frozen here (`docs/architecture/adr/0043-operator-read-console.md`).

### Tests

- New `tests/TouchlineManager.Application.Tests/Ops/AdminCursorTests.cs`: the job and audit page cursors
  round-trip, treat no cursor as the first page, and refuse a value the server did not produce.
- New `tests/TouchlineManager.Api.IntegrationTests/AdminConsoleTests.cs`: the console's reads from every
  side — anonymous, a plain manager, an operator with a completed factor, and a read-only `support`
  operator — the job page's status filter and withheld payload, a keyset walk over thirty rows that
  neither skips nor repeats, the `INVALID_CURSOR`/`INVALID_FILTER` refusals, a matchday's nine fixtures
  with their club names, an unknown matchday as `404`, and the audit search returning a recorded
  suspension without its hashed IP.

## Stage 14 — Operator access, MFA, and the first admin surface

Stage 14 opens with the track every later one leans on: an operator who can see the live game and act
on it. §10.8 fixes the rule — _"All admin mutations require MFA-authenticated role, explicit reason,
idempotency key, and audit event"_ — and none of it existed. No production path could grant
`admin`/`operator`/`support`; no second factor existed beyond `UserRoles.MfaRequired`; and no admin
route group was mapped. This milestone makes roles real, adds TOTP MFA with a two-step login, and
gates the first operator surface behind both. It is the first Stage 14 milestone (`F-46`, `F-47`,
ADR-0042).

### Added

- **Role administration** (`F-46`, §6.2, ADR-0042): `User.RevokeRole`, the audited `GrantRole` and
  `RevokeRole` use cases, and `tools/access-admin` — a console over the same composition roots, in the
  shape of `tools/world-seeder`, that grants, revokes, resets an authenticator, and lists. It is the
  bootstrap for the first administrator and the break-glass path when one loses their factor.
- **TOTP multi-factor authentication** (`F-46`, ADR-0042): a pure RFC 6238 implementation pinned to the
  RFC's Appendix B vectors, the `MfaCredential` aggregate with single-use recovery codes, and
  `auth.mfa_credentials` / `auth.mfa_recovery_codes`. The secret is encrypted at rest with AES-256-GCM
  under `Auth:EncryptionKey`, because a value that must be recovered to compute a code cannot be hashed.
- **A two-step login** (`F-46`, ADR-0042): a password success for an account with a confirmed factor
  returns a short-lived challenge instead of a session, and `POST /auth/mfa/login` completes it. The
  `mfa` claim is earned there and carried by the refresh session, so a rotated session keeps it. The
  challenge carries a purpose claim, and the access-token validator rejects any purpose, so a challenge
  is never a session. Self-service enrolment, confirmation, disable (refused for roles that require the
  factor) and recovery-code rotation are at `/auth/mfa/*`.
- **The gated admin surface** (`F-46`, `F-47`, ADR-0042): an always-mapped `admin` route group.
  `GET /api/v1/admin/health/game` reports world and season status, the next kickoff, and queue depth,
  dead letters and the oldest overdue job — behind `AdminRead` (support, operator, or admin with a
  completed second factor). `POST /api/v1/admin/users/{id}/suspend` and `/restore` are behind
  `AdminMutate` (support excluded, `E-3`) and a fresh `X-MFA-Code`, re-asserting the factor per
  mutation as ADR-0002 requires; both require a reason and an idempotency key and commit an audit entry
  with the change. Suspension revokes the account's sessions immediately.

### Notes

- **One migration, no web-client change.** `Stage14OperatorAccess` adds `auth.mfa_credentials`,
  `auth.mfa_recovery_codes`, and `auth.refresh_sessions.mfa_completed_at`. The `202` challenge branch
  only triggers for an account holding a confirmed factor, and none can hold one until a role is
  granted, so the existing client is unaffected until the MFA screens land.
- **The security policy is now enforced, not promised.** `S-6`, `E-1` and `E-3` move from "Stage 14
  will" to enforced; `data-classification.md` gains the multi-factor secret and recovery-code handling
  rules and the redaction note; `threat-model.md` gains `I-10` and the residual risk about a lost
  authenticator.
- **`Auth:EncryptionKey` is a new required secret** in every non-development environment, validated at
  startup beside `Auth:SigningKey`. `docs/architecture/adr/0042-operator-access-and-mfa.md` records the
  decision, the two-step login, and why the secret is encrypted rather than hashed; `modules.md`,
  `data-model.md`, `mvp-traceability.md`, `test-strategy.md`, `adr/README.md` and the README status
  line are updated with it.
- **The read console (jobs, matchdays, audit) and the remaining §10.8 commands are the next milestone.**
  So are the web MFA screens; the contract is frozen here.

### Tests

- New `tests/TouchlineManager.Domain.Tests/Auth/TotpTests.cs`: the RFC 6238 Appendix B vectors, the
  acceptance window, and the RFC 4648 base32 vectors including a round trip.
- New `tests/TouchlineManager.Domain.Tests/Auth/MfaCredentialTests.cs`: confirmation, secret
  replacement, and single-use recovery codes.
- New `tests/TouchlineManager.Application.Tests/Auth/RoleAdministrationTests.cs` and
  `MfaUseCasesTests.cs`: role changes with their audit and actor, and the multi-factor flows including a
  role that forbids disabling.
- New `tests/TouchlineManager.Infrastructure.Tests/Auth/MfaPersistenceTests.cs`: the stored column is
  ciphertext holding neither the base32 nor the raw secret, recovery codes cascade with the credential,
  and one account has one authenticator at the database.
- New `tests/TouchlineManager.Api.IntegrationTests/MfaTests.cs` and `AdminEndpointsTests.cs`: the whole
  journey over the real stack, the `mfa` claim earned and kept across a refresh, the challenge refused at
  `/me`, and the admin gate asserted from anonymous, plain manager, un-enrolled operator, operator
  without a fresh code, and operator with one — then suspension closing sessions and auditing its reason.

## Stage 13 — Privacy-safe operational funnels

The last Stage 13 deliverable: the two funnels an operator needs to run the game — how far the membership
walks onboarding, and who is still holding a club — read as counts over rows the game already writes. No
new table, no migration, no client collection, and no personal data: the surface is the product's first
role-gated endpoint, and its boundary is asserted by tests. This is the sixth Stage 13 milestone (`F-54`,
ADR-0041).

### Added

- **The onboarding and retention funnels, as counts** (`F-54`, §16 Stage 13, `LGL-5`):
  `GET /api/v1/ops/analytics/funnels` returns `registered → verified → profile → club` and the
  active/inactive/closed tenure and recent-login counts, derived by `IOperationalAnalyticsQueries` from
  `auth.users`, `world.managers`, and `world.club_tenures`. It adds no table and no personal data, and
  answers `Cache-Control: no-store`.
- **The product's first role-gated endpoint** (`LGL-6`, ADR-0002): the read requires the `operator` or
  `admin` role through a new `OperationalAnalyticsRead` policy — the policies Stage 2 defined, applied for
  the first time.
- **Operational funnel counters on the OpenTelemetry meter** (`§14.1`): `IOperationalMetrics` records an
  onboarding step and a tenure change as counts with no identity, and both hosts register the meter so a
  collector sees them; with no collector the calls are a no-op.

### Notes

- **No migration, no schema change, no web-client change.** The whole milestone is the server: a port, a
  projection query, a metrics port, six emission call sites, one endpoint, and its tests.
- **The disclosure boundary is executable.** The response carries counts and the world/season context and
  nothing else; `AnalyticsTests` fails the build if an account address appears in the body, and the metric
  vocabulary is pinned by name and tag.
- **`docs/architecture/adr/0041-privacy-safe-operational-funnels.md`** records the decision, including
  why the counts are derived rather than collected and why there is no client beacon. The ADR index gains
  its row; `mvp-traceability.md` gains `F-54` and its §2 mapping row; `test-strategy.md` gains the Layer 4
  and Layer 5 sentences; `content-and-fictional-data-policy.md` gains the analytics-boundary review gate
  in §5; `threat-model.md` gains `I-9`; `modules.md` names the funnels under `ops`; and the README status
  line closes the stage.
- **Product analytics beyond these two funnels is Stage 14/15 work.** Match distributions, financial
  health, auction liquidity, AI behavior, job lag, support volume, and scrubbed frontend error reporting
  belong to master plan §15 under `F-48`.

### Tests

- New `tests/TouchlineManager.Infrastructure.Tests/Ops/OperationalAnalyticsQueriesTests.cs`: a manager
  walking the funnel moves each of the four onboarding counts and the active-tenure count by exactly one,
  and resigning moves the tenure from active to closed — asserted as deltas over a shared seeded world.
- New `tests/TouchlineManager.Infrastructure.Tests/Ops/OperationalMetricsTests.cs`: the onboarding
  counter carries only its `step` tag, the tenure counter carries `event` and `reason`, and every step has
  a stable code.
- New `tests/TouchlineManager.Api.IntegrationTests/AnalyticsTests.cs`: anonymous is refused, a plain
  manager is forbidden, and an operator reads counts-only, with no account address in the raw body.

## Stage 13 — Guided help and the first-steps surface

A manager no longer has to already be standing on a screen to learn what the game does. A help screen states
the five subjects the stage promises — how the competition works, matchdays and deadlines, formations and
instructions, scouting and the market, and the season and its rollover — each as the rule, what follows from
it, and a link to the screen that owns the work. A manager who has just taken over a club is met on the
dashboard by a short first-steps list they can put away, and the deadline they are racing now says which time
zone it falls in. This is the fifth Stage 13 milestone (`F-53`, ADR-0040).

### Added

- **A help surface over the rules a manager needs** (`F-53`, §16 Stage 13): `features/help/` at `/help`,
  reached from the shell navigation. The five topics are authored as data beside the component, following
  `welcome/`, so the page reads nothing from the server and cannot render empty, stale or half-loaded. Each
  topic states the settled rule, the consequence, and the action, and links to the screen that owns it
  (`VOI-2`, `VOI-3`, `ACC-6`).
- **First-steps guidance on the dashboard** (§2.4): a card inside the claimed-club branch listing four steps —
  read how the game works, check the squad is legal, set the formation and instructions, and prepare the next
  team sheet — the last of which appears only while a fixture is actually waiting. It is dismissed for the
  session, and dismissal moves focus to the club heading rather than dropping it onto the document body.
- **Deadlines name their time zone** (`VOI-4`, `CAL-4`): a new `formatDeadline` renders a deadline as an
  absolute moment with the zone beside it, and the dashboard, fixtures and prepare screens use it.

### Fixed

- **A deadline read as a bare local time.** `formatInstant` documented itself as naming the zone but never
  set `timeZoneName`, so every deadline rendered without saying where it was. The docstring is corrected and
  the three deadline screens now use the formatter that does name it. `formatInstant` keeps its behaviour for
  the eighteen call sites that render a timestamp, and no deadline moves — only how the moment is shown.

### Notes

- **No migration, no server change, no new runtime dependency.** The milestone is the web client and its
  tests. `game-rules.md` is unchanged: rendering a settled rule is not a rule change, and ADR-0021 already set
  the split where the codes are the server's and the wording is the client's.
- **The help copy is bounded by the content policy, and the boundary is asserted.** No topic states hidden
  potential, a seed, an internal valuation, or a detection threshold (`MAT-11`, `VOI-11`), so retirement is
  described as an announcement a manager receives and never as a chance. The topic spec fails the build if one
  of those tokens appears, and if a link stops being an absolute in-app path.
- **The new screen joins the axe gate.** `/help` is added to the signed-in route list in
  `journeys/accessibility.spec.ts`, so ADR-0039's gate covers it at desktop and mobile, and the new untagged
  `journeys/help.spec.ts` rides the same two projects.
- **`docs/architecture/adr/0040-guided-help-and-first-steps.md`** records the decision, including why the
  guidance is not a tour, why nothing is persisted in browser storage, and why the zone's name is a
  deadline-only formatter. The ADR index gains its row; `mvp-traceability.md` gains `F-53` and its §2 mapping
  row; `test-strategy.md` gains the Layer 7 and Layer 8 sentences; `content-and-fictional-data-policy.md`
  gains the help-copy review gate in §5; and the README status line and `features/README.md` name the new
  folder.
- **Product analytics is still the one Stage 13 deliverable outstanding**, left in ADR-0040 for its own
  privacy-reviewed milestone.

### Tests

- New `features/help/help.spec.ts`: the five subjects are covered in order, every topic has a title, a
  summary, at least three points and a destination, every link is an absolute in-app route with descriptive
  text, and no point discloses a server-only value.
- `core/world/presentation.spec.ts` gains a `formatDeadline` block: it names the zone and so differs from
  `formatInstant`, it renders a different moment in a different zone, and it honours the configured zone and
  locale by default. The existing `formatInstant` assertions are untouched.
- New `features/dashboard/dashboard.spec.ts`: the first steps appear for a manager holding a club, the
  team-sheet step is absent when no fixture is waiting, and dismissing removes the guidance with focus
  landing on the heading.
- New `journeys/help.spec.ts`: a manager takes over a club, dismisses the first steps, reaches Help from the
  navigation, finds all five subjects, follows a topic to its screen, and resigns the club so the shared
  world is left as found.

## Stage 13 — Accessibility remediation and the axe gate

The promise in §11.3 is now enforced where it is easiest to break. An automated axe journey runs over
every core screen at desktop and mobile, and the gaps it can see were fixed: the squad roster has a name,
a tactics slot can be moved without a mouse, a scrollable table region is reachable by keyboard, a focused
control keeps its ring, and focus follows the navigation rather than staying on the link that was pressed.
This is the fourth Stage 13 milestone (`F-52`, ADR-0039).

### Added

- **An axe gate over the core routes** (`F-52`, §15.6): `@axe-core/playwright` and a new `@a11y` journey
  (`journeys/accessibility.spec.ts`) scan every public screen and every manager screen a claimed club
  reaches, filtered to the WCAG 2.2 A/AA tags. It runs in the desktop and mobile projects the suite
  already has — so the card layouts are audited on a phone and the tables on a desktop — and collects
  each screen's violations so one run names every offending page. `npm run test:a11y` runs it alone.
- **A focus-ring guard** (`§11.3`): the journey focuses a text control and asserts a ≥2px outline, because
  the ring survives only through the unlayered global `:focus-visible` rule.
- **A keyboard path to move a tactics slot** (`TAC-7`, §11.3): a focused slot marker moves with the arrow
  keys, bounded to the pitch, and the selection status line announces the new position — the non-drag
  alternative for a slot's position, alongside the assignment table that already covers player and role.

### Fixed

- **The squad roster was the one unnamed table.** PrimeNG's `p-table` renders `<table role="table">` with
  no accessible name; it is named now through PrimeNG pass-through (`[pt]="{ table: { 'aria-label': … } }"`).
- **Scrollable table regions had no keyboard access.** Every `overflow-x-auto` table wrapper that can
  scroll on a narrow screen is now a focusable, named `role="group"`, so a keyboard user can scroll it
  (WCAG 2.1.1) instead of needing a pointer.
- **A slot's state was invisible to assistive technology.** "Out of position" and "unavailable" were
  rendered `aria-hidden` and were absent from the marker's accessible name; the name now carries the state
  and the empty case.
- **Focus did not move on navigation.** A single-page app swaps the view without moving focus; the shell
  now focuses `<main>` on every navigation after the first (WCAG 2.4.3). The router already set the title.
- **Contrast and labelling details:** the footer's support reference `<code>` met 4.5:1; the training
  card's per-player focus select is single-sourced through `aria-label` with a plain visible label (it had
  both a wrapping `<label>` and an overriding `aria-label`); and the mobile menu's `aria-controls` no
  longer points at an element that only exists while the menu is open.

### Notes

- **No migration, no server change, no new runtime dependency.** Everything here is the web client and its
  tests; `@axe-core/playwright` is a test-only dev dependency (its lockfile change is committed).
- **The gate rides the existing e2e job.** Because the `@a11y` journey lives in `journeys/`, the CI e2e
  step already runs it at desktop and mobile; the Canvas viewer is scanned in the matchday step, where a
  real replay exists. No workflow change was needed.
- **Axe is a floor, not a proof of AA.** It cannot see reduced-motion behaviour or a screen reader's
  experience, so the manual keyboard and screen-reader checks in §15.6 remain.
- **`docs/architecture/adr/0039-accessibility-baseline-and-axe-gate.md`** records the decision;
  `mvp-traceability.md` gains `F-52` and its §2 mapping row, and `test-strategy.md` gains the Layer 8
  paragraph and the accessibility gate row.

### Tests

- New `journeys/accessibility.spec.ts` (tagged `@a11y`): axe over the signed-out screens and the
  onboarding and manager screens, plus the focus-ring guard. It passes at desktop and mobile.
- `matchday/matchday.spec.ts` scans the match center while a replay is on screen, covering the Canvas
  viewer's `role="img"`, narration, and transport.
- `support/accessibility.ts` (new) holds the axe helper, the violation report, and the focus-ring check.

## Stage 13 — Responsive layouts, touch targets, and the breakpoint suite

The app is responsive in the way Stage 13 promises, and it is proven at more than one width. Below
`md` the sidebar becomes a header menu, the dense screens present cards instead of tables a phone
could only scroll sideways, every control is at least 44px tall, and the core Playwright suite now
runs at a desktop and a mobile breakpoint with a tablet project for the layout boundary. This is the
third Stage 13 milestone (`F-44`, ADR-0038).

### Added

- **A mobile navigation disclosure** (`F-44`): below `md` a menu button in the header toggles a
  vertical panel of the same fourteen destinations, and the desktop sidebar is hidden. It is a
  disclosure rather than a drag drawer, so it needs no focus trap and closes on Escape and on the
  destination chosen. Both layouts iterate one `NAV_ITEMS` model, so a destination cannot exist on one
  and be missing from the other.
- **A 44px touch floor** (`§11.3`): `TOUCH_TARGET` in `control-styles.ts` composes `min-h-11` into
  every text input, select and button; checkboxes gained a `CHECKBOX_ROW` label target so the whole row
  is tappable; and `LINK_ACTION` gives action links the same height while `LINK` stays bare for links
  inside a sentence. The tactics drag chips and the shell's navigation and header controls were raised
  to match.
- **Card layouts for the dense screens** (`F-44`): squad (and its contracts), the training focus list,
  the tactics assignment table, the onboarding club list, scouting results, and transfer listings now
  render their `hidden md:block` table beside a `md:hidden` card list built from the same data, so a
  phone never presents a wide table it can only scroll — and the transfer bid control is no longer in
  a seventh column off the screen.
- **A breakpoint E2E suite** (`F-44`, ADR-0038): the core `playwright.config.ts` declares a `desktop`
  and a `mobile` project that each run **every** journey, and a `tablet` project that runs only the
  `@responsive` spec. A shared `support/navigation.ts` opens the disclosure when it is present, so one
  journey runs at both widths; `journeys/responsive.spec.ts` asserts the disclosure-versus-sidebar
  switch, the card-versus-table layouts, the 44px targets, and that no screen makes the page scroll
  sideways.

### Fixed

- **Four tables had no scroll container.** The squad table, the contracts table, the training focus
  table, the tactics assignment table, and the onboarding club table could overflow the page on a
  narrow screen. Each scrolls inside its own box now, or is replaced by cards below `md`.
- **Row actions were ~20px tap targets.** Squad, fixtures, inbox, and match-viewer links used as
  actions now use `LINK_ACTION`.

### Notes

- **No migration and no server change.** Everything here is the web client and its tests.
- **Both layouts are in the DOM; one is hidden.** `display:none` controls are inert for a person, but
  a DOM-based test locator can still see them, so the journeys scope to the layout the breakpoint is
  showing via the `data-testid` hooks on each pair.
- **`docs/architecture/adr/0038-responsive-shell-and-touch-targets.md`** records the decision;
  `mvp-traceability.md` `F-44` now carries this milestone's evidence, and `test-strategy.md` gains the
  breakpoint paragraph and gate. The ADR index, which had missed ADR-0037, is corrected.

### Tests

- New `journeys/responsive.spec.ts` (tagged `@responsive`): the shell shows the menu below `md` and the
  sidebar above it; opening the menu reveals the destinations and Escape closes it; a converted screen
  shows cards below `md` and its table above it; the shown controls are at least 44px tall; and squad,
  training, the club list, finances, competitions, transfers, scouting, and the inbox do not overflow
  the page.
- `journeys/squad.spec.ts` and `journeys/training.spec.ts` now branch on the breakpoint — asserting the
  card list on a phone and sorting, the table, and the labelled focus selects on a desktop — so the
  same journey proves both layouts.
- `journeys/inbox.spec.ts` was corrected. Its "empty inbox" assumption predated the takeover welcome
  message (COM-1), which every onboarding writes; it now asserts that message is on screen, and reaches
  the inbox through the shared navigation helper at both breakpoints.

## Stage 13 — PWA hardening and the offline boundary

The installable PWA becomes real. A deployed update is offered with a reload, an offline read is labelled,
and — for the first time — mutation controls are actually disabled offline, which is what the offline banner
had always claimed. This is the second Stage 13 milestone (ADR-0037).

### Added

- **A service-worker update prompt** (`F-45`, ADR-0007): `UpdateStore` watches `SwUpdate` and raises
  `updateReady` when a new version has downloaded; the notice offers **Reload** (activates the version and
  reloads onto it) and **Later**. An unrecoverable worker state offers the reload alone.
- **`SystemNotices`**, one component for the offline, stale, and update banners, so their wording and order
  are decided in one place and their states are unit-tested without the shell's thirteen stores.
- **A stale-read indicator**: `SyncStore` now tracks `lastRefreshedAt` and `refreshFailed` and exposes
  `isStale`; a read that could not reach the server while online is labelled as possibly older rather than
  passed off as current.
- **Refetch on visibility and reconnection** (`§11.2`): `SyncStore` reads again the moment the tab becomes
  visible or the browser comes back online, instead of waiting out the sixty-second tick.
- **Reference-data caching** (`§11.4`): a `reference-data` data group caches the public world summary and
  country list, beside the already-cached published match presentations. Volatile reads are not cached.
- An `apple-touch-icon` in `index.html`.

### Fixed

- **Offline mutations are now blocked, as the banner always said they were.** Every mutation control across
  onboarding, tactics, training, prepare, transfers, the contract actions, and settings is disabled while
  offline, so a screen cannot save because it is disabled rather than because the network refused it
  (ADR-0007, ADR-0037).
- **`ngsw-config.json` prefetched `/index.csr.html`**, a file the build never emits (there is no
  SSR/prerender). The dead entry is gone.

### Notes

- **No migration and no server change.** Everything here is the web client: an update store, a notices
  component, the sync store's freshness signals, per-screen `canMutate` gating, and the worker configuration.
- **Offline mutations are still never queued** (ADR-0007): no service-worker background sync, no optimistic
  money, bids, claims, renewals, or team sheets. A control is disabled rather than replayed late.
- **The stale label is honest about what it can see.** The application cannot observe a service-worker cache
  hit, so "stale" means the last refresh failed, not "this response came from the cache" (ADR-0037).
- **`docs/testing/test-strategy.md`** gained the new Layer 7 and Layer 8 material, and `mvp-traceability.md`
  `F-45` now carries this milestone's evidence; `F-44` (responsive/touch) remains open.

### Tests

- New frontend unit suites: `update-store.spec.ts` (a ready version is offered, a detected or failed one is
  not, reload activates and reloads, an unrecoverable state is reported, and a disabled or absent worker makes
  the store inert) and `system-notices.spec.ts` (each banner shown and hidden in the right states, and the
  buttons wired). `sync-store.spec.ts` gained immediate refetch on visibility and reconnection, the
  `isStale`/`refreshFailed` transitions, and that stopping removes the listeners.
- A new Playwright stack (`playwright.pwa.config.ts`, `npm run test:pwa`) builds the production web, serves
  it from a static origin that proxies `/api`, and asserts the manifest is served, the service worker takes
  control, an offline reload still loads the shell, and a mutation control is disabled offline and enabled
  again on reconnection. Wired into the CI end-to-end job.

## Stage 13 — Account sessions, export, and time-zone preferences

Stage 13's account surface is complete. A manager can see every device that can sign in and end one
without ending the rest, download everything the game holds about them as one JSON file, and choose the
time zone every deadline is rendered in. This is the first Stage 13 milestone (ADR-0036).

### Added

- **A session list and per-session revoke** (`F-06`, `F-07`, ADR-0036): `GET /api/v1/auth/sessions` lists
  the account's active sessions and marks the one that is asking; `DELETE /api/v1/auth/sessions/{id}`
  revokes one and returns `404 SESSION_NOT_FOUND` for an unknown or unowned id. The current session is
  identified by hashing the refresh cookie the request carries, so the endpoints sit under `/auth`, where
  the path-scoped cookie travels. The session in use cannot be revoked from the list (`CURRENT_SESSION`);
  signing out is how it ends. `RevokeByIdAsync` is a set-based, owner-scoped conditional update, so an
  account can never revoke a session it does not own and two concurrent revokes cannot both succeed.
- **A machine-readable account export** (`F-07`, master plan §12.4): `GET /api/v1/me/export` returns one
  JSON document — the account and its consents, the manager profile, every tenure the account has held,
  the active sessions, and the current club's ledger as the own transactional history — scoped from the
  authenticated account and sent `Cache-Control: no-store`. It carries no password hash, token, token
  hash, client-fingerprint hash, or security stamp.
- **Editable locale and time zone** (`CAL-4`): `PATCH /api/v1/manager-profile` changes the manager's
  formatting preferences under `If-Match`, reusing `Manager.ChangePreferences` and validating the zone
  against the runtime's database rather than a pattern. The client adopts the stored locale and zone as
  its defaults, so every deadline screen renders in the manager's chosen zone and re-renders when it
  changes.
- **The settings screen grows three cards**: the session list with a per-device sign-out, the export
  download, and the locale and time-zone picker.

### Notes

- **No migration.** The features read and update existing tables; the export is a projection and the
  preference change updates `world.managers`.
- **The export's scope is written down** (ADR-0036, data classification §6): a club's matches and its
  earlier ledgers are world records that outlive the managers who ran it, so they are not copied into the
  departing manager's personal export; the tenure history records the clubs they left.
- **`docs/security/data-classification.md`** §6 and its Stage 13 verification item were updated,
  `game-rules.md` `CAL-4` now names the stored preference, and `mvp-traceability.md` F-06 and F-07 carry
  the new evidence.

### Tests

- New API integration suite (`AccountManagementTests`): the list marks the current session and carries no
  token or fingerprint material; revoking another session stops it refreshing; the current session is
  refused; an unknown one is `404`; the export omits every secret and is `no-store`; and the preference
  change is `428` without `If-Match`, `412` when stale, `400` for an unknown zone, and updates what the
  position reports.
- A new auth persistence test asserts `RevokeByIdAsync` scopes to the owner and leaves siblings and other
  accounts alone.
- Web unit tests: the sessions store's read, revoke, in-flight guard, failure, and clear; the presentation
  helpers' configured locale and zone; and the onboarding store adopting and changing the time zone.

## Stage 12 — The five-season staging run

Stage 12's last exit criterion is met: **at least five consecutive automated staging seasons reconcile
cleanly**. A new worker integration test seeds a world, provisions an adjacent tier, attaches a human/AI
mix, and then plays five seasons end to end — the worker locks nothing, but its queue and the real
resumable rollover machine do every part of each season's close — asserting after each that the closing
season reconciled and the next one opened complete, and at the end that squads, money, movement, and
history are all still sound. A latent defect the run exposed is fixed: the AI transfer market's daily job
could never be produced, because its scheduler was written but never registered.

### Added

- **`StagingSeasonRunTests`** (`PR-1`–`PR-6`, `TBL-13`, master plan §16 Stage 12, ADR-0035): a seeded world
  plus one provisioned second tier for a single country, and two human tenures in that pair, driven through
  five consecutive seasons. Each season's fixtures are published deterministically
  (`DeterministicDigest`-derived scores) and the projections rebuilt with the same tool live publication
  uses, exactly as the single-season rollover suite does; the season's real `competition.season-rollover`
  job is then enqueued and executed by the worker. After every rollover the test asserts the world's season
  pointer advanced once, the closing season completed and the next is active and starts on a legal
  matchday, every active tier has eighteen clubs, thirty-four matchdays, three hundred and six fixtures,
  and an opening table, every closing division reconciles with `RebuildDivisionProjections(apply: false)`,
  movement is three up and three down, and the season's finance summary and position awards were written.
- **The run's end-state assertions**: six seasons and five completed rollovers; every club's squad still
  legal (`SQ-2`); every account's balances replay exactly from its ledger (`FIN-18`) and none went
  negative; both contract outcomes of the human/AI mix occurred (an unmanaged club renewed, a present
  manager's unrenewed player expired, `CON-6`); every club with no active tenure still has the default side
  the AI supplied (`INS-12`); and no rollover job was dead-lettered.
- **A redelivery case** (`ADR-0031`): a season rolled over twice in the run returns `AlreadyCompleted`,
  creates no third season, and leaves its three promotions exactly once.
- **`AiMarketSchedulerTests`**: a worker integration test that starts the worker's composition with only
  the AI-market scheduler enabled and asserts the day's `market.evaluate-ai` row appears. It is the
  regression guard for the registration fix below.

### Fixed

- **The AI transfer market's scheduler was never registered.** `AiMarketScheduler` (`TRF-12`, ADR-0025),
  its options, its handler, and its job type all existed, but `AddJobQueueWorker` never added the hosted
  service, so nothing placed the daily evaluation row and the AI clubs never traded. It is now registered
  beside the other materialisers; `AiMarketSchedulerTests` fails without it.

### Notes

- **No migration and no production surface.** The run is a test over existing tables; it adds no HTTP
  route, CLI, or configuration an operator must reason about. The production admin surface remains
  Stage 14's.
- **The seasons are fabricated, not simulated.** The run's subject is continuity across seasons, not the
  match engine, so each season's results are deterministic and the projections are rebuilt from them, as
  the one-season rollover suite already does; the engine and the matchday pipeline are pinned by their own
  suites. The rollover scheduler is present, as on a staging worker, and the run-now trigger (the
  scheduler's own materialisation under the same business key, ADR-0034) makes each job due immediately
  rather than waiting out the materialiser's poll.
- **The "default tactic" criterion is read as the AI clubs' side.** The seeder creates no tactical plan and
  a human-held club that never saved one is repaired by the snapshot builder at the lock (`TAC-10`), so
  the run asserts a side for every club with no active tenure rather than a tactic a human never set
  (`INS-12`); ADR-0035 records the reading.
- **ADR-0035** records the decisions: a CI gate rather than a runnable surface, the real durable pipeline
  with deterministic fabricated play, the seeded two-tier world with a human/AI mix, the enforced exit
  criteria and the one that is read carefully, and the scheduler registration as a defect the run exposed.
  `docs/product/mvp-traceability.md` F-29 and Layer 6 of `docs/testing/test-strategy.md` were updated, and
  `.env.example` gained the previously undocumented `Diagnostics__EnableRolloverTrigger`.

### Tests

- A new worker integration suite (`StagingSeasonRunTests`): the five-season run and the mid-run redelivery
  no-op, both over a real PostgreSQL 17 container. A second new worker test (`AiMarketSchedulerTests`)
  pins the scheduler materialisation.

## Stage 12 — Operator preview and resume for the season rollover

The rollover becomes operable. An operator can now see exactly what a rollover would move and pay before it
runs, close a season on demand in a non-production environment, and resume a failed rollover instead of being
stuck with a dead-lettered job. This is the fourth Stage 12 milestone (ADR-0034); the five-season compressed
staging run and Stage 13's PWA hardening are the later ones.

### Added

- **A read-only rollover preview** (`PR-4`, `TBL-13`, ADR-0034): `POST /api/v1/ops/diagnostics/preview-rollover`
  reports preflight (unpublished matchdays and each division's reconciliation), the promotion/relegation plan
  and its totals for every country, the position awards the finalize phase would pay, and whether the rollover
  is ready — all without writing anything or taking a lock (`ADR-0020`).
- **A run-now trigger** (`PR-4`): `POST /api/v1/ops/diagnostics/run-rollover` enqueues the season's real
  `competition.season-rollover` job, due now, under the scheduler's own business key, exactly as the calendar
  deadline would. The worker still runs the resumable machine (ADR-0016's pattern).
- **An audited resume control** (`PR-4`, ADR-0031, ADR-0034): `POST /api/v1/ops/diagnostics/resume-rollover`
  retries a `Failed` rollover from `Started`, records `world.season_rollover.resumed` with the operator's
  reason, and returns its dead-lettered job to the queue.
- **`IJobQueue.RequeueAsync`** (ADR-0003): resets a `dead_letter` job to `pending` with a fresh attempt budget
  on an operator's authority, and reports whether it acted. It is the only way to run a job the queue has
  stopped retrying, and Stage 14's admin tools will reuse it.
- **`SeasonRolloverPlanLoader`**: the closing season's shape, extracted from `RunSeasonRollover` and shared
  with the preview, so a dry run and the run it previews read one plan and cannot drift (ADR-0031 decision 5).
- A `Diagnostics:EnableRolloverTrigger` switch. Off by default and never set outside a test environment, so
  the controls have no production surface (master plan §17.12); the rollover itself stays worker-only
  (ADR-0031 §7).

### Notes

- **No migration.** The preview reads existing tables and columns; `RequeueAsync` updates `ops.jobs`; the
  audit action reuses `ops.audit_entries`.
- **The production operator surface is Stage 14's.** These are non-production diagnostics controls, not the
  admin UI, MFA, or runbooks the hardening stage owns.
- **ADR-0034** records the decisions: a read-only preview reusing the machine's plan, a trigger that only
  enqueues, an audited resume on a new queue operation, and the non-production gate.

### Tests

- New application tests (over fakes): the resume retries, audits with the reason, and requeues (falling back
  to an enqueue when no dead-lettered row survives); it is a no-op when the rollover has completed, has not
  failed, or does not exist, and refuses a blank reason; the trigger enqueues the real job due now.
- New real-PostgreSQL tests: `RequeueAsync` resets a dead-lettered job with a fresh budget, and leaves a
  pending or unknown one alone; a preview reports not-ready before the season is played, then three-up/
  three-down, the tier awards, and writes nothing; a rollover forced `Failed` by a corrupted table row is
  reconciled, resumed, and completes with each club moved exactly once.
- New API tests: the three controls are `404` when the flag is off; with it on, the preview reads the current
  season, the run enqueues under `season:{id}:rollover`, and a resume without a reason is a `400`.

## Stage 12 — Season history, career stats, and the provisioning seed fix

The season's records become readable, and a latent generation defect is fixed. A club's finished seasons
are read back from the entries the rollover closed, a player's career is aggregated from the season
statistics, the manager whose club moved is told where it went, and a provisioned tier is now named from the
world seed so it cannot collide with the tier above it. This is the third Stage 12 milestone; the operator
dry-run/resume preview, the five-season staging run, and Stage 13's PWA hardening are the later ones.

### Added

- **Club season history** (`PR-4`, `PR-6`): `GET /api/v1/clubs/{clubId}/history` projects each finished
  season's tier, final rank, promotion/relegation, and closing cash and reputation, newest first, plus the
  next season the club is already placed in when the rollover has created it (`PR-5`). Public game data, like
  the table; the closing cash comes from the entry, which the season finance summary agrees with by
  construction (ADR-0032).
- **Player career stats** (`STA-2`): the player profile now carries the whole career — the totals across
  every season and each season's line — aggregated from the retained `competition.player_season_stats` rows.
  Nothing new is stored, and the career average rating is recomputed from the summed basis points and rated
  appearances, so a season with more rated games carries the weight it should.
- **Promotion/relegation message** (`PR-1`, `COM-1`): the rollover's move phase posts one inbox message to
  each attended club whose tier changed, inside the move transaction, next to the placement it announces. AI
  clubs are told nothing, and a redelivery posts nothing twice.
- **A "Seasons" screen** (`/history`) showing the manager's own club's history and, when known, the next
  season; and a Career section on the player profile.

### Fixed

- **Club identity is seeded from the world seed, never the per-tier provisioning seed** (`PYR-11`,
  ADR-0033). A provisioned tier drew its name offset from the provisioning request's seed, which folds the
  target tier in, while the seeded tier above drew from the raw world seed; the two eighteen-slot windows
  could overlap on the shared name cycle (~16%), which the unique name index rejected with 23505 and which
  would dead-letter a provisioning job in production. `TierGenerationRequest` now carries the world seed
  separately from the tier-scoped request seed, and the provisioning generator is bumped to
  `division-gen-v2`.

### Notes

- **No migration.** The new reads use existing tables and columns; the movement message reuses the inbox's
  `table` category rather than widening its check constraint.
- **A career is derived, never stored.** The season statistics survive rollover, so a career is the same
  projection the leaderboard reads, summed; a player who has never appeared has no career rather than a row
  of zeroes.

### Tests

- New domain and application tests: the club-identity invariant across tiers, the season-history mapping,
  and the movement message's factory and renderer.
- New real-PostgreSQL tests: a provisioned tier's names come from the world seed and cannot collide, a
  club's history and next-season placement read after a real rollover, and a player's career sums two
  seasons. The rollover suite now also asserts the movement message reaches the club's manager.
- The provisioning flake is gone: `ProvisioningTests` is deterministic, so the Infrastructure suite is
  168/168.

## Stage 12 — Rollover continuity: contracts, retirement, awards, and summaries

A season no longer just moves clubs; it settles the people and the money. The rollover gains a **`squads`**
phase between `finalized` and `moved` that expires unrenewed contracts into free agency, renews the squads of
clubs nobody manages, retires players through a hidden announce-then-play rule, and repairs any club left
below the minimum. The finalize phase posts the season's **position awards** and writes a **season finance
summary** per club. This is the second Stage 12 milestone (ADR-0032); the operator dry-run/resume preview, the
five-season staging run, and the season-history screens are the later ones.

### Added

- **The `squads` rollover phase** (`SeasonRolloverPhase.Squads`, ADR-0032). It runs before the move phase —
  the plan's §7.5 order (resolve contracts, then move clubs) — and find-or-creates the next season first,
  because an emergency replacement's registration names `effective_season_id`. `Move`'s guard moves from
  `Finalized` to `Squads`; `Complete` is unchanged. The phase is resumable and idempotent like the rest.
- **`SettleSquadContinuity`** (`CON-6`, `CON-9`, `SQ-8`): retirements first, then expiry/renewal, then repair,
  all staged into the phase's transaction. A present manager's unrenewed player closes `expired`, ends their
  registration, and becomes a `free_agent`; a club nobody manages renews; a club left below the minimum is
  repaired with audited emergency replacements.
- **`AiContractPolicy`** (`ai-contract-v1`): a pure, versioned policy that renews an unmanaged club's best
  expiring players up to `AiContractTargetSquadSize` and then only as far as `SQ-2` legality requires, and
  releases the rest. "Unmanaged" is no tenure **or** an inactive one (`OCC-2`).
- **`RetirementPolicy`** (`retirement-v1`, `CON-10`): announce-then-play. From 32 a player may announce that
  the coming season is their last, with a chance that rises each season and is gated by ability and condition;
  forced announcement and forced retirement ages (36/38 and 37/39, outfield/goalkeeper) cap it. An
  announcement is stored on `squad.players.retirement_announced_season_number`, delivered to the manager as an
  **inbox message** (`InboxTemplates.RetirementAnnounced`), and the announced player is kept for one final
  season. The numbers are hidden — named rule-set values, never serialized.
- **Emergency replacements** (`SQ-8`): `PlayerGenerator.GenerateEmergencyReplacement` generates one player on a
  one-season contract per gap, on its own seed stream, so a repair is reproducible and never collides with a
  club's generated squad. Each repaired club is audited (`SquadAuditActions.EmergencyReplacement`) and logged
  as an operations alert.
- **Position awards** (`FIN-5`): `LedgerPostings.PositionAward` finally has its caller. Each closing club is
  paid `WorldRuleSet.PositionAwardMinorFor` inside the finalize transaction, after the entry close has read
  the season-end cash, so `ClubSeasonEntry.ClosingCashMinor` is unchanged and the award is the next season's
  first money. Idempotent by the phase checkpoint and the ledger's correlation key (`FIN-17`).
- **`finance.club_season_finances`** and **`finance.club_season_finance_lines`** (`FIN-19`, master plan §6.8):
  one summary per club per season — opening cash, closing cash, and one signed total per ledger category —
  written once at rollover over the season's own window and never edited. Record only: no read surface yet.
- **`PYR-9`**: `CapacityEvaluator` and the diagnostics trigger now target the next season when the current one
  is rolling over, and defer if it does not exist yet, so the closing season is never mutated.
- **ADR-0032**, on the `squads` phase and its ordering, the expiry and AI-renewal rules, the retirement rule
  and its gating, the award's placement, the season-summary window, and the `PYR-9` target. `game-rules.md`
  gained `CON-9`, `CON-10`, and `FIN-19` plus their constants; `data-model.md` gained the two finance tables
  and the `players.retirement_announced_season_number` column; modules and traceability were updated. The rule
  set advanced to `world-rules-v9`.

### Tests

- New domain tests: the retirement rule (start age, forced caps, the ability and fitness gate, determinism, and
  that a weak squad announces more than a strong one), the AI contract policy (best-first, the legal floor,
  the surplus, terms by age, order independence), the season finance aggregate, and `Player.AnnounceRetirement`.
- New application tests: the finance settlement (award per club, summary as opening plus categories, the empty
  case) and contract continuity over fakes (retirement, release versus renew, goalkeepers-first repair, and an
  announcing player kept for a final season). The inbox retirement template is pinned by renderer tests.
- The real-PostgreSQL rollover suite now also asserts 126 position awards, 126 season summaries, and that
  unmanaged clubs' expiring contracts were renewed; the domain rollover suite covers the new phase's
  transitions and order.

### Notes

- **Settlement awards sit outside the closing season's summary.** The summary's window is the season's own
  (`starts_at` to `rollover_ends_at`); the award is posted after it, so `closing_cash_minor` in both the entry
  and the summary is the season-end figure and the two agree (ADR-0032).
- **A present manager's lapse is theirs.** Expiry follows `CON-6` literally, and free-agent signing is still
  post-MVP, so an unrenewed player leaves the world's squads; the emergency path restores a club only to the
  minimum. That is the rule, not an oversight.
- **The retirement announcement is the only thing a manager sees.** The chance, its growth, and the gate live
  in `WorldRuleSet` and are never serialized; only "this is my last season" reaches the inbox.
- **The award correlation key uses the compact GUID form.** The readable `rollover:{D}:{D}` form is 82
  characters and the ledger caps a correlation key at 80; the quiet factory had never met the limit before it
  had a caller.

## Stage 12 — Season rollover, promotion/relegation, and continuity

A season that ends and a next one that begins. The closing season is frozen, its standings are finalized, its
entries are closed with their final rank, three clubs go up and three come down between every adjacent pair of
active tiers, and the next season is opened with fresh entries, a fresh 34-fixture schedule, and an opening
table — all of it a resumable state machine under a world-scoped lock, so an interrupted rollover resumes at
the checkpoint it reached and a redelivered one moves nothing twice. This is the first milestone of the stage;
the continuity work — contract expiry, retirement, position awards, and the season finance summary — is the
rest.

### Added

- **`competition.season_rollovers`** (master plan §6.4, ADR-0031): the checkpoint row of one season's
  rollover. `unique (world_id, season_id)` makes the rollover a singleton for a closing season however many
  times the materialiser runs, and the `phase` column (`started|frozen|finalized|moved|completed|failed`) is
  the resume point master plan §7.5 asks for.
- **`SeasonRollover` and `SeasonRolloverPhase`**: the state machine as an aggregate with guarded, idempotent
  transitions. A repeat of a step that already happened is a no-op — the queue is at-least-once — and an
  out-of-order step is refused. `Retry` returns a failed rollover to the top, which is safe because every
  phase is a find-or-create.
- **`PromotionRelegation`** (`promotion-relegation-v1`, `PR-1`–`PR-10`): a pure function from each active
  tier's finalized ordering to one movement per club. Three up and three down between every adjacent pair of
  active tiers, so the top tier promotes nobody and the lowest relegates nobody as consequences of there being
  no adjacent tier rather than as special cases. It reads no clock or database, so a finished season's
  movement is reproducible from its stored standings.
- **`RunSeasonRollover`** (master plan §7.5): the orchestrator. Each phase commits its own work together with
  the phase it reached, under one world-scoped advisory lock: preflight refuses a season that is not fully
  played (a *transient* failure the queue retries, without freezing) and a season whose projections do not
  reconcile (a *defect* it marks failed and dead-letters for an operator); freeze runs `Season.BeginRollover`;
  finalize completes every `DivisionSeason`, closes every `ClubSeasonEntry` with its final rank, movement
  flags, closing reputation, and closing cash (`PR-4`); move creates the next season and places every club
  into it with a schedule (`PR-5`); complete advances the world's season pointer, seals the closing season,
  and activates the next (`PR-6`, `TIME-3`).
- **`GameWorld.AdvanceToNextSeason`**: the one place the game season pointer moves, and `Season.Create`
  for the next season at the first configured matchday after the rollover window (`CAL-6`).
- **`DivisionScheduleGenerator`**: the fixture-list and opening-table generation extracted from
  `WorldGenerator` so the seeder, the provisioning worker, and the rollover cannot drift — the rationale
  ADR-0030 used for `BuildTier`. `SeedsFor` derives a season-scoped schedule and tie-draw seed from the
  world's identity, the country code, the season number, and the tier, so each season's calendar is different
  and still reproducible (`CAL-8`, `TBL-11`).
- **`competition.season-rollover`**, the durable job, and **`SeasonRolloverScheduler`**, which materialises it
  once the current season's `ends_at` has passed. Registered only by `AddJobQueueWorker`, gated by
  `Rollover:EnableRollover`, so an API process can never close a season (ADR-0001, ADR-0008).
- **`ISeasonRolloverRepository`** and its persistence, plus the reads the rollover needs on
  `IWorldRepository` (`ListActiveDivisionsAsync`, `ListDivisionSeasonsAsync`, `ListClubSeasonEntriesAsync`),
  `IClubRepository` (a batch load for closing reputations), and `IClubTenureRepository` (the open clubs, so a
  next-season entry records who controlled the club at its start).
- **ADR-0031**, on the one world-scoped job with a checkpoint row, the world lock, country-scoped movement,
  the shared generation path, and the transient-versus-defect failure classification.
- `docs/product/game-rules.md` §7 gained the movement rule's version (`promotion-relegation-v1`) and §18 the
  constant; `docs/architecture/data-model.md` carries `competition.season_rollovers` and the note that
  `club_season_entries` is now closed at rollover; modules and traceability were updated.

### Tests

- 22 new domain tests: the movement rules across one, two, and three active tiers (three-up/three-down,
  lowest tier relegates nobody, top tier promotes nobody, order-independence, the refusals), the rollover
  state machine's phase guards and idempotent repeats, and the world's season pointer.
- A new real-PostgreSQL rollover suite over a seeded world with a provisioned second tier: preflight refuses
  an unplayed season and leaves the season unfrozen; a finished season rolls over, moving exactly the three
  promoted and three relegated clubs, opening a next season with a full schedule in every tier, and leaving
  the closing season's results immutable; a redelivered rollover is a no-op that moves nothing twice.
- A new worker integration test: the scheduler materialises the rollover once the season's deadline passes,
  the queue claims it, and the handler alone closes the season and opens the next.

### Notes

- **The plan's three job names are one job and a checkpoint row.** Master plan §7.2 names
  `PrepareSeasonRollover`, `ExecuteCountryRollover`, and `FinalizeSeasonRollover`, but a job that
  dead-letters cannot be resumed, so the resumable unit is the checkpoint row and the three names are its
  phase groups (§7.5's requirement is the checkpoint, not the job count). ADR-0031 records the decision.
- **A season that is not finished is retried; a season that does not reconcile is stopped.** Unpublished
  matchdays are a race with a late publication, so the job fails transiently and the row is untouched; drifted
  projections mean deterministic generation or publication did not reproduce, so the rollover is marked
  failed, audited, and dead-lettered rather than hot-looped.
- **Movement is by club, and the closing season is never rewritten.** A manager travels with their club
  (`PR-3`); the next season is new entries, new division-seasons, and a new schedule, so the finished season's
  membership, results, and table stay the record of what happened (`PR-6`).
- **Deferred to the rest of Stage 12:** contract expiry into free agency (`CON-6`) with a deterministic
  rollover renewal for AI/unmanaged clubs and audited emergency replacements as the residual safety net
  (`SQ-8`); the announce-then-play **retirement** mechanism (a player has a 30% chance to announce at 32, rising
  15 percentage points a season, forced by 37 for outfielders and by 39 for goalkeepers, gated by ability and
  fitness so only very good, very fit players reach the cap and the rest retire around 32–35 and 33–36); the
  position awards (`FIN-5`, the `LedgerPostings.PositionAward` factory already exists with no caller); the
  `finance.club_season_finances` summary; and promotion/relegation news. Also `PYR-9`: a provisioning request
  made while the rollover holds the country lock should target the **next** season, and `CapacityEvaluator`
  still resolves the current one — the guard Stage 11 left in place for this stage to change.
- **Deferred beyond it:** the operator dry-run/resume preview and the "five consecutive automated staging
  seasons" run (Stage 12's later milestones), and the season-history and next-season screens. Resumability is
  proven here by the state machine's phase guards, the transient-then-success path (an unplayed season refuses
  and is then rolled over once it is played), and a redelivered rollover moving nothing twice; the exhaustive
  kill-after-every-checkpoint harness belongs with the staging-season run, where a compressed clock drives
  several seasons end to end.

## Stage 11 — Automatic pyramid growth, AI vacancies, inbox, and inactivity

A pyramid that grows by itself and a club that comes back when you stop playing. A full tier now provisions
the next one through the worker — generated, backfilled with deterministic AI-vs-AI bootstrap results,
validated, and activated before it is claimable — and a tenure that goes quiet warns, hands its routine
decisions to the AI, and finally closes and returns the club, all against real UTC time. The comms module
completes: a division news feed, per-manager notification preferences, deadline reminders, and the outbox
that carries notification email off the request path. Part A hardens Stage 10 first, as the plan requires.

### Provisioning and backfill

- **The generic provisioning path** (`PYR-4`–`PYR-8`, ADR-0030). `ProvisionDivision` runs from
  `world.provision-division`, materialised by the worker-only `ProvisioningScheduler`. It generates the tier
  with the **same code the seeder uses** (`WorldGenerator.BuildTier`, extracted so the two cannot drift,
  `PYR-14`), backfills the matchdays already passed this season by running the ordinary lock → resolve →
  publish workflow once per round **in round order**, validates, and only then activates. A validation
  failure dead-letters for an operator rather than hot-looping a deterministic defect.
- **Bootstrap provenance** (`PYR-7`). A backfilled fixture carries `is_bootstrap`, surfaced on the fixture
  DTOs, so a generated result is never mistaken for one a manager played.
- **A tier is claimable only after validation** (`PYR-8`): 18 clubs, 18 legal squads (≥2 goalkeepers), 34
  matchdays / 306 fixtures passing `ScheduleValidator`, 18 standings, and a projection rebuild that
  reconciles (`TBL-13`).
- **Growth continues generically** (`PYR-11`): activation re-runs `CapacityEvaluator` under the country lock,
  so a full tier-2 requests tier 3 by the same path.

### Inactivity

- **The inactivity ladder** (`OCC-1`–`OCC-3`, `OCC-8`, ADR-0027). `EvaluateInactivity` runs once a day from
  the worker and advances every open tenure against real UTC time: warn at 10 days, hand routine decisions to
  the AI at 14, close and free the club at 21. A suspended account is skipped — it keeps its club but is not
  an absence.
- **A login is the return** (`OCC-2`, `OCC-5`): `Login` resumes an inactive tenure (and clears the pending
  warning) in the same unit of work as the session.
- **The AI fills the tenure's gaps** (`OCC-2`, `INS-12`): the AI club evaluation now includes clubs whose open
  tenure is inactive, and it only fills gaps, so a present manager's plan is never overwritten. The market is
  deliberately left to the human, because it commits money.
- **A freed club grows the pyramid** (`PYR-1`): a closure re-runs `CapacityEvaluator` for its country.

### Inbox, news, and notifications

- **The division news feed** (`COM-1`, ADR-0029): `comms.news_items`, `PostNews`/`GetNews` over the same
  factory-and-renderer contract as the inbox, and `GET /news`. Posted inside the transaction that produces the
  event — tier activation, transfer completion, and round publication.
- **Notification preferences** (`COM-8`, ADR-0029): `comms.notification_preferences`, `GET`/`PUT
  /settings/notifications` with `ETag`/`If-Match` (`CONC-1`). Every switch defaults on and absence means the
  defaults.
- **Deadline reminders** (`COM-7`): `SendDeadlineReminders` writes one reminder per human club when a round's
  team sheet locks within `reminder_lead_hours` (24h), materialised by the worker.
- **The outbox** (`COM-9`, `MOD-4`, ADR-0028): `OutboxWriter` stages an intention in the caller's
  transaction; `DispatchOutbox` drains it and sends notification email at-least-once, rescheduling transport
  faults and dead-lettering defects. Worker-only, minute-bucketed.
- **New inbox templates**: `inbox.onboarding.welcome`, `inbox.occupancy.inactivity_warning`,
  `inbox.occupancy.inactivity_closed`, and `inbox.deadline.team_sheet`.

### Added

- **Four ADRs**: ADR-0027 (the inactivity ladder), ADR-0028 (the outbox), ADR-0029 (news and preferences),
  and ADR-0030 (provisioning execution and bootstrap provenance), all indexed.
- **Non-production diagnostics triggers** for provisioning and inactivity, gated by `Diagnostics` flags and
  never mapped in production (§17.12), so the stage's journeys can drive the real worker.
- **The `Provisioning`, `Inactivity`, `Reminders`, and `Outbox` configuration sections**, with their
  schedulers registered only by `AddJobQueueWorker` — the API must never provision, age a tenure, or send
  mail (ADR-0001, ADR-0008).
- `docs/product/game-rules.md` §16 gained `COM-6`–`COM-9` and §18 the `provisioning_generator_version`,
  `reminder_lead_hours`, notification-preference defaults, and outbox bucket; `docs/architecture/data-model.md`
  carries `comms.notification_preferences`, the two new comms indexes, and the `is_bootstrap` /
  `inactivity_warning_at` columns; modules, traceability, and the README were updated.

### Notes

- **Suspension stays automatic-only, by decision.** No new admin endpoints: the §10.8 admin surface, the
  suspend/restore/assign-AI commands, and `INT-4` collusion signals remain Stage 14. A suspended account keeps
  its club and is skipped by the ladder.
- **Auth transactional email stays inline.** Moving verification and reset mail onto the outbox would widen
  this change into the account lifecycle; the deferral is recorded in ADR-0028 rather than half-done.
- **`PYR-9` next-season targeting is Stage 12.** `CapacityEvaluator` still targets the current season, guarded
  by a comment where the rollover will change it, and the provisioning job does not mutate a closing season.
- **A mid-season provision simulates up to ~34 AI-vs-AI rounds in one job** under the country lock. It is
  bounded by the season length and observable through the job log; the work is deliberately in-job because a
  terminal job cannot be resumed per round.

## Stage 10 — Scouting and timed auctions

The transfer market's core loop: a manager searches every player in the world, keeps private shortlists, lists a
player with a minimum fee, and bids against rivals on a fixed daily window. A bid reserves its funds through the
existing ledger the moment it leads, and the resolution settles the winner — payment, credit, and the player's
contract and registration — in one serializable transaction. The AI transfer market (`TRF-12`) is the next
milestone of the stage.

### Added

- **The `market` module** (master plan §6.7; ADR-0024): `market.shortlists`, `market.transfer_listings`,
  `market.transfer_bids`, and `market.transfer_outcomes`, with the partial unique indexes that make the rules
  structural — one open listing per player (`TRF-14`), one leading bid per listing per club (`TRF-6`), and one
  outcome per listing ("resolution happens once", `TRF-9`). `bid_sequence` is a database-assigned identity, so a
  tie between equal amounts resolves to the earliest committed bid rather than to arrival time (`TRF-8`, `T-5`).
- **Scouting** (`SCT-1`): a server-side, keyset-paged search over every club's players with exact public
  attributes, position and age filters, and a bounded page (`D-2`). Nothing hidden — no potential, reputation,
  or internal valuation — reaches a client (`I-1`).
- **Private shortlists** (`SCT-3`): a manager's own watch list, with a length-bounded note, keyed by the manager
  profile rather than the club so it survives a change of club.
- **Transfer listings** (`TRF-1`, `TRF-2`, `TRF-14`): a seller lists an eligible player with a minimum fee and
  the buyer's precomputed wage and contract length (`CON-5`). The listing resolves at the next daily window at
  least 48 hours away, and never within the six-hour pre-kickoff blackout (`TRF-3`).
- **Bids** (`TRF-4`…`TRF-7`): ascending bids with a configured minimum increment, one active bid per club per
  listing that may be raised. The leading bid reserves its funds through `LedgerPostings.BidReservation`, and
  the displaced leader's reservation is released in the same transaction (`FIN-10`).
- **Auction resolution** (`TRF-8`…`TRF-11`): the durable job `market.resolve-auction`, materialised per listing
  with the business key `listing:{id}:resolve`. It runs `SERIALIZABLE`, revalidates the winning bid against both
  clubs' accounts and squads and the player's registration, and settles the transfer atomically — the buyer pays
  (`TransferPayment`), the seller is credited (`TransferProceeds`), the seller's contract closes as `transferred`,
  its registration ends, and the buyer's contract and registration are created. A retried job is a no-op.
- **Market reads and history** (`INT-6`): a club's own listings and bids, and the public history of completed
  transfers, behind the same keyset cursor the rest of the product uses.
- **Market notifications** (`F-41`): an outbid bidder and both sides of a completed transfer are told through the
  inbox, using the durable template-and-parameters contract the other messages use (`MAT-8`).
- **The scouting and transfers screens**, and a `world-rules-v7` rule set carrying the auction window, the
  blackout, the minimum bid increment, and the shortlist note bound (`RULE-1`, `RULE-3`).

### Changed

- **The ledger gains four categories' worth of use**: `finance.bid_reservation`, `finance.reservation_release`,
  `finance.transfer_payment`, and `finance.transfer_proceeds` are now written, resolved through
  `LedgerEntryText` like every other line.
- **`ISquadRepository` grows two read methods** — the active contract and registration of a player — so a
  transfer can close the old pair and stage the new one in a single unit of work (`MOD-2`).

### Notes

- The AI transfer market (`TRF-12`), collusion signals (`INT-4`), and admin trace views are the next Stage 10
  milestone; free-agent signing (`CON-7`) waits on rollover producing free agents (`CON-6`, Stage 12).
- The market's two infrastructure suites run against their own seeded world, because a resolution moves a player
  between clubs and would otherwise break the world-seeding assertions that check every club's squad.

### The AI transfer market

A market that trades without you. Every club no human holds now lists its surplus and bids for the players
that improve it, through the same listing and bid writers a manager's command uses — within the same ledger
and the same squad rules, so `INS-12`'s "the AI receives no bypass" is a property of the code path rather
than an intention. `EvaluateAiMarket` runs on the worker's daily schedule, records every decision in
`market.ai_market_decisions`, and is idempotent per day, so the queue's at-least-once delivery cannot list a
second player or reserve a second time. This closes Stage 10's last exit criterion, *"AI uses no privileged
finance and produces a healthy measured market"*, and the missing journey with it.

#### Added

- **The `market` AI tables and the shared cores.** `market.ai_market_decisions` (master plan §6.7) records
  one append-only row per decision: the club, the instant, the action, the player, the listing or bid it
  produced, the digest of the inputs the policy read, and the policy version. Its checks make the row
  self-describing — an action is a known code and names exactly the listing or the bid, never both — and
  `length(inputs_hash) = 64` keeps the digest a digest.
- **`PlayerValuation`** (`player-valuation-v1`, `TRF-12`): a pure, versioned value derived from the wage
  scale the generator and the renewal quote already use, moved by bounded age and potential factors. A
  squad, a renewal, and a fee are priced by one family of rules rather than by a second, disconnected money
  scale, and the value is class C2 — an input to the AI and the basis of a listing's asking price, never a
  field in a response (`MAT-11`).
- **`AiMarketPolicy`** (`ai-market-v1`): a pure, deterministic function from a club's shape and the open
  market to its listings and bids. Supply is a squad above `AiMarketTargetSquadSize` or a family above its
  generator quota, ranked by weakness; demand is a positional need **or** a listed player better than the
  club's weakest in that family; neither ever takes the club below `SQ-2`, above `SQ-3`, above a player's
  valuation, or above a bounded share of its spendable cash (`FIN-10`), and both are bounded per pass. The
  upgrade clause is what gives the market a first buyer: a need alone would require a sale to have happened,
  and nothing sells until a transfer settles.
- **`ListingWriter`/`BidWriter`, and their `IListingWriter`/`IBidWriter` ports.** The listing and bid cores
  a manager's command ran are extracted into writers that stage the row, its ledger postings, its
  notification, and its audit row and never save. `CreateListing`/`PlaceBid` now resolve the caller's club
  and call them as a user actor; `EvaluateAiMarket` calls them as a service actor, so an AI listing or bid
  is refused by the same eligibility, legality, and affordability rules a human's is.
- **`EvaluateAiMarket`** (master plan §7.2, `TRF-12`): the worker-only evaluation. It reads every club no
  human holds with its squad and spendable cash and the listings open before the day began, decides in club
  identity order, carries each decision out through the shared writers, and commits the whole pass in one
  transaction. Each decision's deterministic daily key (`ai-l:{club}:{player}:{yyyyMMdd}`,
  `ai-b:{club}:{listing}:{yyyyMMdd}`) makes a retried pass a replay rather than a second listing or bid.
- **`market.evaluate-ai`**, the durable job, and **`AiMarketScheduler`**, which materialises one row per UTC
  day so the row is the deadline and a worker that was down when the day opened runs the evaluation late
  rather than skipping it (ADR-0003). Registered only by `AddJobQueueWorker`, so an API process can never
  list or bid; `AiMarket:EnableEvaluation` is on by default, because a world where the AI neither buys nor
  sells is not the stage.
- **`IAiMarketRepository`**/**`AiMarketRepository`**: the evaluation's read — the AI clubs with their squads,
  placements, and spendable cash, and the market open before the day — in a fixed handful of queries however
  deep the pyramid gets, carrying the hidden potential the valuation reads.
- **The rule set becomes `world-rules-v8`** (`RULE-1`): the valuation's weeks-of-wage and its age and
  potential factors, the AI market's target squad size, its per-pass caps, and its bid budget fraction. A
  world stamped with an earlier version keeps being read against it.
- **The market end-to-end journey** (`tests/web-e2e/playwright.market.config.ts`, `market/market.spec.ts`,
  master plan §15.5 journey 4): three managers on three browser contexts list a player, bid, and outbid
  through the real transfers screen, the harness resolves the auction through the non-production trigger,
  and the journey asserts the seller's leading amount and bidder count, the completed transfer in the public
  history, the winner's bid `won`, and the displaced bidder's `outbid`. Its own throwaway database and the
  worker, with the AI market and the auction scheduler switched off so neither competes with the managers.
- **ADR-0025**, on the pure versioned policy, the valuation derived from the wage scale, the AI routed
  through the human write path, the day-scoped market that makes a retry idempotent, and the decision
  record.
- `docs/product/game-rules.md` §14.3 and §18 gained the AI market's behaviour and its constants; the
  deferred "exact AI valuation and bidding bands" item is now specified.
- 19 new domain tests for the valuation's monotonicity and refusals and the policy's determinism, its
  surplus and need rules, its guards, and its caps; 4 new application tests for the orchestration — the
  service actor, the deterministic daily keys, a refused decision counted rather than thrown, the decision
  record, and the pass that commits nothing; 7 new infrastructure tests over a real seeded world that list
  and bid, replay a retry without duplicating, settle a transfer, keep every club legal and every balance
  replayable, release the displaced reservation when a second club outbids, and assert the decision table's
  checks by name against the schema (`MIG-7`); and 1 new Playwright journey driven across three signed-in
  contexts.

#### Fixed

- **Outbidding released the displaced reservation off the wrong account.** `PlaceBid` posted the displaced
  leader's `ReservationRelease` against the **caller's** account rather than the leader's, so a club that
  outbid another drove its own reserved balance negative and `ClubAccount.Post` refused the whole command.
  The path had never run: every existing market test placed a single bid, and the outbid case was exactly
  the journey this milestone adds. The release now comes off the account that holds the reservation, and
  `MarketOutbidTests` pins both accounts, both bid statuses, and the ledger replay. The AI market makes this
  reachable in ordinary play — two AI clubs bidding on one listing *is* an outbid — so it is a
  competitive-integrity fix, not a journey convenience.

#### Notes

- **The valuation is a multiple of the wage, and that is the point.** A separate fee scale would let a
  player's price, their wage, and their renewal drift apart; deriving the value from the wage the rule set
  already prices keeps one family of rules across all three, and makes the asking price a term a manager can
  read beside the wage they would pay.
- **The AI reaches the market through the human path because the alternative is a second money path.** The
  writers are the same code a manager's command runs; only the actor and the idempotency key differ. A
  parallel AI writer would be a second place `FIN-10` is enforced and a second place `TRF-14` and `SQ-2`
  live — the drift `INS-12` and ADR-0022 exist to prevent.
- **The market needs two passes to start, and that is honest rather than a defect.** A freshly generated
  world has balanced squads at the generator quotas, so no club has a positional need; the first pass lists
  its surplus, and the second buys where a listing improves the squad. The measured market therefore shows
  supply on day one and bidding on day two.
- **A pass bids only on what existed before the day began.** Without that bound a same-day retry would bid
  on the listings the first pass had just created, so the job would not be idempotent. Bounding the market
  to the day makes a pass a function of the day rather than of the instant it happens to run.
- **A club trims one player and waits.** It lists nobody while it already has a listing open, so one pass
  cannot flood the market and a repeated pass lists nothing new. The cost is that a bloated squad clears
  slowly; the benefit is a market that grows rather than a wall of listings.
- **The whole pass commits once.** Every listing, bid, ledger posting, and decision row for a whole
  evaluation is one unit of work, so a transient failure leaves nothing behind and the retry re-derives the
  same decisions.
- **Deferred to Stage 14, with reasons:** the collusion review signals (`INT-4`) and the market's admin
  trace views. Neither is a Stage 10 exit criterion, and both need an operator surface that does not exist
  yet; the decision rows and the ledger correlation keys this milestone writes are what those views will
  read. **Deferred to Stage 12:** free-agent signing (`CON-7`), which waits on rollover producing free
  agents (`CON-6`), as ADR-0024 already records.

### Hardening the market's guarantees

Stage 10's exit criteria ask for concurrent bids and resolutions to have a deterministic winner and exact
money movement, for a locked fixture snapshot to survive a transfer, and for listing and bidding to be
rate-limited per manager (`INT-5`). These follow the AI market milestone so the stage closes on its own
terms rather than on the core loop alone.

#### Added

- **Concurrency tests over real PostgreSQL 17** (`MarketConcurrencyTests`): two resolutions racing one
  listing settle it exactly once — one outcome, the buyer charged once, the seller credited once, the
  reservation consumed once — and two clubs bidding at once leave exactly one leader and exactly one
  reservation, with every affected account's ledger replaying to its balances (`TRF-6`, `TRF-9`, `FIN-10`,
  `FIN-18`). The losing transaction's serialization or uniqueness refusal is tolerated rather than asserted
  away, because the winner is whoever commits and the loser's retry is a no-op.
- **The frozen-snapshot test** (`MarketSnapshotLockTests`): a player transferred after a fixture's snapshot
  locked still appears in that side, the snapshot is byte-identical afterwards, and only the player's live
  contract and registration move (`SQ-7`, `MAT-1`).
- **Per-manager rate limits on the market's commands** (`INT-5`, master plan §7.7):
  `POST /transfers/listings` and its cancellation are throttled by `RateLimitPolicies.MarketListing`, and
  `POST /transfers/listings/{id}/bids` by `RateLimitPolicies.MarketBid`. The limiter now runs after
  authentication, so a market command partitions by the authenticated manager rather than by address; the
  auth policy is unchanged, still partitioning its anonymous callers by address.
- **`RateLimiting:Market*` configuration** and an integration test that pins the refusals, plus the
  `ADR-0025` index row that the previous milestone had left out.
- **ADR-0026**, on serialising the bids on one listing with a transaction-scoped advisory lock.

#### Fixed

- **Two clubs could both hold the lead on one listing.** The bid writer read the current leading bid and
  then wrote without a lock, and the database's only guard was the per-club partial unique index (`TRF-6`),
  which does not stop two *different* clubs from each inserting a leading bid. Under a race both
  reservations stood, the listing could settle only one of them, and the other club's funds stayed reserved
  with no path that released them (`TRF-7`, `FIN-10`). Bids on a listing are now serialised with a
  transaction-scoped advisory lock (`AdvisoryLockKey.Listing`), and `PlaceBid` and the AI pass each run
  their bid writes in one explicit transaction (`ADR-0026`); the second arrival now reads the first's
  committed leading bid and outbids it properly. The concurrency test above is what caught it — it was the
  first test to place two bids on one listing at the same instant.

#### Notes

- **The limiter's position in the pipeline is the design.** Authentication runs before it so the subject
  claim is populated and a market command can be bucketed per manager, which is what `INT-5` asks for; the
  auth endpoints are anonymous at that point and keep their address partition.
- **Deferred still:** the collusion review signals (`INT-4`) and the market's admin trace views remain
  Stage 14, as the previous milestone's notes record.

## Stage 9 — Contracts and basic club finances

The ledger every balance is rebuilt from. A club's money becomes an append-only record: an account no longer
opens with a starting balance written onto its row but with a first entry, and `FIN-18`'s "replay must exactly
reconstruct cash and reserved balances" is now a property the seeded world is checked against rather than a
claim in a document. This is the first milestone of the stage; the income and expense runs, the renewals, the
warnings, the emergency path, and the finance screens are the rest.

### Added

- **`finance.ledger_entries`** (master plan §6.8): one immutable row per balance change, carrying the cash
  and reserved deltas it applied, the balances those produced, its category, its source, and the correlation
  key of the operation it belongs to. `unique (club_id, sequence)` is the ordered walk `FIN-11` needs, and
  `unique (correlation_id, category)` is `FIN-17` made structural — a retried operation collides with its
  first entry rather than posting a second. The checks bound what an entry may claim: a sequence starts at
  one, a category and a source are one of the known codes, the resulting balances are never negative and
  never reserve more than the cash behind them (`FIN-13`), and an entry moves something.
- **`LedgerEntry`, `LedgerPosting`, `LedgerCategory`, and `LedgerSourceType`**: the entry as an immutable
  value carrying the balances it produced; the intent a workflow proposes before an account decides it can
  afford it; and the two stable vocabularies — what a move is (`FIN-3`…`FIN-9`, the reservations `FIN-10`
  makes, the emergency grant `FIN-16`, and the compensating correction `FIN-12`) and which workflow wrote it.
  Both walk by code so a column holds every value and a reordered enum cannot move an entry between accounts.
- **`ClubAccount.Post` is the only path a balance changes** (`FIN-11`, `FIN-12`). It applies a posting's cash
  and reserved deltas, refuses one that would leave either negative or leave reserved funds above the cash
  behind them (`FIN-10`, `FIN-13`), advances the account's sequence, and records the entry — so a wage, a gate
  receipt, an award, a reservation, and a release all pass through one guard rather than restating it.
- **The opening balance is a posting.** `ClubAccount.Open` now opens at zero and the seeder funds the club
  with a `LedgerCategory.OpeningBalance` entry (`FIN-1`), so there is no balance in the database a replay
  cannot reproduce. The account's balances are the running total of its ledger, and `club_accounts` is the
  projection the entries sum to (`FIN-18`).
- **`LedgerPostings`** builds the postings the game makes, starting with the opening balance; the description
  is a stable template key and a stored parameter document, the contract the commentary and the inbox already
  use (`MAT-8`, master plan §8.6), so a ledger line can be rendered in another language later without being
  rewritten.
- **`ILedgerRepository`** and its implementation: the append-only write, staging an entry so the workflow that
  moves money commits it beside the state it changes.
- **ADR-0022**, on the append-only ledger, the balances as its projection, the opening balance as the first
  entry, the two deltas on one posting, and idempotency as a unique index rather than a lock.
- `docs/product/game-rules.md` §13 now describes the ledger the rules are applied to; the data model's
  constraint row for `ledger_entries` names the indexes and checks as built.
- 18 new domain tests (389 total) for the postings — opening, a debit, a reservation and its release, and the
  refusals (negative cash, a reservation beyond the available cash, a debit that would spend reserved money,
  a release below zero, a posting that moves nothing) — and the entry's own guards and code vocabularies;
  3 new application tests (101 total) for the opening posting's fields, its parameter document, and a negative
  opening amount; and 5 new infrastructure tests (143 total) over a real seeded world that assert every
  account is opened by exactly one entry stating its balance, that the ledger replays to the stored balances,
  and that the sequence, idempotency, and balance constraints are enforced by name. The world-seeding test now
  also asserts an account carries one ledger entry.

### Notes

- **The account is a projection, and the opening balance had to move to the ledger for that to be true.**
  `club_accounts.cash_minor` and `reserved_minor` are the running total of the entries behind them
  (`FIN-18`), so a starting value written onto the row at generation would be the one fact a replay could not
  reproduce. Opening at zero and posting the balance is what makes the two agree from the first club onward,
  and it is ADR-0022's central decision.
- **An entry stores the balances it produced, not only the deltas.** The deltas are what moved; the resulting
  balances are what the club held, and storing them lets a replay be checked against the state the account
  actually reached rather than only against today's sum — and lets the database refuse an entry that claims a
  negative balance at insert.
- **A reservation is a reserved delta on an ordinary entry.** Cash and reserved funds move independently, so
  one posting carries both deltas and the account guards both: `FIN-10`'s affordability and `FIN-13`'s
  non-negativity are the same check in the same place, and a bid in Stage 10 will express itself as a
  `BidReservation` against it rather than reaching for a second balance path.
- **Idempotency is an index, not a lock.** `unique (correlation_id, category)` refuses a duplicate write
  whether a retry is sequential or concurrent, which is the guarantee `FIN-17` needs and the reason the
  correlation key is required rather than optional on every posting.
- **Deliberately deferred to the rest of Stage 9:** the income and expense postings themselves — gate revenue
  (`FIN-3`), the weekly sponsorship credit, wages, and the operating cost (`FIN-4`, `FIN-7`, `FIN-9`) as a
  durable run, and the awards (`FIN-5`) at rollover; `finance.club_season_finances` and the finance reporting
  read with its `/finances` screen; the deterministic renewal quote and accept/decline (`CON-3`, `CON-4`) with
  the rollover expiry; the dashboard's expiring-contract, payroll, and minimum-squad warnings; the emergency
  grant and replacement path with its operations alert (`FIN-16`); and finance administration with
  compensating entries only (`FIN-12`). The `ledger_entries` description columns are written now and rendered
  by the reporting screen when it lands, because the table arrives with the stage's first milestone.

## Stage 8 — Competition depth, discipline, injuries, and AI match management

A result that costs you players. A publication no longer only moves the table: the cards and injuries its
events describe become a discipline record for each player and an absence for each injury, and every club
that played serves one fixture against the absences it already had. The suspension a sending-off earns is
therefore a player the next round's frozen side cannot name — which is `DIS-5` and `DIS-1` as behaviour
rather than as a table. This is the first milestone of the stage; the season statistics, the AI's own
lineup policy, the inbox that reports all of it, and the discipline screens are the rest.

### Added

- **The discipline and injury rules** (game rules §11, `DIS-1`, `DIS-2`, `DIS-4`): five league yellows earn
  a one-fixture suspension, a sending-off earns one, and an injury's fixture absence maps to the band that
  describes it — minor at one or two, moderate at three or four, major at five or six. The rule set is now
  `world-rules-v5`, as a stage's constants arrive with that stage (`RULE-1`); a world stamped with an
  earlier version keeps being read against it.
- **`competition.discipline_records`** (master plan §6.4): one row per player per division-season, advanced
  by publication. Its `unique (division_season_id, player_id)` makes the accumulation a lookup rather than a
  search, and its `check` keeps the counts from going negative. It is the season's accumulation only —
  whether a booking is the fifth is a question asked of a running total (`YellowSuspensionsEarned`), and the
  suspension itself is not stored twice.
- **`DisciplineRecord`**: the aggregate with the accumulation as behaviour. It counts bookings and
  sendings-off, and it answers "does this match's card cross the threshold" by comparing the total divided
  by the threshold before and after — which is how the fifth booking and the tenth each earn a ban without
  the count ever being reset away from the rollover `DIS-3` names.
- **`MatchEffectsCalculator`**: a pure function from a matchday's card and injury events to one effect per
  player. It mirrors the engine's own reconciliation rule (`MAT-5`) — a second yellow is both the booking it
  was and the sending-off it became — so the discipline a manager reads agrees with the match statistics,
  and it decides no threshold of its own, because "how many yellows is a ban" belongs to the rules.
- **Publication is where a result reaches the squad** (`DIS-1`, `DIS-4`, `DIS-5`). Each club that played
  serves one fixture against every open absence it had, and then the round's own cards and injuries are
  applied. The order is the rule: an absence is never served by the match that caused it, so a player sent
  off in round ten misses round eleven, and a three-fixture injury keeps them out of three.
- **`IAvailabilityRepository`**, the squad module's staging port for absences: it loads the open records of
  the clubs that played, tracked, so the publication serves them through
  `PlayerUnavailability.ServeFixture` rather than by editing a count. The matchday repository gained the
  effects read, the discipline lookup, and the discipline stage.
- **The absence already reached the frozen side, and this milestone is what fills it.** Stage 6's snapshot
  builder already excluded an unavailable player and recorded the repair (`DIS-6`, `DIS-7`); nothing
  produced an absence until now. A suspended player is therefore repaired out of the next side by the same
  code that repairs an empty slot, with no new selection rule.
- 11 new domain tests (340 total) for the accumulation, the threshold arithmetic including the tenth
  booking, the record's refusals, and every injury band; 6 new application tests (58 total) for the effect
  calculator; and 2 new infrastructure tests over a real seeded world (118 total) that play a round with an
  injected sending-off and injury, assert the discipline record and the absence, freeze the next round and
  assert neither player is named in it, and assert the absence is served exactly once.

### Notes

- **The discipline record carries no `pending_suspension_fixtures` column, deliberately.** Master plan §6.4
  lists one, but an outstanding suspension is already a `PlayerUnavailability` of type `suspension` — the
  record the snapshot builder reads and the publication serves. A second "how many matches are left" on the
  discipline row would be two answers to one question and the first thing to drift.
- **A second yellow books the player and sends them off.** The engine emits one `second_yellow_card` event
  rather than a card and a red, and both the match statistics and this calculator read it as both. That is
  the reading the standings' tie-break columns already record for the other direction (a second yellow
  counts there as a red and not as a second yellow), and a test in the engine's own suite pins the two
  vocabularies together.
- **A sending-off and a yellow accumulation in the same match are summed into one absence.** They are two
  rules that both fired, not two things for a player to serve twice over the same span, and one record with
  a longer count is what `ServeFixture` was built to advance.
- **The effects are applied by publication and not by simulation.** A result is private until its round
  publishes (`MAT-7`), and an absence is the same: one applied at staging would be visible to the next lock
  before its cause was public. Applying them in the publication's own serializable transaction means the
  result, the table, and the absence become public together or not at all.
- **An absence is served once per published fixture, which is once per matchday per club.** A club plays
  exactly one league fixture a round (`CAL-9`), so a three-fixture injury is three rounds, and the
  mismatching fixture is not a case the calendar can produce.
- **The injury's severity is derived at application, not drawn by the engine.** The engine draws an absence
  of one to six fixtures and records it on the injury event; which band that is describes the player to a
  manager and is game-rules vocabulary, so `WorldRuleSet.InjurySeverityFor` owns the mapping and the
  engine is left knowing only fixtures.
- **Deferred to the rest of Stage 8:** player and club season statistics and the stats/tie-break views;
  condition, fatigue, and morale deltas from a match (`TRN-11`, `TRN-13`); the deterministic AI lineup,
  tactics, substitutions, training, and renewal policy (`INS-12`); the `comms` inbox and news module that
  `DIS-7` reports repairs through; the projection rebuild and reconciliation tools; and the discipline and
  competition screens. **Deferred beyond it:** the discipline accumulation's reset at rollover (`DIS-3`) and
  an unserved suspension carrying into the next season (`DIS-8`), both of which belong to Stage 12.

### The load a match leaves on the squad

A result that costs you energy. Publishing a round no longer only moves the table and books its players:
every player who appeared carries the match's load, so condition is consumed, fatigue accumulates, and morale
moves with the result and with how much they contributed. The rule is measured against the daily recovery
job, so the two together are what decide whether a squad can sustain a Tuesday-Thursday-Sunday season. This
is the second milestone of the stage; the season statistics, the AI's own lineup policy, the inbox, and the
discipline screens are the rest.

#### Added

- **`MatchLoadCalculator`** (`match-load-v1`, `TRN-11`, `TRN-13`): a pure function from the frozen facts of a
  match — each participant's minutes, their stamina, their side's instructions, and the scoreline — to one
  signed delta per player. It reads no clock, database, culture, or random source and produces no draw at
  all, because the load is arithmetic rather than chance. Condition is consumed in proportion to minutes and
  costs more for a less fit player and a side that plays harder; fatigue accumulates on the same inputs at
  its own scale, because fatigue is the multi-match load while condition is the short-term freshness. Every
  coefficient is versioned with the calculator, exactly as the training constants are with `training-v1`, so
  a balancing change is a named rule change.
- **Morale is weighted by playing time** (`TRN-13`): a win, a draw, or a defeat moves a player by a bounded
  amount scaled by the minutes they were on the pitch. A full match carries the result whole, a late cameo
  carries it in proportion, and a player who never left the bench carries none of it — the rule names
  playing time as an input, so the absence of it is not an input. Contracts and transfers, the other inputs
  the rule names, belong to the stages that own them.
- **`PlayerState.ApplyMatchLoad`**: the first *additive* mutation of a stored player state. It adds the
  computed deltas to the value the player actually holds and clamps every result to the stored scale
  (`TRN-5`…`TRN-7`), rather than writing the snapshot's own arithmetic as an absolute value. That is what
  stops a match from erasing whatever training happened between the lock and the publication — the daily
  progression job writes the same row.
- **The stored result document carries the player lines** (`match-statistics-v2`). It already held the two
  sides' statistics; it now holds the engine's per-participant lines beside them, which is where publication
  reads the minutes each player played. It is the engine's own output, written once when a fixture is staged,
  so this is one copy rather than a second source of truth — but a delayed publication reads the facts the
  result was made from rather than re-simulating under whatever engine build has since shipped (ADR-0017).
  A version-1 document is refused rather than read, because a reader that accepted it would apply no load to
  anybody.
- **Publication applies the load** (`TRN-11`, `TRN-13`). `PublishMatchday` loads the round's match loads and
  the tracked state of the players who appeared, and applies the deltas in the same transaction that
  publishes the nine fixtures, rebuilds the table, and applies the cards and injuries. A result is private
  until its round publishes (`MAT-7`), and a load is the same: one applied at staging would be visible to
  the next lock before its cause was public. A round that is not fully staged loads nobody, and a
  republished round loads nobody twice.
- **`IMatchdayRepository.LoadMatchLoadsAsync`** carries the frozen snapshot and the stored result of each
  published fixture as their versioned documents, the same shapes the match read already carries; and
  **`IPlayerStateRepository`** is the squad module's staging port for a player's state, beside the
  availability port the discipline milestone added.
- **ADR-0017**, on deriving the load from the stored result rather than from a re-simulation or an engine
  output change, and on applying it at publication.
- 2 new domain tests (342 total) for the additive match-load mutation and its clamping; 10 new application
  tests (68 total) for the calculator — the exact cost of a neutral full match, the fitness and intensity
  factors, the morale of a win, a draw, a defeat, and a cameo, the bench, and the ordering; and 2 new
  infrastructure tests (120 total) over a real seeded world that publish a round and assert the players who
  appeared were loaded and the ones who did not were not, and that publishing the same round twice loads
  nobody twice.

#### Notes

- **The bench is not loaded, and that is the rule rather than an omission.** `TRN-13` makes playing time an
  input to morale, and `TRN-11` makes minutes the basis of condition and fatigue; a player with none of
  either is not something a match has evidence about. A future "unhappy at not playing" rule would be a
  different rule with a different input.
- **The player lines are stored, not re-derived, and that is the decision ADR-0017 records.** The
  presentation read re-derives its commentary and highlights from the frozen snapshot, and publication could
  have done the same for the minutes — but a round published after an engine release would then no longer be
  able to reproduce an older result, which is the opposite of what a stored, hashed result is for. Storing
  the lines keeps publication's cost independent of the engine and the result complete.
- **The coefficients are balancing values and the two scales are deliberately different.** A neutral
  maximum-stamina ninety minutes costs about 720 basis points of condition and adds about 504 of fatigue,
  measured against a daily recovery of a few hundred of each; the tests pin the arithmetic and the directions
  rather than claiming any particular week is optimal. Calibrating a whole season is the multi-season
  simulation work the plan assigns to Stage 9.
- **Aggression now has a cost that is not a card.** The intensity mapping raises a side's load for an
  attacking mentality, a high tempo, a high press, and a high line, and lowers it for the conservative
  values — and running the clock down is the one instruction that buys freshness. That is `INS-9`'s
  "no tactic multiplies strength without a counter-cost" expressed in the players' legs.
- **A version-1 result document is no longer readable.** No production world exists, so the cost is only
  that a database seeded before this change must be reseeded for a played match to be readable; the schema
  was bumped rather than read leniently because a document with no participants would silently load nobody.
- **The load is applied to the live state, not to the snapshot's.** The snapshot froze each player's
  condition and fatigue at the lock, and reading the base from there would have been simpler — but the
  daily progression job may have run between the lock and the publication, and a match that overwrote that
  would erase a trained day. The delta is the match's contribution; the base is the player's own.
- **Deferred to the rest of Stage 8:** player and club season statistics and the stats/tie-break views; the
  deterministic AI lineup, tactics, substitutions, training, and renewal policy (`INS-12`); the `comms`
  inbox and news module; the projection rebuild and reconciliation tools; and the discipline and competition
  screens. The morale effects of contracts and transfers wait for the stages that own them (Stage 9 and
  Stage 10).

### The inbox that reports the round

A result you are told about. The `comms` module arrives: publishing a round now writes each club's manager a
message about the result, its new league position, a booking's suspension, and an injury, and locking a round
reports the side the game had to repair — `DIS-7` at last has somewhere to send its report. `GET /inbox`
reads them by cursor with the unread count, `POST /inbox/{id}/read` and `/inbox/read-all` mark them, `GET
/sync` is the one lightweight poll the shell runs for its badge, and `/inbox` is the screen. This is the
third milestone of the stage; the season statistics, the AI's own lineup policy, the projection tools, and
the discipline and competition screens are the rest.

#### Added

- **`comms.inbox_messages`** (master plan §6.9): one row per message, addressed to a manager, carrying a
  stable category, a template key, and the parameters that fill it. Its `check` puts the category among the
  known codes rather than an ordinal, so a reordered enum cannot move a message between shelves, and its
  partial index on the unread rows is what the count and the badge are read against.
- **`InboxMessage` and `InboxCategory`** (`COM-1`, `COM-5`): the aggregate with reading as its only mutation.
  Reading twice is a no-op, so a retried command cannot advance a version or move a timestamp, and there is
  no archive or delete because the MVP offers neither — a half-built shelf with no way to reach it is the
  surface §17.12 keeps out.
- **The templates and their renderer** (`COM-2`): `InboxTemplates` owns every stable key and the parameter
  document that goes with it, and `InboxMessageText` turns a stored key and document back into English. That
  is master plan §8.6's commentary contract applied to the inbox — durable tokens, derived prose — so a
  message written today can be rendered in another language later without rewriting history. A key the build
  does not know is refused by name rather than shown as a vague placeholder, because writer and reader share
  one set of constants and an unknown key is a defect worth seeing.
- **`MatchdayNotifications`**: one composer that turns a round's facts into the messages its managers read,
  so a result, a table move, a card, an injury, and a repaired side all address their manager the same way
  and resolve a name once. It stages messages and never saves, so they commit inside the transaction that
  publishes or locks the round.
- **Publication reports the result, the table, the cards, and the injuries** (`COM-4`). Every club that
  played is told its score and its new position, and only a position that changed produces its own message;
  a booking's accumulation and a sending-off each earn a suspension message, and an injury one — written in
  the publication's own serializable transaction, so a result and the news of it become public together.
- **Locking reports the repaired side** (`DIS-7`): the decisions the snapshot builder made are grouped by the
  club whose side it was and sent to that club's manager. `DIS-7` said "reported to the affected manager"
  before there was an inbox to report it through.
- **The inbox reads and marks** (master plan §10.7): `GET /inbox` — a keyset page newest-first with the
  unread count — plus `POST /inbox/{messageId}/read` and `POST /inbox/read-all`. The recipient comes from the
  account and is never named by the request, so a manager can only read their own mail (`INT-1`, §10.9), and
  a cursor this build did not produce is refused by name (`INVALID_CURSOR`).
- **`GET /sync`** (§10.7, §11.2, ADR-0007): the unread inbox count and the server's instant, the one
  lightweight read the shell repeats. It is deliberately lenient — an account with no manager profile gets a
  count of zero rather than a refusal — because a background poll that refused would surface as an error for
  a manager who simply has nothing yet.
- **The `/inbox` screen** (§11.1, F-41) and the navigation destination flipped to available. The shelf name
  is text, an unread message says so in words, and each line links to the match, player, or fixture it is
  about, so the list is reachable by a keyboard and a screen reader rather than only by eye (§11.3). "Load
  older" walks the cursor, and the two marks carry their own pending and failure states.
- **The shell's unread badge and its poll** (`SyncStore`): one read a minute while the tab is visible and the
  browser is online, suspended in a hidden or offline tab, and dropped when the session ends — so the badge
  is fresh without a burst of requests when a laptop wakes up. A mark on the inbox asks the poll to read
  again at once, so the badge agrees with the screen the manager is looking at instead of lagging a tick.
- **The `comms` error codes and DTOs** (`CommsErrorCodes`, `InboxResponse`, `SyncResponse`), the module's
  write and read ports, and its two providers, following the module map's `comms` entry rather than a new
  shape.
- 5 new domain tests (347 total) for the message's factory and its read contract; 13 new application tests
  (81 total) for every template's English, the unknown-key refusal, the cursor round trip, and the
  composition — which clubs are told what, that an AI club is told nothing, and that a result names its
  opponent; 3 new infrastructure tests (123 total) over a real seeded world that play a round and read it
  back, walk the keyset page across thirty same-instant messages without a skip or a repeat, and mark read;
  and 4 new API integration tests (85 total) that claim a club, play its round, read the result over HTTP,
  watch the unread count fall as they read and mark all, and check the no-profile, bad-cursor, and
  unauthenticated refusals.
- 15 new web unit tests (202 total): the category names and destinations, the store's cursor walk, its two
  marks and their refusal paths, and the sync poll's cadence and its three quiet cases — signed out, offline,
  and hidden. 2 new Playwright cases (27 across both stacks): the inbox reached from the navigation with its
  empty state, and the guard for a visitor with no session.

#### Notes

- **A message's wording is not stored.** The row holds a key and a parameter document, and the English is
  rendered on the way out, so changing a sentence is not a migration and the same message can be re-rendered
  in another language. The cost is one small render per message on the read, which the page's own limit
  bounds.
- **A message goes to whoever held the club when the event happened, not to the club.** §6.9 addresses a
  manager, and that is the right reading: a result is something a person is told, and a takeover the next day
  should not hand the previous manager's mail to their successor.
- **An AI club is told nothing, and that is the whole addressing rule.** The targets read joins clubs to
  their open tenures, so a club nobody holds has no recipient and produces no message. Nothing special-cases
  the AI.
- **The table move is its own message, and only on a change.** Every club that played already hears its new
  position in its result, so a second message on every round would be noise; one that fires only when the
  position actually moved is a fact worth a line.
- **The messages commit with the round.** They are staged in the publication's serializable transaction and
  the lock's own, the same argument the absences and the match load make: a message about a result nobody can
  read yet would be the same defect as a leaked score (`MAT-7`).
- **`news_items` is not in this milestone, deliberately.** §6.9 lists a world/country/division-scoped news
  table, and §16 Stage 8 asks for "news/inbox templates". There is no read that returns a news item, so
  building the table now would ship a row nothing shows — the header above records why. The division-scoped
  round summary that would use it belongs with Stage 11's full inbox center.
- **No deadline-reminder message yet, and that is also deliberate.** §16 lists "deadlines" among the
  templates, but a reminder is a scheduled job rather than a fact of a played round, and Stage 11 owns it
  alongside the notification preferences it needs to know whether a manager wants one. The `COM-*` rules
  record the deferral.
- **`/sync` carries only the unread count.** §11.2 describes "lightweight changed-resource hints", and a hint
  with no consumer is a field nobody reads; the inbox badge is the first thing that needed the poll, and the
  next module that needs a hint adds it beside the count.
- **`recipient_manager_id` is the column, not the data model's `manager_id`.** The document now matches the
  executable schema, along with one `template_key` rather than a title/body pair (one template renders both)
  and no `related_entity_type` (the category decides what the related id is) or `archived_at` (nothing
  archives). `docs/architecture/data-model.md` §3.4 and §3's constraint table were updated with the code.
- **The read is measured against the manager, not the match's season.** A played result stays readable across
  a rollover, and the inbox is a manager's mail rather than a season's — so the messages are keyed on the
  recipient and never on the current season (`CAL-6` is about the world's calendar, which this does not
  read).

### The AI club's own management

A side nobody picked. Every club no human holds now has a real default plan — a formation, its eight team
instructions, and a full eleven — and a training plan, written by a deterministic policy and accepted by the
same validator a manager's save goes through. Until now an untended club fielded the builder's neutral
four-four-two and trained at an implicit default, which is a repair rather than a manager; `INS-12` asks for
the AI's own decisions, and this is them. The season statistics, the projection tools, and the discipline and
competition screens are the rest of the stage.

#### Added

- **`AiClubPolicy`** (`ai-policy-v1`, `INS-12`): a pure, versioned function from a club's identity and its
  squad to its tactics, its default eleven, and its training. Every draw comes from a `Pcg32` stream seeded
  from the club identity and the policy label, so the same club always decides the same way and two clubs
  differ — and changing the draw order or the formation list is a named policy version rather than a silent
  constant, exactly as `training-v1` and `match-load-v1` version themselves. It reads no clock, database,
  culture, or global random source.
- **The tactics vary and the training does not.** The formation and the eight instructions are drawn per
  club, because a division of eighteen identical sides is not a league and every choice is one the engine
  already bounds (`INS-9`). Training is the neutral `balanced`/`normal` for every AI club, because a training
  focus is a persistent development path rather than a match-to-match lever, and a club permanently dealt a
  random focus would be a fairness problem dressed as variety.
- **The eleven is chosen by the rule the builder already repairs with** (`DIS-6`): position suitability,
  then condition, then ability, then stable player ID, with exactly one recognised goalkeeper and only
  available players. A squad that cannot field a legal eleven yields the plan's shape with no lineup, which
  `TAC-10` allows and the snapshot builder fills at the lock.
- **`EvaluateAiClubs`** (master plan §7.2): the use case that gives every AI-controlled club the plans it
  is missing. It builds the policy's decision into `TacticalSlotDefinition`s, passes them through the same
  `TacticalPlanValidator` a human's save calls, and writes only what fails nothing. A plan a previous run or
  a manager wrote is left exactly as it is, so a repeat run writes nothing and a resignation does not reset a
  club (`WORLD-9`, `OCC-5`).
- **`world.evaluate-ai-clubs`**, the durable job, and **`AiClubScheduler`**, which materialises one row per
  UTC day so the row is the deadline and a worker that was down when the day opened runs late rather than
  skipping (`ADR-0003`). Registered only by `AddJobQueueWorker`, so an API process can never write a squad
  plan; `AiClubs:EnableEvaluation` is on by default, because the work is idempotent and a world without it is
  not the stage. The job handler is a thin shell and reports the counts.
- **`IAiClubRepository`**/**`AiClubRepository`**: the evaluation's read — every club with no open tenure,
  with its squad, its condition, its availability, and which of its plans already exist — in a fixed handful
  of queries however deep the pyramid gets. "Open" spans active and inactive (`OCC-8`), so an away manager's
  club is not the AI's to set up; making safe decisions for them is the inactivity ladder's work (`OCC-2`),
  which Stage 11 owns.
- **ADR-0018**, on the pure versioned policy, the gap-filling evaluation, the worker-only daily job, and the
  neutral training choice.
- `docs/product/game-rules.md` §9 and §18 gained the policy's behaviour and the `ai_policy_version` constant.
- 8 new domain tests (355 total) pinning determinism, the variety across clubs, the validator's acceptance,
  the one-goalkeeper rule, availability, the no-goalkeeper case, and the neutral training; 5 new application
  tests (86 total) over the orchestration — a club with no plans gets both, a club with one keeps it and gets
  the other, a complete club is left alone, an eleven-less squad still gets a legal plan, and every plan
  written passes the validator; and 3 new infrastructure tests (126 total) over a real seeded world that
  evaluate every AI club, assert each stored plan is valid against its own squad, run twice, and assert a
  club a human holds is never loaded.

#### Notes

- **The validator caught the first version's bug, which is the point of routing through it.** The empty-lineup
  case was building each slot's occupant with `dictionary.GetValueOrDefault(slot)`, which for a `Guid`
  dictionary returns `Guid.Empty` rather than null — an eleven of slots pointing at a player who does not
  exist. `TacticalPlanValidator` refused it as `PLAYER_NOT_ELIGIBLE`, and the domain test failed before the
  application test could hide it. A policy that wrote its own plan straight to the tables would have shipped
  an illegal side that the builder then silently repaired at every lock.
- **A record's collection member is compared by reference, so the determinism test compares field by field.**
  Two calls do build equal decisions, but not the same slot-list instance; the test asserts the formation,
  instructions, slots, and training are equal rather than that the two records are.
- **The evaluation re-picks nothing.** A club's plan is fixed until a manager changes it: the AI does not
  rebuild its eleven as injuries and transfers move the squad, because the snapshot builder already repairs
  an unavailable or ineligible pick at the lock (`DIS-6`), and re-picking on every evaluation would rewrite a
  manager's plan the moment they resigned. A state-reactive AI lineup is a later feature if balance needs it.
- **A held club keeps its plan, and that is the deliberate reading of "left alone".** The evaluation is
  measured against the clubs with no open tenure, so a club that is taken over simply stops being considered;
  the side it already has — the AI's or the previous manager's — is what the new manager inherits (`WORLD-9`).
- **The AI's training plan is a stored row equal to the implicit default, and that is on purpose.** The
  progression job already assumes `balanced`/`normal` for a club with no plan (`TRN-1`), so the row changes
  no behaviour today; it makes the AI's choice explicit and inspectable, and it is what a future
  state-reactive policy would replace in one place. The alternative — writing no row and leaving the default
  implicit — would leave the AI's training decision with no home to grow into.
- **Nothing here is reachable from a command.** The plans are written by the worker's evaluator alone, and the
  only surface this milestone adds is a job type and a scheduler — `MAT-2`'s principle, applied to the squad.

### The season's statistics

A season you can measure. A result now says who set a goal up and how well each player played, and the
matchday publication grows a player's season totals from the result and its events: appearances, starts,
minutes, goals, assists, shots, shots on target, saves, cards, and an average match rating. `GET
/divisions/{divisionId}/statistics` reads a division's leaderboard, and `/competitions/:id/statistics` is
the screen. This is the fourth milestone of the stage; the projection rebuild tools and the discipline
screens are the rest.

#### Added

- **The engine gains assists and a match rating** (`engine-v2`, `engine-rules-v2`, ADR-0019). A goal from
  open play or a corner credits one teammate with the assist, and every player who took the pitch gets a
  rating in basis points on the 0–10,000 scale the API converts to a 0–10.0 figure (`TRN-8`). Both live on
  the player line, so a season's totals are read from the stored result rather than re-simulated, and the
  result document is bumped to `match-statistics-v3` to carry them.
- **The play model is unchanged, and that is deliberate.** The assist is drawn from a stream derived from
  the match seed and the goal's own sequence number, never from the play stream, and the rating is
  arithmetic over facts the match already records. Adding a draw to the play stream would have shifted
  every later decision and moved the scoreline distributions the engine was calibrated against; instead the
  measured bands did not move, and the 82 new engine tests pin the assist and rating contracts beside the
  existing ones.
- **`competition.player_season_stats`** (master plan §6.4): one row per player per club per division-season,
  keyed to include the club because a player may move mid-season. Its checks are the ones a manager would
  believe: nothing negative, a start is a subset of an appearance, shots on target a subset of shots, and
  the stored average inside the scale. The average is computed from the stored total and count rather than
  stored, for the same reason a table's goal difference is (`TBL-3`).
- **`PlayerSeasonStat`**: the projection aggregate with the accumulation as behaviour, following
  `Standing` and `DisciplineRecord`. `Accumulate` takes one match's line and refuses one that cannot be
  true, so a projection cannot be advanced by a malformed fact.
- **`SeasonStatisticsCalculator`** (`season-stats-v1`): a pure function from a round's stored results and
  shot and save events to one line per player who appeared. Goals, assists, minutes, cards, and the rating
  come from the engine's line; the shots and saves are counted from the same events the score is derived
  from (`MAT-5`). A player who stayed on the bench produces no line.
- **Publication advances the statistics** (`STA-1`): `PublishMatchday` computes the round's lines and
  applies them in the same serializable transaction that publishes the nine fixtures, moves the table, and
  applies the cards, injuries, and load — so the totals and the results they summarise become public
  together. The round's results are read once and feed both the load and the statistics.
- **`GET /divisions/{divisionId}/statistics`** (§10.5): the division's player rows most goals first, then
  assists, then name, with the average rating on its display scale. Public game data, like the table.
- **The `/competitions/:divisionId/statistics` screen** (§11.1) and a link from the table screen. A real
  `<table>` with a caption, column headers, and a player row header, so assistive technology reads it as
  tabular data, and the rating column reads `—` before a player has one.
- **ADR-0019**, on the additive engine change with a derived assist stream, the rating as engine output,
  the projection and its key, and the deliberate deferral of `club_season_stats` and of retaining the
  previous engine version pre-launch.
- `docs/product/game-rules.md` §15.1 (`STA-1`…`STA-4`) and the `season-stats-v1` and rating constants;
  `docs/product/match-engine.md` updated for version 2.
- 82 new engine tests (603 total) for the assist attribution, the per-goal assist reconciliation, the
  rating's bounds and its absence for a player who did not appear, and the rating's movement with a goal;
  9 new domain tests (364 total) for the accumulation, its refusals, and the computed average; 5 new
  application tests (91 total) for the calculator over a stored result and its events; 2 new infrastructure
  tests (128 total) over a real seeded world that publish a round and assert a line per player who appeared
  and no double count on a republish; 3 new API integration tests (88 total) that read a played division's
  leaderboard, reconcile its goals with the fixtures' scores, and check the unknown-division and
  unauthenticated refusals; and 4 new web tests (206 total) for the rating label and the statistics read.

#### Notes

- **A penalty has no assister, and the assist is not in the event stream.** A goal event names the scorer
  and the goalkeeper it beat and nothing else, so the player who made the chance is chosen when the goal is
  scored and kept on the line beside the goals. That is why the line carries it rather than the event.
- **The assist consumes no draw of the play.** Crediting one from the play stream would advance every
  subsequent decision, so the same seed would no longer produce the scoreline the engine was tuned to. The
  derived stream is seeded from the match seed and the goal's sequence number, so assist attribution varies
  per goal, is reproducible, and touches nothing else about the match.
- **The rating is the engine's, not the projection's.** It is produced once with the result so a season's
  average has one definition, and it is derived only from facts the match already records — minutes, goals,
  assists, saves, cards, and the result — so it carries no hidden value (`MAT-11`). The projection reads it
  rather than recomputing it.
- **`club_season_stats` is deliberately not in this milestone.** §6.4 lists a club-level aggregate "not
  represented in standings", but a club's competitive row is already the standings projection and a club's
  shooting or possession aggregate has no read and no screen; building the table now would ship a row
  nothing shows (`§17.12`). It belongs with the screens that would consume it.
- **A previous engine version is not retained, and that is the same call the statistics document made.**
  ADR-0004 says a released engine version is never altered in place; there is no production world, so the
  cost is that a database seeded before this change must be reseeded for an old snapshot or result to be
  readable, and the migration and the document schema say so. Retaining a compiled previous version becomes
  required before launch, and ADR-0019 records it as a pre-launch item rather than pretending it is done.
- **The average is truncated, not rounded, from the stored total and count.** It is a function of two
  columns beside it, and the response rounds it to the tenth of a point a rating is read in — one definition
  of the average, converted once at the edge (`TRN-8`).
- **The statistics screen sorts nothing.** The projection stored the order — goals, then assists, then name
  — so a screen that re-sorted would be a second, drifting definition of the leaderboard, exactly as the
  table's rank is the server's (`TBL-12`).
- **The tie-break view is not here.** §16 Stage 8 lists "tie-break views"; the table screen already reads
  the order the projection stored and reimplements no rule, and the competition-rules page that would show
  the ordering and the stored draw key (`TBL-11`) belongs with the rest of the Stage 8 competition screens.
- **The player profile does not yet carry the player's own season line.** §11.1 lists "season stats" on
  `/players/:id`, and the read that would add it is the same projection this milestone fills; it is the
  squad module's read to widen and belongs with the screen work that follows, rather than being bolted onto
  the competition milestone's commit.

### The projections a repair can rebuild

A projection you can trust. A division's table and its players' season statistics are caches of facts the
world already stores — the published fixtures and the results and events they were played from — and this is
the tool that proves it: `RebuildDivisionProjections` recomputes both from those facts, reports exactly what
has drifted, and, when asked to, sets it back. Left alone it is the reconciliation an operator inspects;
applied it is the repair `TBL-13` promises, done with the same arithmetic the live publication runs. This is
the projection rebuild and reconciliation tool §7.2 calls `RebuildProjection`; the discipline and competition
screens are what is left of Stage 8.

#### Added

- **`PlayerSeasonStatLine`, and `PlayerSeasonStat.Create` / `Rebuild`** (`STA-1`, `TBL-13`): a whole season's
  line as one value, and the two operations that write one. `Create` opens a fresh projection already holding
  a season's totals; `Rebuild` replaces a stored line's totals with a recomputed one rather than adding to
  them — the operation the aggregate lacked, because until now a line could only be opened empty and
  accumulated match by match, so a drifted row had no way back. Its checks mirror the aggregate's and the
  database's: an appearance is required, a start is a subset of an appearance, shots on target a subset of
  shots, and a season's rating total is at most ten thousand basis points per rated appearance.
- **`SeasonStatisticsCalculator.Aggregate`** (`STA-2`): the same arithmetic the live publication performs
  match by match, applied in one pass over every published result of a division-season. It sums stored match
  facts rather than re-simulating, so the totals cannot disagree with the results they summarise, and the
  average rating stays a total and a count on the aggregate because that is where its one definition lives.
- **`RebuildDivisionProjections`** (`TBL-13`, `TBL-14`, master plan §7.2): the reconciliation and the repair
  in one use case, chosen apart by an `apply` flag. It recomputes the table with `StandingsCalculator.Rank`
  over the division's published outcomes and the statistics with `SeasonStatisticsCalculator` over its
  published results, and reports each row or line that differs, is missing, or has no source. The dry run
  writes nothing; the apply reads and writes inside one serializable transaction — the isolation and the
  read-inside shape the publication uses — so a peer publication cannot interleave between the recomputation
  and the correction. A stored line no published result supports is removed, so the repair leaves the
  projection equal to its source rather than merely close to it.
- **`IMatchdayRepository` gains the division-wide loads** the rebuild reads: every published fixture's
  match-load row, every shot and save event, and every stored season line of a division-season, plus the
  removal stage. They are the whole-division forms of the per-matchday reads the publication already uses, so
  the rebuild and the live path parse one shape in one place.
- **`competition.rebuild-division-projections`** (§7.2): the durable job, keyed on the division-season so a
  repeated enqueue is a no-op, and its handler — a thin shell that refuses a payload with no division-season,
  or one the world does not have, as a permanent failure, because retrying cannot produce the missing row.
  It is worker-only and has no public command, like every other writer of a projection (`MAT-2`).
- **ADR-0020**, on the one-use-case-two-modes tool, the recompute-and-rewrite domain operations, the
  read-inside-the-transaction repair, the removal of an unsupported line, and the deliberate deferral of the
  discipline accumulation.
- `docs/product/game-rules.md` §6 (`TBL-14`) and §15.1 (`STA-6`) gained the reconciliation rule;
  `docs/architecture/data-model.md` §5 now names the job and records why `discipline_records` is not rebuilt.
- 6 new domain tests (370 total) for `Create` and `Rebuild` — that a rebuild replaces rather than sums, and
  its refusals; 3 new application tests (94 total) for the aggregation — the sum across a player's matches,
  the two clubs a transfer leaves, and the unrated appearance that is not a zero; and 5 new infrastructure
  tests (133 total) over a real seeded world that publish a real round and then reconcile it: a published
  division reconciles with no drift, a corrupted table row and a corrupted player line are each reported by a
  dry run and corrected by an apply, the repaired projections equal a fresh computation, a stored line no
  result supports is removed, and an unknown division-season is refused.

#### Notes

- **The two tools are one use case separated by a flag, and that is deliberate.** `apply: false` is the
  reconciliation read and `apply: true` is the repair, and both run the same recomputation. A separate
  "check" path would be a second definition of what the projections should be — exactly the drift the tool
  exists to find.
- **The apply reads inside its transaction, and the first version did not.** It computed the drift from
  reads taken before `BeginTransactionAsync` and then wrote, which would have let a publication commit a
  round between the recomputation and the correction and left the repair writing a stale answer. The
  publication guards itself by reading and writing in one serializable transaction; a repair that recomputes
  a whole division is the same shape, and it was moved inside for the same reason.
- **A repair removes a line nothing supports.** `player_season_stats` has a unique key and no other delete
  path, so a line written for a result that a later repair voided would sit beside the totals forever. A
  projection is a cache (master plan §5), so the rebuild deletes a line whose player and club no published
  result names — which is what "equal to the live projection" requires, and the fixture's own test asserts a
  removed row.
- **The discipline accumulation is not rebuilt, and the reason is its consequence.**
  `docs/architecture/data-model.md` §5 lists `discipline_records` among the rebuildable projections, and its
  card counts are derivable from the same events — but the record's whole purpose is a suspension already
  served against specific fixtures, and replaying the accumulation without replaying the service would leave
  a player's bookings and their absences disagreeing. That reconciliation needs the rollover that owns the
  accumulation's reset (`DIS-3`), and it is recorded as deferred rather than half-built (`§17.12`).
- **There is no HTTP surface.** The repair is a durable job with a business key, enqueued the way the plan's
  operator workflow will; the admin module that would give it a button, a reason, and an audit entry is
  Stage 14's. A diagnostic trigger now would be the feature-incomplete route `§17.12` keeps out, so the tool
  is proven by its own integration tests rather than by an endpoint.
- **The tests play in their own division-season.** The other workflow tests use the first division-season and
  the seeded-table test reads the last, so a rebuild test that corrupts a row cannot disturb either; it plays
  in the third, which nothing else touches, and names it by country code rather than by creation order.
- **Deferred to the rest of Stage 8:** the discipline and competition screens, including the tie-break view
  and the competition-rules page that shows the stored draw key (`TBL-11`).

### The competition's rules and its draw

A rule you can read. `GET /divisions/{divisionId}/rules` answers with the points a result is worth, the
table's tie-break criteria in the order they are applied, and the draw the season committed to before it
began — the stored seed, its published hash, and every club's derived key. `/competitions/:divisionId/rules`
is the screen, reached from the table. It closes `TBL-11`: "the final draw key is generated before the
season, stored, and visible in competition rules" is now a page a manager can open rather than a claim in a
document.

#### Added

- **`TieBreakers`** (`TBL-2`…`TBL-10`): the table's ordering as one ordered list of stable codes, so the
  rules view and the calculator describe one order rather than two. `StandingsCalculator` gained
  `PointsForLoss` beside the two point values it already carried, so the points a page shows are the points
  the table awards rather than a second copy of `TBL-1`.
- **The rules read** (master plan §10.5): `GET /divisions/{divisionId}/rules`, `GetDivisionRules`, and
  `ICompetitionQueries.GetDivisionRulesAsync`. It returns the division and its season, the points, the
  ordered criteria, the stored tie-draw seed and hash, and one key per club. The keys are derived from the
  stored seed with `StandingsCalculator.DrawKeyOf` rather than stored, so a key cannot disagree with the draw
  it came from, and the hash is carried because it is what makes the draw checkable.
- **The mapping reads the domain.** `FixtureMapping.ToResponse` takes the points and the ordering from
  `StandingsCalculator` and `TieBreakers` rather than from the read, so a page cannot describe an order the
  table does not follow.
- **The `/competitions/:divisionId/rules` screen** (§11.1): the criteria in words as an ordered list, the
  seed and its hash, and a table of every club's draw key. The club table is a real `<table>` with a caption
  and a row header, so assistive technology reads it as tabular data (§11.3), and the table screen links to
  it beside the statistics link.
- **ADR-0021**, on the ordering as one domain definition, the tie-draw seed and its hash being public while
  the match seed stays protected, the per-club keys derived rather than stored, and the read being public
  game data.
- 1 new domain test (371 total) pinning the order as the rules' literal codes and that no criterion is
  listed twice; 1 new application test (95 total) for the projection over the domain's own points and order;
  2 new infrastructure tests (135 total) over a real seeded world — the read returns the division-season's
  stored seed and hash, the hash is the digest of the seed, every club's key is distinct and derived from the
  stored seed, and an unknown division answers nothing; 3 new API integration tests (91 total) that read a
  division's rules over HTTP and check the unknown-division and unauthenticated refusals; and 4 new web tests
  (210 total) for the criterion wording and the store's rules read, its failure, and its clearing.

#### Notes

- **The draw is shown, and that is `TBL-11` rather than a disclosure decision.** An ordering whose last
  criterion is a draw is only meaningful if a manager can see the draw the season committed to before it was
  played — a stored secret would be indistinguishable from one chosen after the fact. The protected value is
  the match seed (`MAT-9`); the tie-draw seed is not it, and it decides only the order of two clubs that no
  result separated.
- **The hash sits beside the seed because verification needs both.** `tie_draw_hash` is a digest of
  `tie_draw_seed` (`SeedWorld`), so showing the pair lets a reader confirm the seed has not been edited since
  the season began. The integration test asserts the relationship, so a seeder change that broke it fails a
  fast test rather than quietly making the page's evidence worthless.
- **The ordering is the server's and the words are the client's.** The response carries stable codes
  (`points`, `draw_key`, …) and the web maps them to English, the split the commentary tokens and the inbox
  templates already use (§8.6), so a criterion added to the rule set reaches the page without a second
  ordering being maintained in TypeScript.
- **Nothing is sorted in the client.** The criteria arrive in the order the table applies them and the clubs
  by name, so the screen renders the sequence it is given rather than reimplementing a rule — the principle
  the table's stored rank already follows (`TBL-12`).
- **A surface the plan did not route is still a screen the rules asked for.** §10.5 lists the division reads
  it knows about and `TBL-11` asks for "competition rules"; the route is
  `/competitions/:divisionId/rules`, reached from the table whose division it describes, so it is a detail
  route rather than a navigation destination like the statistics screen beside it.
- **Deferred to the rest of Stage 8:** a division's discipline as its own view — its bookings and its
  outstanding suspensions — which the statistics screen half covers (each player's card counts are already
  public) but which has no page for the suspensions themselves; the `club_season_stats` aggregate, still
  without a reader; and the player profile's own season line, which is the squad module's read to widen
  rather than the competition module's to bolt on.

### The season on the screens that read it

A season you can inspect player by player and card by card. A player's profile now carries their own line
of the season's statistics — appearances, minutes, goals, assists, cards, and the average match rating —
read with the profile from the projection the division leaderboard already shows, so the two can never
disagree. And a division gains a discipline view: every player the season's cards have touched, with the
suspension they still owe, in one read beside the table, the statistics, and the rules. This closes the
discipline and competition screens, the last of Stage 8's deliverables.

#### Added

- **The player profile's own season line** (`STA-2`, master plan §11.1, F-17): `GET /players/{playerId}`
  now carries `SeasonStats` — the goals, minutes, cards, and rating the division leaderboard shows for that
  player — alongside the attribute grid, state, contract, and registration. It is the same stored row the
  leaderboard reads, narrowed to one player, so the profile and the leaderboard cannot disagree about a
  player's season; a player who has not taken the pitch has no line at all, and the screen says so rather
  than showing a row of zeros (`STA-4`). The squad module's read was widened to carry it rather than a
  second read being bolted on, because the profile is a squad screen.
- **A division's discipline** (`DIS-9`, master plan §10.5): `GET /divisions/{divisionId}/discipline`,
  `GetDivisionDiscipline`, and `ICompetitionQueries.GetDivisionDisciplineAsync` answer with every player the
  season's cards have touched — their bookings, their sendings-off, and the fixtures they still miss through
  suspension — read from the season's stored accumulation (`DIS-2`, `DIS-4`) and the open absences the
  publication serves (`DIS-5`), so the page and the side a manager may actually name cannot disagree about
  who is suspended. Public game data, like the table it sits beside, in the order the server ranked it —
  most sendings-off, then most bookings, then name.
- **The `/competitions/:divisionId/discipline` screen** (§11.1) and links from the table and statistics
  screens. It is a real `<table>` with a caption, column headers, and a player row header, so assistive
  technology reads it as tabular data (§11.3); the suspension column reads the fixtures a player still
  misses, and a dash when they owe none, because a suspension is measured in fixtures and never in days
  (`TRN-12`). `suspensionRemainingLabel` is the one place that phrase is built.
- **The profile's season summary** (`seasonStatRows`): the player's line as labelled values, built once so
  the rating is read to the tenth of a point its one definition uses (`TRN-8`) and so a test pins both the
  labels and the order.
- 3 new application tests (98 total) for the two projections — the discipline crossing in the order it was
  ranked, and the season line's rating converted to its display scale with an unrated player given no
  average; 3 new infrastructure tests (138 total) over a real seeded world that publish a round and read the
  profile's season line back, and inject a sending-off and a booking and read the division's discipline, its
  suspension, and its order; 4 new API integration tests (95 total) that play a round and read the two
  screens over HTTP, reconcile the profile's line with the leaderboard's row, and check the unknown-division
  and unauthenticated refusals, plus the assertion in the squad profile test that a fresh world gives a
  player no season line; and 6 new web tests (216 total) for the suspension phrase, the season summary, and
  the discipline store's read, its failure, and its clearing.

#### Notes

- **The profile's line is the leaderboard's row, not a second computation.** `GetPlayerAsync` reads
  `competition.player_season_stats` for the player's club in the season being played and hands it to the
  same mapper the leaderboard's rows go through, so there is one definition of a player's season and the two
  screens are two views of it. The cost is that the squad read reaches a competition table; the alternative
  — a second read and a second mapping — is exactly the drift the projection exists to prevent.
- **The discipline view derives its suspension figure from the open absence rather than storing it.** Master
  plan §6.4's `discipline_records` has no `pending_suspension_fixtures` column and deliberately keeps none:
  an outstanding suspension is already a `PlayerUnavailability` the publication serves and the snapshot
  builder reads (`DIS-6`), so the page reads the same record the side is repaired against. When a player
  holds more than one open ban they run concurrently rather than in sequence, so the read reports the
  greatest remaining count rather than their sum, which is what "fixtures still to miss" actually means.
- **The club on a discipline row is resolved through the player's active contract.** The accumulation is
  keyed on the division-season and the player, so the row has no club of its own; the club comes from the
  same active-contract join the squad read makes (`SQ-6`). A player with no active contract would not appear
  — which today cannot happen, because the seeder gives every player one and nothing closes one yet.
- **Nothing here is a command.** Both surfaces are reads: the profile is the owning manager's read and the
  discipline is public game data, and neither produces or changes a card, an absence, or a statistic
  (`MAT-2`). The projections they read are written by the matchday publication alone.
- **The two screens' ordering is the server's, and neither sorts.** The discipline rows arrive in the order
  the read ranked them, and the season summary in the order the product reads it, so a screen reimplements
  no rule — the principle the table's stored rank and the statistics leaderboard already follow (`TBL-12`).
- **Touching `squad-presentation.ts`, its spec, and `player.html` reflowed a few pre-existing lines to the
  pinned Prettier's 100-column width.** The formatting is whitespace only; no behaviour changed. The web
  job's `prettier --check` is not in CI (the frontend job runs build and unit tests only), and three files
  this change does not otherwise touch — `squad.html` and the two `attribute-value` files — already exceed
  the limit on `main`, so they were deliberately left alone rather than swept into this change (`§17.15`).
- **Deferred to Stage 9 and beyond:** the `club_season_stats` aggregate, still without a reader; a
  division's results history as its own read; and the discipline view's per-club filter, which has no
  consumer until the competition screens grow one.

## Stage 7 — Text match center and 2D highlights

A result you can watch. `GET /matches/{id}` answers with the score and the statistics, and
`GET /matches/{id}/presentation` answers with the commentary timeline and the keyframe highlights — an
immutable representation, tagged with the result's own hash and cached accordingly. The `/matches/:matchId`
screen plays that replay: a Canvas interpolating the server's keyframes at the display's refresh rate, a
commentary timeline that doubles as event navigation, and the playback controls the plan asks for — 1x, 2x,
4x, pause, skip, replay, and seek. This is the first milestone of the stage; the end-to-end watch journey is
the rest of it.

### Added

- **The match reads** (master plan §9.5, §10.5): `GET /matches/{matchId}` — the score, both sides'
  statistics, and where the match was played — and `GET /matches/{matchId}/presentation` — one line of
  commentary per narrated event and the highlights in event order. Both are public game data, like the
  calendar and the table, so neither is gated on holding a club.
- **Only a published match is readable** (`MAT-7`). The visibility rule lives in the read itself rather
  than in a check a caller might forget: a fixture that is `staged` already has a match id in the database,
  and the query answers nothing for it rather than leaving it to the mapping to hide.
- **`IMatchQueries`/`MatchQueries`**: the match viewer's own read port, separate from the write-and-lookup
  side the matchday workflow uses (`MOD-3`). It is measured against the match's own season rather than the
  world's current one, so a result stays readable across a rollover — a played match is history.
- **`GetMatch` and `GetMatchPresentation`**: the two use cases. The summary reads the stored statistics
  document, so a read cannot disagree with what was published; the replay is re-derived from the frozen
  snapshot, and the re-derived output hash is compared with the stored one before anything is returned, so
  a replay of a different match is impossible (`MAT-9`).
- **The immutable replay's cache contract** (§9.5): the presentation is a strong entity tag — the output
  hash of the result it was derived from — and answers `If-None-Match` with `304 Not Modified`, with a
  long-lived `immutable` cache directive. The `EntityTagHeader` gained the opaque-token form and the
  `If-None-Match` comparison (RFC 9110 §13.1.2, weak) alongside the concurrency versions it already had.
- **The match center screen** (`/matches/:matchId`, master plan §9.5, §11.1) and a **Watch** link on each
  published result in the fixtures list. The screen shows the scoreline, the statistics as a `<table>` with
  a caption and row headers, the commentary timeline, and the highlight player.
- **`MatchPlayback`**: the replay's state machine, framework-neutral and timer-free. It is advanced by
  whatever drives the animation with the real elapsed milliseconds, and a speed scales that time before it
  is applied to the playhead — so the animation and the match clock are always at the same point of the
  same highlight. Highlights play in event order and are never reordered, so two chances in the same minute
  are two queued entries (`§9.4`).
- **The framework-neutral Canvas renderer** (`features/match-viewer/renderer/`): `pitch-layout` keeps the
  pitch in its real proportions and maps normalized coordinates onto it; `keyframe-interpolator` lerps
  between the keyframes that bracket a moment and holds at the ends rather than extrapolating; `render-loop`
  is a `requestAnimationFrame` wrapper whose point is to be stoppable; and `canvas-match-renderer` draws the
  pitch, both sides, the ball, its trail, and the shirt numbers, scaling to the device pixel ratio without
  touching normalized geometry (`§9.1`, `§9.3`, `§9.4`).
- **The animation is off the change-detection path.** The loop draws every frame, but signals are written
  only when something discrete changes — the highlight, the play state, the speed — so a 60 Hz animation
  does not re-evaluate the commentary list 60 times a second (`§9.4`). The loop is stopped on pause, on the
  tab going hidden, and on destruction (`§9.4`).
- **Accessibility** (§9.4, §11.3): the highlight's narration is outside the Canvas and announced politely;
  the Canvas is a labelled image rather than the only way to follow the replay; every control is a labelled
  button (the speed and text-only controls are `aria-pressed`); the timeline offers a *Watch* action only
  for lines that have a highlight behind them; and the home side is drawn as circles and the away side as
  squares, so the teams are never told apart by colour alone.
- **Reduced motion and a text-only mode** (§9.4): `prefers-reduced-motion` is read and re-read on change,
  and a *Text only* control hides the diagram entirely and keeps the narration and the timeline. The viewer
  never starts animating by itself.
- **The service worker's presentation cache** (§11.4): a `dataGroups` entry for
  `/api/v1/matches/*/presentation`, network-first with the last answer kept for seven days, so a recently
  viewed replay can be read offline while a mutation is never queued (`ADR-0007`).
- **The match reads' own API host.** A second collection fixture with its own PostgreSQL container and its
  own seeded world, because publishing a matchday moves a division's table and puts scores on its fixtures
  — which is exactly what the other API tests assert is not yet true.
- 7 new API integration tests (77 total) that play a round through the real workflow and then read it over
  HTTP: the score agreeing with the goal events, possession complementary, every goal linked to a highlight
  and reconciling with the scoreline, every highlight a 23-entity keyframe payload within the 750 KB budget,
  the ETag and `304` with a stale tag answered in full, neither payload carrying a seed or a hash or a shot
  quality, an unpublished result not found with a stable code, and an unauthenticated visitor refused.
- 36 new web unit tests (186 total): the playback state machine's speed scaling, long-frame walking,
  same-minute queueing, skipping, replaying, and seeking; the presentation helpers' clock, outcomes, and
  statistics rows; the store's paired read and its failure; and the renderer's interpolation and pitch
  geometry.
- 2 new Playwright cases (24 total): the guard for a visitor with no session, and the not-found state a
  manager gets for a match that has never been played.

### Notes

- **The presentation is re-derived, not stored, and that is a deliberate reading of §6.6.** The plan lists
  a `match.highlights` table; this milestone does not add one. The engine is pure and the snapshot is
  frozen and verified, so the commentary and the highlights are a function of data the world already keeps,
  and re-deriving them means they can never drift from the result they describe — a stored second copy is
  exactly what a later change leaves behind. The cost is one deterministic simulation per presentation read
  (p95 3.5 ms) behind an immutable cache, and the guarantee it buys is that the replay always matches the
  published result, which the use case asserts by hash.
- **The replay carries no server instant, unlike the mutable reads.** `TIME-5` asks responses to carry the
  server's time so a wrong client clock cannot mislead a manager, but an immutable, cached representation
  with a timestamp on it would be a stale clock dressed as a fresh one. The summary carries it; the
  presentation deliberately does not.
- **The read is measured against the match's own season.** The world's current season resolves most reads
  (`CAL-6`), and using it here would have made last season's results unreadable the moment the rollover
  window opened — which is the opposite of what "immutable" is for.
- **A separate API fixture was cheaper than a weaker assertion.** The other API tests read a freshly seeded
  calendar and assert that no score is public yet; a test that publishes a round in that world would have
  made them fail for a reason that is not a defect. The match reads therefore run against their own
  container, the same argument the matchday workflow's infrastructure fixture already makes.
- **`AuthScenario.CreateVerifiedManagerAsync` gained an overload over the email recorder.** It only ever
  consulted the fixture for captured mail, so a host with its own database can use the same honest flow
  without the shared fixture type.
- **The playback clock is the active highlight's minute, and that is the honest reading of §9.4's "the
  presentation clock pauses while a normal-speed highlight plays".** The clock is tied to the playhead, so
  it moves when the replay moves to the next event and holds while one is being shown, rather than ticking
  against a wall clock that has nothing to do with what is on screen.
- **The animation is stepped from the frame delta the browser gives, not from a stored start time.** A
  backgrounded tab that returns, or a slow frame, is applied as an elapsed amount rather than skipped, and
  the player walks forward across as many highlights as the elapsed time covers rather than assuming it
  landed inside the current one.
- **The e2e stack cannot yet watch a full match, and the journey says so.** Playwright's `webServer` starts
  the API and the web client but not the worker, and a real-time matchday is days away; so the browser
  journey asserts the route, the guard, and the not-found state, and the full "prepare and watch a complete
  fixture" journey is deferred rather than faked with a stub. The replay's contract is proven instead by the
  API integration tests, which drive a real round through the real workflow first.
- **The initial bundle is 531.0 kB, still under the 550 kB warning.** It grew because the shell now drops
  the previous manager's fixture and match state on sign-out, which pulls both feature stores into the
  eager chunk; the viewer itself — the renderer included — is a 18.6 kB lazy chunk (`ADR-0007`).

### The end-to-end watch journey

A fixture you can prepare, play, and watch. The stage's last exit criterion: a manager prepares a side for
their next fixture, a real round plays through the real worker — locked, simulated from a frozen snapshot,
and published — and the match center replays it in the browser. The only thing the harness asks for is that
the round play now rather than in a few days; the worker does every part of the work (ADR-0016).

#### Added

- **`TriggerMatchday`**: the use case behind the trigger. It enqueues a round's lock and resolution jobs with
  the same business keys and payload the scheduler builds, due now — and nothing else. Publication is
  enqueued by the resolution in its own transaction, exactly as on the calendar, so a triggered round is
  indistinguishable from a materialised one and a repeat inserts nothing (ADR-0003, ADR-0016).
- **A non-production diagnostics endpoint** (`POST /api/v1/ops/diagnostics/play-matchday`), mapped only when
  `Diagnostics:EnableMatchdayTrigger` is set and off everywhere by default, so it has no production surface
  (§17.12). Like the Stage 1 job probe it is unauthenticated; the gate is configuration.
- **The matchday end-to-end stack** (`tests/web-e2e/playwright.matchday.config.ts`): the API, the web client,
  and — for the first time in the suite — the **worker**, which has no HTTP surface and so is started without
  a readiness gate. It runs against a throwaway database reset and reseeded every run
  (`support/matchday-global-setup.ts`), so the journey's played round never touches the shared world the other
  journeys use. `npm run test:e2e:matchday` runs it on its own.
- **The journey** (`tests/web-e2e/matchday/matchday.spec.ts`): onboard, prepare a side through the prepare
  screen, ask the trigger to play the round, wait for the worker to publish it, then watch the replay —
  asserting the scoreline the server published, the commentary timeline, the statistics table, and, when a
  highlight exists, that Play starts the animation (master plan §16, §15.5 journey 3).
- **The viewer now starts its replay from the presentation it was given.** The component built its playback
  once with an empty highlight list and never rebuilt it when the presentation loaded, so every control
  rendered and the Play button did nothing. The state machine and the renderer were each tested on their own
  and the seam between them was not; a load now rebuilds the player exactly once, and a component test pins
  it so the next omission fails a fast test rather than a browser run.
- 4 new API integration tests (81 total) for the trigger: it enqueues a round's lock and resolution rows and
  only those; a repeat inserts nothing; an unknown round answers `MATCHDAY_NOT_FOUND`; and the endpoint is
  `404` when the flag is off. 1 new frontend test (187 total) for the viewer's replay seam. 1 new Playwright
  journey (25 across both stacks).

#### Notes

- **The compressed clock was the wrong tool for this journey, and ADR-0015 is untouched.** Compression
  multiplies the real gap between its anchor and the moment the test acts — thirty to sixty seconds of API and
  dev-server boot — by the rate, so at a season-compressing rate the round publishes before the manager
  prepares, or the season ends mid-suite. ADR-0016 records the trigger instead; the clock remains the mechanism
  for a human watching a season and for the rollover journey.
- **The trigger does nothing the scheduler does not.** It is the same materialisation, off the worker only for
  a non-production diagnostic, and the queue's unique business key makes a repeated call free. That is what
  keeps the journey honest: the API enqueues, and the worker — the only thing that may advance a matchday —
  executes.
- **The worker's `webServer` entry has no `url` and no `port`, deliberately.** Playwright waits for a
  readiness it can observe only when it is given one; with neither it starts the process and moves on, and the
  journey polls for the published result rather than assuming a ready worker.
- **The matchday stack shares the main stack's ports.** The two suites never run at once — separate CI steps,
  one at a time locally — and sharing the ports keeps the dev-server proxy and CORS settings valid. The cost is
  that a `npm run dev` stack must be stopped first; `reuseExistingServer: false` makes that a loud failure
  rather than a silent test of the wrong database.
- **The matchday journey does not give its club back.** The shared-world journeys resign at the end so the
  108-club pool is not exhausted; this one owns its whole world and discards it with the run.

## Stage 6 — Season schedule, fixtures, and the matchday worker

A season you can see the shape of. Seeding the world now generates each division's full fixture list: the
nine matches of round one through the nine of round thirty-four, played on Tuesdays, Thursdays, and
Sundays, with the team-sheet lock derived from each kickoff. The first milestone of the stage is the
calendar and the two tables it lives in; the lock-and-snapshot workflow, the durable matchday worker, the
staged simulation, and atomic publication follow.

### Added

- **`competition.matchdays` and `competition.fixtures`.** A matchday is one round of one division's season
  — the nine fixtures that lock, resolve, and publish as a unit (`CAL-10`, `MAT-7`) — and a fixture is one
  match with the score it publishes. The database carries the rules rather than a convention: the round
  number is bounded to 34 (`CAL-1`), the lock must precede the kickoff it is derived from (`CAL-3`), a club
  cannot play itself, a score together with its match exists *exactly* in the `staged` and `published`
  states so a half-resolved fixture is impossible, and `published_at` agrees with the published status. A
  club appears once per matchday as host and once as visitor through two matchday-scoped unique indexes,
  and the round number is unique per division-season.
- **The `Matchday` and `Fixture` aggregates with their lifecycles as behaviour.** Five fixture states —
  scheduled, locked, simulating, staged, published — plus `void` for an operator removal (`MAT-10`). Every
  transition guards itself and every repeat of a transition that has already happened is a no-op, because
  the workflow that drives them runs from a durable job that may be retried at any boundary (§7.4,
  ADR-0003). A matchday marks itself staged once every fixture has, and publishes only from staged.
- **`RoundRobinSchedule`**: the fixture list generator (`CAL-8`). The circle (Berger) method builds one
  single round-robin — every club meets every other exactly once, and plays exactly once a round — and the
  second half replays it with the venues swapped, which makes "each pair meets once home and once away" and
  a club's seventeen home and seventeen away fixtures true by construction rather than by a correction
  pass. The clubs are shuffled by a `Pcg32` stream seeded from the division-season's stored schedule seed,
  so the same seed reproduces the same list. Home and away are decided round by round, alternating a club's
  venue wherever the pairing allows; the second half is emitted in *reverse* round order, so the last
  first-half round and the first second-half round are the same pairing with opposite venues and a run
  never crosses the halfway point.
- **`ScheduleValidator`**: the `CAL-9` properties checked by name before a schedule is written — round
  count, one fixture per club per round, each ordered pairing once, each pair reciprocated, equal home and
  away counts, and a bounded run of games at one venue. The generator is built so they hold; this is what
  turns "by construction" into evidence, and a malformed schedule fails generation instead of becoming a
  season that quietly cannot be played correctly.
- **The seeder generates the schedule.** Running `npm run seed` now creates 34 matchdays and 306 fixtures
  per division — 1,836 matches across the six countries — with each matchday's lock thirty minutes before
  its kickoff and every fixture still `scheduled`. The clubs are passed in identity-generation order, the
  only stable order there is: ids are UUIDv7 and differ per run, so the fixture list is keyed on that order
  plus the recorded seed.
- **`WorldRuleSet.MaxConsecutiveHomeOrAway`** (`CAL-9`), and the rule set is now `world-rules-v4`, as a
  stage's constants arrive with that stage (`RULE-1`). A world stamped with an earlier version keeps being
  read against it.
- 35 new domain tests (296 total): the schedule's properties across every plausible division size and
  fifty seeds, its determinism and seed-sensitivity, its refusals, and every fixture and matchday
  transition including the idempotent repeats; and 6 new infrastructure tests over real PostgreSQL,
  including two that force a malformed row and assert the check constraint by name (`MIG-7`).

### Notes

- **The run of games at one venue is four, not three, and the constant is fitted to the schedule rather
  than the schedule to the constant.** The first attempt alternated venues by round and position parity and
  left four-game runs; alternating on the previous round's venue and emitting the second half in reverse
  cut the maximum to three for small divisions but still produced four at 18 and 20 clubs. Four is a normal
  home stand in a real fixture list, so `CAL-9`'s "acceptable" is met at four, and the property tests
  demonstrate the bound holds rather than aspiring to it.
- **The second half is the first half reversed, and the reversal is doing real work.** Mirroring in the
  same order lets a run straddle the halfway point; mirroring in reverse order makes round 17 and round 18
  the same pairing at opposite venues, so the boundary always alternates and a run inside the first half
  maps to an identical run inside the second. The maximum run over the whole season is therefore the first
  half's, which is what makes the bound provable rather than measured.
- **A fixture's kickoff is denormalized from its matchday.** The fixture list and the countdown both read
  it, and a list query wants it on the fixture's own row. It is written once, from the matchday, and never
  moved — `CAL-11` forbids shifting a scheduled kickoff because simulation was late.
- **The `squad.fixture_team_sheets.fixture_id` foreign key is deliberately still absent.** Stage 4 shipped
  the shell without it, noting that "Stage 6 adds it", and Stage 6 does — but only once a write path
  produces team sheets for real fixtures, which is the prepare-match milestone. Adding it now would force
  an unrelated squad test to arrange a fixture in the same commit, which §17.15 forbids.
- **`WorldBootstrapGenerator.Version` records `world-gen-v3`.** The bootstrap now produces clubs, squads,
  *and* the fixture list, so the run that emits them names the version that covers all three (`FIC-8`,
  `PYR-14`).
- **The schedule generator versions itself separately** (`schedule-gen-v1`) and its version is folded into
  a world run's input hash, because changing how a fixture list is drawn is a reproducibility fact
  independent of how clubs or players are generated.
- **The database's share of `CAL-9` is smaller than the rule.** "One fixture per club per matchday" cannot
  be expressed across two columns, so the two matchday-scoped unique indexes catch a club hosting twice or
  visiting twice, and the full property — one match per club per round, each pair once each way — remains a
  generation-time check, exactly as master plan §6.4 allows ("generation validation plus database support
  where practical").
- **Deferred to the rest of Stage 6:** the fixture and matchday reads with the next-fixture dashboard and
  the prepare-match screen (both delivered in the next milestone, below), the durable job queue's full
  lock/snapshot workflow, the immutable snapshot and the seeded commitment hash, the staged simulation and
  atomic nine-fixture publication, the standings and statistics projections, and the compressed test clock.
  **Deferred beyond it:** rollover, lower-tier provisioning, and the match viewer.

### The fixture list and the prepare-match screen

A season you can read and a side you can prepare. `GET /fixtures/mine` answers the dashboard and the
fixtures screen with the club's thirty-four fixtures and the next one named, `GET /divisions/{id}/fixtures`
answers the division's whole calendar in thirty-four rounds, and the prepare-match screen reads one fixture
in full and saves a club's selection for it under the sheet's version. This is the second milestone of
Stage 6; the lock-and-snapshot workflow, the durable matchday worker, the staged simulation and atomic
publication, and the projections follow.

#### Added

- **The fixture reads** (master plan §10.5, §11.1): `GET /divisions/{divisionId}/fixtures` — a division's
  whole calendar, the 306 fixtures of a season grouped into the 34 matchdays that lock and publish as a unit
  (`CAL-10`), with its eighteen clubs carried once so the fixtures reference them by identity — plus
  `GET /fixtures/mine`, the manager's own club's season with `NextFixtureId` named, and
  `GET /fixtures/{fixtureId}`, one fixture in full with `ManagedClubId` and `ManagedSide` resolving the
  caller's end. Fixtures are public game data, so none of the three is gated on holding a club; what the
  tenure adds is which side is theirs.
- **A score is public only once its matchday has published** (`MAT-7`, `CAL-10`). A staged fixture's row
  already carries the score and the match — that is what staging means — but the mapping publishes both only
  from `published`, so a half-resolved round cannot leak one of its nine results through a read.
- **`IsLocked`, from the deadline and the status together** (`CAL-3`, `SQ-7`). A fixture's sheet is closed
  when its status has moved past `scheduled` *or* when its lock instant has passed, because the deadline is a
  rule and the status is bookkeeping: a delayed lock job must not leave a sheet editable a minute after it
  was due.
- **The fixture team sheet** (master plan §10.4): `GET /fixtures/{fixtureId}/team-sheet` — the opponent and
  the deadline, the plan the side is prepared from, all eighteen slots filled or empty, and the squad it may
  pick from, in one response — plus `PUT`, which replaces the whole selection. The club, the fixture, the
  plan version the sheet references, and the deadline all come from the server, and none of them is taken
  from the request (`INT-1`).
- **`FixtureTeamSheetValidator`** (`SQ-4`, `SQ-9`, `TRN-12`): one pure rule over the submitted selection and
  the club's selectable and unavailable players, refusing a slot number outside 1–18, a repeated slot, a
  repeated player, a player who is not selectable, an unavailable player, and a starting eleven that is not
  all picked. It is the same rule the Stage 8 snapshot builder will repair a locked sheet through
  (`INS-12`), and it is shared with the screen as a validation preview of stable codes rather than a field
  message.
- **The sheet's version is its strong entity tag** (`CONC-1`, ADR-0009). A replace requires `If-Match` —
  answered `428` without it — and a stale one is answered `412`, so a side prepared on one device cannot be
  silently overwritten on another. The first save for a fixture carries no tag, because there is nothing to
  be conditional against yet, and `squad.fixture_team_sheets.version` is now a concurrency token, so a raced
  save is refused by the database and not only by the use case's own comparison.
- **The two foreign keys Stage 4 shipped without** (ADR-0011): `squad.fixture_team_sheets.fixture_id` and
  `squad.player_unavailability.source_fixture_id` both now reference `competition.fixtures`, in one
  migration applied against real PostgreSQL 17. Stage 4 recorded that Stage 6 would add them once a write
  path produced a team sheet for a real fixture; the prepare-match screen is that write path. The second is
  nullable on purpose, because an absence can come from training rather than from a match (`TRN-12`).
- **The `/fixtures` screen and the prepare-match screen** (§11.1): the club's season split into what is still
  to play and what has been played, with the next fixture called out and a countdown to its deadline on a
  slow interval rather than a one-off render; and the prepare screen, where the eleven starting slots take
  their family and role from the club's plan and the bench is seven more places. Every slot is a labelled
  select rather than a drag, so the screen is reachable from a keyboard and a screen reader (§11.3), and a
  refused save lists the validator's issues against the slots they concern. When the fixture has locked the
  controls are disabled and the screen says so; when the club has no default plan it points at the tactics
  screen instead.
- **The dashboard's next-fixture card**, with the opponent, the kickoff, the deadline, and the countdown, and
  the `/fixtures` navigation destination flipped to available. A failure to read the fixtures leaves the rest
  of the dashboard intact rather than blanking it, because the card is a second read of a different resource.
- 9 new domain tests (305 total) for the team-sheet validator; 6 new API integration tests (68 total)
  covering the calendar and the club's list, the prepare screen's read, the create/replace lifecycle with its
  `428`/`412` refusals, the validation preview, and the other-club and no-club refusals; a new infrastructure
  test over real PostgreSQL for the foreign key by name (`MIG-7`), beside the team-sheet round trip, which
  now arranges a real fixture instead of a random one; 21 new frontend tests (145 total) over the
  presentation helpers and the store's concurrency contract; and 2 new Playwright journeys (20 total) —
  the prepare screen with its version conflict survived by reapplying, and the guard for a visitor with no
  session.

#### Notes

- **The idempotency of publication is enforced in the mapping, not in the query.** A staged row genuinely
  holds its score; what keeps it private is that `FixtureMapping.Visible` publishes the score, the match, and
  the outcome only from `published`. That is one place to read when the question is "can a manager see this
  yet", and it is the same predicate the Stage 7 viewer will need.
- **The reapply path had to read before it wrote, and the browser journey is what found it.** A `412`
  reloads the sheet asynchronously, and the first version of the store reapplied against whatever version it
  happened to hold — so a manager who clicked *Reapply my changes* the moment the conflict appeared re-sent
  the version that had just been refused and was refused again, with no way out but a page reload. Reapply
  now reads the sheet and conditions the save on what that read returns. The store's unit tests did not
  catch it because they ran the reload to completion first; the journey clicked when a person would.
- **A fixture's own row carries its kickoff as well as its matchday's.** It was written once from the
  matchday when the schedule was generated, and it is what the club's list reads; the matchday's own instant
  is still what a round-level read reports.
- **The season a read is measured against is the world's current one**, resolved once by
  `CurrentSeasonQuery`, so a read during the rollover window keeps answering for the season being played
  rather than for the next one whose rows already exist (`CAL-6`).
- **A replacement selection is a delete and an insert, not a set of edits.** Swapping two players between two
  slots cannot be expressed as single-slot updates without passing through a state that violates one of the
  sheet's unique indexes, so the use case removes the previous entries and adds the new ones and the two
  commit in one transaction. The API test that replaces a whole side with a different one is what proves the
  ordering is safe against real PostgreSQL.
- **The plan a sheet is described by is the sheet's own when one exists, and the club's default otherwise.**
  A stored selection is always described by the shape it was prepared against, and a fresh screen shows the
  shape a new save would use; a save rebases the sheet onto the current default's version, which is what
  `FixtureTeamSheet.Rebase` was added for in Stage 4.
- **The bench's size needs no rule of its own.** `SQ-4` asks for "up to seven substitutes" and nothing about
  which numbers they take, and slots 12–18 are exactly seven, so a set of distinct slot numbers cannot name
  more. The validator deliberately adds no contiguity rule: it would refuse a legal side for a reason the
  rules do not give.
- **`GET /divisions/{divisionId}/table` is not in this milestone.** §10.5 lists it, but the standings
  projection is the rest of Stage 6; a table read with nothing behind it would be an empty screen pretending
  to be a feature.
- **The match viewer, `GET /matches/{id}`, is likewise not here.** A fixture names its match once a result is
  staged, and nothing produces one yet: simulation and publication are the next milestone.
- **The fixtures screen polls nothing and the countdown is the only thing that moves.** A calendar changes
  once a matchday publishes, and that is a moment the manager is already on the screen for; a 30-second
  interval re-renders the countdown only.
- **Deferred to the rest of Stage 6:** the durable job queue's materialiser and lock/snapshot workflow, the
  immutable input snapshot with its seed commitment hash, the staged simulation and atomic nine-fixture
  publication, the standings and statistics projections, and the compressed test clock. **Deferred beyond
  it:** rollover, lower-tier provisioning, and the match viewer.

### The durable matchday worker, the frozen snapshot, and the table

A matchday that plays itself. The calendar's deadlines become durable jobs, each round freezes both clubs
into one immutable input before it kicks off, the engine simulates it from that input alone, and the nine
results become public together with the table they move. Three jobs, three business keys, and no way for a
client to influence any of it.

#### Added

- **The `match` module's four tables** (`MAT-1`, `MAT-9`, master plan §6.6): `match.input_snapshots` (one
  per fixture, immutable), `match.matches` (one per fixture, carrying both hashes and the versioned
  statistics document), `match.events` (the durable narrative, `unique (match_id, sequence)`), and
  `match.simulation_attempts` (every attempt, successful or not, `unique (fixture_id, attempt_number)`). The
  constraints carry the rules: a fixture is frozen once, simulated once, and an attempt number happens once,
  so at-least-once delivery cannot produce a second row of any of them.
- **`InputSnapshot`, `SimulatedMatch`, `SimulationAttempt`, and `MatchEvent`**: the match module's
  aggregates, each with a factory that validates and no mutator at all, because a frozen input and a played
  result are history (ADR-0014). `MatchEventType` mirrors the engine's event vocabulary by value, with the
  stable codes the domain needed and the engine did not, and two of them — "a second yellow is a sending-off,
  not a second yellow" and "a penalty goal is a goal" — are the definitions the table's card columns and the
  score reconciliation read.
- **`MatchSnapshotBuilder`**: a pure function from two prepared sides to one frozen input, with the
  deterministic repair `DIS-6` asks for. A club's sheet is honoured slot by slot; a slot it left empty, or
  filled with somebody who cannot play in it, is decided by position suitability, then condition, then
  ability, then the player's identity. The eleven must contain exactly one recognised goalkeeper, which is
  why a goalkeeper is never placed outfield and an outfield player is never placed in goal; the bench is
  filled to seven with at most one goalkeeper, so an untended club's match is a match. Every decision is
  recorded with its club, its slot, the player who was dropped, and why (`DIS-7`).
- **A club with no saved plan takes the field in the default formation with the neutral instructions.** The
  seed world still ships no tactical plans for its AI clubs, and this is what makes the game playable before
  a single manager has opened the tactics screen — and what `TeamInstructionSet.Neutral` exists for, since
  the enums' zero values are the extremes rather than the middle. The full AI lineup and tactic policy is
  Stage 8's, and it will replace this one selection rule without touching the workflow.
- **`MatchSnapshotDocument` and `MatchStatisticsDocument`** (`match-snapshot-v1`,
  `match-statistics-v1`): the stored documents, each with a schema discriminator that reading refuses to do
  without (§4.5). The snapshot document holds the engine's input *and* the repairs, because §6.6 requires
  both and the engine's contract has nowhere to put a repair; its attributes round-trip through the engine's
  own validating factory, so an edited document is refused rather than simulated.
- **`MatchSnapshotFactory`**: the one place a snapshot is built — content hash, seed derived by HMAC from
  `World:MatchSeedSecret`, commitment, document, input hash — and the one place it is read back.
  `ReadVerified` re-checks the seed, the commitment, and the input hash before returning the input, so a
  result can only be produced from a snapshot that is provably still what it was (`MAT-9`).
- **The three matchday jobs** (`competition.lock-matchday`, `competition.resolve-matchday`,
  `competition.publish-matchday`) with business keys derived from the matchday's identity, and
  `MatchdayScheduleScheduler`: the materialiser §7.2 calls `EnsureScheduleJobs`, which turns the calendar
  into work. It is worker-only, holds no deadline of its own, and re-derives everything from the clock on
  every pass, so a restart, a redeploy, and a scale-down all produce the same rows (ADR-0003).
- **`LockMatchday`**: one transaction and one matchday-scoped advisory lock for the whole round. It freezes
  each fixture's input, freezes the club's prepared sheet, and marks the fixture locked, so the round is
  either frozen or untouched — a half-locked round would leave some managers able to edit a side and others
  not, for no reason the rules give.
- **`ResolveMatchday`**: per-fixture transactions, so a worker killed after six of nine leaves six staged
  results that a retry finds and leaves alone. Each fixture is marked simulating before the engine is
  called, so a run that dies mid-simulation is visible as one that started. A fixture whose lock never ran
  has its snapshot taken here rather than being simulated from live tables (§7.3), and the fixture's lock
  state and its snapshot commit together. When all nine are staged, the round marks itself staged and
  enqueues publication in the same transaction.
- **`PublishMatchday`**: one serializable transaction that publishes the nine fixtures, rebuilds the
  division's table from its published results, and marks the round published. A round that is not fully
  staged publishes nothing and moves nothing (`MAT-7`), and the repair path for a table is the same code as
  the live one (`TBL-13`).
- **`StandingsCalculator` and `Standing`**: the ordering rules of `TBL-1`–`TBL-12` as one pure function
  over published results, plus the projection row the table is stored in. The last tie-break is the draw key
  derived from the seed the division recorded before the season (`TBL-10`, `TBL-11`), and a club identity is
  compared beyond it only so the order is total. Cards come from the match's events, which is why
  `TBL-8`/`TBL-9` need no second accumulator.
- **The seeded world opens with a table**: eighteen rows per division, at nil-nil, ordered by the same draw
  the season committed to, so a manager who signs in before the first ball is kicked sees a table rather
  than an empty screen — and sees the same order the first publication will keep.
- **`GET /divisions/{divisionId}/table`** (§10.5): the division's table for the season in progress, public
  game data like the calendar, in the order the projection stored, with the server's own instant so a client
  with a wrong clock cannot mislead a manager about when it was read (`TIME-5`).
- 24 new domain tests (329 total): the table's ordering rules with every criterion tested by a construction
  that leaves all the earlier ones level — including the head-to-head group rule and the case where goal
  difference and goals scored disagree — and the match aggregates' guards, codes, and immutability.
- 29 new application tests (52 total) for the snapshot builder and the documents: the repair order and each
  repair reason, the one-goalkeeper rule, the bench, determinism across builds, the refusal of a club with
  no goalkeeper, the round trip that reproduces the input hash, and the vocabulary mapping that keeps the
  two enums' values equal.
- 7 new infrastructure tests over a seeded world in their own container (106 total): the lock's freezing and
  its idempotency, the resolution's staging and re-run, a round interrupted mid-simulation resuming without
  a lost or duplicated result, a re-simulation from the stored snapshot reproducing the output hash, a
  partial round publishing nothing, and a published round moving the table to exactly the ranking the
  published results compute.
- 2 new API integration tests (70 total) for the table read, its public access, and its unknown-division
  refusal; and 1 new worker integration test (4 total) that runs a whole matchday through the real
  composition: the scheduler materialises it, the queue claims it, the handlers lock, simulate, and publish,
  and all three jobs reach a terminal state.

#### Notes

- **The lock and the resolution serialise on a matchday-scoped advisory lock, and the test that found it is
  the one that made both due at once.** They are thirty minutes apart in normal operation and never meet;
  they meet when the worker was down across both deadlines and comes back to two overdue jobs. The
  integration test arranged exactly that and the two raced: the resolver takes the snapshot itself when the
  lock job has not run, so both tried to freeze the same fixture and one died on the snapshot table's unique
  index. ADR-0003 names one division-matchday publication as a genuine singleton, so the fix was the lock
  the ADR already called for rather than a new mechanism.
- **The resolver's snapshot and the fixture's lock state commit together, and that is a fix to a bug the
  first version had.** A retried resolution that found an existing snapshot skipped the fixture's
  `Lock` transition, and a crash between the two writes would then have left a fixture that was still
  `scheduled` with a frozen input — which `Stage` refuses. The ensure step now locks the fixture whenever it
  is still scheduled, so the pair is written or neither is.
- **The bench's slot numbers were burning a shirt number per passed-over goalkeeper.** The first version
  iterated the free slot numbers from the same enumerator that decided whether to add a candidate, so
  skipping a second goalkeeper consumed a number without recording a repair — and the integration test's
  repair count came back as 34 or 35 per fixture instead of 36. The count is now asserted exactly, which is
  what caught it.
- **`MatchEventTypes.MaxCodeLength` was sixteen and `second_yellow_card` is eighteen.** The column was
  sized from the constant, so the first staging attempt failed with `value too long for type character
  varying(16)` — a reminder that a "longest code" constant is a claim about the data and belongs in a test
  that walks the enum, which `Every_stored_code_round_trips` does.
- **The standings are rebuilt rather than incremented, and the publication reads its own writes.** The
  transaction publishes the nine fixtures, commits, reads the division's published results back, and ranks
  them from scratch, so the projection cannot drift from the fixtures it summarises. It costs a few
  milliseconds three times a week.
- **A snapshot's repairs carry their club.** The first version did not, and a repair list for one fixture
  holding two sides is ambiguous at slot level: "slot 5 was repaired" names two different players. The
  document carries the club for the same reason, and so does the inbox item `DIS-7` will eventually send.
- **A club with no available goalkeeper refuses its round by name.** Locking is all-or-nothing, the job
  dead-letters with the club's identity, and the round does not happen until an operator repairs it. That is
  deliberate: a forfeit would decide a competitive outcome by a rule the game does not have, and Stage 5
  already recorded that the engine has no mechanism for a makeshift keeper.
- **Nothing here is reachable from a command.** There is no endpoint that locks, simulates, or publishes:
  the three use cases are driven by jobs, and the only public surface this milestone adds is a read
  (`MAT-2`).
- **Deferred to the rest of Stage 6:** the compressed test clock, and the web table screen that reads the
  new endpoint. **Deferred to other stages, with reasons:** player and club season statistics (the engine's
  player line has no assists and no rating, so the projection would publish columns that could never be
  filled — Stage 7 completes that contract), discipline records and suspensions, injuries, condition and
  morale (all need the engine to return state deltas or a rule that decides them, which is Stage 8's subject
  — ADR-0012 made the same call for training injuries), gate receipts (Stage 9 owns the ledger), the inbox
  (`DIS-7`'s report needs the inbox to exist), the highlight table (Stage 7's viewer is its first consumer),
  and rollover, lower-tier provisioning, and the match viewer.

### The division table on screen

A table you can read. `/competitions` shows the manager's own division — eighteen clubs in the order the
server ranked them, with their own row marked — and `/competitions/{divisionId}/table` reads any division
by identity, which is what a shared link uses. This is the read the Stage 6 table endpoint was waiting
for; the compressed test clock is all that remains of the stage.

#### Added

- **The `DivisionTable` transport shapes** (`competition.models.ts`): `DivisionTableRow` — rank, club
  identity, played/won/drawn/lost, goals for and against, goal difference, points, and the two card counts
  — and `DivisionTable`, which carries the division, the country and tier it sits in, the season, the rows,
  and the instant the server answered (`TIME-5`). The mirror of `TableResponses.cs`, like the rest of the
  module's shapes.
- **`CompetitionApi.divisionTable`**, the read of `GET /divisions/{divisionId}/table` (§10.5).
- **The two table reads in `CompetitionStore`.** `loadMyDivisionTable` reads the manager's own division;
  `loadDivisionTable` reads a named one. Both hold one loading state and one error and blank the previous
  table first, so a failed read cannot leave the last division's rows on screen.
- **`ManagedClubId` on the store**, named by the manager's own read. The club's division is on no
  competition read, so it is taken from the fixture list the club already has — the same read the dashboard
  makes for the next fixture — and that read also names the club. The named-division read deliberately
  marks nothing, because the caller may hold a club elsewhere or none at all.
- **`goalDifferenceLabel`** (`TBL-3`): a difference is signed as text — `+7`, `-3`, `0` — so the column is
  read rather than tinted (§11.3).
- **The `/competitions` screen** (master plan §11.1) and the route `/competitions/:divisionId/table`. The
  table is a real `<table>` with an `sr-only` caption, `scope="col"` headers whose abbreviations carry
  their full words, and a `scope="row"` header per club, so assistive technology reads it as tabular data.
  The manager's own row is marked with words ("your club") as well as weight, never by a colour alone. The
  rows arrive already ranked, so the screen sorts nothing and reimplements no tie-break rule (`TBL-11`). A
  division whose matchdays are all still to play says so, because the seeded table is eighteen nil-nils and
  that is a fact about the season rather than an empty screen.
- **The `Competitions` navigation destination** flipped to available, so the shell links to the table.
- 5 new web unit tests (150 total) over `goalDifferenceLabel` and the two table reads — the own-division
  read naming the club, the named read marking nothing, the refusal, and the clear — and a new Playwright
  journey file (22 total): the table reached from the navigation with the manager's row marked, and the
  guard for a visitor with no session.

#### Notes

- **The manager's division is resolved from their fixtures, and that is a gap in the reads rather than a
  choice.** `GET /fixtures/mine` is the only read that names a club's division; the tenure and the
  onboarding state do not carry it. Reusing it costs one round trip on a screen that is opened rarely, and
  it is the same read the dashboard already makes for the next fixture. A dedicated "my division" read
  belongs with the country and division browser a later navigation will need.
- **Nothing is sorted in the client.** The projection stored the order and the server ranked it
  (`TBL-1`…`TBL-11`), so a screen that re-sorted would be a second, drifting definition of the table.
- **The card columns are carried but not shown.** They arrive because `TBL-8`/`TBL-9` break ties by them
  and the response is one document, but the discipline view that gives them a column is Stage 8's.
- **A wide table on a narrow screen scrolls rather than collapsing.** Ten columns is what a league table
  is, and hiding half of them on a phone is the "squeezed desktop table" §11.3 warns about; the wrapper
  scrolls horizontally and the club column stays the row's subject.
- **The named route is declared before the bare one.** `/competitions/:divisionId/table` and
  `/competitions` share one component, and the longer path is listed first so the two-segment navigation
  destination and the three-segment deep link cannot be confused for one another.
- **Deferred to the rest of Stage 6:** the compressed test clock. **Deferred beyond it:** rollover,
  lower-tier provisioning, and the match viewer.

### The compressed test clock

A season you can watch without waiting for one. A non-production environment may compress the clock the
season is read against, so the matchdays that are days apart arrive in minutes and the worker's own
deadlines drive the acceleration. Real time is the default, it is off unless configured, and a production
host refuses to start with it on (`TIME-6`).

#### Added

- **`ClockOptions` and `CompressedClock`** (ADR-0015, `TIME-6`): game time is
  `VirtualAnchorUtc + (realNow − RealAnchorUtc) × Rate`. Both anchors are configuration rather than process
  state, which is the point — the API computes "server now" and the worker computes "is this job due" from
  the same real instant and the same settings, so the two cannot disagree about whether a deadline has
  passed. With no virtual anchor the mapping is a pure speed-up; setting one also offsets the world so a
  season can be pinned just before its first kickoff. The arithmetic is clamped to a thousand years of game
  time so an over-long run degrades rather than overflowing, and the clock is built from validated options,
  so a rate or an anchor that cannot mean anything fails when it is resolved.
- **`AddGameClock`**, the choice at composition (ADR-0015): the API, the worker, and the world seeder call
  it, and it replaces the real clock only when the configuration asks for compression *and* the environment
  is not Production. A production host that asks for it throws by name instead of silently running at real
  speed — an operator who believed a season was accelerated is exactly the accident the rule prevents. Both
  roots log a warning when a compressed clock is in force.
- **The clock configuration is validated at startup**, like the world and auth options: `Rate` between 2 and
  100,000 and a required `RealAnchorUtc` when `Mode` is `Compressed`, so a half-written compressed
  configuration fails immediately in every host rather than at the first job.
- 10 new infrastructure tests (116 total) for the clock: the scale and both anchors, the default virtual
  anchor, the UTC contract, the bounded extreme, the two constructor refusals, and — the safety property —
  that Production cannot build a compressed clock while a non-production environment gets one and the
  default is left untouched.

#### Notes

- **There is no HTTP control surface.** Enabling compression is a configuration change and a restart; no
  endpoint moves time. A runtime-writable "now" is a deadline-critical write surface §10 does not define,
  and §17.12 keeps unreachable features out of the API rather than half-built (ADR-0015).
- **Nothing about a played match depends on the clock.** A seed comes from the world secret and the frozen
  snapshot and the engine reads no time at all (`MAT-9`), so a compressed clock changes *when* a deadline is
  reached and never *what happens* when it is. That is what makes it safe to accelerate a world and still
  expect reproducible results.
- **A browser's countdowns are real time, so on a compressed world they disagree with the server's clock.**
  The server's `IsLocked` answer is authoritative and the client is refused after the lock, so the control
  state is right; only the countdown phrase misleads. Closing the gap means the fixture and team-sheet reads
  carrying a server instant for the client to measure against — the `TIME-5` work those reads do not yet do —
  and it is recorded as deferred rather than half-built (ADR-0015).
- **The clock was already the only source of time.** The queue reads `IClock` for every enqueue, claim,
  completion, and retry delay, and both schedulers read it for their horizons, so no component needed to be
  taught about compression — the clock is the only thing that changed (`TIME-2`).
- **A compressed world cannot be re-anchored without a restart**, and all countries share the one accelerated
  cadence. Both are accepted: it is a test tool whose natural lifetime is a run, and the MVP already gives
  every country one season cadence (§3.4).

## Stage 5 — The pure match engine

A match you can replay. `MatchSimulator.Simulate` takes one frozen snapshot and returns one result, and the
same snapshot always returns the same result down to the byte — not because the engine is careful, but
because there is nothing in it that could vary. It reads no clock, database, network, filesystem, culture,
or `Random.Shared`, and it names itself in the result so a scoreline can always be explained by the rules
that produced it. Version 1 is `engine-v1` / `engine-rules-v1`, and its formulas, constants, and measured
distributions are specified in [`docs/product/match-engine.md`](docs/product/match-engine.md).

### Added

- **The engine's own `Pcg32`** (`Randomness/Pcg32.cs`), a pinned XSH-RR implementation with its golden
  sequence asserted, plus `MatchSeed`, which derives the secret seed by HMAC-SHA256 over the world secret,
  the fixture, the snapshot's content hash, and the engine version (`MAT-9`, master plan §8.2). The content
  hash deliberately excludes the seed, because hashing a seed into its own derivation is circular; the
  full input hash — facts plus seed — is what gets stored. `CommitmentOf` produces the publishable
  commitment, so the raw seed can stay protected and still be verified later (`MAT-10`, `MAT-11`).
- **`EngineRulesV1`**: every tunable the engine has, as one validated, versioned, immutable record. Its
  canonical description is derived by reflection over its own properties rather than written out by hand,
  so a new constant cannot be silently omitted from the hash a result records — the failure mode of a
  hand-maintained list is that two genuinely different configurations claim the same provenance.
  `EngineConfiguration.HashOf` is what a snapshot is frozen against, and the engine refuses to simulate a
  snapshot whose configuration hash does not match the rules supplied.
- **The input and output contracts** (master plan §8.3): `MatchInputV1` with `MatchSideV1`,
  `MatchParticipantV1`, `MatchSlotV1`, and `MatchInstructionsV1`; `EngineEventV1` as a flat, ordered,
  fact-only event; `MatchResultV1` with statistics, player lines, and both hashes. `Validate` refuses a
  snapshot by name — a lineup of ten, a slot at an off-pitch or duplicated coordinate, a role that
  disagrees with its family, a side fielding two recognised goalkeepers, a participant from another club,
  and a dozen more — because a malformed snapshot that simulated anyway would produce a plausible result
  indistinguishable from a real one.
- **`CanonicalMatchSerializer`**, and with it the three digests: content (the facts, seed excluded), input
  (the facts and the seed), and output (the result, bound to its input hash). Collections are sorted,
  numbers are formatted invariantly, and every field is labelled on its own line, so a value cannot change
  position unnoticed and two adjacent numbers cannot be read as one.
- **The rating model** (master plan §8.4): `UnitRatingWeights` with a versioned table per unit that checks
  itself the first time it is read; `UnitRatingCalculator` over nine units; `TacticalModifiers`; and
  `LineupResolver`, which resolves each slot's occupant and their role familiarity once at kickoff.
  `INS-10`'s out-of-position penalty is a single definition, so a makeshift side is priced consistently by
  the ratings and by cohesion.
- **The simulation**: a possession-based model in a documented phase order — the defending side's foul,
  then progression out of build-up, then creation, then the chance. Goals come only out of resolved chances
  (`MAT-4`), the shooter is drawn by the attribute the chance asks for, and the penalty taker is the best
  finisher on the pitch rather than a draw. `DisciplineSimulator`, `InjurySimulator`, and
  `SubstitutionPlanner` cover the rest, and `MatchResultBuilder` derives every statistic from the event
  stream so `MAT-5`'s reconciliation holds by construction.
- **Commentary tokens** (`commentary-v1`): a template key, the facts, a variant key, and the English text.
  The key and parameters are the durable part, which is what makes the same match narratable in another
  language later without re-simulating it. Three variants per template are chosen by event sequence, so a
  long match does not read as one sentence repeated and a change to a sentence cannot change a result.
- **Semantic highlights** (`highlights-v1`): every goal and every penalty always shown, then the best
  chances by the goal probability they were resolved against. 22 player entities plus the ball, one track
  per entity holding normalized keyframes, a 5–8 second duration, and an accessible narration. Two caps —
  a count and a 750 KB payload budget — shed the lowest-quality highlights first and never a goal.
- **`docs/product/match-engine.md`**: the executable specification for version 1 — the two arithmetic
  scales, the draw-order contract, every formula, every constant with its value, the measured
  distributions, and a map of which test suite pins what.
- **ADR-0013**, on integer basis-point arithmetic and the bounded scoreline effect.
- **The simulation laboratory** (`tools/simulation-benchmarks`), which was a `Hello, World!` stub: one
  match with its hashes, a distribution table over any number of matches with target bands, and a timing
  run reporting p50/p95/p99, allocation per match, throughput, and the hardware it ran on.
- 521 engine tests, up from zero, covering the pinned PRNG sequence, all 10,935 instruction combinations,
  the golden output hash, twenty named input refusals, the statistical distributions, and the engine's
  purity by reflection.

### Notes

- **There is no floating-point number anywhere in the engine** (ADR-0013). A rating scale of 0–100 — one
  attribute point is five units — and a basis-point scale of 0–10,000 carry every formula, and two
  probabilities compose exactly as `a * b / 10_000` in integer arithmetic. Floating-point is reproducible
  on one binary, but .NET makes no cross-platform guarantee for the transcendental functions, and an
  engine whose value is that a result is re-derivable anywhere cannot rest on that.
- **The score distribution needed a mechanism, not a coefficient.** With independent goals the model is
  close to Poisson, and the measurements said so: a mean of 3.15 and **4.2%** of matches with seven or more
  goals, against football's roughly 2.5%. Lowering the mean to 2.84 fixed most of it; a bounded,
  score-derived creation modifier — a side three goals up stops chasing a fourth — fixed the rest, and the
  measured tail is now 2.59%. `GameStateModifier` is a pure function of the score that consumes no draw, so
  it cannot drift, and it is capped so a rout stays a rout.
- **The bounded modifier alone was not enough, and the first attempt at it barely moved anything.** It
  shifted creation between the sides without reducing the total, which is what the measurement showed the
  moment it was run. The honest summary is that the mean sets the tail and the modifier shapes it; both
  were needed and the proportions were measured rather than reasoned about.
- **A shot event carries the goal probability it was resolved against.** Without a quality signal a save
  from three yards and a save from thirty are identical in the event stream, and "notable saves above a
  configured threshold" is not expressible. It is a fact about a shot, derived from attributes the owning
  manager can already see — emphatically not a hidden player value — but it is also not something a
  player-facing response may carry (`MAT-11`), so the commentary tests hold an allowlist of parameter names
  and the contracts assembly keeps its data-classification guard.
- **The validation caught two real bugs while the engine was being built.** Home advantage was written as
  1,030 basis points rather than 10,300 — a 3% multiplier entered as a 0.103 one — and a subtractive
  cohesion penalty was filed among the multipliers. Both were refused at startup by the rules' own
  validator, which is the argument for validating a configuration rather than trusting it.
- **`FoulShareOfTurnoverBasisPoints` and `AggressiveTacklingCardMultiplierBasisPoints` were removed or
  wired up before the stage closed.** The first was superseded by rolling fouls independently of
  progression and nothing read it; the second existed but the booking chance was using the *foul*
  multiplier. Bookings now scale separately from fouls, because committing more fouls is not the same as
  committing worse ones, and the aggressive setting is a genuine disciplinary risk rather than merely a
  busier one.
- **The engine defines its own vocabulary rather than reusing the domain's enums.** The numeric values
  mirror `AttributeName`, `PlayerPosition`, `PlayerRole`, and the eight instructions deliberately, and the
  application layer will map between them by value — but the engine may not depend on `Domain` (`DEP-2`,
  ADR-0004), because a generated world's reproducibility and a played match's reproducibility are separate
  versioned contracts.
- **A side whose goalkeeper is sent off has no player in the Goalkeeping unit**, so every shot against them
  is close to a formality. That is the correct shape rather than a gap: `MAT-6` has no mechanism for naming
  a new goalkeeper mid-match, and a side that loses theirs is in trouble.
- **The most aggressive possible instructions are tested over every seed**, because the degraded paths are
  the ones nobody exercises: aggressive tackling maximises sendings-off, a high press maximises injuries,
  and a side can end up with a goalkeeper outfield and a rating built from an empty band.
- **Performance is not a problem yet, and the budget is recorded rather than targeted.** p95 is 3.5 ms per
  match against a 100 ms budget, at ~2.1 MB allocated and ~834 matches a second single-threaded, so a
  nine-fixture division matchday is about 11 ms of simulation. Throughput will come from running
  independent fixtures concurrently; one match is always single-threaded (ADR-0004).
- **Deferred to Stage 6:** the fixture calendar, the lock and snapshot workflow, atomic nine-fixture
  publication, and the matchday worker — everything that turns this library into a season. Nothing in
  Stage 6 should need an engine change, which is the point of having built it as a pure library first.

## Stage 4 — Squads, contracts, tactics, and training foundations

A world you inherit a squad from. Every seeded club now owns a legal twenty-two-player senior squad,
generated from the same world seed as its identity, so a manager who takes a club over inherits players
rather than a name. This is the first of the stage's milestones: the schema and the generation the rest
of the stage writes and reads.

### Added

- **The `squad` schema** (shipped first, ahead of the rest of the stage): `players`, `player_attributes`,
  `player_state`, `player_contracts`, `player_registrations`, `player_unavailability`, `tactical_plans`,
  `tactical_slots`, `training_plans`, `player_training_focus`, `fixture_team_sheets`, and
  `team_sheet_entries`, in one migration applied against real PostgreSQL 17 before being committed. The
  range checks (`TRN-4` on all twenty-eight attributes, `TRN-5`…`TRN-7` on state, `TAC-9` on slot
  coordinates), the partial unique indexes (`SQ-6` one active contract and one active registration per
  player, `INS-11` one default plan per club), and the slot and team-sheet uniqueness constraints are all
  in the database rather than in a convention.
- **The player aggregates with the rules as behaviour**: `Player` (identity, physique, positions, and
  the two hidden C2 values), `PlayerAttributes` over a twenty-eight-attribute set with a canonical order
  and a checksum that makes an out-of-band edit visible, `PlayerState` (condition, fatigue, morale,
  sharpness in basis points, and the carried development remainder), `PlayerContract` (a 1–3 season term,
  `CON-1`), `PlayerRegistration` (eligibility from a fixture boundary, `SQ-7`), `PlayerUnavailability`
  (measured in fixtures, not days, `TRN-12`), and the tactics and training rows the later milestones
  write into: `TacticalPlan` with the six presets and the eight instructions, `TacticalSlot`,
  `FixtureTeamSheet`, `TeamSheetEntry`, `TrainingPlan`, `PlayerTrainingFocus`, and `SquadLegality`.
- **Deterministic squad generation.** `PlayerGenerator` and versioned `PlayerNamePools` and
  `PlayerAttributeProfiles`: three goalkeepers, seven defenders, seven midfielders, and five attackers
  per club, an age spread, per-position attribute emphasis, state, a wage, and one active contract and
  registration each. Every value is a pure function of the seed, the club's ordinal within its country,
  its tier, and the season's game year — keyed on the ordinal rather than the club id, because ids are
  UUIDv7 and differ per run while the logical squad must not.
- **Names that cannot duplicate themselves inside a squad**, by construction rather than by retry: the
  given-name and surname pools are coprime and larger than a squad, so a club's consecutive ordinals
  visit distinct pairs — the same argument the club name pools already make. A name that collides with
  the fictional-data blocklist advances deterministically to the next candidate for the same seed
  (`FIC-6`), which today's pools do not trigger.
- **The seeder generates the squads.** Running `npm run seed` now creates 2,376 players for the 108
  clubs, and `world.generation_runs` records the count alongside the clubs and accounts (`SQ-1`).
- 59 new domain tests (234 total) covering generation determinism, the golden squad, squad legality and
  composition, the attribute, state, contract, position, and tactics invariants, and the name pools'
  blocklist and injectivity; and 14 new infrastructure tests over real PostgreSQL covering the squad
  constraints, the team-sheet round trip, and the seeded squads.
- **ADR-0011**, on hidden player values as server-only columns rather than a restricted table, and on
  contract/registration agreement as an application invariant rather than a database constraint.
- **The squad, player, and contract reads** (second milestone; master plan §10.3): `GET
  /clubs/{clubId}/squad`, `GET /players/{playerId}`, and `GET /contracts`, each answering only for the
  club the caller actually holds. The squad response carries the state and contract each row needs and a
  legality summary, so the screen can warn about a squad below the minimum without recomputing `SQ-2`.
- **The ownership refusal, by name** (master plan §10.9, §15.4): `CLUB_NOT_MANAGED` for another club and
  `NO_CLUB` for an account that holds none, because "your view is stale" and "you have nothing yet" send a
  manager to two different screens and one forbidden response would leave the client guessing.
- **The `/squad` and `/players/:id` screens.** The squad table is PrimeNG's, the app's first PrimeNG
  component, lazy-loaded on its route; sorting is the table's own and announces its direction through
  `aria-sort`, and filtering is client-side over a response already bounded to 25. The player profile
  renders all twenty-eight attributes grouped by family beside the state, contract, and registration.
- **Attribute display with non-colour indicators** (F-17, master plan §11.3): an `AttributeValue`
  component renders every attribute as its number *and* the word for its band, and state values are shown
  on the 0–100 scale the API converts to (`TRN-8`). A unit test asserts both halves render, so a band that
  only changed the colour would fail.
- **The C2 guard `data-classification.md` §2.1 asks for**, now that a player response exists: a
  reflection test over the contracts assembly that fails if a squad DTO ever grows a `Potential` or
  `Reputation`, or exposes a basis-point field, plus an API test that reads the player payload as raw JSON
  and asserts neither hidden value crossed the wire.
- 8 new API integration tests (48 total), 3 data-classification tests, 20 new frontend tests (77 total),
  and 2 Playwright journeys (14 total) covering the squad screens and their refusals.

### Notes

- **The squad constants went into `WorldRuleSet`, and its version is now `world-rules-v2`.** `RULE-1`
  asks for one versioned rule set, and the file's own documentation says a stage's constants arrive with
  that stage. A world already stamped `world-rules-v1` keeps being read against v1, which is the
  versioning model working rather than a migration.
- **A `GenerationRun` records `world-gen-v2`.** The bootstrap now produces clubs *and* squads, so the
  version it records is the bootstrap's, and the sub-generators' versions are folded into the input hash.
  The Stage 3 test that pinned the club generator's version moved with it.
- **Shortlists are not here.** `modules.md` gives `market.shortlists` to the market module, and search
  and shortlisting land in Stage 10, so the squad schema is twelve tables rather than thirteen.
- **The team-sheet tables ship before fixtures do.** `competition.fixtures` arrives in Stage 6, so
  `fixture_team_sheets.fixture_id` and `player_unavailability.source_fixture_id` carry no foreign key
  yet. Stage 3 set the precedent by shipping the competition and finance shells with their stage, and the
  fixture-independent lineup work needs somewhere to land. ADR-0011 records it.
- **`training_plans` is one current row per club**, updated in place with a bumped version. The
  data-model phrase "partial unique … latest plan wins" is read as "one row per club", matching the
  entity definition, which has no supersede column; plan history is a Stage 12 concern.
- **No club is given a tactical or training plan yet.** A default plan appears when a manager first sets
  one (the tactics milestone) or when the AI does (Stage 8); Stage 12 requires "a default tactic" only
  when it rolls seasons.
- **The player generator reuses `Pcg32` and `DeterministicDigest` rather than a third copy.** The FNV-1a
  seeding that was private to `ClubIdentityGenerator` moved onto `DeterministicDigest` as `SeedOf`, and
  the Stage 3 golden name tests are what prove the extraction changed nothing.
- **The C2 test landed with the squad reads, as ADR-0011 predicted.** It cannot pass vacuously — a third
  assertion fails if the squad contracts are renamed or made internal — and it is scoped to the squad
  namespace rather than the whole assembly, because a club's public reputation is a legitimate
  `Reputation` property on a world DTO and is C0.
- **The squad reads are own-club only.** The squad list carries condition and the contract list carries
  wages, both C1 ("readable where the viewer is authorized — for example own club"), and §10.9 asks
  resource policies to enforce club ownership. The public, unattached player profile is Stage 10's
  scouting surface (`SCT-1`), which is why `GET /players/{playerId}` is scoped to the owner here.
- **State comes back on the user-facing scale, not in basis points** (`TRN-8`: the API converts; the
  database is authoritative). The conversion lives once, in the application mapper, and the response has
  no basis-point field — a rule the data-classification test now enforces across every squad DTO.
- **A squad row does not carry the attribute grid.** The table is about readiness — who is available,
  tired, or expiring — and the grid is the player profile's job. Attribute-based sorting is the Stage 10
  search surface, which will need indexed queries anyway.
- **The squad table needed PrimeNG's template-reference API, not `pTemplate`.** PrimeNG 22 reads its
  slots through `contentChild('header')` and friends, so an `ng-template pTemplate="header"` compiles and
  then renders nothing — a silent empty table. The header, body, and empty-message templates are declared
  as `#header`, `#body`, and `#emptymessage`. Worth knowing before the next dense screen.
- **The initial bundle budget moved from 500 kB to 550 kB.** Using PrimeNG puts its table styling into the
  global stylesheet, which is part of the initial payload; the table's JavaScript stays in the lazy
  `/squad` chunk. The initial total is 524 kB (123 kB transferred), so the new threshold is a deliberate
  26 kB of headroom rather than a blanket relaxation, and `maximumError` is unchanged at 1 MB.
- **`GET /clubs/{clubId}` is deliberately not in this milestone.** §10.3 lists it, but no screen needs it:
  the dashboard already composes the club, country, division, season, and finances.
- **A world seeded by an earlier generator has no players, and the seeder will not add any.** It is
  idempotent, so it reports the world and stops. An environment carrying a `world-gen-v1` world needs a
  fresh database (or a reset) before the squad screens have anything to show; the end-to-end suite in this
  change was verified against a freshly seeded one.
- **Deferred to the rest of Stage 4:** the tactics presets, slots, validator, and ETag contract (delivered
  in the next milestone, below); the training endpoints and the deterministic daily progression job behind
  a feature flag; and the contract renewal quote. **Deferred beyond it:** fixtures, match effects, full
  finances, transfers, and the public scouting surface.

### Tactics

A club you can shape. A manager now saves a formation, a slot layout, the roles, the eight team
instructions, and the default eleven, on a plan whose `version` is the contract that stops two devices
overwriting each other. This is the second of the stage's milestones: the model, the validator, and the
API. The `/tactics` screen — the drag-and-drop board and its keyboard alternative — is the rest of it.

#### Added

- **The formation presets as data** (`TAC-1`…`TAC-6`): `FormationLayouts` holds each preset's eleven slots
  — family, role, and normalized coordinates — and checks its own table the first time it is used, so a
  preset that names not-eleven slots, repeats a number, puts a role in the wrong family, or strays off the
  pitch fails by name rather than deep inside a save. Slot 1 is the goalkeeper in every preset.
- **The tactics validator** (`TAC-7`…`TAC-10`, `INS-10`, `INS-12`): one pure function over plain slot
  values and two sets of facts — who is selectable and who is unavailable. It refuses the wrong slot
  count, duplicate slot numbers, coordinates off the pitch, two slots on the same point, a role that
  disagrees with its family, a repeated player, a player who is not a selectable member of the club, an
  unavailable player, and a half-filled lineup. An out-of-position player is deliberately **not** an
  issue: `INS-10` makes familiarity a penalty the engine applies, not a reason to refuse a side.
- **The tactics API** (master plan §10.4): `GET /tactics` — the club's plans, the squad they are picked
  from, and every preset's own arrangement — plus `POST /tactics`, `PUT /tactics/{planId}`, and
  `POST /tactics/{planId}/make-default`. The first plan a club saves becomes its default (`INS-11`), and
  making one default demotes the previous one in a single transaction with the demotion committed first,
  because the partial unique index is checked per statement.
- **The ETag contract** (`CONC-1`, ADR-0009): a plan's `version` is its strong entity tag. `GET` returns
  it in the body, both writes require it in `If-Match` (answered `428` without it), and a stale one is
  answered `412`. `squad.tactical_plans.version` is now a concurrency token, so a raced save is refused by
  the database and not only by the use case's own comparison — an empty migration records the token in the
  model snapshot.
- **The validation preview**: a refused plan answers `400 PLAN_VALIDATION_FAILED` with the issues as
  stable codes, each naming its slot and player, so the screen can draw them on the pitch rather than
  render a field message.
- 15 new domain tests (249 total) for the preset layouts and every validator rule; 2 new infrastructure
  tests (89 total), one of which is the only place the concurrency token itself can be observed; and 9 new
  API integration tests (57 total) covering the create/revise/default lifecycle, the `412`/`428`/`404`
  refusals, and the validation preview.

#### Notes

- **The preset layouts are the server's, not the client's.** `GET /tactics` returns every preset's default
  arrangement, so the screen renders a formation without reproducing eighteen coordinates, and the same
  numbers reach the renderer, the input snapshot hash, and — in Stage 5 — the engine (`TAC-9`).
- **Tactical zones are not modelled yet.** `TAC-7` speaks of dragging "within validated tactical zones";
  this milestone enforces the bounds and the unambiguous half of the overlap rule — two slots on one point
  — but there is no zone map, so a dragged slot is not constrained to its preset's region. Zone geometry
  arrives with the pitch model that gives it a consumer.
- **The lineup is all-or-nothing, and the domain decides it.** A request's lineup may name any number of
  slots; whether the eleven are full is `TAC-10`, and the domain answers it with `SELECTION_INCOMPLETE`, so
  the pitch's rules live in one place instead of being split between a field validator and the domain.
- **`TacticalSlot.Reshape` was added** so a formation change re-lays a plan's existing slots in place
  rather than deleting and reinserting them. Slot numbers stay stable, which is what keeps a team sheet
  prepared against a plan version referring to the same eleven positions (`data-model.md` §3.2).
- **`CurrentSeasonQuery` was extracted from the squad queries.** The tactics read resolves the same "season
  in progress", and a second copy would be where the two answers quietly diverged.
- **The migration is deliberately empty.** Marking the plan version a concurrency token changes the model,
  not the schema; the migration exists to record it in the model snapshot, and its `Up` says as much.
- **The API tests are tolerant of a re-used club.** The world is seeded once for the whole API collection
  and a released club keeps the plans its previous manager saved, so the create test reads whether a
  default already exists rather than assuming the club is fresh. The alternative was a test that passed
  only on its first run.
- **Deferred:** the `/tactics` screen (the board, the role and instruction pickers, the keyboard
  alternative, and the `412` reapply UX) closes this milestone; the training endpoints and the
  deterministic daily progression job follow; then the contract renewal quote.

### The tactics board

A side you can see and shape. The `/tactics` screen draws the eleven slots at their normalized
positions, lets a manager drag a player onto one, move a slot, pick a role and the eight team
instructions, and save the plan under the version that stops two devices overwriting each other. This
closes the tactics milestone of Stage 4: the model, the validator, the API, and now the screen.

#### Added

- **The board** (master plan §11.1, `TAC-9`): the eleven slots rendered at the coordinates the engine
  will hash — depth on the vertical axis, width across it — so what a manager sees is the stored layout
  rather than a second, display-only copy. A slot is a focusable button with an accessible name, so the
  pitch itself is keyboard- and screen-reader-reachable.
- **The accessible, non-drag alternative** (§11.3, `TAC-8`): an assignment table of eleven rows, each with
  a role picker constrained to its family's roles and a player picker grouped by position family. This is
  the path a keyboard, a screen reader, or a touch screen uses to set a side; dragging is a convenience on
  top of it, not the only way in.
- **Drag-and-drop**: a player chip dragged onto a slot assigns them, and a slot dragged onto the pitch
  moves it, with the drop position converted back to a normalized coordinate and clamped to the pitch
  (`TAC-7`, `TAC-9`).
- **Formation presets that keep the lineup** (`TAC-1`…`TAC-6`): choosing a formation re-lays the eleven
  slots from the server's own arrangement, and a player stays in their slot number — the right back picked
  in a 4-4-2 is still slot 2 in a 4-3-3.
- **The plan lifecycle**: the plan chooser (the default first), a rename, a new plan, and the eight
  instruction pickers (`INS-1`…`INS-8`). Creating the club's first plan makes it the default, and any
  plan can be promoted with `make-default` (`INS-11`).
- **The ETag save and its conflict UX** (`CONC-1`, ADR-0009, §11.2): every save sends the version the
  client last read in `If-Match`. A `412` keeps the manager's edits, pulls the server's state, and offers
  an explicit *Reapply my changes* or *Use the server's version* — never a silent overwrite.
- **The validation preview, drawn on the board** (§10.4): a refused save returns the validator's issues as
  stable codes; the screen lists them in words, names the slot or the player, and tints the slots they
  concern. The role picker offers only a slot's own family's roles, so a `ROLE_FAMILY_MISMATCH` is
  unpickable rather than merely reported.
- 29 new frontend tests (106 total) over the plan-draft transforms (a formation change keeps the picks, an
  ordered request, a partial lineup sent rather than dropped), the presentation helpers (labels, pitch
  geometry, the wording for every validator code), and the store (the conditional save, the `412` reapply,
  the default promotion); and 2 Playwright journeys (16 total) — the board, its accessible assignment, and
  a version conflict survived by reapplying, plus the guard for a visitor with no session.

#### Notes

- **The save always sends the whole layout, and the lineup only when somebody is picked.** Sending the
  slots means a manager's dragged positions are what is stored, not the preset they started from. Omitting
  the lineup when the sheet is empty is how a legal template is saved; sending a *partial* one is
  deliberate, because the server refuses it with `SELECTION_INCOMPLETE` (`SQ-4`) and omitting it would
  silently save a plan with nobody in it — the manager would believe their picks were kept.
- **Out-of-position is a warning, not a refusal** (`INS-10`): a makeshift side is a legitimate choice, so
  the board marks it and the engine penalises it, exactly as the validator intends.
- **A formation change moves the slot, not the player.** Slot numbers are stable across presets, which is
  what keeps a team sheet prepared against a plan version referring to the same eleven positions
  (`data-model.md` §3.2).
- **Dragging uses the browser's native drag-and-drop.** A mouse drives it; the assignment table is the
  touch and keyboard path, so a phone can set a side today. Pointer-driven dragging for touch is Stage 13's
  responsive work, where it is tested at mobile breakpoints.
- **Tactical zones are still not modelled**, so a dragged slot is bounded by the pitch but not confined to
  its preset's region — the same gap the API milestone recorded, waiting for the zone geometry that gives
  it a consumer.
- **The dirty check compares the request, not the object graph.** "There is something to save" is exactly
  "the request the save would send differs from the last one", so a re-built or re-ordered draft does not
  read as a change and a save is never sent needlessly.
- **`GET /tactics` is one read for the whole screen** — the plans, the squad, and every preset's
  arrangement — so the board renders a first, empty plan without reproducing eighteen coordinates in the
  client, and the same numbers reach the renderer and the snapshot hash.

### Training

A club you can develop. A manager sets the team's focus and intensity, points any individual at one
attribute family, and a deterministic daily job advances every player in the world by a day of training —
recovery, bounded development against their hidden potential, and a fraction carried forward so nobody
loses a day to rounding. This closes the last two deliverables of Stage 4 that were still open: the
training endpoints, and the daily progression. The `/training` screen follows as its own milestone.

#### Added

- **The deterministic progression calculator** (`TRN-1`, `TRN-2`, `TRN-9`, `TRN-10`): `DailyProgression`
  is a pure function of the player, the day, the plan in force, the age curve, and the hidden potential,
  seeded from the player identity and the day and versioned (`training-v1`). It reads no clock, database, or
  global random source, so a replay reproduces the same day; it produces recovery (condition, fatigue,
  sharpness, and a bounded morale drift) and development (a fraction per emphasised attribute, carried
  forward and spent one point at a time). Intensity buys development and costs condition and fatigue;
  recovery focus buys freshness and develops nobody. An individual focus steers growth to its own family
  rather than adding to the team's, which is what makes `TRN-2` a decision.
- **Two aggregate mutations**, the first that move a stored player value outside generation:
  `PlayerState.ApplyProgression` (basis points, the development remainder, and the day) and
  `PlayerAttributes.Apply` (re-validates the 1–20 scale and restamps the checksum). Training never lowers an
  attribute and never pushes one past the player's potential or the scale (`TRN-4`, `TRN-9`).
- **The training API** (master plan §10.4): `GET /training` — the plan, the squad it applies to, and the
  option lists so the client never reproduces the enumerations — plus `PUT /training` and
  `PUT /players/{playerId}/training-focus`. The plan's `version` is its strong entity tag: a change to a set
  plan requires `If-Match` (answered `428` without it, `412` when stale), and an individual focus that
  exists carries the same contract (`CONC-1`, ADR-0009).
- **The daily progression job** (`TRN-3`, master plan §7.2): `RunDailyProgression` loads every club's
  roster — human and AI alike, with the implicit `balanced`/`normal` default for a club that has set no
  plan — advances the players whose day it has not already done, and commits once.
  `DailyPlayerProgressionJobHandler` is a thin shell over it, and `DailyProgressionScheduler`, a worker-only
  hosted service, materialises the day's date-keyed row so the row is the deadline and a late run is late
  rather than lost (ADR-0012). The run is idempotent for a day, so at-least-once delivery cannot develop a
  player twice.
- **`WorldRuleSet.DailyProgressionUtc`** (02:00 UTC, `TRN-3`), and the rule set is now `world-rules-v3`, as
  a stage's constants arrive with that stage (`RULE-1`). A world stamped `world-rules-v2` keeps being read
  against it.
- **The training persistence**: `ITrainingRepository`/`TrainingRepository` (the plan and focus as tracked
  aggregates, and the whole-world roster in four flat queries rather than one graph) and
  `ITrainingQueries`/`TrainingQueries` (the plan, the club, and the squad with each player's focus in one
  round trip).
- **`Training:EnableDailyProgression`, off by default.** The switch is configuration rather than an
  `ops.feature_flags` row because that table does not exist yet, and the scheduler and handler both honour
  it (master plan §17.12). The player-facing endpoints are complete, so they are not gated.
- **ADR-0012**, on the materialised world job, the configuration-gated switch, the pure deterministic
  calculator, and the deliberate deferral of training injuries to Stage 8.
- 12 new domain tests (261 total) covering determinism, the scale and basis-point bounds over a year, that
  training never lowers an attribute, that a focus develops only its family, the relative cost of intensity,
  the age limit, and the potential ceiling; 3 new infrastructure tests (92 total), the important one being
  the run over a real seeded world — 108 clubs and 2,376 players advanced once, the repeat a no-op, the next
  day everyone again; and 5 new API integration tests (62 total) covering the read, the create/revise with
  `428`/`412`, setting and clearing a focus, and the other-club and no-club refusals.

#### Notes

- **The scheduler is `EnsureScheduleJobs`' first form.** Master plan §7.2 names a materialiser for future
  lock and matchday jobs, but its real subject — fixtures — arrives in Stage 6. Rather than a second
  mechanism later, the worker service that ensures the daily row is written so the fixture calendar can
  join it (ADR-0012).
- **Training injuries are not in this milestone.** `TRN-12` requires them, but the band-to-fixture mapping
  is the discipline rule `PlayerUnavailability`'s own documentation gives to Stage 8; a calculator that
  invented one would be a competing definition of injury severity. The development and recovery paths are
  complete without it.
- **The facilities baseline is a constant.** `TRN-9` lists it as an input, and facilities are a post-MVP
  feature (§2.3); it becomes a real input when there is a facility to read (ADR-0012).
- **Development can leave one attribute behind for a season.** Each day's whole points are spread in a draw
  order seeded from the player and the day, so over a long run the family develops as a group but a single
  attribute is not guaranteed a point every season. That is the intended shape — some attributes come on
  faster — and the tests assert the family-level contract rather than per-attribute growth, which would be
  a coin flip.
- **The individual focus overrides the team's family selection, not adds to it.** Team focus still governs
  load and recovery, so a `fitness` plan with a `technical` individual focus means the player trains hard
  and develops on the ball. That is the reading of `TRN-2` that makes the optional instruction matter.
- **The plan takes effect from the day it is saved.** A future-dated plan would need the job to honour
  several at once for no MVP benefit; the effective date is still stored, because the row records when the
  choice was made.
- **A player may be pulled off individual focus and returned to the team plan** with a null family, which
  deletes the row. The clear is idempotent, so a retried clear is not a conflict.
- **`Player.Potential` is read by the job, not by a DTO.** The development ceiling is class C2 and the
  progression repository is an application-layer port, not a manager-facing projection; the
  data-classification test that guards the contracts assembly is untouched and still passes.
- **Deferred to the rest of Stage 4:** the `/training` screen. **Deferred beyond it:** the contract renewal
  quote (whose `CON-3` inputs include playing time, which does not exist until Stage 6's fixtures),
  fixtures, match effects, full finances, transfers, and the public scouting surface.

### The training screen

A club you can develop, on screen. The `/training` screen sets the club's team focus and intensity, and
points any player at one attribute family — or returns them to the team plan — on the plan's version and
each focus's own. This closes the last open deliverable of Stage 4: the model, the API, the daily
progression job, and now the screen.

#### Added

- **The training screen** (master plan §11.1, F-20): the plan's two choices are labelled selects and the
  roster is a table, each row carrying the condition and fatigue a manager weighs when setting a load and a
  per-player focus picker (`TRN-1`, `TRN-2`). Every focus control is a labelled select rather than a drag,
  so the screen is reachable from a keyboard and a screen reader, and condition and fatigue carry their band
  word beside the number so colour is decoration rather than the message (§11.3).
- **The plan's ETag contract** (`CONC-1`, ADR-0009, §11.2): a first plan is sent without `If-Match`, because
  there is nothing to be conditional against, and a revise sends the version the client last read. A `412`
  keeps the manager's choices, pulls the server's state, and offers an explicit *Reapply my changes* or
  *Use the server's version* — never a silent overwrite.
- **The per-player focus contract**: setting a new focus carries no tag, while changing or clearing a set one
  sends the focus's own version. A stale one is refused rather than overwriting a change made elsewhere, and
  the roster is refreshed with the refusal explained.
- **The option lists come from the server.** `GET /training` already returns every focus, intensity, and
  attribute-family code, so the screen renders them without reproducing the enumerations, and the empty
  value of the focus select is the clear that returns a player to the team plan.
- 17 new frontend tests (123 total) over the presentation helpers (a label for every code the server can
  send, the team plan first, the effective-date formatting) and the store (the create-without-tag, the
  revise-under-version, the `412` reapply, the new/set/cleared focus, and the stale-focus refresh); and a
  new Playwright journey (18 total) — the plan, a version conflict survived by reapplying, an individual
  focus set and cleared, plus the guard for a visitor with no session.

#### Notes

- **The screen reads one response.** `TrainingResponse` carries the plan, the club, the option lists, and
  the squad with each player's focus, so the screen makes one round trip and the same numbers reach the
  controls that the progression job reads (`TRN-3`).
- **A plan equal to the implicit defaults is not dirty.** When a club has not set a plan it is already
  training `balanced`/`normal`, so the save button stays disabled until a manager actually changes a choice
  rather than creating a row that says what the job already assumes.
- **The roster's order is the server's** — goalkeepers first, then by name — the order a manager reads a
  squad in, so the client does not re-sort it.
- **The individual focus is written straight into the read model**, not drafted: each is a single value on a
  single player, and a document to stage around it would be the tactics draft's machinery without its
  reason.
- **Tactical zones, drag-and-drop, and pointer input are not here**, exactly as the tactics and training API
  milestones recorded: they belong to the pitch model and the Stage 13 responsive work.

## Stage 3 — World generation, six countries, clubs, and onboarding

A world you can onboard into. Six fictional national pyramids are generated from one seed, a manager
creates a profile, chooses a country, takes over an AI club in its lowest active tier, and inherits it
exactly as it stands. A country that fills with humans queues the generation of the next tier underneath
it.

### Added

- **The schema the rest of the stage writes into** (shipped first, in its own commit): the `world` schema
  (game worlds, countries, manager profiles, clubs, club tenures, division provisioning requests,
  generation runs), the `competition` shell (seasons, divisions, division-seasons, club season entries),
  and the `finance` shell (club accounts), in one migration applied against real PostgreSQL 17 before
  being committed. The versioned `WorldRuleSet` holds every constant from game rules §3, and its version is
  stamped onto the world and each season, so a historical season is interpreted against the rules that were
  actually in force when it was played.
- **The world aggregates with the rules as behaviour**: `GameWorld` (freeze/resume), `Country`, `Manager`
  (the resignation cooldown), `Club` (tier-scaled baselines and a slug derived from the name rather than
  supplied beside it), `ClubTenure` (the whole of the ownership model), `DivisionProvisioningRequest`,
  `GenerationRun`, `SeasonCalendar` (a pure function from a first matchday to a season's whole window),
  and a `CountryCapacity` value object that answers "does this country have room, and should it grow?" in
  one place instead of in an endpoint.
- **Mappings that put the plan's constraints in the database rather than in a convention**: partial unique
  indexes for one open tenure per club and per manager (`OCC-9`), unique `(country_id, target_tier)` for
  provisioning (`PYR-3`), unique per-world club name and slug, one club per season, and the `FIN-13`
  checks that neither balance can go negative and reservations cannot exceed the cash behind them.
- **Deterministic world generation.** `Pcg32`, a pinned, explicitly tested PRNG, plus versioned fictional
  name pools for all six locales (`england`, `spain`, `germany`, `italy`, `france`, `romania`), a curated
  blocklist of real football identities, and `ClubIdentityGenerator`. Every generated value is a pure
  function of the seed, the country's pool, and the club's ordinal within its country, so the same seed
  reproduces the same pyramid and different seeds produce visibly different ones.
- **Club names that cannot collide, without a retry loop.** Places and suffixes are woven by ordinal rather
  than multiplied, so a division alternates both instead of naming eighteen clubs after one place; the
  combination cycle is their least common multiple, which makes the pairing injective and the names unique
  by construction. Past the cycle the generator adds a qualifier and then a numeral, so a deep pyramid runs
  out of names only by running out of integers (`PYR-11`).
- **The world seeder**, as a use case and as `tools/world-seeder`. `--seed` and `--first-matchday` override
  the configuration; everything else comes from `World:*`. It is idempotent — running it against an
  existing world reports that world and writes nothing (`WORLD-1`) — and it records the seed, generator
  version, and a digest of the non-seed inputs in `world.generation_runs` (`PYR-14`).
- **The onboarding commands**: `CreateManagerProfile`, `ClaimClub`, and `ResignClub`. The takeover validates
  the club is an active, AI-controlled member of the country's lowest active tier, disables a manager who
  already holds a club or is serving the resignation cooldown, and returns the inherited club.
- **`CapacityEvaluator`**, which answers "should this country grow?" in one place. It runs after every
  successful takeover and after every resignation, and creates the next tier's
  `DivisionProvisioningRequest` exactly once (`PYR-1`, `PYR-2`).
- **Advisory locks**: `IAdvisoryLock` and `AdvisoryLockKey`, implemented over
  `pg_advisory_xact_lock`. A takeover takes the manager's lock and then the country's, always in that
  order, which is what keeps the pair deadlock-free (`PYR-3`).
- **Explicit transactions** on `IUnitOfWork` (`BeginTransactionAsync`, `IDatabaseTransaction`,
  `TransactionIsolation`), so a workflow that must hold a lock across more than one save can.
- **The onboarding reads**: world, countries, per-country capacity, available clubs, onboarding state, and
  the inherited-club dashboard, each one query shaped for one screen.
- **The API** from master plan §10.2, plus `GET /clubs/{clubId}/dashboard`. Claim refusals carry the stable
  codes §7.6 requires — `CLUB_ALREADY_CLAIMED`, `MANAGER_HAS_ACTIVE_CLUB`, `MANAGER_PROFILE_REQUIRED`,
  `MANAGER_IN_COOLDOWN`, and `CAPACITY_PROVISIONING` — and the last two carry the moment the cooldown
  lapses and the provisioning request's state and polling hint (`PYR-10`).
- **The Angular onboarding screens** (`/onboarding/manager`, `/onboarding/country`, `/onboarding/club`) and
  a dashboard that routes on state: no profile, no club, or a club. `OnboardingStore` holds the reference
  data and the claim's idempotency key; `requireVerifiedEmail` keeps a manager who cannot claim away from
  the screen that would ask them to.
- 109 new domain tests (175 total) covering the PRNG's pinned sequences, the generator's determinism,
  uniqueness, deep-pyramid behaviour, and the blocklist; 38 world infrastructure tests over real
  PostgreSQL, including the concurrent-takeover and fill-a-tier races; 5 API onboarding tests; 14 new web
  tests (57 total); and a Playwright onboarding journey.
- **ADR-0010**, on why a takeover serialises with an advisory lock rather than with `SERIALIZABLE`.

### Fixed

- **The worker's composition root could not resolve `IRequestContext`.** The API registered an HTTP-backed
  implementation and nothing registered a default, so any use case that writes an audit row would have
  failed the moment the worker ran one. `ServiceRequestContext` is now the default, and the API's own
  registration overrides it — which is the arrangement `IRequestContext`'s own documentation described and
  nothing implemented.

### Notes

- **The onboarding endpoints sit at the version root, not under `/api/v1/world`.** Master plan §10.2
  addresses them as resources (`/countries`, `/club-claims`, `/club-tenure`), and the committed path is the
  contract. It is the same reasoning that puts `/me` at the root.
- **A takeover holds an advisory lock in a read-committed transaction rather than a serializable one.**
  §7.6 asks for serializable, but a snapshot-isolation transaction fixes its snapshot at its first
  statement — which in this workflow is the lock request — so it would still be reading pre-lock state
  after the lock was granted, and the losing manager would get a constraint violation instead of the
  documented refusal. ADR-0010 records the decision; the partial unique indexes are untouched and remain
  the backstop.
- **The deterministic PRNG in `Domain` will not be the match engine's.** The engine ships its own
  `Pcg32` in Stage 5, and that is deliberate: a generated world's reproducibility and a played match's
  reproducibility are separate versioned contracts, and the engine's output hashes are pinned per engine
  version (ADR-0004). The dependency rules settle the question anyway — `Domain` depends on nothing (DEP-1)
  and the engine may not depend on it (DEP-2).
- **Ordinals are per country and never restart per tier.** Tier 1 takes 0–17, tier 2 takes 18–35. If each
  tier restarted at zero, every provisioned tier would propose the same eighteen names and the unique name
  index would reject the second one. A test generates twelve tiers and asserts the names never repeat.
- **Name pools use invented places only, and never a real club's home town.** Generation is where licensed
  identity would enter the product, so the pools are the primary defence and the blocklist is the backstop
  (`FIC-5`). A test runs the blocklist check across every pool and every generated name.
- **A club's founding game year is the first season's game year.** A plausible older founding date would
  have to be invented from the seed, and inventing history is not what a reproducibility rule should be
  doing with its randomness.
- **The tier-1 money, stadium, and reputation baselines are provisional.** They halve per tier, which is the
  shape Stage 9 needs, and they are recorded in `game-rules.md` as balancing values rather than as rules.
  Calibrating them is Stage 9's multi-season simulation work, not a guess made now.
- **Provisioning is requested, not executed.** The takeover creates the `DivisionProvisioningRequest` and
  returns `CAPACITY_PROVISIONING` with its state; generating the tier, its squads, and its backfilled
  results is Stage 11. Until then a full country stays full, on purpose: a half-built tier that a manager
  could claim would be worse than a wait.
- **Only a club claim requires a verified address.** Master plan §12.1 lists club claims, bidding, listing,
  and display-name changes; creating a manager profile and resigning are authenticated but not
  verification-gated, because an unverified account can reach neither a club nor a bid.
- **The dashboard shows only what exists yet.** Identity, competition placement, control, and money. Squad,
  contracts, fixtures, and history join the response in the stages that create them rather than appearing
  now as permanently-empty fields.
- **The Playwright journey gives its club back.** It resigns at the end, which exercises the resignation
  path and returns the club to the pool. Without that, each run would consume one of the 108 clubs and the
  suite would start failing after eighteen runs for a reason that is not a defect.
- **`appsettings.json` had to travel with the seeder tool.** The generic host does not copy it the way the
  web SDK does, so the project declares it explicitly and the tool sets its content root to the binary's
  directory rather than the process's working directory.
- **`game_worlds` has no JSONB feature-flags column**, although master plan §6.3 lists one. Feature flags
  belong in `ops.feature_flags`, where they are queryable and versioned; a JSONB blob on the world row is
  precisely the unfinished modelling the JSONB policy forbids (`JSN-5`).
- **`club_season_entries` carries `season_id` as well as the division-season.** Without it, "one club
  appears in exactly one division per season" is not expressible as a database constraint: a promotion bug
  could enter one club into two divisions in the same season and every standings query would double-count
  it.
- **A failed provisioning request can be retried, and the retry reuses the recorded seed.**
  `unique (country_id, target_tier)` means a second request for the same tier cannot exist, so without that
  transition one failed run would block the tier permanently. Reusing the seed is what keeps `PYR-14` true
  across attempts. A domain test caught the gap.
- **Tier names are descriptive rather than evocative** — "England Top Division", "Spain Division 2" — so no
  generated name can drift towards a real competition's branding (`WORLD-3`).
- **A seeded division starts active and a provisioned one does not.** Stage 11's tier stays in
  `provisioning` until its generation, validation, and backfill all complete (`PYR-8`); the tier the seeder
  creates is claimable the moment it exists, because there is nothing left to backfill.

## Stage 2 — Identity and authenticated walking skeleton

The auth module: the account schema, the full credential lifecycle, session rotation and reuse
detection, the request security the rest of the product will build on, the Angular screens that drive
it, and the browser journeys that prove it works end to end.

### Added

- `auth` schema: `users`, `user_roles`, `refresh_sessions`, `email_tokens`, `user_consents`, with
  unique indexes on normalized email and display name, a unique index on each token hash, a check
  constraint on the status lifecycle, and restrictive foreign keys to `users` (ADR-0002).
- `ops.audit_log` and an `IAuditWriter` that stages audit rows **in the same unit of work** as the
  change they describe, so an action without an audit trail is impossible rather than merely
  discouraged. Auth actions (register, verify, login outcomes, rotation, reuse, logout, reset,
  profile change, deletion) are recorded with actor, target, correlation ID, and a hashed IP.
- Domain auth aggregates with the lifecycle as behaviour: `User` (register, verify, lockout,
  suspend/restore, change password, display name, deletion), `RefreshSession` (issue, rotate,
  revoke), `EmailToken` (issue, consume, supersede), `UserConsent`, `UserStatusRules`, and a pure
  `AccountLockoutPolicy` escalation ladder.
- Use cases for the whole lifecycle: `RegisterUser`, `VerifyEmail`, `ResendVerificationEmail`,
  `Login`, `RefreshAccessToken`, `Logout`, `LogoutAll`, `ForgotPassword`, `ResetPassword`,
  `GetProfile`, `UpdateProfile`, `DeleteAccount`, plus the shared `EmailTokenIssuer` and
  `SessionIssuer`. Each returns an explicit outcome rather than throwing for expected results.
- FluentValidation request validators with shared email, display-name, and password rules, returning
  per-field Problem Details that the client can render against individual form controls.
- Infrastructure providers: `IdentityPasswordHasher` over ASP.NET Core's password hasher with an
  explicit work factor and transparent rehash, `SecureTokenService` (CSPRNG tokens, SHA-256 storage
  hashes, HMAC client fingerprints), `JwtAccessTokenIssuer` (HS256 with the security stamp embedded),
  `SmtpEmailSender` (MailKit), and the EF Core repositories implementing the auth ports.
- The endpoints from master plan §10.1, including `GET/PATCH/DELETE /api/v1/me`.
- Bearer authentication that re-reads the account on every authenticated request and compares the
  security stamp, which is what makes a stateless token revocable. Policies for a verified manager
  and for the `admin`, `operator`, and `support` roles — the groundwork for the Stage 14 MFA
  requirement.
- Response security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, a
  `default-src 'none'` CSP, and `Permissions-Policy`), plus rate limiting on the auth endpoints.
- The HttpOnly, `Secure`, `SameSite=Lax` refresh cookie, scoped to the refresh path, with `Secure`
  relaxed only in Development where the client is served over plain HTTP.
- A fake-email local workflow: `Email__Host`/`Email__Port` point at the Compose mail catcher, and the
  integration tests substitute a recording sender.
- The Angular auth screens — register, sign in, confirm email, forgotten password, reset password, and
  settings — as standalone components with reactive forms. Field messages are resolved in one place:
  the client's own rule wins while the manager is editing, and the server's per-field `errors` map
  takes over afterwards.
- The `SessionStore`, which holds the access token in memory and nowhere a template or another script
  can reach it, restores the session before the first route activates, and **shares one rotation
  between concurrent callers**. Without that sharing, a screen firing three requests on load would
  present an already-consumed refresh token and trip the server's reuse detection against its own
  legitimate user.
- The route guards in both directions, and a `returnUrl` that is honoured only when it is a
  same-origin absolute path — navigating to an arbitrary value would turn the sign-in screen into an
  open redirect.
- The token interceptor: attaches the bearer token, keeps auth endpoints out of both the token and the
  retry (a rejected sign-in is an answer, not an expiry), recovers from an expired token exactly once,
  and ends the session when the rotation fails instead of retrying forever.
- The Playwright suite in `tests/web-e2e`. It owns its stack — `globalSetup` brings up PostgreSQL and
  the mail catcher and applies migrations, then the config starts the API and the web client — and it
  reads the confirmation and reset links out of the mail catcher rather than being handed a token.
  Ten journeys cover registration and confirmation, the session surviving a reload, the unconfirmed
  account's restriction, account closure, renaming with the shell following, signing out everywhere,
  password replacement with a single-use link, and the guards including the external-`returnUrl` case.
- Frontend unit coverage for the session store, the guards, the interceptor, and the field-message
  precedence (43 web tests in total, up from 8).
- An `e2e` CI job that installs Chromium, typechecks the suite, runs the journeys, and uploads the
  report on failure.

### Notes

- **Access tokens carry a security stamp that is checked against the database on every authenticated
  request.** This costs one indexed primary-key lookup and is what makes suspension and
  "sign out everywhere" take effect immediately instead of after the token expires. A cache belongs
  here only when profiling shows it is needed.
- **`DELETE /api/v1/me` takes the confirming password in a JSON body.** ASP.NET Core refuses to infer
  a body parameter for `DELETE`, so the binding is explicit.
- **Only the auth endpoints are rate limited for now.** The broader per-route and per-user limits are
  Stage 14 work, where they can be tuned from load evidence instead of guessed at. The permit limit is
  configuration, so load and functional tests raise it rather than tripping it.
- **`refresh_sessions.revocation_reason`** is an addition to the column list in master plan §6.2:
  reuse detection has to distinguish "rotated" from "signed out" to know whether a presented token is
  a replay, and the reason is also what an operator needs when investigating a session incident.
- `Auth:SigningKey` is validated at startup in every environment. Development has a committed
  dev-only value, following the precedent of the local database password; every other environment
  must supply its own.
- **Playwright arrives in Stage 2, not Stage 7.** `docs/testing/test-strategy.md` originally deferred
  journeys until the match viewer, but master plan §16 makes the full auth lifecycle a Stage 2 exit
  criterion and `docs/product/mvp-traceability.md` asks for a registration journey at F-01. The test
  strategy has been corrected. The remaining master plan journeys still arrive with the screens they
  exercise, because a journey for a screen that does not exist tests nothing.
- **The journey stack raises `RateLimiting:AuthPermitLimit`.** The suite signs in far more often from
  one address than a person would, and a throttle that fired halfway through would look like a bug in
  the feature under test. The limiter is still tested — by the API integration tests, which build a
  host with a deliberately tiny limit and can therefore assert on it directly.
- **The suite is one worker, deliberately.** Every journey shares one database and one mail catcher,
  and a test that read another test's message out of the mailbox would fail for a reason that is not
  real.
- Anonymization after the deletion cooling period, session listing on the settings screen, and TOTP
  enrolment remain outstanding; they belong to the privacy workflow (Stage 14) and the settings
  screen (Stage 13).

### Fixed

- **The settings screen told managers the manager-name field was unavailable while leaving it
  editable.** The input carried both `formControlName` and a `[disabled]` binding. A reactive form
  owns the disabled property — `FormControlName` re-applies the control's own state on every change —
  so the binding was silently ignored. An unconfirmed manager could type a new name into a field the
  same screen said was locked, and only the save button stopped them. The control's own state is now
  set from the profile, and an end-to-end journey asserts the field really is disabled.
- The migration for the auth tables initially omitted the foreign keys to `auth.users`; the migration
  was regenerated rather than patched, since it had not been applied anywhere.

## Stage 1 — Monorepo scaffold and engineering guardrails

### Added

- Solution `TouchlineManager.slnx` with `Domain`, `Application`, `Infrastructure`, `Contracts`,
  `MatchEngine`, the `Api` and `Worker` composition roots, seven test projects, and two tool
  projects. Identifier naming uses the real product name throughout.
- Central package management (`Directory.Packages.props`), lockfiles, and a pinned SDK
  (`global.json`). `TreatWarningsAsErrors` with .NET analyzers at `Recommended`.
- Durable job queue on PostgreSQL: `ops.jobs` with enqueue idempotency on
  `(job_type, business_key)`, `FOR UPDATE SKIP LOCKED` claims, leases with expiry, exponential
  backoff with jitter, and dead-lettering for permanent domain failures (ADR-0003).
- Job poller as an Infrastructure hosted service, registered only by the worker composition root so
  the API can never execute a deadline.
- API composition root: validated options that fail startup when misconfigured, correlation
  middleware, RFC 9457 Problem Details, liveness/readiness/detailed health endpoints, CORS
  allowlist, OpenAPI in Development, and one route group per bounded module.
- Log redaction filter shared by both composition roots, enforcing the redaction list in
  `docs/security/data-classification.md` §4, with tests.
- Angular 22 PWA: standalone components, strict TypeScript and strict templates, PrimeNG 22 with
  Aura and Tailwind 4, documented CSS layer ordering (`tailwind-base, primeng, tailwind-utilities`),
  service worker and manifest, responsive shell with safe-area insets, skip link, reduced-motion
  handling, connectivity banner and correlation-ID footer.
- `IClock` seam with a fake clock in tests; no code path uses server local time.
- Docker Compose providing PostgreSQL 17 and a mail catcher, plus `npm run dev` as the single
  command that starts the backing services, the API, the worker and the web client.
- GitHub Actions pull-request pipeline: locked restore, `dotnet format` verification, Release build
  with warnings as errors, all test projects, generated migration SQL as a review artefact, and the
  Angular typecheck/build/test job.
- Architecture tests that fail the build when the dependency rules in
  `docs/architecture/modules.md` §2 are crossed.

### Fixed

- `global.json` pinned an SDK version the installed preview sorts below, so no SDK resolved.
- `EnqueueNoOpJob` and the job handler registry were registered as singletons while consuming the
  scoped `DbContext`. Use cases are now scoped, and the worker resolves handlers from each job's own
  scope. Caught by the API integration tests at startup.
- The log redactor missed `refresh_token`-style keys, because `_` is a word character and therefore
  provides no word boundary for a plain `\btoken\b` pattern.
- The private-field naming rule also applied to `const` and `static` fields, which are PascalCase by
  convention. Only private instance fields take the `_` prefix now.
- Local PostgreSQL publishes host port **55432**, not 5432. Docker Desktop will happily bind a second
  listener on an already-occupied host port, after which connections land unpredictably — observed
  here when 5433 was chosen and a second process bound it too. A port outside the standard and
  ephemeral ranges removes the class of problem.
- Both `package.json` files carry an `.npmrc` setting `include=dev`. A shell exporting
  `NODE_ENV=production` otherwise silently omits devDependencies, producing an install that cannot
  build.

### Notes

- EF Core is pinned to 10.0.4 to match the Npgsql provider's floor, so the graph contains exactly
  one EF Core version.
- FluentAssertions is pinned to 7.2.0. Version 8 changed to a licence that is not free for
  commercial use.
- Container images for the API, worker and web, and separate database roles for migrations and
  runtime, are deferred to Stage 14 where they are built and exercised for real rather than
  half-implemented now.
- `docs/product/match-engine.md` and the operations runbooks are intentionally absent; they are
  Stage 5 and Stage 14 deliverables.

## Stage 0 — Product rules, architecture, and executable specifications

### Added

- `docs/product/master-plan.md` adopting the approved plan.
- `docs/product/game-rules.md`: the normative rule set, with stable references for every
  configurable value, a consolidated constant table, and an explicit list of values deliberately
  left to balancing.
- ADR-0001 … ADR-0009 covering the modular monolith, authentication and sessions, the PostgreSQL
  job queue, the deterministic match engine, dynamic pyramid expansion, semantic highlights,
  PWA-first delivery, deployment topology, and time/identity/concurrency conventions.
- C4 context and container diagrams, the module map, and the first ER diagrams.
- Glossary; UI tone and fictional-data policy; disaster/recovery-relevant threat model and data
  classification; MVP traceability for 51 features plus a guardrail per non-goal.
- `docs/product/stage-0-review.md`: the consistency review, the resolved contradiction about where
  shortlists live, and the three decisions requiring a product owner's answer.

### Fixed

- Master plan §5.2 and §6.5 disagreed about the schema for shortlists. Resolved to
  `market.shortlists` and recorded, because placing it in `squad` while the market module writes it
  would violate the module-ownership rule.
- Closed two gaps that would otherwise have produced two implementations: what happens at the top of
  a pyramid (`PR-9`, `PR-10`) and whether an unserved suspension carries across rollover (`DIS-8`).
