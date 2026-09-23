# ADR-0001: Modular monolith with explicit module boundaries

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0008](0008-deployment-topology.md), [`modules.md`](../modules.md)

## Context

The product is a persistent, server-authoritative MMO whose most dangerous operations are
cross-cutting: club takeover, auction resolution, matchday publication, and season rollover
each touch identity, competition, squad, market, finance, and communications state in one
logical step. Those operations must be atomic or the game loses competitive integrity.

The MVP is built and operated by a very small team. Any topology that turns a single
business operation into a distributed transaction adds failure modes that cannot be
recovered from by an operator under time pressure during a live matchday.

At the same time, the long-term workload is genuinely uneven: match simulation, matchday
publication, and finance runs will eventually demand independent scaling from the read-heavy
management API.

## Decision

Adopt a **modular monolith**:

- One API deployable (`apps/api`).
- One durable worker deployable (`apps/worker`).
- One PostgreSQL 17 database.
- One Angular PWA (`apps/web`).
- One pure, dependency-free match-engine library.

Module boundaries are made explicit in two places at once, so an extraction later is a
mechanical exercise rather than a rewrite:

1. **C# namespaces and projects** — `TouchlineManager.Domain`, `.Application`,
   `.Infrastructure`, `.Contracts`, `.MatchEngine`, with bounded modules expressed as
   namespaces and feature folders inside them.
2. **PostgreSQL schemas** — `auth`, `world`, `squad`, `competition`, `match`, `market`,
   `finance`, `comms`, `ops`.

Additional binding rules:

- Cross-module writes happen only through an application use case. No endpoint, job
  handler, or repository reaches into another module's tables directly.
- Domain depends on nothing. MatchEngine is pure. Api and Worker are composition roots and
  contain no game formulas.
- Architecture tests fail the build when dependency direction or module namespace rules are
  violated.
- API and Worker are separate processes sharing one image family, so they can be deployed
  and scaled independently while still sharing one database and one set of use cases.

## Consequences

**Positive**

- Takeover, bid resolution, matchday publication, and rollover are single local
  transactions. This is the property the whole MVP depends on.
- One migration stream, one backup story, one set of observability conventions.
- Refactoring inside a module is cheap; no network contract to version.

**Negative**

- Module boundaries decay unless enforced. Mitigation: architecture tests are a stage gate,
  and module ownership is documented in `modules.md`.
- A single database is a single point of contention. Mitigation: bounded queries, explicit
  indexes, projection tables, and job concurrency limits; scale only from measured evidence.
- Independent scaling requires a future extraction project. Mitigation: the schema and
  namespace split is designed so the highest-load module (`match`) can be extracted first.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Microservices from day one | Forces distributed transactions (sagas/2PC) into auction resolution, publication, and rollover — exactly the operations that must not be eventually consistent. Also multiplies operational load for a small team. |
| Single project, no module boundaries | Cheapest initially, but makes the later extraction effectively a rewrite and lets domain rules leak into endpoints. |
| Logical separation without database schema separation | Leaves cross-module foreign keys and join paths ambiguous; still permits accidental coupling through SQL. |
| Separate worker with its own database | Splits matchday publication across two stores. Directly violates the "never publish five of nine results" requirement. |
