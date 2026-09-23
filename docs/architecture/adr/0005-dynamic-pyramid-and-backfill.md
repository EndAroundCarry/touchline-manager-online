# ADR-0005: Dynamic pyramid expansion with deterministic backfill

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (partial in Stage 3, full in Stage 11)
- **Related:** [`../../product/game-rules.md`](../../product/game-rules.md) §3.2, [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0004](0004-deterministic-match-engine.md)

## Context

The world launches with exactly one 18-club division per country, all AI-controlled. A
country must be able to absorb an unbounded number of new human managers without ever
leaving a manager unable to play, and without allowing a live season to be corrupted by a
tier appearing mid-season.

Creating a tier is not just inserting 18 rows. A new division must begin with a legitimate
standing: an 18-club table that reflects results already played this season, otherwise a
manager joining in round 20 sees an empty table against opponents who have 19 results, and
promotion/relegation at rollover becomes meaningless.

Three failure modes must be impossible:

1. Two concurrent takeovers each creating the next tier (duplicate division, 36 clubs).
2. A new tier appearing mid-season with no historical results.
3. A partial assignment where a manager is charged a claim but the club is unusable.

## Decision

**Monotonicity.** Once a tier exists it is never removed, even if human occupancy later
falls. Capacity planning must never depend on shrinking the world.

**Trigger.** After every successful takeover and every tenure-status change, evaluate the
country's current **lowest active tier**. If all 18 clubs hold active human tenures and no
provisioning record exists for `(country, target_tier)`, then create a
`world.division_provisioning_requests` row for tier `N+1`.

**Uniqueness and serialization.** The request table carries `unique (country_id, target_tier)`,
and the evaluation runs inside a transaction holding a **country-scoped PostgreSQL advisory
lock**. Concurrent takeovers therefore cannot create two tiers or two requests.

**Generation is asynchronous and durable.** A durable job (`ProvisionDivision`) performs
generation outside the takeover transaction: 18 AI clubs, balanced squads with contracts and
registrations, finance accounts, division-season entries, fixtures, and the matchday calendar.
It is driven by a recorded generation seed and generator version so the same seed reproduces
the same logical tier.

**Backfill.** For matchdays that have already passed in the current season, the job simulates
AI-vs-AI fixtures **in sequence** with a deterministic engine version and records them as
bootstrap results. Publication applies standings and player state exactly as a normal
matchday would, so the new division inherits a real table, real player condition, real
discipline records, and real finances.

**Activation gate.** The division becomes claimable only after generation, validation, and
backfill all complete. Validation asserts: 18 clubs, unique identities, 18 valid squads with
two goalkeepers each, a complete 34-round schedule, one standing per club, and reconciliation
between standings and published bootstrap fixtures.

**Rollover interaction.** If onboarding demand arrives while season rollover holds the
country lock, the request targets the **next** season. A closing season is never mutated.

**Capacity response.** If capacity is temporarily exhausted, onboarding returns a stable
`CAPACITY_PROVISIONING` problem response with the request status and a polling hint. It must
never partially assign a club, and it must never silently fail.

**No ceiling.** Filling tier 2 provisions tier 3 through the identical generic path. There is
no hard-coded maximum tier.

**Provenance.** Bootstrap fixtures and their projections are marked so audits, statistics, and
support tooling can always distinguish generated history from played history. Bootstrap
matches never appear in a human manager's personal match history as if they had been played.

## Consequences

**Positive**

- A country can absorb unlimited managers without operator intervention.
- A manager joining a freshly created tier sees a coherent table, coherent opponents, and a
  coherent fixture list immediately.
- Duplicate-tier creation is prevented by data, not by luck: unique constraint plus advisory
  lock plus idempotent job.
- The same code path serves tier 2, 3, 4, so correctness work is not repeated per tier.

**Negative**

- Backfilling a late-season tier is expensive: up to ~17 rounds × 9 fixtures of simulation
  plus projections. Mitigation: bounded worker concurrency, sequential per-matchday ordering
  to preserve state causality, progress reporting, and job lease renewal.
- Generated history is indistinguishable from played history unless provenance is modelled.
  Mitigation: explicit marking is a required part of the schema, not an afterthought.
- Rollover must reason about "a tier created during this season" for promotion/relegation.
  Mitigation: a newly provisioned lower tier participates in promotion/relegation at the next
  rollover, stated as a rule rather than inferred.
- Deterministic backfill couples the provisioning job to the match engine version, so both
  must be retained for reproducibility.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Start all six countries with multiple tiers | Wastes thousands of generated players, creates empty divisions that dilute competition, and does not solve unbounded growth anyway. |
| Create the tier synchronously during takeover | Produces a slow, failure-prone onboarding request; a crash mid-generation leaves a half-built division and a charged claim. |
| Start the new tier with an empty table | Managers would compete simultaneously in a division where some clubs have played 19 matches and others 0; promotion/relegation would be arbitrary for at least a season. |
| Wait for the next season to create the tier | Leaves managers unable to play for weeks, contradicting the onboarding goal. |
| Fill the new tier with AI results generated with a different engine version | Breaks reproducibility and makes historical reconciliation impossible. |
| Cap the number of tiers | Creates a hard wall where either clubs must be shared or managers must be refused. |
