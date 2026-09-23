# Touchline Manager Online

Persistent, asynchronous, server-authoritative football-management MMO. Six fictional national pyramids, three matchdays per week (Tue/Thu/Sun 19:00 UTC), AI managers for every vacant club.

- **Master plan:** [`docs/product/master-plan.md`](docs/product/master-plan.md)
- **Game rules (all configurable values):** [`docs/product/game-rules.md`](docs/product/game-rules.md)
- **Architecture:** [ADRs](docs/architecture/adr/README.md) · [context/containers](docs/architecture/context.md) · [modules](docs/architecture/modules.md) · [data model](docs/architecture/data-model.md) · [threat model](docs/architecture/threat-model.md)

## Stack

- .NET 10 (pinned by `global.json`), ASP.NET Core Minimal API, EF Core 10 + Npgsql, PostgreSQL 17
- Angular standalone components + Signals, PrimeNG, Tailwind CSS, installable PWA
- Docker Compose for local PostgreSQL + Mailpit mail catcher
- Deterministic match engine (`src/FootballManager.MatchEngine`) — pure library, no clock/DB/random globals

## Prerequisites

- .NET SDK 10 (see `global.json`)
- Node.js 24+ and npm
- Docker with Compose

## One-command start

```bash
npm run dev
```

This starts PostgreSQL + Mailpit (`infra/compose.yaml`), applies EF migrations, then runs API, worker, and web concurrently.

| Service | URL |
|---|---|
| API | http://localhost:5080 |
| Worker health | http://localhost:5081 |
| Web | http://localhost:4200 |
| Mailpit UI | http://localhost:8025 |

Health endpoints:

- `GET /health/live` — liveness only (never touches the database)
- `GET /health/ready` — readiness including database state

## Prove the durable job pipeline

```bash
curl -X POST http://localhost:5080/api/v1/admin/jobs/noop
```

The API enqueues an `ops.noop` job transactionally; the worker claims it with `FOR UPDATE SKIP LOCKED`, executes the handler, and marks it succeeded in `ops.jobs` (Development-only endpoint, Stage 1 scaffold).

## Build and test

```bash
npm run build          # dotnet build + web build
npm run test:dotnet    # unit + architecture + Testcontainers integration tests (needs Docker)
```

CI: `.github/workflows/pr.yml` (restore, build with warnings as errors, migration drift check, tests, web build).

## Repository layout

See plan §5: `apps/` (api, worker, web), `src/` (Domain, Application, Infrastructure, Contracts, MatchEngine), `tests/` (unit, integration, architecture), `infra/`, `docs/`, `tools/`.

## Implementation status

- [x] Stage 0 — product rules, ADRs, diagrams, glossary, threat model
- [x] Stage 1 — monorepo scaffold, guardrails, durable no-op job end-to-end
- [ ] Stage 2 — identity and authenticated walking skeleton
- … (plan §16 defines Stages 2–21)
