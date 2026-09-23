# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16.

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
