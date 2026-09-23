# Architecture Decision Records

Index of ADRs for Touchline Manager Online. Format: Context / Decision / Consequences / Alternatives.

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](0001-modular-monolith.md) | Modular Monolith | Accepted | 2026-09-22 |
| [0002](0002-auth-and-session-model.md) | Authentication and Session Model | Accepted | 2026-09-22 |
| [0003](0003-postgresql-durable-job-queue.md) | PostgreSQL-Backed Durable Job Queue | Accepted | 2026-09-22 |
| [0004](0004-deterministic-match-engine.md) | Pure Deterministic Match Engine | Accepted | 2026-09-22 |
| [0005](0005-dynamic-pyramid-provisioning.md) | Dynamic Pyramid Provisioning and Backfill | Accepted | 2026-09-22 |
| [0006](0006-semantic-highlight-keyframes.md) | Semantic Keyframe Highlights | Accepted | 2026-09-22 |
| [0007](0007-pwa-first-delivery.md) | PWA-First Delivery with No Offline Mutations | Accepted | 2026-09-22 |
| [0008](0008-deployment-topology.md) | MVP Deployment Topology | Accepted | 2026-09-22 |

Rules:

- Supersede by writing a new ADR with a higher number and flipping the old one to **Superseded by ADR-NNNN**.
- ADRs record decisions that change money, deadlines, competitive fairness, ownership, security, or persistent history — such changes require an ADR plus an update to `docs/product/game-rules.md` where applicable (plan §17.3, §17.16).
