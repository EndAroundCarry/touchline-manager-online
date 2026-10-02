# Touchline Manager

An old-school football management game with a modern online structure: a persistent,
server-authoritative MMO where managers prepare a club asynchronously and compete against each
other on fixed matchdays. Every club, player, competition and badge is fictional.

**Matchdays:** Tuesday, Thursday and Sunday at 19:00 UTC. Team sheets lock 30 minutes before
kick-off. The server decides results; a client can never simulate or influence one.

> **Status: Stage 15 underway — the non-production stepped clock and its test toolbar, and the player-facing rules, privacy, terms, status and support pages; the engine roadmap's continuous-replay milestone has landed — `engine-v4` plays a possession along a real passage and the match center replays it as one continuous film with a highlights reel. Stage 14 is complete: roles, TOTP MFA, the gated admin surface, the operator read console, the recovery commands, the remaining §10.8 mutations (AI assignment, finance repair, announcements, and feature flags), the load, supply-chain, and restore drills, the read-only incident switch, and the local telemetry stack with its dashboards and SLO alerts.** Stage 13 is
> complete: the responsive PWA, account sessions, the offline boundary, the accessibility gate, the
> guided help, and the privacy-safe operational funnels. The playable
> game is being built in the staged order defined in the master plan. Stage 1 delivered the monorepo, the durable job
> pipeline, the API and worker composition roots, the health and observability baseline, and the Angular
> PWA shell. Stage 2 added the account schema, the full credential lifecycle, rotating refresh sessions
> with reuse detection, the audit trail, the request security headers, the Angular auth and settings
> screens, and the end-to-end journeys. Stage 3 added deterministic world generation, six fictional
> national pyramids, atomic club takeover with pyramid expansion, and the onboarding screens. Stage 4 added
> the `squad` schema, a legal twenty-two-player squad per club, the squad, player and contract screens, the
> tactics board and its ETag contract, and the training screen with its deterministic daily progression
> job. Stage 5 added the match engine: a pure, versioned, hash-pinned simulation of one fixture from a
> frozen snapshot, with commentary tokens, semantic highlights, and a simulation laboratory for tuning.
> Stage 6 turned that engine into a season: a 34-round fixture calendar, the fixture and prepare-match
> screens, the durable matchday worker that locks a round's team sheets, simulates its nine fixtures from
> frozen snapshots, and publishes results and table together, the division table screen, and a compressed
> test clock for non-production environments. Stage 7 added the match center: the score, statistics and
> commentary over HTTP, a deterministic replay re-derived from the frozen snapshot behind an immutable
> cache, and a Canvas viewer that plays the keyframe highlights. Stage 8 gave a result its consequences:
> publication turns a round's cards and injuries into each player's season accumulation and into
> suspensions and absences measured in fixtures, loads every participant's condition, fatigue, and morale,
> advances the season's statistics, and writes each manager an inbox report; a club no manager holds is set
> up by a deterministic policy, and a division's projections can be reconciled and its rules, statistics,
> and discipline read. Stage 9 made a club's money an append-only ledger: every balance change is a
> `finance.ledger_entries` row, an account is the projection those rows sum to, and a club's opening
> balance is its first entry — so a replay reproduces its cash and reserved funds exactly (`FIN-18`), and
> the weekly run charges wages, pays sponsorship, and grants an emergency shortfall when a club cannot pay.
> Stage 10 added the transfer market: server-side scouting and private shortlists, listings that resolve at
> a daily window under a reservation held in the ledger, and an AI market that trades through the same
> writers and affordability rules a manager's command uses. Stage 11 made the pyramid grow by itself — a
> full tier provisions the next through the worker, generated, backfilled with deterministic bootstrap
> results, validated, and activated before it is claimable — and completed the comms module with the news
> feed, notification preferences, deadline reminders, and an outbox that carries email off the request
> path; a tenure that goes quiet now warns, hands routine decisions to the AI, and finally closes and
> returns the club. Stage 12 is the season's end and the next season's beginning: a world-scoped, resumable
> rollover state machine that freezes the season, finalizes its standings, closes its entries, applies
> three-up/three-down between every adjacent active tier, and generates the next season's entries, fixtures,
> and opening table — all or nothing, and resumable after every checkpoint (`PR-1`–`PR-6`, ADR-0031). Its
> continuity work — contract expiry, retirement, position awards, and the season finance summary — landed
> next, and the operator controls now give a non-production operator a dry-run preview of what a rollover
> would do, a run-now trigger, and an audited resume of a failed rollover (`PR-4`, ADR-0034). The stage
> closes with a five-season staging run: five consecutive seasons rolled over by the worker alone, each
> reconciles and opens a complete next season, over a seeded two-tier world with a human/AI mix
> (`StagingSeasonRunTests`, ADR-0035). Stage 13 has since completed the account surface — sessions,
> machine-readable export, and time-zone preferences (ADR-0036) — and the PWA's update prompt, stale-read
> label, and offline mutation blocking (ADR-0037). Its responsive work is now done too: below `md` the
> sidebar becomes a header menu, the dense screens present cards instead of tables, every control is at
> least 44px tall, and the core Playwright suite runs at desktop and mobile breakpoints with a tablet
> project for the layout boundary (`F-44`, ADR-0038). Accessibility is now enforced too: an axe gate over
> the core routes at desktop and mobile, and the gaps it found fixed — the squad roster named, a tactics
> slot moved with the arrow keys, scrollable table regions reachable by keyboard, and focus following the
> navigation (`F-52`, ADR-0039). The guided help is the stage's fifth milestone: a `/help` reference that
> states the rules a manager needs and links to the screen that owns each, first-steps guidance on the
> dashboard that can be dismissed, and deadlines that now name their time zone (`F-53`, ADR-0040). The
> stage closes with its analytics deliverable: the onboarding and retention funnels, read as counts over
> rows the game already writes, behind the product's first role-gated endpoint, with the same transitions
> counted on the OpenTelemetry meter and no client collection at all (`F-54`, ADR-0041). Stage 14 has
> since begun with the access foundation: roles are real and administered by the `access-admin` tool,
> `support`/`operator`/`admin` must complete a TOTP second factor to finish signing in, and the first
> gated admin surface reports the live game and suspends or restores an account — every mutation
> requiring a fresh code, a reason and an idempotency key, and committing an audit entry with the
> change (`F-46`, `F-47`, ADR-0042). The read console's diagnostic reads have since landed: the durable
> job queue, a stuck matchday with its failed simulation attempts, and a search over the append-only
> audit trail, all behind the same role and second factor (`F-46`, `F-47`, ADR-0043). The recovery commands
> have since landed: an operator can retry a dead-lettered job, cancel a stuck job, and requeue a stuck
> round's resolution or publication — each audited with a reason, and accompanied by the on-call runbook
> (`F-46`, `F-47`, ADR-0044). The backend admin surface is now complete with the repairs and broadcasts:
> a club can be handed back to the AI, a finance error corrected with a compensating entry, an
> announcement published, and a feature flag set (`F-46`, `F-47`, ADR-0045). The stage's operational
> proof has landed too: a k6 load suite at three times the projected launch population, a supply-chain
> scan suite, and a real point-in-time restore drill with integrity checks (`F-49`, ADR-0046). The
> read-only incident switch followed: one audited flag refuses every manager command with `503
> READ_ONLY_MODE` while reads, sign-in, and the operator console stay up and the worker keeps advancing
> deadlines (`F-51`, ADR-0047). Most recently the telemetry stack has arrived — an OTLP collector,
> Prometheus, Alertmanager, Grafana, and Tempo receiving the export the hosts always had, with dashboards
> for the availability and latency objectives and four alerts, each naming the runbook section that
> answers it (`F-48`, ADR-0048). Stage 15 has since begun with the stepped clock: a non-production mode
> that freezes game time at a stored instant and moves it only when an operator presses a button, so a
> season can be played one day or one matchday at a time through the real worker (`TIME-6`, `TIME-7`,
> ADR-0049). Its player-facing pages followed — rules, privacy, terms, a live service status and support,
> reachable signed out and backed by the product's first anonymous read (`F-55`, ADR-0050). Most recently
> the engine roadmap's continuous-replay milestone has landed. `engine-v4` / `engine-rules-v4` makes the
> passage the unit of movement: a possession begins where the last one left the ball — or at a restart —
> progresses into the attacking third through a real chain of touches, and ends at an outcome-appropriate
> point, so event coordinates, shot maps, and direct free kicks are finally meaningful; `replay-v3` then
> turns a recorded match into one continuous condensed film of the whole match, joined rather than cut,
> with a companion highlights reel that gives every chance a genuine lead-in, and the match center plays
> it with a continuous clock, a scrolling commentary feed, and a scrubber (`MAT-8`, `MAT-11`, ADR-0051,
> ADR-0052).

