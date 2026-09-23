# Online Football Manager MMO — Master Implementation Plan

> **Status:** Approved design candidate for implementation
> **Source:** Expanded from `AI Agent Master Execution Plan Cros.txt`
> **Product type:** Persistent, asynchronous, server-authoritative football-management MMO
> **First public client:** Responsive installable web PWA
> **Initial countries:** England, Spain, Germany, Italy, France, Romania
> **Data identity:** Fully fictional clubs, players, competitions, badges, and names
> **Primary stack:** .NET 10, ASP.NET Core, PostgreSQL 17, Angular standalone components and Signals, PrimeNG, Tailwind CSS, HTML5 Canvas

---

## 1. Purpose and implementation mandate

This document is the executable master plan for building the game from an empty repository. It replaces the original single-player/client-triggered prototype assumptions with a persistent MMO architecture while preserving its useful ideas: a fast deterministic C# match engine, text commentary, lightweight 2D highlights, Angular management screens, PostgreSQL persistence, and eventual Capacitor clients.

The implementation agent must build in the stage order defined in this document. The early stages produce a narrow but playable vertical slice; the **public MVP is not complete until the full core management loop, multiplayer scheduling, automatic league expansion, season rollover, security, operations, and release gates are implemented**.

### 1.1 Priority order

When requirements compete, use this order:

1. Competitive integrity and server authority.
2. Correctness, durability, and deterministic recovery.
3. A coherent management-game loop.
4. Accessibility and responsive usability.
5. Operational simplicity.
6. Performance proven by measurements.
7. Visual polish.
8. Optional realism and feature depth.

### 1.2 Normative language

- **MUST** is required for the public MVP.
- **SHOULD** is expected unless an Architecture Decision Record (ADR) documents a reason not to do it.
- **MAY** is optional or post-MVP.

---

## 2. Product contract

### 2.1 Product vision

Build an old-school football-management game with a modern persistent MMO structure. Each person creates an account, becomes a manager, takes over a fictional club, prepares a squad asynchronously, and competes against other human managers on fixed matchdays. AI managers keep every division playable when human managers are absent.

The game should emphasize decisions rather than reflexes:

- Inspect the squad and player attributes.
- Select a lineup and bench.
- Choose a formation, player roles, and team instructions.
- Manage fatigue, condition, morale, injuries, suspensions, training, contracts, wages, and cash.
- Scout and buy or sell players through fair timed auctions.
- Prepare before a fixed deadline.
- Receive a server-generated result, text commentary, and replayable 2D highlights.
- Climb the table, earn promotion, avoid relegation, and continue across accelerated seasons.

### 2.2 Public MVP goals

The public MVP MUST provide:

- Account registration, email verification, login, session rotation, password reset, logout, and account settings.
- One active manager career/club tenure per account.
- Country selection and atomic takeover of an AI-controlled club.
- A single persistent world containing six independent fictional national pyramids.
- One initial 18-club top division in every launch country.
- Automatic lower-tier creation whenever the current lowest tier in a country reaches 18/18 active human managers.
- AI control for vacant clubs and all clubs without a human tenure.
- Three scheduled league matchdays each week: Tuesday, Thursday, and Sunday.
- Complete squad, player, lineup, tactics, training, availability, contract, and finance workflows.
- League fixtures, tables, results, player/team statistics, promotion, relegation, and season rollover.
- Server-authoritative match locking, simulation, publication, and recovery.
- Text commentary and deterministic, replayable 2D keyframe highlights.
- Search/scouting, shortlists, transfer listings, server-resolved timed auctions, and AI market participation.
- Inbox/news notifications for material game events.
- Inactivity handling and safe return-to-AI control.
- Responsive desktop/mobile/tablet UI as an installable PWA.
- Administration, audit, observability, backup, restore, deployment, and incident controls sufficient to operate a live MMO.

### 2.3 Explicit MVP non-goals

Do not put these on the public MVP critical path:

- Real clubs, player names, competition marks, badges, kits, or licensed data.
- Multiple world shards; the schema may support a `world_id`, but production starts with one world.
- User-created clubs, badge uploads, kit uploads, or club renaming.
- Domestic cups, continental competitions, national teams, or friendlies.
- Staff hiring and staff attributes.
- Youth academy, youth teams, or annual youth intake.
- Live in-match tactical changes, WebSockets, synchronous PvP, or a client-side match simulation.
- Private transfer negotiations, player agents, loans, swaps, clauses, installment payments, or transfer windows.
- Fog-of-war attribute estimates or a staffed scouting network; MVP scouting is database search plus shortlists.
- Advanced stadium, facilities, sponsorship negotiation, merchandising, taxes, currencies, or debt.
- Social chat, forums, private messages, manager associations, or user-generated public content.
- Native Android/iOS packages; Capacitor follows the PWA release.
- Offline mutations. Deadline-sensitive writes must never be silently queued.
- Native AOT as a release gate. Adopt it later only if compatibility and measured benefits justify it.
- A guaranteed zero-cost production deployment.

### 2.4 Core game loop

1. Register and verify an account.
2. Create a unique manager profile.
3. Choose one of the six countries.
4. Take over an available AI club in that country's lowest active tier.
5. Review inherited squad, league position, finances, contracts, training, and next fixture.
6. Set a valid default lineup/tactic and optionally a fixture-specific team sheet.
7. Train players, renew contracts, shortlist targets, and enter transfer auctions.
8. The server locks the next fixture 30 minutes before kickoff.
9. At the matchday deadline, the worker simulates every fixture from immutable snapshots.
10. The division matchday is published atomically; tables, stats, discipline, finances, condition, inbox, commentary, and highlights update.
11. Repeat through a 34-round double round-robin season.
12. At rollover, apply promotion/relegation, age players by one game year, expire contracts, settle awards/finances, generate replacements where required, and schedule the next season.

---

## 3. Settled world and competition rules

All values in this section should be stored in a versioned rule set rather than scattered as magic constants.

### 3.1 World model

- Production starts with one persistent `GameWorld`.
- Every domain aggregate carries or can derive `world_id` to preserve future shard/test-world support.
- The six launch countries are England, Spain, Germany, Italy, France, and Romania.
- Country and league display names are fictionalized or generic; do not use protected league branding.
- Every active division has exactly 18 clubs.
- A new production world begins with tier 1 active in all six countries; all clubs initially use AI control.
- Clubs persist forever unless an audited administrative repair retires one. Human managers control clubs through time-bounded tenures; they do not own database club records.
- New managers may take over an AI club only in the country's **lowest active tier**. Upper-tier vacancies remain under AI control until normal promotion/relegation changes their position or a later job-market feature is added.
- A manager takes over the club exactly as it exists: squad, contracts, cash, table record, fixtures, suspensions, injuries, transfer commitments, and history do not reset.

### 3.2 Dynamic pyramid expansion

Expansion is monotonic: once a tier exists, it is never removed because occupancy later falls.

1. After every successful takeover and every tenure-status change, evaluate the country's current lowest active tier.
2. If all 18 clubs have active human tenures and no next-tier provisioning record exists, atomically create a `DivisionProvisioningRequest` for tier `N + 1`.
3. Use a PostgreSQL transaction and a country-scoped advisory lock so concurrent takeovers cannot create duplicate tiers.
4. A durable worker generates the new tier, 18 AI clubs, balanced squads, finances, fixtures, and historical state.
5. The new division uses the current country season and the same matchday calendar.
6. For matchdays that have already passed, simulate deterministic AI-vs-AI results in sequence, apply table/player state, and mark them as bootstrap simulations. This gives the new division a legitimate inherited table immediately.
7. The division becomes claimable only after generation, validation, and backfill complete.
8. If provisioning occurs while season rollover is locked, target the new season rather than mutating the closing season.
9. If capacity is temporarily unavailable, onboarding returns a stable `CAPACITY_PROVISIONING` response and offers polling; it must not partially assign a club.
10. When tier 2 fills with humans, tier 3 provisions using the same algorithm; there is no hard-coded maximum tier.

### 3.3 Human occupancy and abandonment

- “Human managed” means an active `ClubTenure` whose account is not suspended and whose release date is null.
- At 10 days without a login, send an inactivity warning.
- At 14 days, mark the tenure `inactive`; AI begins making safe lineup/training decisions, but the manager may resume control by logging in.
- At 21 days, close the tenure and return the club fully to AI control after an inbox/email warning.
- Existing fixtures, auctions, bids, transfers, and financial commitments continue; abandonment never rewinds club state.
- Voluntary resignation closes the tenure immediately and starts a seven-day real-time cooldown before another takeover.
- A suspended account loses write access immediately. An administrator may assign temporary AI control before the standard inactivity period.
- The thresholds are configuration values and must be recorded in audit events when changed.

### 3.4 Season calendar

- One season has 34 matchdays: each club plays every other club once home and once away.
- Standard kickoff: Tuesday, Thursday, and Sunday at 19:00 UTC.
- Display all deadlines in the viewer's local timezone while retaining UTC as the authority.
- Team sheets lock 30 minutes before kickoff.
- The first production season begins on a configured future Tuesday with enough onboarding lead time; do not derive it from deployment time.
- After matchday 34, run a seven-day rollover period. The next season schedule begins on the first configured matchday after rollover.
- All countries share the same real-time season cadence in the MVP, but jobs and data are country/division scoped.
- Generate schedules using a tested circle/Berger algorithm, mirror the second half, and validate exactly 34 fixtures per club, one fixture per club per round, one home and one away meeting for each pair, and acceptable home/away streaks.

### 3.5 League table and tie breakers

Award 3 points for a win, 1 for a draw, and 0 for a loss. Order by:

1. Points.
2. Goal difference.
3. Goals scored.
4. Wins.
5. Head-to-head points among tied clubs.
6. Head-to-head goal difference.
7. Fewer red cards.
8. Fewer yellow cards.
9. A deterministic season draw derived from season ID and club IDs.

The final draw key must be generated before the season, stored, and visible in competition rules. Never use database row order as a tie breaker.

### 3.6 Promotion and relegation

- If a lower adjacent tier exists, the top three clubs are promoted and the bottom three clubs in the higher tier are relegated.
- If no lower tier exists, the lowest active tier has no relegation.
- Promotions/relegations apply to clubs, so the human manager remains with the club.
- Movement occurs only during season rollover and only after all fixtures, disciplinary updates, finance postings, and table validations are final.
- A newly provisioned lower tier participates in promotion/relegation at the next rollover even if it was backfilled during the season.
- Apply all movements in one country-scoped serializable transaction, then create the next season's entries and fixtures.
- Store immutable season-entry/history rows; never rewrite the prior season's division membership.

### 3.7 Squad and registration rules

- Generated squad target: 22 senior players per club.
- Minimum registered senior squad: 18 players, including at least two goalkeepers.
- Maximum registered senior squad: 25 players.
- Match team sheet: exactly 11 starters and up to 7 substitutes.
- Maximum substitutions: 5, selected by deterministic engine rules because matches are asynchronous.
- A player can have only one active club contract and one current registration.
- Players transferred after a fixture snapshot locks are eligible only for later fixtures.
- If expiry or an administrative repair would leave a club below the minimum, create audited emergency replacement players on minimum contracts. This is a safety net, not a normal squad-building route.

### 3.8 Tactical rules

MVP formation presets:

- 4-4-2
- 4-3-3
- 4-2-3-1
- 4-1-4-1
- 3-5-2
- 5-3-2

Managers may drag slots within validated tactical zones, but cannot create overlapping/out-of-bounds positions. Each slot has a position family and role. MVP team instructions:

- Mentality: defensive, cautious, balanced, positive, attacking.
- Tempo: low, normal, high.
- Passing: short, mixed, direct.
- Width: narrow, normal, wide.
- Pressing: low block, mid block, high press.
- Defensive line: deep, normal, high.
- Tackling: stay on feet, normal, aggressive.
- Time wasting: off, situational, on.

