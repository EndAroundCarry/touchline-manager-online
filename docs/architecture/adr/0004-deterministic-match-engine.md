# ADR-0004: Deterministic, versioned match engine

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (implemented in Stage 5)
- **Related:** [`../../product/master-plan.md`](../../product/master-plan.md) §8, [ADR-0006](0006-semantic-highlight-keyframes.md)

## Context

Matches decide promotion, money, and reputation, and are resolved while nobody is watching.
That creates three hard requirements simultaneously:

1. **Server authority** — a client must never be able to produce or influence a result.
2. **Reproducibility** — the same frozen input must always yield the same output, so a
   disputed result can be re-derived and an engine defect can be remediated without
   guesswork.
3. **Evolvability** — balance will change over a live season, but changing results already
   played would rewrite history.

Additionally, the engine is the one component that may later be benchmarked, extracted, or
parallelised, so it must not be tangled with infrastructure.

## Decision

**Isolation.** `TouchlineManager.MatchEngine` is a pure library. No method may touch the
database, system clock, network, filesystem, current culture, `Random.Shared`, or thread
scheduling. It depends on domain-neutral contracts only. Enforced by architecture and unit
tests.

**Determinism.**

- A stable, explicitly tested PRNG (PCG32) is implemented in-project. `System.Random`
  implementation details are never relied upon.
- The match seed is derived by HMAC over world secret + fixture ID + locked snapshot hash +
  engine version. This binds the result to the exact input and prevents a manager from
  choosing a favourable seed.
- Every random draw occurs in a documented order. Simulation never iterates an unordered
  dictionary or set, and never depends on parallelism.
- Canonical serialization sorts all collections and uses invariant numeric formatting before
  hashing. Input hash and output hash are stored.
- One match is simulated single-threaded. Throughput comes from running *independent*
  fixtures concurrently.
- Golden tests pin complete output hashes for representative inputs, and determinism is
  verified across supported OS/architecture targets in CI.

**Versioning.** Any change to formulas, draw order, serialization, or event semantics
requires a **new engine version**. A released engine version is never altered in place.
Engine builds and container digests are retained so historical matches remain replayable.

**Seed handling.** The raw seed is stored protected (encrypted at rest) alongside a public
commitment hash. The commitment is publishable; the seed is released only under the
documented operations policy (for example, during an integrity investigation).

**Input immutability.** Simulation reads only from an immutable input snapshot
(`match.input_snapshots`) captured at lock time. It never reads mutable live tables. A
delayed lock blocks kickoff simulation rather than allowing simulation from live state.

**Presentation is downstream.** Commentary and highlights consume emitted events. They cannot
change the outcome, and they cannot reveal hidden attributes.

## Consequences

**Positive**

- A published result can be re-derived byte-for-byte, which is the foundation of every
  dispute-handling and void/replay runbook.
- Balance changes are safe: new engine version, new matches, unchanged history.
- The engine is independently testable and benchmarkable without infrastructure.
- Statistical validation (100k+ simulation distributions) is meaningful because the
  randomness is controlled and reproducible.

**Negative**

- Discipline required: a harmless-looking refactor that changes draw order silently changes
  every historical replay. Mitigation: golden hash tests fail loudly, and engine changes are
  flagged in review.
- Protected seed storage adds a secrets dependency to the simulation path.
- Version proliferation: old engine versions must remain compiled and testable, which costs
  build time and requires retaining artifacts.
- No use of convenient framework helpers (`Random`, LINQ over hash sets, culture-dependent
  formatting) inside the engine, which is a constant code-review concern.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `System.Random` | Implementation is not contractually stable across runtimes/versions; a runtime upgrade could silently change historical replays. |
| Statistical/non-seeded simulation | Cannot be reproduced; disputes are unresolvable; void/replay remediation impossible. |
| Client-side or client-triggered simulation | Directly violates server authority; trivially exploitable. |
| Storing full event streams only, no hashes | Detects nothing; provides no commitment to the input. |
| Altering formulas in place with a config flag | Makes historical replay depend on configuration archaeology rather than a versioned artifact. |