---

## Start here

```bash
npm install          # once: installs the root task runner
npm run dev          # starts PostgreSQL + mail catcher, then API, worker and web
```

`npm run dev` is the single command for local development. It:

1. starts PostgreSQL 17 and a mail catcher via Docker Compose and waits for their health checks,
2. runs the API on `http://localhost:5080`,
3. runs the background worker as a **separate process** (it is never hosted inside the API),
4. runs the Angular dev server on `http://localhost:4200`, proxying `/api` to the API.

Then:

| Where | What |
|---|---|
| <http://localhost:4200> | Web client |
| <http://localhost:5080/health> | Health, including the database check |
| <http://localhost:4200/welcome> | Landing page |
| <http://localhost:8025> | Mail catcher UI |

PostgreSQL is published on host port **55432**, not 5432, so it cannot collide with another database
already on your machine. Override with `POSTGRES_PORT` in a local `.env` (copy `.env.example`).

First run only, create the database schema:

```bash
npm run migrate
```

Press `Ctrl+C` once to stop the three application processes. `npm run infra:down` stops the
containers; `npm run infra:reset` also deletes the data volume.

### Prerequisites

- .NET SDK 10 (pinned by `global.json`)
- Node.js 24
- Docker (for PostgreSQL and the mail catcher, and for the integration tests)