Every instruction has a bounded effect and trade-off. No tactic may multiply a team rating without a counter-cost. Out-of-position players receive a deterministic familiarity penalty.

### 3.9 Training and player state

- Club training focus: balanced, recovery, fitness, attacking, defending, technical, tactical.
- Optional individual focus selects one attribute family.
- A daily 02:00 UTC progression job updates development, fatigue, condition, morale, and training injury chance.
- Development is deterministic from player, day, training plan, age curve, hidden potential, facilities baseline, and engine version.
- Training cannot improve a displayed attribute above 20 or below 1.
- Condition is stored from 0–10,000 basis points; fatigue from 0–10,000; morale from 0–10,000. APIs convert to user-facing values.
- Matches consume condition and increase fatigue based on minutes, intensity, stamina, and tactics. Rest and recovery restore them.
- Training injuries and match injuries create explicit unavailability records measured in fixtures, not wall-clock days.
- Morale reacts to playing time, results, contracts, and transfers using bounded changes.

### 3.10 Injuries and discipline

- Injury severities map to 1–6 fixture absences in the MVP.
- Five league yellow cards produce a one-match suspension; reset accumulation at season rollover.
- A red card produces a one-match suspension in MVP rules; later rule sets may vary severity.
- Suspension service is based on the club's next eligible league fixtures.
- A locked team sheet containing a newly ineligible player must be repaired by the snapshot builder before simulation using deterministic bench/reserve selection.

### 3.11 Contracts

- Contract lengths are 1–3 game seasons.
- Wages are charged weekly after the Sunday matchday.
- Renewal presents a deterministic server-calculated quote based on ability, potential, age, playing time, morale, tier, and remaining term.
- The manager accepts or declines the quote; free-form negotiation is post-MVP.
- A transfer closes the seller contract and creates the displayed, precomputed buyer contract.
- Expired players become free agents at rollover unless renewed.
- Free-agent signing beyond emergency replacements is post-MVP unless needed to keep the transfer market healthy; if enabled during implementation, use the same timed-auction mechanism with a zero seller fee and explicit signing wage.

### 3.12 Basic finances

Use integer minor units (`bigint`) and a single canonical in-game display currency for MVP. Do not use floating-point money.

Income:

- Home-match gate revenue based on tier, attendance factor, form, and a fixed stadium baseline.
- Weekly sponsorship credit.
- Promotion and final-position awards.
- Transfer income.

Expenses:

- Weekly player wages.
- Transfer fees.
- A small fixed weekly operating cost.

Rules:

- Clubs cannot place bids that exceed available cash after existing reservations.
- Cash and reserved funds update transactionally with an append-only ledger.
- No loans, debt, overdrafts, owner injections, stadium spending, or user purchases in the MVP.
- AI clubs obey the same affordability constraints.
- A safety job detects clubs unable to field a legal squad or pay the next wage run and applies a logged emergency grant only when required to preserve competition integrity. This must emit an operations alert and be tuned out through balancing.

### 3.13 Scouting and transfers

MVP scouting means a global searchable player database with exact public attributes, filters, sorting, player profiles, and private shortlists. Attribute uncertainty and scouts are later features.

Timed auction rules:

- A seller may list an eligible player with a minimum fee.
- Listings resolve at fixed daily resolution windows, with at least 48 hours of exposure.
- Do not schedule auction resolution within six hours before a matchday kickoff.
- Bids are ascending and visible as the current amount and bidder count; manager identity may remain hidden until completion.
- Enforce a configured minimum increment.
- A club has at most one active bid per listing and may raise it.
- Reserve the leading bid's funds transactionally. Release the former leader's reservation when outbid.
- Resolution uses the highest valid amount; equal amounts use the lowest database-assigned `bid_sequence` (earliest committed bid), then immutable bid ID.
- On resolution, revalidate account status, balance reservation, squad limits, seller minimum squad, active contracts, and listing status in one serializable transaction.
- The winning club pays, the seller receives funds, registration changes, the old contract closes, and the generated new contract begins atomically.
- Failed invariants cancel or skip the invalid bid according to documented rules and consider the next valid bid; every outcome is audited.
- AI clubs list surplus players and bid within valuation, positional need, squad size, and budget bands. They never receive hidden discounts or unlimited money.
- Direct offers, loans, swaps, clauses, installments, anti-sniping extensions, and private negotiation are post-MVP.

---

## 4. Architecture decisions

### 4.1 Architecture style

Use a **modular monolith** with one API deployable, one durable worker deployable, one PostgreSQL database, one Angular PWA, and one pure match-engine library.

Do not start with microservices. Module boundaries must be explicit in code and database schemas so high-load modules can be extracted later without forcing distributed transactions into the MVP.

### 4.2 Runtime topology

```text
Browser / installed PWA
        |
        | HTTPS REST/JSON, ETag, polling
        v
ASP.NET Core API -------------- Email provider
        |
        | PostgreSQL transactions/outbox/jobs
        v
PostgreSQL 17 <--------------- .NET Worker
                                     |
                                     +-- fixture locks and matchdays
                                     +-- auctions
                                     +-- daily progression
                                     +-- finances/inactivity
                                     +-- division provisioning
                                     +-- season rollover
```

- No WebSockets or SSE are required for MVP.
- The web client polls only where freshness matters, uses ETags, slows in background tabs, and refetches after visibility/network changes.
- The API is the only public writer. The worker invokes application use cases against the same domain and database.
- Match simulation is never exposed as a manager-triggered public command.

### 4.3 Recommended MVP deployment

Use this reference topology unless an ADR selects an equivalent managed alternative:

- **Frontend:** Cloudflare Pages with custom domain, immutable hashed assets, Brotli, and SPA fallback.
- **API:** Render paid web service from the API container; always-on production instance.
- **Worker:** Render background worker from the same repository/image family, independently scalable.
- **Database:** Render managed PostgreSQL 17 in the same region on a plan with automated backups/PITR.
- **Email:** Postmark transactional email.
- **DNS/TLS/WAF:** Cloudflare.
- **Telemetry:** OpenTelemetry export plus provider logs/metrics; frontend error reporting with PII scrubbing.

Do not claim `$0/month`. Track expected service tiers, limits, storage, backup retention, and egress in `docs/operations/cost-model.md`.

### 4.4 Technology choices

Backend:

- .NET 10 SDK pinned by `global.json`.
- ASP.NET Core Minimal APIs grouped by feature.
- EF Core 10 and Npgsql.
- FluentValidation or equivalent explicit request validators.
- OpenAPI generated in CI.
- Built-in rate limiting, health checks, authorization policies, data protection, and structured logging.
- A custom PostgreSQL-backed durable job queue using `FOR UPDATE SKIP LOCKED`; do not rely on in-memory timers for business deadlines.
- xUnit, FluentAssertions, Testcontainers for PostgreSQL, and `WebApplicationFactory`.

Frontend:

