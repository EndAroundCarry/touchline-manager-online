# Touchline Manager

An old-school football management game with a modern online structure: a persistent,
server-authoritative MMO where managers prepare a club asynchronously and compete against each
other on fixed matchdays. Every club, player, competition and badge is fictional.

**Matchdays:** Tuesday, Thursday and Sunday at 19:00 UTC. Team sheets lock 30 minutes before
kick-off. The server decides results; a client can never simulate or influence one.

> **Status: Stage 4 delivered — squads, tactics, and training.** The playable game is being built in the
> staged order defined in the master plan. Stage 1 delivered the monorepo, the durable job pipeline, the
> API and worker composition roots, the health and observability baseline, and the Angular PWA shell. Stage
> 2 added the account schema, the full credential lifecycle, rotating refresh sessions with reuse
> detection, the audit trail, the request security headers, the Angular auth and settings screens, and the
> end-to-end journeys. Stage 3 added deterministic world generation, six fictional national pyramids,
> atomic club takeover with pyramid expansion, and the onboarding screens. Stage 4 added the `squad`
> schema, a legal twenty-two-player squad per club, the squad, player and contract screens, the tactics
> board and its ETag contract, and the training screen with its deterministic daily progression job. The
> contract renewal quote waits for the playing-time data Stage 6 introduces; the next stage is the pure
> match engine.

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
generation; that generation is Stage 11, so a full country stays full until then. Why a takeover
serialises the way it does is in [ADR-0010](docs/architecture/adr/0010-club-takeover-serialisation.md).

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
