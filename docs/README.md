# Documentation Index

Start here. Documents are ordered from "what the product is" to "how it is operated".

## Product rules (normative)

| Document | Purpose |
|---|---|
| [`product/master-plan.md`](product/master-plan.md) | The approved master implementation plan. Product contract, architecture, stages, definition of done. |
| [`product/game-rules.md`](product/game-rules.md) | **The normative rule set.** Every configurable value, with stable refs (`WORLD-4`, `CAL-3`, `TRF-8`, …) cited by tests, runbooks, and support. |
| [`product/glossary.md`](product/glossary.md) | Canonical vocabulary. Use these terms verbatim in code, schema, API, and UI. |
| [`product/mvp-traceability.md`](product/mvp-traceability.md) | Every MVP feature mapped to a module and a stage, plus a guardrail per non-goal. |
| [`product/content-and-fictional-data-policy.md`](product/content-and-fictional-data-policy.md) | UI voice, localization readiness, accessibility-in-content rules, and the fictional-data/legal mandate. |
| [`product/stage-0-review.md`](product/stage-0-review.md) | Stage 0 consistency review, exit criteria, and the decisions requested before later stages. |

## Architecture

| Document | Purpose |
|---|---|
| [`architecture/context.md`](architecture/context.md) | C4 context and container diagrams, deployment mapping, trust boundaries, principal data flows. |
| [`architecture/modules.md`](architecture/modules.md) | Bounded modules, dependency direction (enforced by tests), module interaction rules. |
| [`architecture/data-model.md`](architecture/data-model.md) | Schema map, per-schema ER diagrams, critical constraints and indexes, JSONB policy, migration policy. |
| [`architecture/adr/`](architecture/adr/README.md) | Architecture Decision Records and the rules for changing them. |

## Security

| Document | Purpose |
|---|---|
| [`security/threat-model.md`](security/threat-model.md) | Assets, adversaries, trust boundaries, STRIDE threats with mitigations, and the deadline-critical workflow review. |
| [`security/data-classification.md`](security/data-classification.md) | Data classes, handling matrix, retention, logging redaction list, environment rules. |

## Testing

| Document | Purpose |
|---|---|
| [`testing/test-strategy.md`](testing/test-strategy.md) | The test layers, what each one is for, the gates, and the rules that keep them useful. |

## Reserved for later stages

- `docs/product/match-engine.md` — engine formulas and versioned constants (Stage 5)
- `docs/product/balancing.md` — measured distributions and tuning (Stage 5, 9, 10)
- `docs/operations/runbook.md` and peers — who may act, prechecks, exact action, validation, rollback (Stage 14)
- `docs/operations/cost-model.md` — provider tiers, limits, storage, backup retention, egress (Stage 14)
- `docs/api/` — committed OpenAPI artifacts and compatibility notes (Stage 6, once endpoints exist)

## Conventions

1. `game-rules.md` is normative for behaviour. If code disagrees with it, one of them is a bug.
2. A rule change requires an ADR **and** a `game-rules.md` update in the same change set.
3. Accepted ADRs are never edited to say something new — supersede them instead.
