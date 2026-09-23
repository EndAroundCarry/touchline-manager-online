# ADR-0005: Dynamic Pyramid Provisioning and Backfill

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §3.2, §3.6, §7.6, §11 (Stage 3/11), plan §16 Stage 11

## Context

Six countries start with one 18-club top division each. New managers may only take over clubs in the country's **lowest active tier**. When that tier reaches 18/18 active human tenures, capacity must grow — without pre-creating empty divisions, without blocking the claiming transaction on generation work, and without ever deleting a tier that later loses occupancy.

## Decision

- **Monotonic expansion:** tiers, once created, exist forever. Occupancy falling never removes a tier.
- **Trigger:** after every successful takeover and tenure-status change, evaluate the country's lowest active tier. If all 18 clubs have active human tenures and no provisioning record exists for tier `N+1`, atomically create a `world.division_provisioning_requests` row.
- **Concurrency:** a country-scoped PostgreSQL advisory lock + serializable transaction guarantees exactly one request per `(country_id, target_tier)` (unique index is the backstop).
- **Asynchronous generation:** a durable `ProvisionDivision` job generates 18 AI clubs, ~396 balanced players, finance accounts, entries, and the remaining fixtures for the current country season.
- **Backfill:** matchdays already passed are simulated as deterministic AI-vs-AI **bootstrap simulations** in sequence, giving the new division a legitimate inherited table. The division becomes claimable only after generation, validation, and backfill complete.
- **Season-lock interaction:** if rollover is locked, provisioning targets the new season instead of mutating the closing one.
- **Onboarding UX:** while provisioning is pending, claims return a stable `409 CAPACITY_PROVISIONING` with polling — never a partial club assignment.
- **Generic path:** tier 3 provisioning reuses the identical algorithm; no hard-coded maximum tier.
- Claims may only target AI-controlled clubs in the lowest active tier; the club is inherited exactly as-is (squad, cash, table, fixtures, commitments — plan §3.1).

## Consequences

- Claim latency stays constant; heavy generation work happens in the worker.
- The world never contains a claimable-but-empty division.
- Backfill determinism depends on the match engine's versioning contract (ADR-0004); bootstrap simulations are marked distinctly in history.

## Alternatives considered

- **Pre-create all tiers empty:** rejected — fake capacity, unmanageable number of empty divisions, violates "lowest active tier" onboarding rules.
- **Synchronous generation inside the claim:** rejected — blocks the 18th claim on minutes of generation and fails atomically with it.
- **Allow claims into upper-tier AI vacancies now:** rejected — deferred by plan §3.1 to a later job-market feature.
