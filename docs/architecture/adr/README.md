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
| [0012](0012-daily-progression-materialised-job.md) | The daily progression is a materialised, feature-gated world job | Accepted (focus model and `training-v1` superseded by ADR-0057) |
| [0013](0013-engine-arithmetic-and-scoreline-effect.md) | Integer basis-point arithmetic and a bounded scoreline effect in the engine | Accepted |
| [0014](0014-matchday-lock-resolution-and-publication.md) | A matchday is locked, resolved, and published by three jobs, and its snapshot is a stored document | Accepted |
| [0015](0015-compressed-test-clock.md) | A compressed test clock is chosen at composition and refused in Production | Accepted (decision 4 superseded by ADR-0049) |
| [0016](0016-non-production-matchday-trigger.md) | A non-production matchday trigger for the end-to-end watch journey | Accepted |
| [0017](0017-match-load-at-publication.md) | A match's load on the squad is derived from the stored result and applied at publication | Accepted |
| [0018](0018-ai-club-policy.md) | A club nobody manages is set up by a pure, versioned policy run by the worker | Accepted (training choice superseded by ADR-0057) |
| [0019](0019-engine-v2-and-season-statistics.md) | The player line carries assists and a rating, and season statistics are a projection of published results | Accepted |
| [0020](0020-projection-rebuild-and-reconciliation.md) | Projections are reconciled and rebuilt by recomputing them from published results | Accepted |
| [0021](0021-competition-rules-and-visible-draw.md) | The tie-break order and the season's draw are public, and the criteria have one definition | Accepted |
| [0022](0022-append-only-club-ledger.md) | The club ledger is append-only, and its balances are a projection of it | Accepted |
| [0023](0023-income-expenses-and-gate-revenue.md) | Income and expenses are postings made by the workflow that causes them, and the gate is drawn inside publication | Accepted |
| [0024](0024-market-scouting-and-timed-auctions.md) | The market is a `market` module, and auctions resolve at a daily window under a reservation held in the ledger | Accepted |
| [0025](0025-ai-transfer-market.md) | The AI transfer market is a pure, versioned policy routed through the human write path | Accepted |
| [0026](0026-bid-serialisation.md) | Bids on one listing are serialised with a transaction-scoped advisory lock | Accepted |
| [0027](0027-inactivity-ladder.md) | The inactivity ladder is one scheduled pass over real time, and a login is the return | Accepted |
| [0028](0028-outbox-notifications.md) | Outbound notifications are written to an outbox and dispatched asynchronously | Accepted |
| [0029](0029-news-and-notification-preferences.md) | The division news feed and notification preferences are comms-module data | Accepted |
| [0030](0030-provisioning-execution-and-bootstrap-provenance.md) | Provisioning execution is a worker job under the country lock, and a backfilled round is generated history | Accepted |
| [0031](0031-season-rollover-state-machine.md) | Season rollover is one world-scoped resumable job with a checkpoint row | Accepted |
| [0032](0032-rollover-continuity.md) | Rollover continuity — contract expiry, retirement, awards, and season finance summaries | Accepted |
| [0033](0033-club-identity-seeded-from-the-world-seed.md) | Club identity is seeded from the world seed, never from the per-tier provisioning seed | Accepted |
| [0034](0034-operator-rollover-preview-and-resume.md) | Operator preview and resume for the season rollover | Accepted |
| [0035](0035-five-season-staging-run.md) | A five-season staging run proves rollover continuity | Accepted |
| [0036](0036-account-sessions-export-and-preferences.md) | Account sessions, machine-readable export, and formatting preferences | Accepted |
| [0037](0037-pwa-update-ux-and-offline-boundary.md) | PWA update UX, offline mutation gating, and the stale-read indicator | Accepted |
| [0038](0038-responsive-shell-and-touch-targets.md) | Responsive shell, touch-target baseline, and the breakpoint test matrix | Accepted |
| [0039](0039-accessibility-baseline-and-axe-gate.md) | Accessibility baseline and the axe gate | Accepted |
| [0040](0040-guided-help-and-first-steps.md) | Guided help and the first-steps surface | Accepted |
| [0041](0041-privacy-safe-operational-funnels.md) | Privacy-safe operational funnels | Accepted |
| [0042](0042-operator-access-and-mfa.md) | Operator access — role administration, TOTP MFA, and the gated admin surface | Accepted |
| [0043](0043-operator-read-console.md) | Operator read console — jobs, matchdays, and audit | Accepted |
| [0044](0044-operator-recovery-commands.md) | Operator recovery commands — job retry and cancel, and matchday resume | Accepted |
| [0045](0045-administrative-repairs-and-broadcasts.md) | Administrative ownership and finance repairs, operator broadcasts, and the flag store | Accepted |
| [0046](0046-load-supply-chain-and-restore-drills.md) | Load, supply-chain, and restore drills, run locally | Accepted |
| [0047](0047-incident-read-only-mode.md) | Read-only incident mode, gated at the request edge | Accepted |
| [0048](0048-telemetry-dashboards-and-slo-alerting.md) | Telemetry dashboards and SLO alerting, over the instruments that exist | Accepted |
| [0049](0049-non-production-stepped-game-clock.md) | A non-production stepped game clock, advanced by an operator | Accepted |
| [0050](0050-public-information-pages-and-status.md) | Player-facing information pages and a public service status | Accepted |
| [0051](0051-engine-v4-continuous-passages.md) | Engine-v4 plays a possession along a continuous passage, and records the film as a side channel | Accepted |
| [0052](0052-replay-v3-film-and-reel.md) | Replay-v3 turns a match into one continuous film with a companion highlights reel | Accepted (decisions 2 and 3 superseded by ADR-0054) |
| [0053](0053-engine-v5-half-time-clock-and-restart-ownership.md) | Engine-v5 resets the half-time clock, gives every dead ball an owner, and completes the passage record | Accepted |
| [0055](0055-engine-v6-skills-where-the-design-says.md) | Engine-v6 puts skills where the design says they are: tiredness, shot contest scale, duel fouls, corner takers | Accepted |
| [0054](0054-replay-v4-constant-pace-film.md) | Replay-v4 plays the whole match as one constant-pace film on one continuous timeline | Accepted |
| [0056](0056-engine-v7-passes-and-take-ons.md) | Engine-v7 counts passes and take-ons on the player line, and the season statistics store them | Accepted |
| [0057](0057-training-programmes-age-curve-and-hidden-aptitude.md) | Training programmes, an age curve, and hidden aptitude (`training-v2`) | Accepted |

## Rules for changing an ADR

1. Accepted ADRs are never edited to say something new. Write a new ADR and mark the old
   one `Superseded by ADR-NNNN`.
2. Any change to money, deadlines, competition fairness, ownership, security, or persistent
   history is an ADR-level change and must be reviewed before implementation.
3. An ADR that changes a settled rule in `game-rules.md` must update that file in the same
   change set, plus the affected stage in `master-plan.md`.