---

## Verify it works

```bash
npm run build         # .NET build, warnings as errors
npm test              # every .NET test project, including Testcontainers integration tests
npm run test:web      # Angular unit tests
npm run format:check  # formatting gate

npm run e2e:install   # once: installs Playwright and its Chromium browser
npm run test:e2e      # end-to-end journeys in a real browser
npm run test:e2e:a11y # just the accessibility (axe) gate, a subset of the journeys
```

`npm run test:e2e` needs Docker. It brings up PostgreSQL and the mail catcher, applies migrations,
starts the API and the web client itself, and drives them through Chromium — so there is nothing to
start by hand first.

The walking skeleton is exercised end to end by
`tests/TouchlineManager.Worker.IntegrationTests`: a job is enqueued against a real PostgreSQL 17
container and the worker is asserted to claim, execute and complete it without the API being
involved. With the API running in Development you can also drive it by hand:

```bash
curl -X POST "http://localhost:5080/api/v1/ops/diagnostics/noop-job?key=demo-1"   # 202, enqueued=true
curl -X POST "http://localhost:5080/api/v1/ops/diagnostics/noop-job?key=demo-1"   # 202, enqueued=false
```

The second call returning `enqueued=false` is the enqueue idempotency guarantee: the same business
key never produces a second job.

---

## Operational drills

Stage 14's operational proof — the load suite, the scans, the restore drill, and the telemetry stack —
runs locally and by hand. Nothing is wired into CI yet
([ADR-0046](docs/architecture/adr/0046-load-supply-chain-and-restore-drills.md),
[ADR-0048](docs/architecture/adr/0048-telemetry-dashboards-and-slo-alerting.md)).

```bash
npm run scan              # dependency, licence, secret, and base-image scans
npm run load:seed         # prepare a world and 108 managers for the load suite
npm run load:reads        # one k6 scenario (add `-- --smoke` to check the plumbing)
npm run drill:restore     # a real point-in-time restore with integrity checks
npm run obs:up            # the telemetry stack: Grafana, Prometheus, and Alertmanager
npm run obs:check         # validate the stack's dashboards, rules, and runbook links
```

Point the hosts at it with `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:14317` and the dashboards fill
in. Each drill is documented where it belongs:
[`docs/operations/supply-chain.md`](docs/operations/supply-chain.md),
[`docs/operations/load-testing.md`](docs/operations/load-testing.md),
[`docs/operations/backup-and-restore.md`](docs/operations/backup-and-restore.md), and
[`docs/operations/observability.md`](docs/operations/observability.md).

---

## Accounts and sign-in

Authentication follows [ADR-0002](docs/architecture/adr/0002-auth-and-session-model.md): a
short-lived access token held in memory, and a rotating refresh token in an `HttpOnly` cookie whose
reuse revokes the whole token family.

To drive it by hand, registration and verification emails land in the mail catcher at
<http://localhost:8025> — no mail server is needed:

