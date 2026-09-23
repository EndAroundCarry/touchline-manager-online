# Stage 0 Review — Product Rules, Architecture, and Executable Specifications

> **Stage:** 0 (master plan §16)
> **Status:** Complete, with three decisions requested before the stages that depend on them.
> **Date:** 2026-09-23

---

## 1. Deliverables

| # | Required by master plan §16 Stage 0 | Delivered as |
|---|---|---|
| 1 | Adopt the plan as `docs/product/master-plan.md` | [`docs/product/master-plan.md`](master-plan.md) (verbatim copy of the approved candidate) |
| 2 | ADRs for the eight named decisions | [`docs/architecture/adr/`](../architecture/adr/) — ADR-0001 … ADR-0008, plus ADR-0009 (time/identity/concurrency) and an index with change rules |
| 3 | `game-rules.md` with every configurable value from §3 | [`docs/product/game-rules.md`](game-rules.md) — 17 rule groups, stable refs (`WORLD-*`, `CAL-*`, `TBL-*`, …), plus a consolidated constant reference and a list of deliberately deferred values |
| 4 | C4 context and container diagrams | [`docs/architecture/context.md`](../architecture/context.md) — C4 context, C4 container, deployment mapping, trust boundaries, principal data flows |
| 5 | First ER diagram | [`docs/architecture/data-model.md`](../architecture/data-model.md) — schema map plus per-schema `erDiagram`s with the critical constraints and indexes |
| 6 | Glossary | [`docs/product/glossary.md`](glossary.md) |
| 7 | English UI/content tone and fictional-data/legal rules | [`docs/product/content-and-fictional-data-policy.md`](content-and-fictional-data-policy.md) |
| 8 | Threat model and data classification | [`docs/security/threat-model.md`](../security/threat-model.md), [`docs/security/data-classification.md`](../security/data-classification.md) |
| 9 | Every MVP feature mapped to a module and a stage | [`docs/product/mvp-traceability.md`](mvp-traceability.md) — 51 features, the §20 checklist, and a guardrail per §2.3 non-goal |
| 10 | Module boundaries | [`docs/architecture/modules.md`](../architecture/modules.md) |

Nothing in Stage 0 produces production code, per the stage's `Deferred` clause.

---

## 2. Consistency verification

### 2.1 Arithmetic and structural checks (all pass)

| Check | Result |
|---|---|
| 18 clubs, double round-robin → rounds per club | 17 opponents × 2 = **34** — matches `CAL-1` |
| Fixtures per round | 18 ÷ 2 = **9** — matches master plan §7.4 ("all nine fixtures") |
| Fixtures per division-season | 34 × 9 = **306** |
| Initial clubs | 6 countries × 18 = **108** — matches Stage 3 exit criteria exactly |
| Initial players | 108 × 22 = **2,376** — matches Stage 4 ("approximately 2,376 initial players") exactly |
| Players per generated tier | 18 × 22 = **396** — matches Stage 11 ("18 AI clubs, 396 players") exactly |
| Season length in real weeks | 34 ÷ 3 ≈ 11.3 weeks, plus `CAL-6`'s 7-day rollover |
| Kickoff vs lock | 19:00 UTC kickoff, 30-minute lock → **18:30 UTC lock** (`CAL-2`, `CAL-3`) |
| Auction blackout vs kickoff | 6 h before 19:00 UTC → **13:00–19:00 UTC** daily blackout (`TRF-3`) |
| Tie-breaker ordering | 9 ordered rules in §3.5 ↔ `TBL-2`…`TBL-10` | 
| Formations / instructions enumerated | 6 presets (`TAC-1`…`TAC-6`), 8 instructions with stated value sets (`INS-1`…`INS-8`) |
| Basis-point scales | condition, fatigue, morale, sharpness all 0–10,000; coordinates and pitch geometry reuse the same 0–10,000 scale |
| Money | `bigint` minor units end to end; no floating point; additions revenue/expenses match `FIN-3`…`FIN-9` |
| Squad numbers | target 22, min 18 (≥2 GK), max 25, 11 starters, ≤7 bench, ≤5 subs — each consistent with §3.7 |
| Contract vs game year | 1–3 game seasons; contract years advance at rollover (`CON-8`, `TIME-3`) |
| Data-model uniqueness | `player_season_stats (division_season_id, player_id, club_id)` and `discipline_records (division_season_id, player_id)` match §6.5/§6.4 verbatim |
| Job idempotency | `unique (job_type, business_key)` in §6.9 ↔ ADR-0003 |

### 2.2 Contradictions found

