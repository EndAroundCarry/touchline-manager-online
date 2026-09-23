# ADR-0008: MVP deployment topology

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (provisioned in Stage 14/16)
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-postgresql-durable-jobs.md)

## Context

The game publishes results on fixed real-world deadlines (Tuesday/Thursday/Sunday at
19:00 UTC). Product behaviour therefore depends on a background worker being alive at a
specific minute. A platform that idles, sleeps, or cold-starts the worker breaks the product
contract, not just performance.

The worker, the API, and the database must share one transaction boundary (ADR-0001), so the
database must be reachable from both with low, predictable latency.

## Decision

Use this reference topology unless a superseding ADR selects an equivalent managed
alternative:

| Layer | Choice | Notes |
|---|---|---|
| Frontend hosting | Cloudflare Pages with custom domain | Immutable hashed assets, Brotli, SPA fallback |
| DNS / TLS / WAF | Cloudflare | Edge TLS, WAF rules, bot management, cache rules |
| API | Render **paid** web service from the API container | Always-on production instance; no free tier |
| Worker | Render background worker, same repository/image family | Independently scalable; always-on |
| Database | Render managed PostgreSQL 17, same region, plan with automated backups and PITR | Migration role separate from runtime role where supported |
| Email | Postmark transactional email | Verification, reset, deadline reminders |
| Telemetry | OpenTelemetry export plus provider logs/metrics | Frontend error reporting with PII scrubbing |

Binding consequences:

- **Never claim `$0/month`.** Expected service tiers, limits, storage, backup retention, and
  egress are tracked in `docs/operations/cost-model.md`.
- API and worker are separately deployable but must remain compatible with each other during
  a rollout. Deployment order is worker (compatibility mode) → API → PWA.
- Migrations run as a controlled pre-deploy job using the migration database role, never at
  application startup.
- The worker must not be scaled to zero, and must not be co-located inside the API process,
  because a web-service restart during a matchday would silently drop publication work.
- Environment separation is mandatory: local, test, staging, and production each have separate
  databases, email settings, secrets, domains, and generated worlds. Restored environments
  must have outgoing email disabled and job leases cleared, and must not execute production
  deadlines.

## Consequences

**Positive**

- A small, well-understood operating surface for a small team: two compute services, one
  database, one CDN.
- Same-region API/worker/database keeps matchday publication latency predictable.
- Managed PITR gives a defensible recovery story without running our own PostgreSQL.

**Negative**

- Real monthly cost from day one, including an always-on worker that is usually idle. This is
  accepted as the price of deadline correctness.
- Single-region deployment means a regional provider incident is a game outage; promotion and
  matchdays pause rather than degrade. Documented in the incident runbook.
- Provider-specific configuration (Render service definitions, Render PostgreSQL plans) is
  captured in `infra/render.yaml` and must be re-derived if the provider changes.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Free-tier hosting (idle web services, sleeping workers) | Directly breaks fixed-kickoff deadlines. A missed matchday is a competition-integrity failure, not a slow page. |
| Self-managed PostgreSQL on a VPS | Adds patching, backup verification, PITR implementation, and failover to a team that should be working on the game. |
| Serverless functions for matchday jobs | Cold starts and execution time limits conflict with long, ordered, resumable workflows (backfill, rollover). |
| Splitting the worker per job family across multiple providers | Multiplies secrets, deploys, and observability for no MVP benefit. |
| Kubernetes | Operationally disproportionate for two services and one database. |