```bash
curl -X POST http://localhost:5080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","displayName":"Manager","password":"correct-horse-battery","acceptTerms":true}'

# Open the link in the mail catcher, then confirm it:
curl -X POST http://localhost:5080/api/v1/auth/verify-email \
  -H "Content-Type: application/json" \
  -d '{"userId":"<from the response>","token":"<from the link>"}'

curl -i -X POST http://localhost:5080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"correct-horse-battery"}'
```

The whole lifecycle — register, confirm, sign in, keep the session across a reload, rename, sign out,
and reset a forgotten password — is also driven through a real browser by `tests/web-e2e`, which
reads the confirmation and reset links out of the mail catcher instead of being handed a token.

`Auth__SigningKey` must be at least 32 bytes and is validated at startup. Development has a
committed dev-only value; every other environment supplies its own (see `.env.example`).

---

## Administering access

Operator accounts are created by a tool, not an endpoint: the first administrator cannot be granted by
an administrator, and a production route that grants roles would itself be a privilege-escalation
surface ([ADR-0042](docs/architecture/adr/0042-operator-access-and-mfa.md)). Grant a role to a
registered account and inspect it:

```bash
dotnet run --project tools/access-admin -- grant ops@example.com operator
dotnet run --project tools/access-admin -- list ops@example.com
```

`support`, `operator`, and `admin` must complete a TOTP second factor. Enrolment is two steps — a
secret and one-time recovery codes, then a code derived from the secret:

```bash
# with the account's access token
curl -X POST http://localhost:5080/api/v1/auth/mfa/enrol -H "Authorization: Bearer $TOKEN"

curl -X POST http://localhost:5080/api/v1/auth/mfa/enrol/confirm \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"code":"123456"}'
```

From then on a password alone is not a session: `POST /api/v1/auth/login` answers `202` with a
challenge, and `POST /api/v1/auth/mfa/login` completes it. An admin mutation repeats the code in an
`X-MFA-Code` header, and every mutation needs a reason and an idempotency key. If an operator loses
their authenticator, `dotnet run --project tools/access-admin -- reset-mfa ops@example.com` removes it
so they can enrol again — the only path, because a self-service reset would defeat the factor.

`Auth:EncryptionKey` protects the stored TOTP secret with AES-256-GCM and must be at least 32 bytes.
Like `Auth:SigningKey` it is validated at startup, has a committed dev-only value, and must be supplied
in every other environment (see `.env.example`).

---

## The world and onboarding

A new database has no world in it, so create one before anyone can register a club:

```bash
npm run seed          # six countries, one 18-club tier each, 22 players per club, funded accounts
```

The seeder is idempotent, so running it again reports the world it already found and writes nothing.
`--seed` and `--first-matchday` override the configuration, and `World__GenerationSeed` overrides the
default seed from the environment:

```bash
npm run seed -- --seed my-world-1 --first-matchday 2026-10-06
```

The same seed and generator version reproduce the same pyramid and the same squads, which is what
[`world.generation_runs`](docs/architecture/data-model.md) records and what the tests assert.

Then onboard by hand, or use the screens at `/onboarding/manager`, `/onboarding/country`, and
`/onboarding/club`:

```bash
# Sign in first and keep the access token from the response.
curl -s http://localhost:5080/api/v1/world -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:5080/api/v1/countries -H "Authorization: Bearer $TOKEN"

curl -X POST http://localhost:5080/api/v1/manager-profile \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"locale":"en-GB","timeZone":"Europe/London"}'

# Pick a club from the country's available-clubs response, then claim it. The idempotency key is
# required: repeating it returns the first outcome rather than creating a second tenure.
curl -X POST http://localhost:5080/api/v1/club-claims \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: 2f0c1a9e-6a5f-4c3a-9f7e-1d2b3c4d5e6f" \
  -d '{"clubId":"<from available-clubs>"}'
```

A country whose lowest tier is full answers `409 CAPACITY_PROVISIONING` and queues the next tier's
generation. The worker then generates, backfills, validates, and activates that tier, so the country grows
by the same generic path for every tier above the first. Why a takeover serialises the way it does is in
[ADR-0010](docs/architecture/adr/0010-club-takeover-serialisation.md); how the tier is built is in
[ADR-0030](docs/architecture/adr/0030-provisioning-execution-and-bootstrap-provenance.md).

---

## Watching a season without waiting for it

