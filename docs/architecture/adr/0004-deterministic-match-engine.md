# ADR-0004: Pure Deterministic Match Engine

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §4.4, §8.1–8.6, §15.3, §17.8

## Context

Matches simulate asynchronously on the server with no manager present. Competitive integrity requires that the same locked input reproduces byte-identical output on any supported machine, that seeds cannot be manipulated after lock, and that historical matches can be replayed years later.

## Decision

- `FootballManager.MatchEngine` is a **pure library**: no database, no clock, no `Random.Shared`, no network, no filesystem, no current culture, no thread scheduling. Architecture tests enforce this.
- **PRNG:** explicit PCG32 implementation with a documented, tested draw order. Never iterate unordered dictionaries/sets during simulation.
- **Seed:** secret match seed = HMAC(world secret, fixture ID, locked snapshot hash, engine version). Store a protected seed plus a public commitment hash; reveal raw seed only per operations/security policy.
- **Snapshot:** one canonical `MatchInputV1` frozen at fixture lock (lineups, attributes/state, tactics, repairs, home advantage, config hash, versions). Canonical serialization sorts collections and uses invariant numerics before hashing.
- **Output:** structured events, statistics, commentary tokens, semantic highlight keyframes; final score must equal goal events; output includes input/output hashes.
- **Versioning:** engine rules live in `Configuration/EngineRulesV1` (versioned, validated at startup). Any formula, draw-order, serialization, or event-semantics change requires a **new engine version**; released versions are never edited in place.
- **Validation:** golden tests pin complete output hashes for representative inputs on Windows and Linux CI; property/fuzz suites check invariants; ≥100,000-seed distribution suite checks goal/draw/cards/possession/injury ranges; benchmark p95 < 100 ms per match on reference hardware.
- **Concurrency:** each match is single-threaded; scale comes from running independent fixture jobs in parallel (plan §4.4).

## Consequences

- Determinism is a test-enforced contract, not a convention — CI fails on cross-platform hash drift.
- Engine changes are deliberate and versioned, which complicates live tuning but protects replayability.
- Commentary/highlights are derived from events and can never change outcomes.

## Alternatives considered

- **`System.Random` seeded:** rejected — implementation details are not a stable cross-runtime contract.
- **Simulate on request/manager trigger:** rejected — violates server authority; simulation is worker-only, never a public endpoint.
- **Floating-point formulas:** rejected for anything affecting outcomes where determinism differs by platform; golden cross-OS tests gate this.