**C-1 — `shortlists` schema placement (resolved).** Master plan §5.2 assigns scouting
shortlists to the `market` module; §6.5 documents the table as `squad.shortlists`. These
cannot both hold: a module writes only its own schema, and the shortlist API surface (§10.6)
belongs to the market feature set. **Resolution:** `market.shortlists`. Recorded in
[`data-model.md` §7](../architecture/data-model.md), including why module ownership wins and
why this is a placement decision rather than a behaviour change.

**C-2 — none found elsewhere.** Checked and consistent: §3.5 tie-breakers ↔ data model;
§6.4 statistics uniqueness ↔ §3.5; §3.2 backfill ↔ §7.5 rollover interaction; §9.3 highlight
entity count ↔ §11.1 viewer; §2.2 MVP list ↔ §16 stages ↔ §20 checklist; §5.2 module list ↔
§6 schema list (other than C-1); `world_id` presence ↔ §2.1 one-world decision; §4.6 UUIDv7 ↔
ADR-0009; §10.9 deadline handling ↔ `TIME-5`.

### 2.3 Non-contradiction checks that required an explicit rule

Two gaps in the plan were closed by adding rules, because without them two readers would
implement two behaviours:

| Gap | Rule added |
|---|---|
| The plan never says what happens at the top of a country's pyramid, where §3.6 only describes relegation from the higher tier and promotion from the lower one. | `PR-9`, `PR-10`: no promotion out of the top active tier; a single-tier country has neither promotion nor relegation. |
| The plan resets yellow accumulation at rollover but never says whether an unserved suspension carries. `DIS-8` now states that it does. | `DIS-8` (see D-2 below for the confirmation request). |

---

## 3. Decisions requested

These are the only places where the plan's text admits two readings **and** the choice
affects ownership, fairness, money, or history. Per master plan §17.3 they are surfaced rather
than silently chosen.

### D-1 — Does an `inactive` tenure still count toward pyramid capacity? *(CONFIRMED — needed before Stage 3/11)*

**Decision (confirmed): an `inactive` tenure still counts as human occupancy until it closes.**
Recorded as `OCC-8`. A temporarily absent manager still holds the club and resumes without a
new claim, so a tier that is full of managers cannot provision an unnecessary extra tier. A
**suspended** account does not count, because §3.3 excludes it explicitly.

Consequence for Stage 3: the capacity evaluation counts tenures whose `control_status` is
`active` **or** `inactive`, excluding only those whose account is suspended or whose release
date is set. This is the query Stage 3 implements.

**The ambiguity that was found.** §3.3 defines "human managed" as an **active** tenure whose
account is not suspended and whose release date is null, while also saying that at 14 days the
tenure is marked **`inactive`** (not closed) and that the manager resumes simply by logging in;
it closes only at 21 days. §3.2 triggers provisioning when "all 18 clubs have active human
tenures". Read strictly, a tier can be physically full of managers while **zero** of them are
"human managed" — making a full tier look empty.

**Rejected alternative:** count only `active` tenures, which would let a tier full of
temporarily-absent managers provision an unnecessary extra tier.

### D-2 — Do unserved suspensions carry into the next season? *(needed before Stage 8)*

§3.10 states suspensions are served against the club's next eligible league fixtures and that
yellow accumulation resets at rollover, but is silent about an unserved red-card suspension at
season end.

**Recorded interpretation (now `DIS-8`):** it carries into the next season's fixtures.

**Alternative:** wipe all outstanding suspensions at rollover, letting a player "serve" a
suspension by the season turning over.

### D-3 — Is a fixed retirement mechanism in MVP scope? *(needed before Stage 12)*

§7.5 step 7 says to apply "retirement rules only after they are introduced in a tested
feature", and notes "MVP may use an age cap and deterministic retirements at rollover" —
leaving it optional. Without any retirement, no player ever leaves the world and generated
squad quotas drift over seasons.

**Recorded interpretation:** MVP uses an age cap plus deterministic rollover retirements, with
the existing emergency-replacement path covering minimum-squad gaps.

**Alternative:** no retirements in MVP, accepting long-run roster drift.

### 3.1 Lower-risk ambiguities recorded, not escalated

| # | Ambiguity | Recorded decision | Needed before |
|---|---|---|---|
| L-1 | §3.13 says "fixed daily resolution windows" without a time of day | One configured daily window at a fixed UTC time outside the 13:00–19:00 UTC blackout; value set during balancing | Stage 10 |
| L-2 | §3.13 says manager identity "may" stay hidden before resolution | Bidder identity hidden before resolution; buyer and seller clubs shown in completed market history | Stage 10 |
| L-3 | Whether the free-agent market is enabled in MVP | Decided by measured market health, as §3.11 permits; the auction mechanism already exists | Stage 10 |
| L-4 | Exact gate/sponsorship/award/wage constants; AI valuation bands; training curve constants; minimum bid increment | Balancing outputs, recorded in the rule set rather than invented at Stage 0 | Stages 9, 10, 4/5 |
| L-5 | §3.4 requires a configured first-season kickoff | A configured future Tuesday with onboarding lead time; set before launch, never derived from deployment time (`CAL-5`) | Stage 16 |

