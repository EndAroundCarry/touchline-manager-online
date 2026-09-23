# ADR-0008: MVP Deployment Topology

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §4.3, §14, plan §16 Stages 14–16

## Context

The MVP needs a production topology that is simple to operate, backs up reliably (a live season cannot be rebuilt cheaply), separates web/API/worker scaling, and has honest, tracked costs. No ADR may promise `$0/month`.

## Decision

Reference topology (an ADR may later select an equivalent managed alternative, but this is the default):

- **Frontend:** Cloudflare Pages — custom domain, immutable hashed assets, Brotli, SPA fallback.
- **API:** Render paid web service, always-on, from the API container.
- **Worker:** Render background worker from the same repository/image family, scaled independently.
- **Database:** Render managed PostgreSQL 17 in the same region, plan with automated backups/PITR; separate migration vs runtime DB roles where hosting permits.
- **Email:** Postmark transactional email (verification, reset, inactivity warnings).
- **DNS/TLS/WAF:** Cloudflare (HSTS, exact CORS allowlist, restrictive CSP, `frame-ancestors`).
- **Telemetry:** OpenTelemetry export + provider logs/metrics; frontend error reporting with PII scrubbing.
- **Delivery:** GitHub Actions — PR pipeline (format, restore with lockfiles, build with warnings-as-errors, Angular strict checks, unit/architecture tests, Testcontainers integration, OpenAPI breaking-change check, golden engine tests, Playwright, scans, image build with size budgets) and a staged deploy: staging → smoke → approval → controlled migration → worker (compat) → API → PWA → monitored release, with code rollback and expand/contract for data.
- **Cost tracking:** `docs/operations/cost-model.md` records expected tiers, limits, storage, backup retention, and egress.
- **Environments:** local, test, staging, production — separate databases, email settings, secrets, domains, and generated worlds.

## Consequences

- Three small managed services + CDN keep the operational surface within one team.
- PITR backups and restore drills (Stage 14) are mandatory before beta.
- Vendor choice is recorded but replaceable: the topology (static web / API / worker / managed PG / transactional email) is the durable part of this decision.

## Alternatives considered

- **Single PaaS running everything (API+worker+static on one vendor):** acceptable fallback if cost demands, but loses CDN/WAF maturity; not selected.
- **Kubernetes / multi-region:** rejected — premature; plan §17.18 forbids scale-out without benchmark evidence.
- **Self-hosted VPS:** rejected for MVP — backup/PITR and on-call burden exceed team capacity.
