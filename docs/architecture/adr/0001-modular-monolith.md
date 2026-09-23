# ADR-0001: Modular Monolith

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §4.1, §5.1, §5.2

## Context

The game is a server-authoritative MMO whose critical workflows (club claims, matchday publication, auction resolution, season rollover) span several domains and must execute as single database transactions. The team starts small and the launch population is unknown, so operational complexity and distributed-transaction risk outweigh any immediate scaling benefit from services.

## Decision

Build one **modular monolith**:

- One ASP.NET Core API deployable, one .NET worker deployable, one PostgreSQL 17 database, one Angular PWA, one pure match-engine library.
- Modules are bounded as PostgreSQL schemas paired with matching C# namespaces: `auth`, `world`, `squad`, `competition`, `match`, `market`, `finance`, `comms`, `ops`.
- Cross-module writes go through application use cases; no endpoint may reach into an arbitrary repository of another module.
- Dependency direction is enforced by `FootballManager.ArchitectureTests` in CI (Domain → nothing; MatchEngine → domain-neutral contracts only; Application → Domain/MatchEngine/Contracts; Infrastructure → Application; Api/Worker = composition roots).
- Module boundaries are explicit in code and schema so a high-load module can be extracted later without redesigning transactions now.

## Consequences

- Simple local transactions, one database, one deployment pipeline.
- No message broker or saga machinery in the MVP.
- Boundary erosion is the main long-term risk; architecture tests and code review must hold the line.
- Extraction later (e.g. `match` simulation onto dedicated workers) remains possible because all state flows through schemas and use cases.

## Alternatives considered

- **Microservices from day one:** rejected — forces distributed transactions into claim/matchday/auction paths, adds operational burden with no measured load justification (plan §17.18).
- **Single undifferentiated project:** rejected — no enforceable boundaries, extraction would require a rewrite.