---

## 4. Security and operations review of deadline-critical workflows

Master plan §16 requires this before Stage 0 can be considered done. The full review is in
[`threat-model.md` §5](../security/threat-model.md); it covers nine workflows (fixture lock,
matchday simulation, matchday publication, auction resolution, club takeover, division
provisioning, season rollover, weekly finance, inactivity evaluation) against six questions:
who may act, atomicity, retry safety, recovery path, operator visibility, and worst case.

**Outcome:** each workflow has a named writer, a single-transaction boundary, an idempotency
key, a retry-safe definition, an operator read and resume path, and an SLO or alert attached.
Two follow-ups were added to later stages rather than left implicit:

1. Distinct score-level protections exist for publication being atomic, but the plan does not
   state what a *staged* fixture may expose. Resolved: staged fixtures are never readable
   through public endpoints (`I-6`, Stage 6).
2. The redaction list in the data classification is enforced by a logging filter with an
   automated test, so "tokens never appear in logs" is verified rather than asserted
   (`data-classification.md` §4, Stage 1).

---

## 5. Environment findings

| Finding | Impact | Action |
|---|---|---|
| Installed SDKs: `8.0.425`, `10.0.400-preview.0.26322.102` | A **preview** SDK would be pinned if we pin it naively. The GA runtime `Microsoft.NETCore.App 10.0.9` and `Microsoft.AspNetCore.App 10.0.9` are present, so the target framework is fine. | Stage 1 pins `global.json` explicitly and states whether a preview SDK is acceptable. Installing a GA `10.0.4xx` SDK is preferred before CI is wired. |
| Docker 29.6.2 available | Testcontainers integration tests are feasible locally | None |
| Node 24.20.0 available | Angular workspace feasible | None |
| Repository state at Stage 0: single `main` commit `7975c4a`, plus this plan document | Clean baseline for Stage 1 | None |

---

## 6. Exit criteria

| Stage 0 exit criterion | Status | Evidence |
|---|---|---|
| No contradictory rule remains across plan, rules, ADRs, and ER model | **Met** | C-1 resolved and documented; §2.1/§2.2 check tables; `PR-9`, `PR-10`, `DIS-8` added |
| Every public MVP feature maps to a module and planned stage | **Met** | 51 items in [`mvp-traceability.md`](mvp-traceability.md) §1, §20 checklist in §2, §2.3 non-goals guarded in §3 |
| Security and operations review the deadline-critical workflows | **Met** | [`threat-model.md` §5](../security/threat-model.md), summary in §4 above |
| No production code beyond what validates decisions | **Met** | Only documentation was created |
| Genuine blockers discovered while formalizing rules are resolved, not silently altered | **Met** | C-1 resolved in the open; D-1…D-3 escalated; L-1…L-5 recorded with the stage that must settle them |

**Stage 0 is complete.** D-1 is confirmed (see above). D-2 and D-3 remain open and are needed
before Stages 8 and 12 respectively; neither blocks Stage 1.

---

## 7. What Stage 1 will produce

Per master plan §16 Stage 1, once confirmed:

1. `TouchlineManager.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
   `.editorconfig`, lockfiles, strict analyzers, root README, `CHANGELOG.md`.
2. Project scaffold for Domain, Application, Infrastructure, Contracts, MatchEngine, Api,
   Worker, the Angular PWA, and all test projects.
3. Docker Compose for PostgreSQL and a mail catcher.
4. Health endpoints (liveness/readiness distinguishing database state), configuration
   validation, correlation middleware, Problem Details, structured logging with the redaction
   filter, `IClock` seams, and empty module endpoint groups.
5. Angular standalone shell with PrimeNG/Tailwind layer ordering, responsive skeleton, PWA
   manifest and service worker, strict CSP-compatible build.
6. Architecture tests enforcing the dependency rules in
   [`modules.md`](../architecture/modules.md) §2, and the first GitHub Actions PR workflow.
7. One no-op durable job end to end, proving API → database → worker composition.

Exit: one documented command starts database, API, worker, and web locally; CI builds every
project and runs tests from a clean checkout; health checks distinguish liveness, readiness,
and database state; architecture tests enforce dependencies.
