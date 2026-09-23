# Modules and MVP Feature Mapping

Bounded modules (plan §5.2), dependency rules (§5.1), and the Stage 0 exit requirement that **every public MVP feature maps to a module and a planned stage**.

## Dependency rules (enforced by `FootballManager.ArchitectureTests`)

- `Domain` depends on nothing (no Application, Infrastructure, ASP.NET, EF Core, UI).
- `MatchEngine` is pure and deterministic: domain-neutral contracts only — never EF, clock, network, filesystem, or global randomness.
- `Application` → Domain, MatchEngine abstractions, Contracts. Commands, queries, policies, validators, ports.
- `Infrastructure` → Application. Persistence, jobs, email, auth storage, providers.
- `Api` and `Worker` are composition roots — no game formulas.
- `Contracts` holds versioned transport DTOs and stable error codes — never EF entities.
- Angular consumes generated OpenAPI DTO types but keeps handwritten feature API services/facades.
- Cross-module writes go through application use cases, never direct controller-to-repository access.

## Bounded modules

| Module | PostgreSQL schema | C# namespace | Owns |
|---|---|---|---|
| `auth` | `auth` | `FootballManager.Application.Auth` | Accounts, credentials, sessions, roles, verification, consent, privacy |
| `world` | `world` | `FootballManager.Application.World` | World, countries, generation, clubs, managers, tenures, capacity/provisioning |
| `squad` | `squad` | `FootballManager.Application.Squad` | Players, attributes, state, contracts, registrations, unavailability, training, tactics, team sheets, shortlists |
| `competition` | `competition` | `FootballManager.Application.Competition` | Seasons, divisions, entries, matchdays, fixtures, standings, discipline, statistics |
| `match` | `match` | `FootballManager.Application.Match` | Snapshots, simulations, events, lineups, highlights, engine metadata |
| `market` | `market` | `FootballManager.Application.Market` | Shortlist reads, listings, bids, auction outcomes, AI market decisions |
| `finance` | `finance` | `FootballManager.Application.Finance` | Balances, reservations, immutable ledger, sponsorship/awards |
| `comms` | `comms` | `FootballManager.Application.Comms` | Inbox, notifications, news, email dispatch intent |
| `ops` | `ops` | `FootballManager.Application.Ops` | Jobs, outbox, idempotency, audit, feature flags, repair records |

The match **engine** itself is the separate pure library `src/FootballManager.MatchEngine/` (ADR-0004); the `match` module is its persistence/publication surface.

## MVP feature → module → stage

Source: plan §2.2 public MVP goals, §16 stages.

| Public MVP feature | Module(s) | Stage |
|---|---|---|
| Registration, email verification, login, session rotation, password reset, logout, account settings | `auth` | 2 |
| One active manager career/club tenure per account | `auth`, `world` | 2–3 |
| Country selection + atomic takeover of an AI club | `world` | 3 |
| Single persistent world, six fictional pyramids, initial 18-club top divisions | `world`, `competition` | 3 |
| Automatic lower-tier creation at 18/18 humans | `world` (job `ProvisionDivision`) | 3 trigger / 11 execution |
| AI control for vacant clubs | `world` (+ AI policies) | 3 derivation / 8 full |
| Three matchdays per week (Tue/Thu/Sun) | `competition`, `ops` | 6 |
| Squad, player, lineup, tactics, training, availability, contract workflows | `squad` | 4 |
| Fixtures, tables, results, statistics, promotion/relegation, season rollover | `competition` | 6 / 12 |
| Server-authoritative match locking, simulation, publication, recovery | `match`, `ops` | 6 |
| Text commentary + replayable 2D keyframe highlights | `match` + web viewer | 7 |
| Scouting search, shortlists, listings, timed auctions, AI market | `market`, `squad` | 10 |
| Inbox/news notifications | `comms` | 8 templates / 11 completion |
| Inactivity handling + safe return-to-AI | `world`, `comms` | 11 |
| Responsive desktop/mobile/tablet PWA | `web` | 1 shell / 13 hardening |
| Administration, audit, observability, backup, restore, deployment, incident controls | `ops` + `docs/operations/*` | 14 |
| Finances: gate, sponsorship, wages, operating cost, awards, ledger | `finance` | 9 |

Deferred non-goals stay out of every stage until a post-MVP stage explicitly adds them (plan §2.3, Stages 17–21).

## Stage exit gate

A stage is complete only when its plan §16 exit criteria pass: green CI, applied migrations, no failing tests, no unresolved dead jobs, docs (`game-rules.md`, `data-model.md`, runbooks) synchronized (plan §17.14, §17.20).