- Latest supported stable Angular version pinned in the lockfile.
- Standalone components, strict TypeScript, Signals, RxJS at I/O boundaries.
- PrimeNG for data-heavy controls.
- Tailwind CSS for layout and responsive utilities with documented CSS layer ordering.
- Angular service worker/PWA package.
- Canvas 2D renderer implemented as framework-agnostic TypeScript, not untyped global JavaScript.
- Vitest (or the Angular workspace's supported unit runner) and Playwright.

Infrastructure:

- Docker multi-stage builds.
- Docker Compose for local PostgreSQL, API, worker, mail catcher, and web dependencies.
- GitHub Actions for validation and deployment.
- No Native AOT or `Parallel.For` inside one match until profiling proves a need. Scale by processing independent fixture jobs concurrently while keeping each match single-threaded and deterministic.

### 4.5 Constrained JSONB policy

Use relational columns for identities, relationships, money, state, attributes used in filters, tactics used in validation, table data, and concurrency. Use JSONB only for:

- Immutable versioned match input snapshots.
- Versioned match-engine configuration snapshots/hashes.
- Typed match-event detail payloads.
- Semantic highlight keyframes.
- Notification/news template parameters.
- Audit before/after metadata where relational querying is not required.

Every JSONB document must include a schema/version discriminator, have application validation, and have golden deserialization tests. Do not use JSONB as a shortcut for unfinished modeling.

### 4.6 Time, IDs, and concurrency conventions

- Store real-world instants as UTC `timestamptz` and expose ISO 8601.
- Use `DateTimeOffset`/`Instant` semantics; never use server local time.
- Inject `IClock`; tests use a fake clock.
- Distinguish real time from accelerated **game season/year**. Player aging and contract years advance at season rollover, not after 365 real days.
- Use UUIDv7 identifiers generated server-side.
- Use `snake_case` database names and C# PascalCase.
- Mutable aggregates have an explicit `version bigint`; APIs expose it as a strong ETag and require `If-Match` on conflicting updates.
- Financial, claim, bid, rollover, and publication operations use explicit transactions and unique correlation/idempotency keys.

---

## 5. Repository and project structure

Create this structure during the scaffold stage:

```text
/
├─ FootballManager.slnx
├─ global.json
├─ Directory.Build.props
├─ Directory.Packages.props
├─ package.json                    # optional root task runner only
├─ .editorconfig
├─ .env.example
├─ README.md
├─ CHANGELOG.md
├─ apps/
│  ├─ api/
│  │  ├─ FootballManager.Api.csproj
│  │  ├─ Program.cs
│  │  ├─ Endpoints/
│  │  ├─ Middleware/
│  │  ├─ Auth/
│  │  └─ OpenApi/
│  ├─ worker/
│  │  ├─ FootballManager.Worker.csproj
│  │  ├─ Program.cs
│  │  └─ HostedServices/
│  └─ web/
│     ├─ angular.json
│     ├─ package.json
│     ├─ public/
│     └─ src/
│        ├─ app/core/
│        ├─ app/layout/
│        ├─ app/shared/
│        ├─ app/features/auth/
│        ├─ app/features/onboarding/
│        ├─ app/features/dashboard/
│        ├─ app/features/squad/
│        ├─ app/features/player/
│        ├─ app/features/tactics/
│        ├─ app/features/training/
│        ├─ app/features/competitions/
│        ├─ app/features/fixtures/
│        ├─ app/features/match-viewer/
│        ├─ app/features/scouting/
│        ├─ app/features/transfers/
│        ├─ app/features/finances/
│        ├─ app/features/inbox/
│        ├─ app/features/settings/
│        └─ app/features/admin/
├─ src/
│  ├─ FootballManager.Domain/
│  ├─ FootballManager.Application/
│  ├─ FootballManager.Infrastructure/
│  ├─ FootballManager.Contracts/
│  └─ FootballManager.MatchEngine/
├─ tests/
│  ├─ FootballManager.Domain.Tests/
│  ├─ FootballManager.Application.Tests/
│  ├─ FootballManager.Infrastructure.Tests/
│  ├─ FootballManager.Api.IntegrationTests/
│  ├─ FootballManager.Worker.IntegrationTests/
│  ├─ FootballManager.MatchEngine.Tests/
│  ├─ FootballManager.ArchitectureTests/
│  ├─ web-unit/
│  ├─ web-e2e/
│  └─ performance/
├─ tools/
│  ├─ world-seeder/
│  ├─ simulation-benchmarks/
│  └─ openapi-client/
├─ infra/
│  ├─ docker/
│  ├─ compose.yaml
│  ├─ render.yaml
│  └─ cloudflare/
└─ docs/
   ├─ architecture/
   │  ├─ context.md
   │  ├─ modules.md
   │  ├─ data-model.md
   │  └─ adr/
   ├─ product/
   │  ├─ game-rules.md
   │  ├─ match-engine.md
   │  └─ balancing.md
   ├─ api/
   ├─ operations/
   │  ├─ runbook.md
   │  ├─ backup-restore.md
   │  ├─ matchday-incident.md
   │  ├─ season-rollover.md
   │  └─ cost-model.md
   └─ testing/
      └─ test-strategy.md
```

### 5.1 Dependency rules

- `Domain` depends on no application, infrastructure, ASP.NET, EF Core, or UI package.
- `MatchEngine` is a pure deterministic library. It may depend on domain-neutral contracts but not EF Core, clock, network, or random framework globals.
- `Application` depends on Domain, MatchEngine abstractions, and Contracts; it contains commands, queries, policies, validators, and ports.
- `Infrastructure` implements persistence, jobs, email, auth storage, and external providers.
- `Api` and `Worker` are composition roots. They contain no game formulas.
- `Contracts` contains versioned transport DTOs and stable error codes, not EF entities.
- Angular consumes generated OpenAPI DTO types but uses handwritten feature API services/facades.
- Architecture tests fail when dependency direction or module namespace rules are violated.

### 5.2 Bounded modules

Use PostgreSQL schemas and matching C# namespaces:

- `auth`: accounts, credentials, sessions, roles, verification, privacy.
- `world`: world, countries, generation, clubs, managers, tenures, capacity.
- `squad`: players, attributes, state, contracts, registrations, training, tactics.
- `competition`: seasons, divisions, entries, matchdays, fixtures, standings, discipline, statistics.
- `match`: snapshots, simulations, events, lineups, highlights, engine metadata.
- `market`: scouting shortlists, listings, bids, auction outcomes.
- `finance`: balances, reservations, immutable ledger, sponsorship/awards.
- `comms`: inbox, notifications, news, email dispatch intent.
- `ops`: jobs, outbox, idempotency, audit, feature flags, repair records.

Cross-module writes occur through application use cases, not direct controller access to arbitrary repositories.

---

## 6. Relational data model

The following is the logical minimum. Exact EF mappings belong in `docs/architecture/data-model.md` and migrations.

### 6.1 Common conventions

Every mutable business table should normally include:

- `id uuid primary key`
- `world_id uuid` where applicable
- `created_at timestamptz not null`
- `updated_at timestamptz not null`
- `version bigint not null default 1`

Use restrictive foreign keys for history. Do not cascade-delete completed seasons, fixtures, financial records, bids, match snapshots, or audit rows. Use status fields and anonymization rather than destructive deletes.

### 6.2 Authentication schema

#### `auth.users`

- `id`, `email`, `normalized_email`, `password_hash`
- `display_name`, `normalized_display_name`
- `email_verified_at`, `status` (`pending`, `active`, `suspended`, `deletion_pending`, `anonymized`)
- `last_login_at`, `security_stamp`, `failed_login_count`, `lockout_until`
- Unique indexes on normalized email and normalized display name.
- Check maximum lengths and allowed status transitions.

#### `auth.refresh_sessions`

- `id`, `user_id`, hashed refresh token, token family ID, issued/expires/last-used/revoked instants, replacement session ID, IP prefix hash, user-agent hash.
- Unique token hash; index active sessions by user.
- Rotation and reuse detection revoke the whole token family.

#### `auth.email_tokens`

- User, purpose (`verify`, `reset`), hashed token, expires/consumed timestamps.
- Unique active token by user/purpose; never store raw tokens.

#### `auth.user_roles`

- User and role (`player`, `support`, `operator`, `admin`).
- Composite primary key. Admin accounts require MFA before production launch.

#### `auth.user_consents`

- Terms/privacy version, accepted timestamp, IP hash.

### 6.3 World and manager schema

#### `world.game_worlds`

- Name, status, active rule-set version, game season number, default kickoff UTC time, feature flags.

#### `world.countries`

- World, stable code, fictional display name, locale/name-pool key, sort order, active flag.
- Unique `(world_id, code)`.

#### `world.managers`

- One-to-one user profile, reputation, takeover cooldown until, selected locale/timezone.
- Unique active manager per user.

#### `world.clubs`

- Country, stable fictional name/short name/slug, generated city/region, badge seed, founding game year, status, stadium baseline, reputation.
- Unique normalized club name and slug within world.
- Club does not contain a mutable `human_manager_id`; derive control from active tenure.

#### `world.club_tenures`

- Club, manager, started/ended timestamps, end reason, last active time, control status (`active`, `inactive`, `closed`), takeover idempotency key.
- Partial unique index: one active tenure per club.
- Partial unique index: one active tenure per manager.
- Preserve all historical tenures.

#### `world.division_provisioning_requests`

- Country, target tier, target season, status, deterministic generation seed, requested/started/completed times, failure diagnostics.
- Unique `(country_id, target_tier)`.

#### `world.generation_runs`

- Kind, seed, generator version, input hash, output counts, status, diagnostics.
- Makes every generated club/player set reproducible and auditable.

### 6.4 Competition schema

#### `competition.seasons`

- World, sequence number, display label, game year, real start/end/rollover timestamps, status, rule-set version.
- Unique `(world_id, sequence_number)`.

#### `competition.divisions`

- Country, tier number, stable display name, status, created season, 18-club capacity.
- Unique `(country_id, tier_number)` and check `tier_number >= 1`.

#### `competition.division_seasons`

- Division, season, status, schedule seed, tie-draw seed/hash, standings-finalized timestamp.
- Unique `(division_id, season_id)`.

#### `competition.club_season_entries`

- Division-season, club, initial control type, final rank, promoted/relegated flags, archived finance/reputation summary.
- Unique club per season and unique `(division_season_id, club_id)`.

#### `competition.matchdays`

- Division-season, round 1–34, lock and kickoff timestamps, publication status.
- Unique `(division_season_id, round_number)`.

#### `competition.fixtures`

- Matchday, home/away clubs, kickoff, status (`scheduled`, `locked`, `simulating`, `staged`, `published`, `void`), score, match ID, publication timestamp, version.
- Checks: different clubs, nonnegative scores, score required only after staged/published.
- Unique home/away pair per leg and one fixture per club/matchday enforced by generation validation plus database exclusion/unique support where practical.
- Index by club/kickoff/status and matchday/status.

#### `competition.standings`

- Division-season and club; played/won/drawn/lost, goals for/against, points, cards, current rank, version.
- Unique `(division_season_id, club_id)`.
- Treat as a transactional projection rebuilt from published fixtures for repair.

#### `competition.player_season_stats`

- Player, club, division-season; appearances, starts, minutes, goals, assists, shots, saves, cards, average rating.
- Unique `(division_season_id, player_id, club_id)`.

#### `competition.club_season_stats`

- Club/division-season aggregates not represented in standings.

#### `competition.discipline_records`

- Player, division-season, yellow count, red count, pending suspension fixtures, last served fixture.
- Unique `(division_season_id, player_id)`.

### 6.5 Squad and player schema

#### `squad.players`

- Fictional full/short name, nationality country code, generated name seed.
- Game birth year/day, preferred foot, height, weight, primary position, secondary positions, status.
- Hidden immutable generation potential/reputation values kept in a restricted table or server-only columns never serialized.
- Index name search using PostgreSQL trigram/full-text support.

#### `squad.player_attributes`

One-to-one player row, all `smallint check between 1 and 20`:

- Technical: finishing, passing, crossing, dribbling, first touch, tackling, marking, heading, technique, set pieces.
- Mental: decisions, vision, positioning, composure, anticipation, work rate, aggression, leadership.
- Physical: pace, acceleration, stamina, strength, agility, jumping reach.
- Goalkeeping: handling, reflexes, one-on-ones, aerial ability.
- Attribute schema version and aggregate checksum.

Use columns rather than JSONB because scouting filters and match formulas depend on these values.

#### `squad.player_state`

- Player, condition/fatigue/morale/match-sharpness basis points, development remainder, last progression date, version.
- One-to-one player row.

#### `squad.player_contracts`

- Player, club, start season, end season, weekly wage minor units, squad status, status, closed reason/timestamps.
- Partial unique index on one active contract per player.
- Index expiring contracts by club/end season.

#### `squad.player_registrations`

- Player, club, effective fixture boundary, status.
- Partial unique active registration per player.
- Registration and active contract must agree; enforce in application transaction and invariant integration tests.

#### `squad.player_unavailability`

- Player, club, type (`injury`, `suspension`), source fixture/job, start timestamp, remaining eligible fixtures, severity, resolved timestamp.

#### `squad.tactical_plans`

- Club, name, formation preset, all enumerated team instructions, is default, version.
- Partial unique one default plan per club.

#### `squad.tactical_slots`

- Plan, slot number 1–11, position family, role, normalized X/Y decimal or scaled integer, assigned default player (nullable).
- Unique plan/slot and plan/player; coordinate checks 0–10,000.

#### `squad.fixture_team_sheets`

- Fixture, club, tactical plan version, status (`draft`, `locked`), locked timestamp/version.
- Unique `(fixture_id, club_id)`.

#### `squad.team_sheet_entries`

- Team sheet, player, designation (`starter`, `substitute`), slot/order, role override.
- Unique sheet/player and sheet/slot.

#### `squad.training_plans`

- Club, team focus, intensity, effective date, version.

#### `squad.player_training_focus`

- Player, club, focus family, effective date.

#### `squad.shortlists`

- Manager and player, notes (private, length-limited), created timestamp.
- Unique manager/player.

### 6.6 Match schema

#### `match.input_snapshots`

- Fixture, engine version, rule-set version, RNG seed ciphertext or protected value, seed commitment hash, snapshot JSONB, snapshot hash, created timestamp.
- Unique fixture.
- Snapshot includes frozen lineups, visible/hidden engine attributes, condition, morale, tactics, suspensions repair decisions, home advantage, and formula-config hash.
- Immutable after fixture enters `locked`.

#### `match.matches`

- Fixture, engine/presentation version, seed commitment, started/completed times, score, aggregate statistics, input/output hashes, simulation attempt ID.
- Unique fixture.

#### `match.lineup_participation`

- Match, player, club, starter/substitute, entered/left minute, assigned role, rating.

#### `match.events`

- Match, monotonically increasing sequence, minute, second, event type, acting club/player, secondary player, x/y location, detail JSONB schema version.
- Unique `(match_id, sequence)`; indexes match/minute and player/type.
- Event types include kickoff, possession transition, foul, yellow/red, injury, substitution, offside, corner, free kick, penalty, shot, save, woodwork, goal, halftime, fulltime.

#### `match.highlights`

- Match, source event, order, duration milliseconds, importance, semantic keyframe JSONB, presentation schema version, payload byte count/hash.
- Unique source event where one highlight per event is intended.

#### `match.simulation_attempts`

- Fixture, job, attempt number, engine build, input/output hash, status, error category, timings.
- Preserve failures for operations without publishing partial results.

### 6.7 Market schema

#### `market.transfer_listings`

- Player, seller club, minimum fee, generated buyer wage/contract terms, opens/ends timestamps, status, version, resolution job ID.
- Checks positive fee, end after minimum exposure, active seller contract.
- Partial unique active listing per player.

#### `market.transfer_bids`

- Listing, bidder club, amount, database sequence, placed timestamp, status (`leading`, `outbid`, `won`, `released`, `invalid`), reservation ledger correlation, version.
- Unique active bid per listing/bidder.
- Index listing/status/amount/sequence.

#### `market.transfer_outcomes`

- Listing, winning bid/player/seller/buyer, fee, old/new contract IDs, resolved timestamp, outcome reason, correlation ID.
- Unique listing.

#### `market.ai_market_decisions`

- Club, evaluation timestamp, action, inputs/hash, resulting listing/bid, AI policy version.

### 6.8 Finance schema

#### `finance.club_accounts`

- Club, cash balance, reserved balance, last ledger sequence, version.
- Checks balances nonnegative.

#### `finance.ledger_entries`

- Club, sequence, category, cash delta, reserved delta, resulting balances, source type/ID, correlation ID, description template/parameters, created timestamp.
- Unique club/sequence and global correlation/category as needed for idempotency.
- Append-only; corrections use compensating entries.

#### `finance.club_season_finances`

- Club/season opening and closing cash plus category totals for reporting.

### 6.9 Communications and operations schema

#### `comms.inbox_messages`

- Recipient manager, category, title/body template keys, parameter JSONB, related entity, created/read/archived timestamps.
- Index unread messages by manager.

#### `comms.news_items`

- World/country/division scope, category, template/parameters, publication/expiry.

#### `ops.jobs`

- Job type, business key, payload JSONB, due time, priority, status, attempt count, maximum attempts, lease owner/until, last error, completed timestamp.
- Unique `(job_type, business_key)` provides enqueue idempotency.
- Index ready jobs by status/due/priority.

#### `ops.outbox_messages`

- Type, aggregate/correlation, payload, occurred/published timestamps, attempts and lease.
- Insert in the same transaction as domain state.

#### `ops.idempotency_records`

- User, endpoint/operation, idempotency key, request hash, response status/body hash/body or resource reference, expiry.
- Unique user/operation/key; reject key reuse with a different request hash.

#### `ops.audit_log`

- Actor user/service, action, target, correlation ID, real timestamp, IP hash, before/after metadata, reason.
- Append-only and access restricted.

#### `ops.feature_flags`

- World/environment scoped flag, value, rollout metadata, version.

#### `ops.repair_actions`

- Incident/ticket, approved actor, repair type, target, dry-run result, execution state, compensating action.

---

## 7. Critical workflows and reliability

### 7.1 Durable job mechanics

Implement `PostgresJobQueue` in `FootballManager.Infrastructure/Jobs`:

1. Enqueue jobs transactionally with a unique business key.
2. Workers claim ready rows using `FOR UPDATE SKIP LOCKED`, set a lease owner and expiry, and commit quickly.
3. Handlers run outside the claim transaction, then atomically mark success or schedule retry.
4. Expired leases become claimable.
5. Use exponential backoff with jitter for transient infrastructure errors.
6. Classify permanent domain errors into dead-letter state and alert operations.
7. Every handler is idempotent based on the business key and destination-table uniqueness.
8. Use PostgreSQL advisory locks only for singleton country/season workflows; normal jobs scale through row leases.
9. Expose queue depth, oldest due age, failures, retries, and execution duration metrics.
10. Provide admin dry-run/retry/cancel operations with audit reasons.

### 7.2 Required recurring/materialized jobs

- `EnsureScheduleJobs`: materialize future lock/matchday jobs.
- `LockFixtureTeamSheets`: 30 minutes before kickoff.
- `ResolveDivisionMatchday`: at kickoff; produces staged fixtures.
- `PublishDivisionMatchday`: publishes only after all fixtures are staged and validated.
- `ResolveTransferAuction`: at fixed auction windows.
- `DailyPlayerProgression`: training/state/injury recovery.
- `WeeklyFinanceRun`: wages, sponsorship, operating costs.
- `EvaluateAiClubs`: lineups, tactics, training, renewals, listings, bids.
- `EvaluateInactivity`: warnings, inactive state, tenure closure.
- `ProvisionDivision`: generate and backfill a new tier.
- `PrepareSeasonRollover`, `ExecuteCountryRollover`, `FinalizeSeasonRollover`.
- `DispatchOutbox`, `DispatchEmail`, `BuildNews`.
- `RebuildProjection`: controlled repair for standings/stats.

### 7.3 Fixture lock workflow

For each club in a fixture:

1. Load the fixture-specific draft; otherwise use the club default plan.
2. Validate active registration, contract, injury, suspension, duplicates, formation, 11 starters, and bench limits.
3. Deterministically repair invalid/missing selections using position suitability, condition, ability, and stable player ID tie breaking.
4. Record all repairs and send the manager an inbox item.
5. Freeze tactical plan, player attributes/state, engine config, and lineups into one canonical input snapshot.
6. Hash the canonical snapshot.
7. Protect/store the RNG seed and publish its commitment hash.
8. Mark the fixture locked with an optimistic version check.
9. Writes after lock affect later fixtures only.

If lock is delayed, kickoff simulation waits for a valid snapshot; the match is not simulated from mutable live tables.

### 7.4 Matchday resolution and atomic publication

1. Acquire a division-matchday business lock.
2. Verify all nine fixtures have immutable snapshots.
3. Enqueue or execute independent simulation attempts with bounded worker concurrency.
4. Each match writes its output as `staged`; it does not update public standings yet.
5. Re-running a completed attempt with the same snapshot/engine must produce the same output hash.
6. Once all nine fixtures are staged, validate event-score consistency, statistics, lineups, and output sizes.
7. In one transaction, mark fixtures/matches published, apply standings and player stats, apply cards/injuries/fatigue/morale, post gate revenue, and write outbox messages.
8. If final publication fails, retry safely; never publish five of nine results.
9. If simulation is delayed, display operational status without changing the scheduled kickoff. Do not invent forfeits.
10. A void/replay requires an operator reason and uses the original snapshot and seed unless a documented engine defect requires a versioned remediation.

### 7.5 Season rollover workflow

Use a resumable state machine with checkpoints, not one opaque transaction:

1. Preflight: all matchdays published, no unresolved mandatory auctions, projections reconcile, backups healthy.
2. Freeze season-scoped writes and pause new listings briefly.
3. Finalize standings and immutable season summaries.
4. Compute promotions/relegations per country.
5. Settle prize income and season finance summaries.
6. Resolve contract renewals/expiries and create emergency replacements if required.
7. Increment game year; age players; apply retirement rules only after they are introduced in a tested feature (MVP may use an age cap and deterministic retirements at rollover).
8. Move clubs into next season entries.
9. Generate schedules and tie-draw keys.
10. Reset discipline accumulation, preserve outstanding red suspensions if rules require it, and initialize stats/tables.
11. Reopen transfers and regular writes.
12. Emit news/inbox events and validate counts.
13. Mark rollover complete.

Every step stores a unique checkpoint and can be resumed. A country failure must not corrupt completed countries; final world-season activation waits for all countries.

### 7.6 Club takeover transaction

`POST /api/v1/club-claims` must:

- Require verified active account and no active tenure/cooldown.
- Validate the requested club is AI-controlled and in the current lowest active tier.
- Lock manager, club, country capacity, and relevant tenure rows.
- Insert tenure and update last-active metadata in a serializable transaction.
- Enqueue welcome/inbox and capacity evaluation outbox events.
- Return the inherited club dashboard summary.
- Be idempotent under retries.
- Return `409 CLUB_ALREADY_CLAIMED`, `409 MANAGER_HAS_ACTIVE_CLUB`, or `409 CAPACITY_PROVISIONING` with machine-readable details rather than a generic error.

### 7.7 Transfer transaction and collusion controls

- All bid/listing commands require idempotency keys and verified club ownership.
- Never accept balance, seller identity, player value, or deadline from the client as authoritative.
- Record bid IP/device-risk hashes for support analysis, but do not auto-punish shared households.
- Detect repeated below-value transactions, reciprocal patterns, account overlap signals, and rapid resign/reclaim behavior. Flag for review; do not silently alter results.
- Rate-limit listing and bid commands per manager and club.
- Keep completed market history public enough for transparency.

---

## 8. Deterministic match engine

### 8.1 Package boundaries and key files

Create:

```text
src/FootballManager.MatchEngine/
├─ MatchEngine.cs
├─ Model/MatchInput.cs
├─ Model/MatchOutput.cs
├─ Model/EngineEvent.cs
├─ Random/Pcg32.cs
├─ Ratings/UnitRatingCalculator.cs
├─ Simulation/PossessionSimulator.cs
├─ Simulation/ChanceSimulator.cs
├─ Simulation/DisciplineSimulator.cs
├─ Simulation/InjurySimulator.cs
├─ Simulation/SubstitutionPlanner.cs
├─ Simulation/MatchState.cs
├─ Commentary/CommentaryTokenBuilder.cs
├─ Highlights/HighlightDirector.cs
├─ Configuration/EngineRulesV1.cs
└─ Serialization/CanonicalMatchSerializer.cs
```

No method in this project may call the database, system clock, `Random.Shared`, network, filesystem, current culture, or thread scheduling.

### 8.2 Deterministic random contract

- Implement a stable, explicitly tested PRNG such as PCG32; do not depend on runtime `System.Random` implementation details.
- Derive the secret match seed with HMAC from world secret, fixture ID, locked snapshot hash, and engine version.
- Store a protected seed and public commitment hash. Reveal or retain the raw seed only according to operations/security policy.
- Every random draw occurs in a documented order. Never iterate unordered dictionaries/sets inside simulation.
- Golden tests pin complete output hashes for representative inputs.
- Changing formulas, draw order, serialization, or event semantics requires a new engine version.

### 8.3 Input snapshot

`MatchInputV1` includes:

- Fixture/world/season IDs and home/away context.
- Engine and rule-set versions.
- Exactly 11 starters and up to 7 substitutes per club.
- Frozen player attributes, state, role familiarity, availability repair notes, and contract/registration IDs.
- Frozen tactical slots and instructions.
- Home advantage and any competition constants.
- Formula configuration hash.
- Seed.

Canonical serialization sorts all collections and uses invariant numeric formats before hashing.

### 8.4 Simulation model

Simulate a sequence of possessions across 90 regulation minutes plus deterministic stoppage time. A possession should progress through phases rather than independently rolling “goal per minute.”

For each phase:

1. Compute team unit ratings for build-up/control, creation, finishing, defensive pressure, defensive shape, goalkeeping, set pieces, fitness, and cohesion.
2. Bound tactical modifiers so attributes remain dominant and every aggressive choice has fatigue/space/discipline trade-offs.
3. Select possession based on control differential and state.
4. Resolve progression, turnover, foul, offside, set piece, or chance.
5. For a chance, derive shot type and expected-goal probability from location, creator/finisher, defense, pressure, and goalkeeper.
6. Resolve save, block, miss, woodwork, or goal.
7. Update score, momentum only if explicitly bounded, fatigue, cards, injuries, and tactical state.
8. Apply deterministic substitutions for injury, severe fatigue, or role need from the selected bench. Human managers do not make live changes in MVP.
9. Emit structured events; commentary and highlights consume events rather than changing the outcome.

### 8.5 Ratings and balancing

- Keep formula constants in a versioned strongly typed configuration file validated on startup.
- Unit ratings use weighted player attributes based on role; no single visible “overall” is authoritative.
- Home advantage is small and configurable.
- Condition, fatigue, morale, out-of-position use, aggressive pressing, cards, and substitutions have bounded effects.
- Target distributions must be measured across at least 100,000 seeded simulations: goals, draws, home wins, cards, shots, possession, injuries, and score tails.
- Add invariants: no negative stats, final score equals goal events, substitutions legal, event times ordered, participants on a valid team, and output finite.
- Do not optimize toward an arbitrary 20 ms target at the expense of clarity. Public MVP target: p95 pure simulation under 100 ms on the production instance and enough matchday throughput with bounded parallel fixture jobs. Record benchmark hardware.

### 8.6 Commentary

Store event facts and commentary template tokens, not irreversible localized prose. For example:

```json
{
  "templateKey": "match.shot.saved",
  "parameters": {
    "shooterPlayerId": "...",
    "goalkeeperPlayerId": "...",
    "clubId": "...",
    "shotZone": "central"
  }
}
```

The API can return localized-ready tokens plus current English text. Templates must avoid repetitive output using deterministic variants selected by event sequence. Commentary cannot reveal hidden attributes.

---

## 9. 2D highlight contract and viewer

### 9.1 Design decision

Do **not** send 100 full frames containing 22 players and a ball. Send compact semantic keyframes and let the Canvas client interpolate with `requestAnimationFrame`. This cuts storage/network cost, scales across display refresh rates, and preserves deterministic replays.

### 9.2 Highlight selection

Create highlights for:

- Every goal.
- Penalties.
- High-quality shots and notable saves above configured thresholds.
- Optional woodwork events.

Cap highlights and payload size per match. All goals always remain. Lower-importance chances may be commentary-only when the cap is reached.

### 9.3 `HighlightPresentationV1`

Each immutable highlight contains:

- Presentation version, source event ID, match minute/sequence.
- Duration target, normally 5–8 seconds.
- Pitch orientation and team colors from safe generated palettes.
- 22 player entities with stable match participant IDs, side, shirt number, and role.
- One ball entity.
- Normalized coordinates from 0–1 or integer 0–10,000.
- Entity keyframes: timestamp, X/Y, optional easing/facing/state.
- More keyframes for involved entities; stationary/default formation anchors for uninvolved players.
- Camera/viewport hint only if needed; MVP can show the whole pitch.
- Outcome metadata for accessibility narration.

Target compressed match-presentation payload below 750 KB and each highlight below 75 KB. Add instrumentation and reject pathological payloads.

### 9.4 Canvas renderer

Create framework-neutral files under:

```text
apps/web/src/app/features/match-viewer/renderer/
├─ canvas-match-renderer.ts
├─ keyframe-interpolator.ts
├─ pitch-layout.ts
├─ render-loop.ts
└─ renderer.models.ts
```

Renderer requirements:

- Scale to CSS pixels and device pixel ratio without changing normalized geometry.
- Interpolate source keyframes at the display refresh rate.
- Draw pitch, teams, ball, shirt numbers, and optional subtle ball trail.
- Keep animation independent from Angular change detection.
- Stop and dispose the animation loop on pause, route change, tab hidden, or component destruction.
- Queue multiple highlights in event order, including the same minute.
- Support 1x, 2x, 4x, pause, skip current highlight, skip all, replay, and seek to event.
- Presentation clock pauses while a normal-speed highlight plays; speed settings consistently affect commentary and animation.
- Respect `prefers-reduced-motion`; provide a text-only mode and static event diagram.
- Expose screen-reader event narration outside the Canvas.
- Do not use color alone to distinguish teams.

### 9.5 Match API split

- `GET /matches/{id}`: summary, score, stats, publication state.
- `GET /matches/{id}/presentation`: immutable commentary and highlights, strongly cached with ETag after publication.
- Hidden match input/simulation diagnostics are manager-inaccessible and admin-protected.
- The standings update immediately on publication. Offer a “hide score until replay starts” preference, but do not promise spoiler isolation across the rest of the game.

---

## 10. API surface

Use `/api/v1`, RFC 9457 Problem Details, stable machine-readable `code`, correlation ID, and field-level validation details. Lists use cursor pagination unless a bounded set is guaranteed. All mutable requests validate content type and size.

### 10.1 Authentication and account

```text
POST   /api/v1/auth/register
POST   /api/v1/auth/verify-email
POST   /api/v1/auth/resend-verification
POST   /api/v1/auth/login
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout
POST   /api/v1/auth/logout-all
POST   /api/v1/auth/forgot-password
POST   /api/v1/auth/reset-password
GET    /api/v1/me
PATCH  /api/v1/me
DELETE /api/v1/me
```

Use short-lived access tokens in memory and a rotating HttpOnly Secure refresh cookie. Enforce exact production origins, refresh-token reuse detection, and CSRF/origin protections. Native clients later replace cookie handling with platform secure storage through a versioned auth adapter.

### 10.2 Onboarding/world

```text
GET    /api/v1/world
GET    /api/v1/countries
GET    /api/v1/countries/{countryId}/capacity
GET    /api/v1/countries/{countryId}/available-clubs
POST   /api/v1/manager-profile
POST   /api/v1/club-claims
POST   /api/v1/club-tenure/resign
GET    /api/v1/club-tenure
```

### 10.3 Club, squad, players, contracts

```text
GET    /api/v1/clubs/{clubId}
GET    /api/v1/clubs/{clubId}/dashboard
GET    /api/v1/clubs/{clubId}/squad
GET    /api/v1/players/{playerId}
GET    /api/v1/contracts
POST   /api/v1/contracts/{contractId}/renewal-quote
POST   /api/v1/contracts/{contractId}/renew
```

Squad response is bounded to 25 players; client-side table sorting is acceptable. Global player search is server-side.

### 10.4 Tactics, team sheets, training

```text
GET    /api/v1/tactics
POST   /api/v1/tactics
PUT    /api/v1/tactics/{planId}
POST   /api/v1/tactics/{planId}/make-default
GET    /api/v1/fixtures/{fixtureId}/team-sheet
PUT    /api/v1/fixtures/{fixtureId}/team-sheet
GET    /api/v1/training
PUT    /api/v1/training
PUT    /api/v1/players/{playerId}/training-focus
```

Require ETag/`If-Match` for plan, sheet, and training updates. Return lock deadline and a validation preview.

### 10.5 Competitions, fixtures, matches

```text
GET    /api/v1/competitions
GET    /api/v1/divisions/{divisionId}
GET    /api/v1/divisions/{divisionId}/table
GET    /api/v1/divisions/{divisionId}/fixtures
GET    /api/v1/divisions/{divisionId}/statistics
GET    /api/v1/fixtures/mine
GET    /api/v1/fixtures/{fixtureId}
GET    /api/v1/matches/{matchId}
GET    /api/v1/matches/{matchId}/presentation
```

There is no public `POST /match/simulate`.

### 10.6 Scouting and market

```text
GET    /api/v1/scouting/players?cursor=&position=&ageMin=&ageMax=&attributes...
GET    /api/v1/shortlist
POST   /api/v1/shortlist/{playerId}
DELETE /api/v1/shortlist/{playerId}
GET    /api/v1/transfers/listings
POST   /api/v1/transfers/listings
DELETE /api/v1/transfers/listings/{listingId}
GET    /api/v1/transfers/listings/{listingId}
POST   /api/v1/transfers/listings/{listingId}/bids
GET    /api/v1/transfers/history
```

Listing, cancellation, and bid commands require `Idempotency-Key`. Deadlines and current leading values always come from server responses.

### 10.7 Finance, inbox, synchronization

```text
GET    /api/v1/finances/summary
GET    /api/v1/finances/ledger?cursor=
GET    /api/v1/inbox?cursor=&unread=
POST   /api/v1/inbox/{messageId}/read
POST   /api/v1/inbox/read-all
GET    /api/v1/sync?cursor=
```

`sync` returns lightweight changed-resource hints and unread counts; it does not duplicate full feature payloads.

### 10.8 Administration

```text
GET    /api/v1/admin/health/game
GET    /api/v1/admin/jobs
POST   /api/v1/admin/jobs/{id}/retry
POST   /api/v1/admin/jobs/{id}/cancel
GET    /api/v1/admin/matchdays/{id}
POST   /api/v1/admin/matchdays/{id}/resume
GET    /api/v1/admin/audit
POST   /api/v1/admin/users/{id}/suspend
POST   /api/v1/admin/users/{id}/restore
POST   /api/v1/admin/clubs/{id}/assign-ai
POST   /api/v1/admin/finance/compensating-entry
POST   /api/v1/admin/announcements
POST   /api/v1/admin/feature-flags/{key}
```

All admin mutations require MFA-authenticated role, explicit reason, idempotency key, and audit event.

### 10.9 DTO and authorization rules

- Never serialize EF entities.
- Commands identify the intended resource, but the server derives manager/club ownership from the authenticated tenure.
- Resource policies enforce club ownership, published-match visibility, private shortlist visibility, and admin roles.
- Hidden potential, PRNG seed, internal valuations, risk flags, email, IP/device hashes, and engine snapshots never appear in player-facing DTOs.
- Return absolute deadlines and server current time to mitigate clock drift.
- Generate TypeScript DTO types from committed OpenAPI; fail CI on unreviewed breaking changes.

---

## 11. Angular PWA plan

### 11.1 Routes and screens

Public:

- `/welcome`
- `/register`, `/verify-email`, `/login`, `/forgot-password`, `/reset-password`
- `/onboarding/manager`, `/onboarding/country`, `/onboarding/club`

Authenticated shell:

- `/dashboard`: next fixture, deadline, condition/injury alerts, table excerpt, cash, inbox, recent result.
- `/squad`: PrimeNG table, virtual scroll only if warranted, sorting/filtering, availability, contract and condition indicators.
- `/players/:id`: attributes, state, history, contract, season stats, shortlist/market action.
- `/tactics`: formation pitch, drag/drop slots, roles, instructions, validation, save/version conflict UX.
- `/training`: team and individual focus.
- `/competitions/:divisionId/table`
- `/competitions/:divisionId/fixtures`
- `/fixtures/:fixtureId/prepare`: opponent summary, deadline, team sheet.
- `/matches/:matchId`: summary, stats, commentary, 2D viewer.
- `/scouting`: global server-side search.
- `/transfers`: listings, bids, own activity/history.
- `/finances`: balance, commitments, category summaries, ledger.
- `/inbox`: categorized messages.
- `/settings`: profile, timezone, reduced motion, score hiding, sessions, account deletion.
- `/admin`: lazy-loaded and role protected.

### 11.2 State and data access

- Use feature-scoped Signal stores/facades, not one global mutable store.
- Use Angular `HttpClient` and RxJS for requests; convert stable view state to Signals.
- Core auth state stores access token in memory only; refresh on bootstrap.
- Cache immutable match presentations and reference data; invalidate mutable club state using ETags/version hints.
- Poll `/sync` every 60 seconds while visible, more frequently near a match/auction only when justified, and suspend in hidden/offline tabs.
- Every command has pending, success, validation, conflict, timeout, and retry UX.
- On `412 Precondition Failed`, show changed server state and let the manager reapply intentionally.
- Do not optimistic-update money, bids, club claims, contract renewals, or fixture locks.

### 11.3 Responsive and accessible UI

- Desktop uses sidebar/navigation and dense tables; mobile uses bottom/top navigation and card/detail layouts.
- Preserve useful text selection; do not apply global `user-select: none`. Restrict it to drag handles and Canvas controls.
- Support safe-area insets.
- Minimum WCAG 2.2 AA for keyboard navigation, focus visibility, contrast, labels, dialogs, tables, reduced motion, and screen-reader status messages.
- Attribute colors (green/yellow/red) must also display numbers/icons/text.
- Tactics board must have an accessible non-drag alternative for assigning player, slot, and role.
- Canvas highlights need text narration and keyboard controls.
- Use responsive performance budgets; do not render a 400-player table when the club squad has at most 25.

### 11.4 PWA/offline boundary

- Cache app shell, fonts/icons, static country/rule reference data, and the most recently viewed published match presentations.
- Display cached data with a clear stale/offline badge.
- Disable and explain mutations while offline; do not queue bids, tactics, lineups, claims, or contract actions.
- Add install manifest, icons, theme colors, update prompt, and service-worker rollback guidance.
- Ensure a newly deployed client and one previous API-compatible client version can coexist during rollout.

---

## 12. Security, privacy, and competitive integrity

### 12.1 Authentication controls

- Email/password only for MVP; social login later.
- Use ASP.NET Core's supported password hasher with an explicitly configured work factor and transparent rehash on login.
- Require verified email before club claim, bidding, listing, or public display-name changes.
- Short-lived access token; rotating refresh session with reuse detection and revocation.
- Generic login/reset responses prevent account enumeration.
- Rate-limit by route, IP prefix, user, and account status.
- Progressive lockout and optional bot challenge after suspicious attempts.
- Require MFA for all support/operator/admin accounts.

### 12.2 Web/API controls

- HTTPS only, HSTS, exact CORS allowlist, restrictive CSP, `frame-ancestors`, MIME protection, referrer policy, and secure cookies.
- Validate all route/body/query fields and reject unknown enum/schema versions.
- Cap body, pagination, search, and match-presentation sizes.
- EF parameterization only; no string-built SQL from request data.
- Protect secrets through environment/provider secret storage; never commit them.
- Use separate database roles for migrations and runtime where hosting permits it.
- Redact tokens, passwords, email tokens, raw email, and JSON snapshots from logs.

### 12.3 Anti-cheat principles

- Clients submit decisions, never outcomes or ratings.
- Match snapshots and simulation run only on the worker.
- Financial reservations and transfers are database transactions.
- Deadlines use server time.
- Every meaningful command records actor, tenure, correlation, version, and source.
- Completed match inputs/outputs are hashed and immutable.
- Bots use the same game constraints as humans.
- Multi-account/collusion signals create review cases; do not expose detection thresholds.
- No real-money trading, premium currency, or purchasable competitive advantage in MVP.

### 12.4 Privacy and account deletion

- Publish privacy/terms versions and record consent.
- Allow export of account/profile/tenure history.
- Deletion first closes the tenure, revokes sessions, then anonymizes account identity after a configured cooling period.
- Preserve anonymized match, transfer, table, finance, and audit records required for competition integrity.
- Set retention periods for raw email delivery events, security/IP hashes, and support data.

---

## 13. Administration and live operations

The operator UI and runbooks MUST support:

- World, country, division, season, matchday, and worker status.
- Queue depth, dead jobs, retries, leases, and oldest overdue job.
- Fixture snapshot/simulation/publication state without exposing hidden data to player roles.
- Division provisioning and backfill progress.
- Auction disputes and finance correlation trails.
- Account suspension/restoration and AI takeover.
- Feature flags and maintenance banners.
- Read-only audit search.
- Compensating finance entries rather than balance edits.
- Safe retry/resume of rollover and matchday workflows.
- Void/replay procedure with reason, preserved original record, and affected-manager notice.
- Game rule/config version display.
- Emergency read-only mode that blocks manager writes while keeping published content available.

Runbooks must define who may act, prechecks, exact action, validation, notification, rollback/compensation, and evidence to retain.

---

## 14. Observability, migrations, backup, and delivery

### 14.1 Observability

- Structured logs include correlation ID, user/manager/club IDs where non-sensitive, job ID, fixture/matchday ID, module, and outcome.
- OpenTelemetry traces cover API requests, database calls, worker jobs, email dispatch, and simulation spans.
- Metrics include HTTP latency/error/rate, DB pool/saturation, job lag/retry/dead count, match simulation duration, matchday publication delay, auction lag, login failures, active managers, club occupancy, payload sizes, and frontend errors.
- SLO starting points:
  - 99.9% monthly API availability excluding announced maintenance.
  - p95 read API under 500 ms and command API under 800 ms excluding long async work.
  - 99% of matchdays published within five minutes of scheduled kickoff.
  - No lost accepted bid, club claim, published fixture, or finance posting.
- Alerts must be actionable and linked to runbooks.

### 14.2 Database migrations

- EF Core migrations live in Infrastructure and are reviewed as SQL in CI.
- Each migration has forward validation and rollback/compensation notes.
- Use expand/migrate/contract for breaking changes; never deploy code that requires a destructive same-step migration.
- Seed only stable reference/rule data in migrations. World generation is an explicit idempotent application tool/job.
- Production migrations run as a controlled pre-deploy job using the migration database role.
- Take/verify a backup before high-risk migrations and season-engine data changes.

### 14.3 Backup and restore

- Enable daily full backup plus PITR on the managed PostgreSQL plan.
- Define retention and off-provider export requirements before public launch.
- Test restore into an isolated environment at least before beta and before public launch.
- Verify restored job leases are cleared safely, outgoing email is disabled, secrets differ, and workers do not execute production deadlines.
- Keep engine/rule builds and container digests needed to replay historical matches.

### 14.4 CI/CD

Pull-request pipeline:

1. Formatting and static analysis.
2. Restore with lockfiles.
3. .NET build with warnings as errors.
4. Angular strict typecheck/lint/build.
5. Unit, architecture, and component tests.
6. PostgreSQL Testcontainer integration/API/worker tests.
7. OpenAPI generation and breaking-change check.
8. Match golden/property tests.
9. Playwright critical flows.
10. Dependency/license/secret/container vulnerability scanning.
11. Production web and container builds with size budgets.

Deployment pipeline:

1. Deploy automatically to staging.
2. Run migration preflight and staging smoke/E2E.
3. Require approval for production.
4. Run controlled migration.
5. Deploy worker in compatibility mode, then API, then PWA.
6. Run smoke tests and monitor release metrics.
7. Roll back code safely; use forward-fix/expand-contract for migrated data.

Maintain local, test, staging, and production environments with separate databases, email settings, secrets, domains, and generated worlds.

---

## 15. Testing strategy

### 15.1 Unit and property tests

- Domain aggregate invariants for claims, tenures, squad size, contracts, tactics, bids, reservations, discipline, promotion, and finance.
- Match formulas and stable PRNG sequences.
- Schedule generation property tests across many seeds.
- Table calculation and all tie-break paths.
- Money arithmetic and ledger conservation.
- Keyframe interpolation bounds.
- Player/world generators: uniqueness constraints, roster quotas, attribute ranges, deterministic output.

### 15.2 Integration tests with real PostgreSQL

Use Testcontainers, never substitute an in-memory EF provider for database behavior. Cover:

- Migrations from empty and previous release schema.
- Partial unique indexes and checks.
- Concurrent club claim: exactly one winner.
- Last club claim creates exactly one next-tier request.
- Concurrent bids reserve/release the correct balances.
- Auction retry never charges twice.
- `SKIP LOCKED` job leases and lease expiry.
- Fixture lock versus tactic/transfer concurrency.
- Matchday retry and atomic nine-fixture publication.
- Season rollover resume after each checkpoint.
- Standings rebuild equals projection.
- Account deletion/anonymization retains competition records.

### 15.3 Match-engine validation

- Golden fixtures pin output hashes for every engine version.
- Same input/seed produces byte-identical canonical output across repeated runs and supported OS/architecture targets.
- Changed seed changes outcomes without invalid state.
- 100,000+ simulation distribution suite checks configured statistical ranges.
- Fuzz/property tests reject malformed input or produce valid bounded events.
- Benchmark cold/warm execution, allocations, matchday throughput, and serialization independently.
- Event-derived score/stats must reconcile exactly with output aggregates.

### 15.4 API and contract tests

- Authentication lifecycle, token rotation/reuse, lockout, authorization, CORS/CSRF assumptions.
- Every manager endpoint tested with no tenure, own club, other club, suspended account, stale ETag, and expired session where relevant.
- Idempotency replay and mismatched-request rejection.
- RFC Problem Details shape and stable error codes.
- OpenAPI compatibility and generated TypeScript type build.
- Rate limits and request-size limits.

### 15.5 Frontend and E2E tests

Unit/component:

- Signal stores and state transitions.
- Deadline/timezone formatting with fake time.
- Tactics validation and keyboard assignment.
- Table filters and non-color attribute indicators.
- Match playback controls, disposal, reduced motion, same-minute highlights.
- Offline command prevention and stale-data banner.

Playwright journeys:

1. Register → verify → login → manager → country → claim club.
2. Review squad → set valid tactic/team sheet → survive version conflict.
3. Worker lock/simulate test hook → table/result/inbox update → watch commentary/highlights.
4. List player → competing bids → outbid/refund → resolve winner → finance/squad update.
5. Training/contract renewal/injury/suspension displays.
6. Inactivity and takeover recovery.
7. Full season rollover with promotion/relegation in a compressed test clock.
8. PWA installability, offline read shell, blocked offline mutation.

### 15.6 Accessibility, security, and performance

- Automated axe checks plus manual keyboard/screen-reader checks for core routes.
- OWASP dependency and dynamic baseline scans in staging.
- Threat-model review before beta and public launch.
- k6 or equivalent API/load scenarios for login bursts, dashboard reads, matchday polling, concurrent bids, and worker publication.
- Test at projected launch population with at least 3x headroom.
- Frontend budgets for initial JS, route chunks, Core Web Vitals, Canvas frame pacing, and memory leaks.

---

## 16. Incremental implementation stages

Each stage ends with passing CI, updated documentation, migration validation, and a demonstrable artifact. Do not start a later domain feature while unresolved correctness failures remain in an earlier stage.

### Stage 0 — Product rules, architecture, and executable specifications

**Dependencies:** None.

**Deliverables/tasks:**

- Adopt this plan as `docs/product/master-plan.md` during implementation.
- Write ADRs for modular monolith, auth/session model, PostgreSQL jobs, deterministic engine, dynamic pyramid/backfill, semantic highlights, PWA-first delivery, and deployment topology.
- Create `game-rules.md` with every configurable value from section 3.
- Create C4 context/container diagrams and first ER diagram.
- Define glossary: world, country, division, division-season, game year, tenure, fixture, matchday, snapshot, publication, active human.
- Define English UI/content tone and fictional-data/legal rules.
- Create threat model and data classification.
- Resolve only genuine blockers discovered while formalizing rules; do not silently alter product behavior.

**Verification/exit:**

- No contradictory rule remains across plan, rules, ADRs, and ER model.
- Every public MVP feature maps to a module and planned stage.
- Security and operations review the deadline-critical workflows.

**Deferred:** All production code except minimal schemas/prototypes needed to validate decisions.

### Stage 1 — Monorepo scaffold and engineering guardrails

**Dependencies:** Stage 0.

**Deliverables/tasks:**

- Create the repository tree, pinned SDK/runtime/package versions, `.editorconfig`, build props, lockfiles, strict analyzers, and root README.
- Scaffold Domain, Application, Infrastructure, Contracts, MatchEngine, API, Worker, Angular PWA, and test projects.
- Add Docker Compose PostgreSQL and mail catcher.
- Add health endpoints, configuration validation, correlation middleware, Problem Details, structured logging, fake clock seams, and empty module endpoint groups.
- Configure Angular standalone shell, PrimeNG/Tailwind layer order, responsive layout skeleton, PWA manifest/service worker, and strict CSP-compatible build.
- Add architecture tests and initial GitHub Actions PR workflow.
- Add one no-op durable job end-to-end to prove API/database/worker composition.

**Verification/exit:**

- One documented command starts database, API, worker, and web locally.
- CI builds all projects and runs tests from a clean checkout.
- API and worker health distinguish liveness/readiness and database state.
- Architecture tests enforce dependencies.

**Deferred:** Business gameplay and production deployment.

### Stage 2 — Identity and authenticated walking skeleton

**Dependencies:** Stage 1.

**Deliverables/tasks:**

- Implement auth schema/migrations and email provider abstraction.
- Implement registration, verification, login, refresh rotation/reuse detection, logout, forgot/reset, account profile, session revocation, and rate limits.
- Add access-token refresh bootstrap and route guards in Angular.
- Build accessible auth/settings screens and fake-email local workflow.
- Implement roles, admin MFA groundwork, consent versions, audit middleware, and account status policies.
- Add exact CORS/CSP/cookie configuration per environment.

**Verification/exit:**

- Full auth lifecycle passes API integration and Playwright tests.
- Raw tokens and passwords never appear in database/log snapshots.
- Suspended and unverified accounts have correct restrictions.
- Refresh replay revokes its token family.

**Deferred:** Club/game data.

### Stage 3 — World generation, six countries, clubs, and onboarding

**Dependencies:** Stage 2.

**Deliverables/tasks:**

- Add world, country, manager, club, tenure, season, division, generation, and capacity schema.
- Build deterministic fictional name dictionaries and procedural badge seeds with legal review notes.
- Implement world-seeder CLI/job: six countries, one tier each, 18 clubs per tier, season shell, finance accounts.
- Implement manager profile, country capacity, available clubs, atomic takeover, resignation/cooldown, and inherited-club dashboard.
- Implement AI control derivation and capacity evaluation that creates provisioning requests, but defer full backfill execution to Stage 11.
- Build onboarding UI with race-safe conflict/capacity handling.

**Verification/exit:**

- Reusing a generation seed produces the same logical world.
- Exactly 108 initial clubs exist with unique valid identities.
- Concurrent claim test produces one winner and no duplicate tenure.
- One user/manager and one club cannot have multiple active tenures.
- Onboarding completes on desktop and mobile viewport.

**Deferred:** Players, matches, generated lower tiers.

### Stage 4 — Squads, player profiles, contracts, tactics, and training foundations

**Dependencies:** Stage 3.

**Deliverables/tasks:**

- Add player, attribute, state, contract, registration, unavailability, tactics, team-sheet, and training schema.
- Extend generator to create 22 balanced players per club (approximately 2,376 initial players), positional quotas, age distribution, attributes, state, and contracts.
- Implement squad and player APIs/screens with sorting/filtering and accessible attribute display.
- Implement tactical presets, drag/drop and keyboard alternative, roles/instructions, default-plan validation, ETag conflicts.
- Implement fixture-independent draft/default lineup editing first.
- Implement training plan and individual focus persistence; build the deterministic daily progression handler behind a feature flag.
- Add contract list and renewal quote/accept foundation.

**Verification/exit:**

- Every club has a legal 22-player squad with two goalkeepers and one active contract/registration per player.
- Tactical validator accepts all presets and rejects duplicates, invalid coordinates, unavailable players, and wrong starter counts.
- Scouting-relevant attribute queries use indexed columns.
- Daily progression is deterministic and bounded.

**Deferred:** Fixtures, match effects, full finances, transfers.

### Stage 5 — Pure match engine and simulation laboratory

**Dependencies:** Stage 4 domain contracts; can overlap UI work only after contracts settle.

**Deliverables/tasks:**

- Implement `Pcg32`, canonical input/output, engine rules V1, unit ratings, possession/chance/set-piece/discipline/injury/substitution logic, structured events, stats, and hashes.
- Implement deterministic AI lineup repair and substitution planner.
- Implement commentary token generation and English templates.
- Implement semantic `HighlightDirector` with keyframe schema.
- Create CLI/benchmark tool to run single matches and large distributions.
- Tune initial generated squads and engine config from measured distributions.
- Version and document every formula/config field.

**Verification/exit:**

- Golden determinism tests pass across local and Linux CI.
- Event/stat/score invariants pass property/fuzz suites.
- Statistical distributions are within documented target ranges.
- p95 simulation and allocation benchmarks meet the recorded MVP budget on reference hardware.
- Engine project has no infrastructure/time/global-random dependency.

**Deferred:** Database publication and viewer UI.

### Stage 6 — Season schedule, fixture preparation, and durable matchday worker

**Dependencies:** Stages 4–5.

**Deliverables/tasks:**

- Add matchday, fixture, team-sheet, snapshot, match, event, highlight, job, outbox, and statistics migrations.
- Implement 34-round deterministic schedule generation for all initial divisions.
- Create fixture list, next-fixture dashboard, prepare-match screen, fixture-specific team sheet, and lock countdown.
- Implement durable job queue, leases, retries, metrics, and admin inspection.
- Implement lock/snapshot workflow and immutable hash/seed commitment.
- Implement staged simulation and atomic nine-fixture division publication.
- Apply table, stats, cards, injuries, fatigue, morale, gate receipts, inbox events, and cache invalidation on publication.
- Add controlled compressed test clock in non-production environments only.

**Verification/exit:**

- Schedule properties hold for all six divisions.
- A complete test matchday locks, simulates, publishes, and updates all projections.
- Killing/restarting the worker at lock, simulation, and publication boundaries causes no duplicate or lost result.
- Partial fixture simulation never leaks a partial table.
- The application now constitutes the first server-authoritative playable management vertical slice.

**Deferred:** Full match viewer, transfers, rollover, lower-tier provisioning.

### Stage 7 — Text match center and 2D highlights (closed vertical slice)

**Dependencies:** Stage 6.

**Deliverables/tasks:**

- Implement match summary/presentation DTOs, immutable cache headers, payload limits, and public/private field filtering.
- Build commentary timeline, match clock, score preference, statistics, 1x/2x/4x, pause, seek, skip, replay, and event navigation.
- Build the semantic keyframe Canvas renderer with DPI scaling, interpolation, disposal, responsive pitch, team distinction, and frame metrics.
- Implement same-minute queueing, tab visibility handling, reduced-motion/text-only/static modes, and screen-reader narration.
- Add PWA caching of recently viewed published presentations.

**Verification/exit:**

- Every goal links to a correct highlight; match score and sequence reconcile.
- Renderer remains smooth on the agreed low/mid mobile test device and desktop, with no animation loop after navigation.
- Keyboard/screen-reader and reduced-motion acceptance tests pass.
- Compressed presentation payloads meet budgets.
- A test manager can prepare and watch a complete scheduled fixture end-to-end.

**Deferred:** More sophisticated camera/graphics and live tactical changes.

### Stage 8 — Competition depth, discipline, injuries, and AI match management

**Dependencies:** Stages 6–7.

**Deliverables/tasks:**

- Complete tables, fixture/results history, club/player season stats, tie-break views, and competition rules UI.
- Complete suspension accumulation/service and injury recovery workflows.
- Implement deterministic AI default lineup, tactics, substitutions, training, and contract renewal policy using the same validators as humans.
- Add news/inbox templates for results, injuries, cards, deadlines, and table movement.
- Add projection rebuild and reconciliation tools.

**Verification/exit:**

- AI clubs always submit/recover a legal side without privileged attributes or bypasses.
- Suspensions and injuries affect the correct future fixtures exactly once.
- Rebuilt tables/stats equal live projections.
- Tie-break test cases cover every ordered rule.

**Deferred:** AI transfer-market behavior until Stage 10.

### Stage 9 — Contracts and basic club finances

**Dependencies:** Stage 8.

**Deliverables/tasks:**

- Implement finance account/ledger, opening balances, gate income, weekly sponsorship, wages, operating costs, promotion/position awards, reservations, and finance reporting.
- Complete deterministic renewal quote/accept/decline and rollover expiry behavior.
- Add dashboard warnings for expiring contracts, payroll risk, and minimum-squad risk.
- Implement emergency grant/replacement safety paths with alerts and audit.
- Add finance administration with compensating entries only.

**Verification/exit:**

- Ledger replay exactly reconstructs cash/reserved balances.
- Every financial handler is idempotent.
- Weekly finance and contract operations survive retries.
- No normal command can create negative cash or reserved balances.
- Long-running balance simulations show the six initial leagues remain viable under expected behavior.

**Deferred:** Loans, debt, facilities, negotiated sponsorship.

### Stage 10 — Scouting, timed auctions, and AI transfer market

**Dependencies:** Stage 9.

**Deliverables/tasks:**

- Implement indexed global player search, profiles, private shortlists, filters, cursor pagination, and valuation display policy.
- Implement listings, eligibility checks, fixed resolution windows, visible leading bids, minimum increments, reservations, outbid release, cancellation rules, and transactional resolution.
- Implement player transfer/contract/registration/finance update as one operation.
- Implement AI roster analysis, listings, valuations, bids, and budgets.
- Add market history, inbox/news, collusion signals, idempotency, rate limits, and admin trace views.

**Verification/exit:**

- Concurrent bid/resolution tests have deterministic winners and exact money movement.
- Worker retry cannot double-transfer or double-pay.
- Seller/buyer squad limits and locked fixture snapshots remain valid.
- AI uses no privileged finance and produces a healthy measured market.
- Full Playwright seller/bidder/outbid/winner flow passes.

**Deferred:** Negotiations, loans, swaps, clauses, agents, transfer windows.

### Stage 11 — Automatic pyramid growth, AI vacancies, inbox, and inactivity

**Dependencies:** Stages 8–10.

**Deliverables/tasks:**

- Implement full country-scoped division provisioning job and deterministic lower-tier strength scaling.
- Generate 18 AI clubs, 396 players, finances, entries, fixtures, and backfilled AI results for the current season.
- Validate backfilled standings/player state/finance before activating claims.
- Implement lowest-tier-only onboarding, provisioning status/polling, and concurrency protection.
- Complete inbox/news center, unread sync polling, email notification preferences, and deadline reminders.
- Implement inactivity warnings, temporary AI assistance, tenure closure, return control, and suspension behavior.

**Verification/exit:**

- Filling tier 1 creates exactly one tier 2; filling tier 2 creates exactly one tier 3 using the generic path.
- Newly active tier aligns to current matchday and has valid historical results.
- Concurrent managers at capacity receive deterministic claim/provisioning outcomes.
- Losing occupancy never deletes a tier.
- Inactive/abandoned clubs preserve every competitive and financial commitment.

**Deferred:** Manager job applications to upper-tier vacancies.

### Stage 12 — Season rollover, promotion/relegation, aging, and continuity

**Dependencies:** Stages 9–11.

**Deliverables/tasks:**

- Implement the resumable rollover state machine, preflight/reconciliation, freeze controls, position awards, promotion/relegation, game-year increment, aging, contract expiry, retirements/replacements if enabled, discipline reset, and new schedule generation.
- Implement season history, club history, player career stats, promotion/relegation news, and next-season dashboard.
- Add operator preview/dry-run and resume controls.
- Run multiple compressed seasons in staging with human/AI mixes and dynamic tiers.

**Verification/exit:**

- Failure injection after every checkpoint resumes without duplicated movement, money, aging, or fixtures.
- Three-up/three-down works at every adjacent active tier; lowest tier does not relegate.
- Historical entries/results remain immutable and queryable.
- Every club begins the new season with a legal squad, finance account, default tactic, and 34-fixture schedule.
- At least five consecutive automated staging seasons reconcile cleanly.

**Deferred:** Cups, continental qualification, complex retirements/youth replacement system.

### Stage 13 — Feature-complete MVP UX and PWA hardening

**Dependencies:** Stages 2–12.

**Deliverables/tasks:**

- Complete all routes, navigation, dashboard cards, loading/empty/error/conflict states, local-time deadlines, notification preferences, session management, account export/deletion.
- Finish responsive mobile/tablet/desktop layouts and touch targets.
- Complete WCAG 2.2 AA remediation, keyboard flows, accessible tables/modals/tactics/viewer.
- Implement service-worker update UX, cached read boundaries, stale banners, and offline mutation blocking.
- Add product analytics limited to privacy-safe operational funnels.
- Create guided onboarding/help for rules, deadlines, tactics, market, and season cadence.

**Verification/exit:**

- Full core E2E suite passes at desktop and mobile breakpoints.
- Manual accessibility review has no critical/high issue.
- PWA install/update/offline-read tests pass.
- No route exposes another manager's private data or forbidden controls.

**Deferred:** Native store packages and localization beyond localization-ready code.

### Stage 14 — Security, load, operations, and disaster-recovery hardening

**Dependencies:** Feature-complete MVP.

**Deliverables/tasks:**

- Complete threat-model mitigations, admin MFA, rate limits, CSP/HSTS/CORS, secret rotation, dependency/container scans, privacy workflows, collusion review queue, and support permissions.
- Add complete telemetry dashboards, SLO alerts, maintenance/read-only mode, and runbooks.
- Execute load tests at 3x projected launch population, matchday burst, and auction contention; tune indexes/pools/concurrency from evidence.
- Test backup/PITR restore and historical engine artifact retention.
- Test deployment rollback and API/PWA compatibility.
- Conduct staging game-day and rollover incident exercises.

**Verification/exit:**

- No unresolved critical/high security finding.
- Load targets and matchday publication SLO pass with headroom.
- Restore drill meets documented integrity checks.
- On-call operator can diagnose and resume failed matchday, auction, provisioning, and rollover workflows from runbooks.

**Deferred:** Premature multi-region or microservice work.

### Stage 15 — Closed beta release

**Dependencies:** Stage 14.

**Deliverables/tasks:**

- Deploy production-like staging/closed-beta world with invite-gated accounts.
- Seed all six countries and run real Tuesday/Thursday/Sunday cadence.
- Instrument onboarding completion, retention, lineup validity, match distributions, financial health, auction liquidity, AI behavior, job lag, and support volume.
- Provide feedback/report tools without public chat.
- Fix integrity, usability, balance, and operational defects; version rules/engine when behavior changes.
- Freeze public-MVP scope and publish player-facing rules/privacy/terms/status/support pages.

**Verification/exit:**

- At least one complete closed-beta season and rollover succeeds under human activity.
- No unresolved data-loss, duplicate-money, duplicate-club, result-integrity, or deadline-critical defect.
- Balance metrics are within documented acceptable ranges.
- Support and incident processes are usable.

**Deferred:** Feature requests not required to correct the core loop.

### Stage 16 — Public MVP launch

**Dependencies:** Successful closed beta and explicit go-live review.

**Deliverables/tasks:**

- Create clean production world or documented beta carry-over decision.
- Verify domains, email reputation, backups, alert routing, capacity, legal pages, admin accounts, feature flags, status page, and rollback plan.
- Run seeder/preflight and publish the first matchday calendar.
- Open registration gradually with a feature flag/rate cap.
- Monitor claim/provisioning, matchday, market, finance, and worker SLO dashboards.
- Publish release notes and known limitations.

**Verification/definition of launch:**

- A new verified account can claim a club, manage every core system, compete on schedule, watch 2D highlights, use the market, complete a season, and continue after promotion/relegation without manual database intervention.
- Operations can recover all critical workflows using supported tools.

### Stage 17 — Management depth after MVP

Add in separate feature flags and migrations:

- Staff roles, recruitment, contracts, and effects.
- Youth academy, annual intake, development squads, and homegrown rules.
- Facilities/stadium improvements.
- Richer morale, promises, player personalities, captaincy, and team cohesion.
- More formations, roles, set-piece plans, and conditional substitution instructions.
- Scout assignments and uncertain attribute reports.

### Stage 18 — Expanded competitions

- Domestic cups with seeded draws and extra time/penalties.
- Continental qualification and competitions across the six countries.
- Competition-specific discipline/registration/rules.
- Friendlies and optional preseason.
- Country-specific fictional rule variations only after generic rules remain stable.

### Stage 19 — Market and community expansion

- Private club-to-club offers, negotiation, loans, clauses, installments, transfer windows, free-agent offers.
- Manager job market and upper-tier vacancy applications.
- Friends, rivalries, achievements, profiles, moderated messaging/community features.
- Anti-abuse/moderation tools before any user-generated communication.

### Stage 20 — Native Android client

- Add Ionic Capacitor around the proven Angular PWA.
- Implement secure native token storage, lifecycle/network handling, deep links, push notifications, haptics, Android signing, privacy forms, Play testing tracks, and device matrix.
- Do not use `npx cap sync android` as the bundle acceptance criterion; CI must build and sign an `.aab` for the intended track.

### Stage 21 — iOS, localization, and scale evolution

- Add iOS secure storage, APNs, signing, App Store compliance, and device testing.
- Translate UI/commentary/content through existing keys and pluralization; start with English and Romanian if product priorities remain unchanged.
- Partition/archive high-volume match/event/audit data only when metrics require it.
- Extract services or add read replicas/caches only after profiling identifies a specific bottleneck.
- Evaluate additional worlds/regions while preserving deterministic season operations.

---

## 17. AI implementation-agent operating rules

1. Work on one stage and one coherent vertical slice at a time.
2. Before coding a stage, inspect current repository state, relevant ADRs, migrations, tests, and the prior stage's exit criteria.
3. If a discovered ambiguity changes money, deadlines, competition fairness, ownership, security, or persistent history, stop and request a decision; do not silently choose.
4. For minor implementation detail, follow this plan, document the assumption in code/tests, and continue.
5. Every persisted model change requires a migration, constraints/index review, seed impact review, and integration test.
6. Every deadline job requires an idempotency business key, retry behavior, failure classification, metrics, and an operator recovery path.
7. Every manager command requires authentication, resource authorization, validation, concurrency behavior, and stable error tests.
8. Keep match-engine changes versioned. Never alter a released engine version in place.
9. Keep generated content reproducible by seed and generator version.
10. Do not bypass domain rules in AI-manager or admin code. Admin repairs are explicit, audited, and compensating.
11. Update OpenAPI, generated types, documentation, and E2E tests with contract changes.
12. Keep feature-incomplete routes behind flags and inaccessible in production.
13. Run focused tests while developing and the full stage gate before marking a stage complete.
14. Never mark a stage complete with failing tests, unapplied migrations, unresolved dead jobs, or missing exit criteria.
15. Create small reviewable commits/checkpoints labeled by stage and feature; do not combine unrelated formatting/refactors with domain behavior.
16. Record architectural changes as ADRs, not only in commit messages.
17. Never use production personal data in local/test fixtures.
18. Do not optimize or split services without benchmark evidence.
19. Do not add post-MVP features while a public-MVP integrity requirement remains incomplete.
20. Keep `docs/product/game-rules.md`, `docs/architecture/data-model.md`, and operations runbooks synchronized with executable behavior.

---

## 18. Principal risks and mitigations

| Risk | Mitigation |
|---|---|
| MMO scope overwhelms the first playable build | Use Stages 1–7 for a narrow internal vertical slice; public launch waits for complete core stages. |
| Scheduled jobs run twice or are missed | PostgreSQL durable queue, leases, unique business keys, idempotent handlers, metrics, and recovery tools. |
| Match results are exploitable or non-repeatable | Server-only immutable snapshots, stable PRNG, engine versions, hashes, golden tests, and audit. |
| Dynamic tier creation corrupts a live season | Country lock, provisioning state machine, deterministic backfill, validation before activation, monotonic tiers. |
| Auction races duplicate players or money | Serializable resolution, reservations, ledger correlation keys, unique constraints, concurrency tests. |
| New manager inherits a broken/unplayable club | AI maintenance, minimum-squad invariants, dashboard warnings, emergency integrity safety path. |
| AI clubs distort competition/market | Same constraints as humans, versioned deterministic policies, simulation telemetry, balance tests. |
| Accelerated seasons conflict with real-world age/contracts | Separate game year/season from UTC; age and contract years only at rollover. |
| Match payloads become too large | Semantic keyframes, payload caps, Brotli, metrics, immutable caching. |
| Mobile UI becomes a squeezed desktop table | Responsive information architecture, mobile cards/details, touch/keyboard alternatives, device tests. |
| Free hosting causes downtime/data loss | No zero-cost promise; use paid always-on worker/API and managed PostgreSQL with PITR. |
| Single database becomes a bottleneck | Correct indexes, bounded queries, projections, job concurrency limits, load tests; scale only from evidence. |
| Inactivity empties upper tiers | AI immediately preserves club operation; tiers never collapse; clubs remain promotable/relegatable. |
| Fictional names accidentally copy real identities | Curated/generated source dictionaries, similarity checks, no real badges/marks, legal/content review. |
| Balance makes finances or squads collapse over seasons | Multi-season simulations, reconciliation metrics, feature flags, versioned rule tuning, emergency alerts. |
| PWA/client version mismatch during deploy | Backward-compatible API window, contract tests, service-worker update UX, phased deployment. |
| Admin repair damages trust | Least privilege, MFA, reasons, immutable audit, dry run, compensating actions, player notification. |

---

## 19. Public MVP definition of done

The MVP is done only when all statements below are true:

### Product/gameplay

- Six fictional country pyramids exist with one initial 18-club tier each.
- Human managers can register, verify, take over AI clubs, resign, become inactive, and return under documented rules.
- A country adds tier N+1 exactly once when tier N fills with humans, and the new tier is aligned/backfilled safely.
- Managers can use squad, player, tactics, lineup, training, injuries, discipline, contracts, scouting, auctions, finances, inbox, fixtures, tables, stats, match viewer, and season history.
- Tuesday/Thursday/Sunday fixtures lock and resolve without client action.
- Matches have coherent text commentary and accessible 2D highlights.
- Promotion/relegation and season rollover work across all active tiers.

### Integrity/reliability

- Clients cannot simulate results, change balances, bypass ownership, or write after lock.
- Match output is deterministic for a frozen version/input/seed.
- Claims, bids, finance, transfers, publication, provisioning, and rollover are idempotent and concurrency tested.
- Worker restart/failure does not lose accepted commands or duplicate outcomes.
- Database constraints protect central invariants.

### Quality

- Unit, property, integration, API, worker, OpenAPI, E2E, accessibility, security, load, migration, backup/restore, and determinism gates pass.
- Core flows meet WCAG 2.2 AA.
- Responsive PWA works on the agreed browser/device matrix.
- Performance and payload budgets are measured and documented.

### Operations

- Production has monitored API, worker, database, backups/PITR, transactional email, DNS/TLS, secrets, alerts, status/support paths, and cost model.
- Operators can inspect/retry jobs, resume matchdays/rollovers, handle suspensions, and make audited compensating repairs.
- Restore and incident drills have succeeded.
- Privacy, terms, consent, export, anonymization, and retention behavior are implemented.

---

## 20. Requirements traceability checklist

| Requirement | Primary stages |
|---|---|
| Old-school core management loop | 4, 8, 9, 10, 13 |
| Online accounts and real managers | 2, 3 |
| Persistent MMO/server authority | 3, 6, 11, 12, 14 |
| Top five European countries plus Romania | 3 |
| One initial division per country | 3 |
| Add second/third/further tier as lower tier fills | 11 |
| AI vacant clubs and inherited state | 3, 8, 11 |
| Three matchdays weekly | 6 |
| Squad/player management | 4 |
| Lineups, formations, roles, tactics | 4, 6 |
| Training, fatigue, condition, morale | 4, 6, 8 |
| Injuries, cards, suspensions | 5, 6, 8 |
| Fixtures, tables, statistics | 6, 8 |
| Deterministic server match engine | 5, 6 |
| Text commentary | 5, 7 |
| 2D highlights | 5, 7 |
| Scouting/search/shortlists | 10 |
| Timed transfer auctions | 10 |
| Contracts and wages | 4, 9 |
| Basic finances | 9 |
| Inbox/news | 6, 8, 11 |
| Inactivity handling | 11 |
| Promotion/relegation/season rollover | 12 |
| Responsive installable PWA | 1, 7, 13 |
| Fully fictional data | 3, 4 |
| Android and iOS after MVP | 20, 21 |
| Staff/youth/cups/social later | 17, 18, 19 |
| Security/admin/observability/backups | 2, 6, 14 |

---

## 21. First implementation action

Begin with **Stage 0**, not framework scaffolding. Convert the settled rules into versioned product documents and ADRs, then produce the initial ER diagram and threat model. Once the Stage 0 consistency review passes, create the Stage 1 monorepo and walking skeleton exactly within the module boundaries above.
