# Glossary

Canonical terms for code, database, UI copy, and documentation. New vocabulary must be added here before use in schemas or API contracts.

## Required core terms

- **World** — A single persistent `GameWorld` instance containing all countries, clubs, players, and seasons. Production starts with exactly one. Every aggregate carries or derives `world_id` so a future second world (test shard, region) does not require schema redesign.
- **Country** — One of the six launch nations (England, Spain, Germany, Italy, France, Romania), represented by a fictionalized/generic display name. Owns a pyramid of divisions. Unique `(world_id, code)`.
- **Division** — A tier within a country's pyramid (tier 1 is the top). Every active division has exactly 18 clubs. Uniquely identified by `(country_id, tier_number)`; persists forever once created.
- **Division-season** — The participation of one division in one season: `competition.division_seasons`. Holds the schedule seed, tie-draw seed, and status for that division for that year. Distinct from the division itself.
- **Game year** — The accelerated in-game calendar unit, distinct from real time. Players age and contracts advance one game year at season rollover — never after 365 real days.
- **Tenure** — A time-bounded `world.club_tenures` row linking a manager to a club (`active`, `inactive`, `closed`). Managers control clubs *through* tenures; they never own club rows. One active tenure per club and per manager (partial unique indexes).
- **Fixture** — A scheduled home/away pairing in a matchday (`competition.fixtures`) with status `scheduled → locked → simulating → staged → published` (or `void`). Carries the final score only after staged/published.
- **Matchday** — One round of a division-season (1–34): `competition.matchdays` with lock timestamp, kickoff timestamp, and publication status. All nine fixtures of a division matchday publish atomically.
- **Snapshot** — The immutable `match.input_snapshots` document frozen at fixture lock: lineups, attributes/state, tactics, repairs, home advantage, config hash, versions, and seed commitment. Simulation reads only snapshots, never live tables.
- **Publication** — The atomic promotion of a fully staged division matchday: fixtures marked published, standings/stats/discipline/finances/condition/inbox updated in one transaction. Partial publication (e.g. 5 of 9 results) is impossible by design.
- **Active human** — A club under an `active` `ClubTenure` whose account is not suspended and whose release date is null. AI controls everything else. Only active-human occupancy counts toward pyramid expansion.

## Supporting terms

- **Tier** — Ordinal position in a country's pyramid; the *lowest active tier* is the only place new managers may claim a club.
- **Club claim** — `POST /api/v1/club-claims`: the atomic takeover of an AI-controlled lowest-tier club, creating a tenure and inheriting the club exactly as it stands.
- **Bootstrap simulation** — Deterministic AI-vs-AI simulation of already-passed matchdays when a new tier is provisioned, giving the new division an inherited table.
- **Team sheet** — The per-fixture selection of 11 starters + up to 7 substitutes for one club (`squad.fixture_team_sheets` + entries). Falls back to the club's default tactical plan when no fixture-specific sheet exists.
- **Lock deadline** — Kickoff minus 30 minutes: after this instant, team sheets are validated, repaired deterministically, and frozen into the snapshot.
- **Staged** — A fixture/match whose simulation output exists but is not yet publicly applied. Staged results become visible only at publication.
- **Basis points** — The stored unit for condition, fatigue, morale, and sharpness (0–10,000). APIs convert to the user-facing 0–100 scale.
- **Rule set version** — Version identifier of the configurable game rules in `docs/product/game-rules.md`, stamped on the world and each season.
- **Engine version** — Version identifier of the match engine formulas/serialization (`EngineRulesV1`); changing formulas requires a new version (ADR-0004). Never edited in place.
- **Provisioning request** — `world.division_provisioning_requests`: the durable intent to create tier `N+1` when the lowest tier reaches 18/18 active human tenures.
- **Tenure status** — `active` (human in control), `inactive` (14 days no login; AI makes safe decisions, human can resume), `closed` (21 days, resignation, or admin action; club returns to AI).
- **ETag / version** — Mutable aggregates expose `version bigint` as a strong ETag; conflicting updates require `If-Match` and otherwise fail with `412`.
- **Idempotency key** — Client-supplied `Idempotency-Key` on commands (claims, bids, listings) recorded in `ops.idempotency_records`; the same key with a different request hash is rejected.
- **Outbox** — `ops.outbox_messages` rows written in the same transaction as domain state and dispatched asynchronously; prevents dual-write loss of events.
- **Job lease** — The `lease_owner`/`lease_until` pair a worker stamps when claiming a durable job with `FOR UPDATE SKIP LOCKED`; expired leases become claimable (crash recovery).