A season is thirty-four matchdays on Tuesdays, Thursdays and Sundays, so the real thing is about eleven
weeks. A non-production environment can compress that clock so the same deadlines arrive in minutes, which
is how staging and the end-to-end suite reach a rollover. It is real-time by default and is refused in
Production — a production host that asks for it fails to start rather than quietly running at normal speed
(`TIME-6`, [ADR-0015](docs/architecture/adr/0015-compressed-test-clock.md)).

Game time is `VirtualAnchorUtc + (realNow − RealAnchorUtc) × Rate`. Set it in the API's and the worker's
configuration (they must agree, so give them the same values), or export it:

```bash
export Clock__Mode=Compressed
export Clock__Rate=1800                              # one real hour is about a game week
export Clock__RealAnchorUtc=2026-09-25T12:00:00Z     # the real instant the map is anchored at
export Clock__VirtualAnchorUtc=2026-10-06T18:25:00Z  # the game instant it maps to (optional)
npm run seed -- --first-matchday 2026-10-06
npm run dev
```

Leaving `VirtualAnchorUtc` unset is a pure speed-up: game time equals real time at `RealAnchorUtc` and runs
ahead of it afterwards. Setting it additionally offsets the world, so pinning a fresh world just before its
first kickoff is a matter of setting both anchors to the moments you want. Both the API and the worker log a
warning when a compressed clock is in force, so an accelerated world is never a surprise.

Two things to expect. The world's stored instants — kickoffs, deadlines, `created_at` — are game time, so
they read years ahead of the wall clock. And a browser computes countdowns from its own clock, so on a
compressed world a countdown says days where the server says minutes; the server still decides whether a
sheet is locked, so the controls are correct and only the phrasing misleads. That gap is `TIME-5` work the
fixture reads do not yet do, and ADR-0015 records it rather than half-building it.

---

## Repository layout

```text
apps/
  api/          ASP.NET Core API — the only public writer
  worker/       Background worker — executes every deadline
  web/          Angular PWA
src/
  TouchlineManager.Domain          Aggregates and invariants. Depends on nothing.
  TouchlineManager.Application     Use cases, ports, validators, policies
  TouchlineManager.Infrastructure  Persistence, durable jobs, logging, providers
  TouchlineManager.Contracts       Transport DTOs, header and error-code constants
  TouchlineManager.MatchEngine     Pure, deterministic, versioned simulation library
tests/          Unit, architecture, and Testcontainers integration tests
  web-e2e/      Playwright journeys against the real stack
tools/          world-seeder, simulation-benchmarks
infra/          Docker Compose for local backing services
docs/           Product rules, architecture, security, operations
```

Dependency direction is enforced by `tests/TouchlineManager.ArchitectureTests` and fails the build
when crossed — the domain layer cannot reach for EF Core, and the match engine cannot reach outside
its own inputs.

---

## Documentation

Read [`docs/README.md`](docs/README.md) first. The two documents that matter most day to day:

- **[`docs/product/game-rules.md`](docs/product/game-rules.md)** — the normative rule set. Every
  configurable value carries a stable reference (`CAL-3`, `TRF-8`, `OCC-2`) that code comments,
  tests, migrations and support replies cite.
- **[`docs/architecture/modules.md`](docs/architecture/modules.md)** — the bounded modules, the
  dependency rules, and which stage delivers each one.

Also worth knowing:

- [`docs/product/master-plan.md`](docs/product/master-plan.md) — the approved implementation plan.
- [`docs/architecture/adr/`](docs/architecture/adr/README.md) — why the system is shaped this way.
- [`docs/security/threat-model.md`](docs/security/threat-model.md) — including the review of every
  deadline-critical workflow.
- [`docs/testing/test-strategy.md`](docs/testing/test-strategy.md) — the test layers and gates.

---

## Non-negotiables

These are product guarantees, not preferences. Breaking one is a release blocker.

1. **The server decides everything.** There is no endpoint that simulates a match. Results come from
   an immutable, hashed input snapshot and a versioned engine.
2. **A matchday publishes atomically.** All nine fixtures or none.
3. **Deadlines are durable rows, not timers.** A deploy cannot lose or repeat a job; every job has a
   unique business key and an idempotent handler.
4. **Money is integer minor units in an append-only ledger**, and balances are never negative.
5. **No offline mutations.** A bid queued past its auction close is worse than a bid the manager
   knows was never sent.
6. **All identity is fictional.** No real club, player, competition or mark appears anywhere.
