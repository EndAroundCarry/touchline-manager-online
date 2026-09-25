# Architecture Decision Records

ADRs record decisions that are expensive to reverse. Behavioural rules live in
[`docs/product/game-rules.md`](../../product/game-rules.md); this folder records *why*
the system is shaped the way it is.

## Status legend

| Status | Meaning |
|---|---|
| Proposed | Written, not yet binding |
| Accepted | Binding; deviations require a new ADR that supersedes it |
| Superseded by ADR-NNNN | Historical only |
| Deprecated | No longer applicable, kept for history |

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-modular-monolith.md) | Modular monolith with explicit module boundaries | Accepted |
| [0002](0002-auth-and-session-model.md) | Authentication and session model | Accepted |
| [0003](0003-postgresql-durable-jobs.md) | PostgreSQL-backed durable job queue | Accepted |
| [0004](0004-deterministic-match-engine.md) | Deterministic, versioned match engine | Accepted |
| [0005](0005-dynamic-pyramid-and-backfill.md) | Dynamic pyramid expansion with deterministic backfill | Accepted |
| [0006](0006-semantic-highlight-keyframes.md) | Semantic keyframe highlights with client interpolation | Accepted |
| [0007](0007-pwa-first-delivery.md) | PWA-first client delivery | Accepted |
| [0008](0008-deployment-topology.md) | MVP deployment topology | Accepted |
| [0009](0009-time-identity-and-concurrency.md) | Time, identity, and concurrency conventions | Accepted |
| [0010](0010-club-takeover-serialisation.md) | Club takeover serialises with an advisory lock, not with `SERIALIZABLE` | Accepted |
| [0011](0011-squad-schema-and-hidden-player-values.md) | Squad schema: hidden player values are server-only columns, and contract/registration agreement is an application invariant | Accepted |
| [0012](0012-daily-progression-materialised-job.md) | The daily progression is a materialised, feature-gated world job | Accepted |
| [0013](0013-engine-arithmetic-and-scoreline-effect.md) | Integer basis-point arithmetic and a bounded scoreline effect in the engine | Accepted |
| [0014](0014-matchday-lock-resolution-and-publication.md) | A matchday is locked, resolved, and published by three jobs, and its snapshot is a stored document | Accepted |

## Rules for changing an ADR

1. Accepted ADRs are never edited to say something new. Write a new ADR and mark the old
   one `Superseded by ADR-NNNN`.
2. Any change to money, deadlines, competition fairness, ownership, security, or persistent
   history is an ADR-level change and must be reviewed before implementation.
3. An ADR that changes a settled rule in `game-rules.md` must update that file in the same
   change set, plus the affected stage in `master-plan.md`.
