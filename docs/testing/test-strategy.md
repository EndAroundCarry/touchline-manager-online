# Test Strategy

> Companion to master plan §15. This document describes the layers that exist and what each one is
> for. Per-stage gates are named in
> [`docs/product/mvp-traceability.md`](../product/mvp-traceability.md).

Every test layer answers a question the layer above it cannot. The point of the split is to make
failures cheap to localise: a broken rule should fail a fast unit test, not a Playwright journey.

---

## Layer 1 — Domain unit tests (`tests/TouchlineManager.Domain.Tests`)

Pure logic with no dependencies: invariant calculations, ordering rules, retry schedules, tie-break
comparators, money arithmetic, schedule generation properties.

*Today:* the job retry policy (43 assertions over backoff, caps, jitter bounds, and argument
validation).

## Layer 2 — Application unit tests (`tests/TouchlineManager.Application.Tests`)

Use cases and policies with ports replaced by hand-written doubles. No database, no HTTP.

*Today:* job handler registration, including that two handlers claiming one job type fails at
construction rather than silently ignoring work, and that the no-op use case derives its business
key from domain identity.

## Layer 3 — Architecture tests (`tests/TouchlineManager.ArchitectureTests`)

Assert the dependency rules from [`docs/architecture/modules.md`](../architecture/modules.md) §2 by
inspecting each assembly's real references. These are the guardrail that keeps a modular monolith
from decaying into a ball of mud.

*Today:* DEP-1 (Domain depends on nothing), DEP-2/DEP-7 (MatchEngine purity), DEP-3 (Application
never reaches outward), DEP-4/DEP-8 (composition roots are leaves), DEP-6 (Contracts stay transport
only).

## Layer 4 — Infrastructure integration tests against real PostgreSQL (`tests/TouchlineManager.Infrastructure.Tests`)

Testcontainers PostgreSQL 17. **Never** an in-memory EF provider: the behaviour under test is
partial unique indexes, check constraints, `FOR UPDATE SKIP LOCKED`, and lease expiry, none of which
an in-memory provider reproduces.

*Today:* redaction filter behaviour (the security control in
[`data-classification.md`](../security/data-classification.md) §4), and the durable queue —
enqueue idempotency, claim and lease recording, a leased job not being re-claimable early, recovery
from an expired lease, future-due jobs not being claimed early, completion being terminal, transient
reschedule within the retry budget, permanent dead-lettering, attempt-budget exhaustion, claim
ordering, and the lease-consistency check constraint.

## Layer 5 — API integration tests (`tests/TouchlineManager.Api.IntegrationTests`)

The real composition root over a real database, driven through `WebApplicationFactory<Program>`.
These catch wiring faults that unit tests cannot: option validation, middleware order, endpoint
mapping, and — as happened in Stage 1 — a service registered with the wrong lifetime.

*Today:* liveness/readiness/detailed health semantics (including that liveness does **not** depend on
the database), correlation ID propagation and substitution of unsafe values, RFC 9457 Problem
Details shape, that error bodies contain no server internals, that unimplemented modules expose no
endpoints, and the job probe's gating and idempotency.

## Layer 6 — Worker integration tests (`tests/TouchlineManager.Worker.IntegrationTests`)

The worker's own composition over a real database, asserting that a job enqueued by one component is
claimed, executed, and completed by another **without API involvement**. This is the walking
skeleton that every real deadline will reuse.

## Layer 7 — Frontend unit tests (`apps/web`, Vitest)

Signal stores, state transitions, and pure view logic. Component tests run in a DOM environment.

*Today:* `ApiError` mapping from Problem Details, and the application root.

## Layer 8 — End-to-end journeys (Playwright) — Stage 7 onward

The master plan's journeys (registration → club claim, squad → team sheet → version conflict,
matchday → highlights, seller/bidder/outbid/winner, rollover) arrive with the features they cover.
Playwright is introduced in Stage 7 with the match viewer, not earlier: a journey test for a screen
that does not exist tests nothing.

## Layer 9 — Match-engine validation — Stage 5

Golden output hashes per engine version, byte-identical repetition across supported platforms,
changed-seed behaviour, a 100,000-simulation distribution suite against documented ranges, and
fuzz/property tests. These live in `tests/TouchlineManager.MatchEngine.Tests` plus the benchmark
tool in `tools/simulation-benchmarks`.

## Layer 10 — Load, security, and restore — Stage 14

k6 scenarios for login bursts, dashboard reads, matchday polling, and concurrent bids; OWASP
dependency and dynamic baseline scans; a backup/PITR restore drill with integrity checks. The plan
requires 3x projected launch headroom.

---

## Gates

| Gate | When | Blocking |
|---|---|---|
| `dotnet format --verify-no-changes` | Every pull request | Yes |
| Release build, warnings as errors | Every pull request | Yes |
| All .NET test projects | Every pull request | Yes |
| Angular typecheck, build, unit tests | Every pull request | Yes |
| Generated migration SQL | Every pull request, uploaded as an artefact | Review |
| Bundle size budgets | Every frontend build | Yes (Angular budgets) |
| Dependency, licence, secret, container scans | Every pull request (Stage 14 for containers) | Yes |
| Restore drill | Before beta and before launch | Yes |

## Rules

1. A bug fix adds the test that would have caught it. A retried job, a duplicate tenure, or a
   partially published matchday is a test, not a ticket.
2. A concurrency guarantee is proven by a test that runs the contending operations for real.
3. `FakeClock`, never a `Thread.Sleep`, for anything time-dependent in a unit or application test.
4. Integration tests are allowed to be slow; unit tests are not. If a unit test needs a database, it
   is in the wrong project.
5. Assertions state the guarantee: `.Should().BeFalse("re-enqueueing the same business action must be
   a no-op (ADR-0003)")` beats `.Should().BeFalse()`.
