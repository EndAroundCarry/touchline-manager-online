# Threat Model

> **Method:** asset-driven STRIDE over the trust boundaries in
> [`../architecture/context.md`](../architecture/context.md) §4, with explicit attention to the
> product's unusual risk: it is a competitive game where money, deadlines, and results are
> decided server-side while nobody is watching.
> **Status:** first pass (Stage 0). Reviewed again before closed beta (Stage 15) and before
> public launch (Stage 16).

---

## 1. What we are protecting

| Asset | Why it matters | Primary store |
|---|---|---|
| **Result integrity** | A forged, replayed, or influenced match result destroys the league's meaning | `match.*`, `competition.standings` |
| **Money integrity** | Duplicate or invented money breaks the market and every club's planning | `finance.club_accounts`, `finance.ledger_entries` |
| **Ownership integrity** | Who controls a club decides who can act in the market | `world.club_tenures` |
| **Deadline integrity** | Lock, kickoff, auction resolution, and rollover are the rules of the game | `ops.jobs`, `competition.matchdays` |
| **Historical integrity** | Past seasons must remain explainable and immutable | `competition.*`, `match.*`, `finance.ledger_entries` |
| **Credentials and sessions** | Account takeover is a competitive-integrity attack | `auth.*` |
| **Personal data** | Email, IP hashes, device hashes, consent records | `auth.*`, `comms.*`, `ops.audit_log` |
| **Operational capability** | Operators must be able to recover without bypassing rules | `ops.*` |

---

## 2. Adversaries

| Adversary | Motivation | Capability |
|---|---|---|
| **Cheating manager** | Win matches, get players cheaply, protect a lead in the table | Uses the product as designed; manipulates timing, multiple accounts, collusion, client tampering |
| **Multi-account / colluder** | Funnel players and money between clubs | Multiple verified accounts, shared devices/networks |
| **Hostile client** | Alter outcomes or balances | Full control of the browser; can replay requests, edit JS, forge headers |
| **Credential attacker** | Account takeover, enumeration, reputation damage | Credential stuffing, password spraying, reset abuse |
| **Insider / over-privileged support** | Curiosity, favouritism, financial or social gain | Legitimate admin tools used improperly |
| **Opportunist** | Cause disruption | Rate floods, oversized payloads, pathological search queries |
| **Failure itself** | Lost or duplicated work | Not an adversary, but the largest real risk: crashes, restarts, deploy timing |

---

## 3. Trust boundaries

See [`../architecture/context.md`](../architecture/context.md) §4. Summary: TB-1 browser↔edge,
TB-2 edge↔API, TB-3 API↔database, TB-4 worker↔database, TB-5 API/worker↔email,
TB-6 operator↔admin API, TB-7 engine↔everything (no boundary: it is pure).

---

## 4. Threats and mitigations

### 4.1 Spoofing

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| S-1 | Stolen refresh cookie used to impersonate a manager | `HttpOnly` + `Secure` + `SameSite=Lax` cookie; hashed at rest; rotation on every use; reuse revokes the whole token family; `logout-all` bumps the security stamp | 2 |
| S-2 | Access token exfiltrated by XSS | Access token held in memory only, minutes-long lifetime; strict CSP with no inline script; `frame-ancestors` denied | 2, 14 |
| S-3 | Credential stuffing | Generic responses (no enumeration), per-route/IP/account rate limits, progressive lockout, optional bot challenge | 2 |
| S-4 | Self-asserted club/manager identity in a command body | Server derives manager and club from the authenticated tenure; client-supplied ownership is ignored (`INT-1`) | 3, 6, 10 |
| S-5 | Forged `Origin` to defeat CSRF assumptions | Exact allowlist asserted by the API itself, not only the edge; state-changing requests rejected on mismatch | 2 |
| S-6 | Operator impersonation | MFA required for `support`/`operator`/`admin`, re-asserted per admin mutation | 2, 14 |
| S-7 | Spoofed email sender damaging deliverability | Provider-managed authenticated sending (SPF/DKIM/DMARC), no user-controlled sender | 14 |

### 4.2 Tampering

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| T-1 | Client submits a result, rating, or statistic | There is no simulate endpoint. The engine runs only in the worker from an immutable snapshot (`MAT-1`, `MAT-2`) | 5, 6 |
| T-2 | Client submits its own balance, fee, or deadline | Balances, fees, and deadlines are always read server-side; requests carry intent only (`INT-1`) | 9, 10 |
| T-3 | Manager edits tactics/lineup after lock | Lock is a durable job that freezes a hashed snapshot; writes after lock affect later fixtures only; optimistic version check on the lock | 6 |
| T-4 | Retried request duplicates a bid, listing, claim, or payment | Idempotency keys with request-hash binding; unique business keys on jobs; destination-table uniqueness (`CONC-3`, `FIN-17`) | 3, 9, 10 |
| T-5 | Bonus bid accepted as a new bid (same amount, later) | Tie resolution uses stored `bid_sequence`, not arrival time (`TRF-8`, `CONC-4`) | 10 |
| T-6 | Standings or stats edited through a non-public path | Projections are rebuildable and reconciled; admin corrections are audited repairs, never direct edits (`TBL-13`) | 8, 14 |
| T-7 | Finance balance edited directly | Ledger is append-only; balances only move through ledger entries; admin tooling offers compensating entries only (`FIN-12`) | 9, 14 |
| T-8 | Historical result quietly changed | Immutable snapshots, stored input/output hashes, engine versions, immutable season/entry history, versioned void/replay procedure (`MAT-9`, `PR-6`) | 5, 6, 12 |
| T-9 | SQL injection through filters or search | EF parameterization only; no string-built SQL from request data; validated enums rejected on unknown schema versions (master plan §12.2) | 1, 10 |
| T-10 | Seed guessed or chosen to produce a favourable result | Seed derived by HMAC over world secret + fixture ID + snapshot hash + engine version; stored encrypted with a public commitment hash (ADR-0004) | 5, 6 |

### 4.3 Repudiation

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| R-1 | A manager denies placing a bid or claiming a club | Append-only audit log with actor, tenure, correlation ID, timestamp, and IP hash; server-side idempotency records hold the request hash | 2, 10 |
| R-2 | An operator denies making a repair | Every admin mutation requires a reason, an idempotency key, and writes `ops.repair_actions` with a dry-run result and compensating action | 14 |
| R-3 | A disputed result cannot be re-derived | Frozen snapshot, stored seed, versioned engine, stored hashes, retained engine artifacts | 5, 6, 14 |
| R-4 | A job "didn't run" and nobody can tell why | Job attempt history, lease timestamps, last error, dead-letter state, and queue metrics | 6 |

### 4.4 Information disclosure

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| I-1 | Hidden potential, internal valuation, or detection thresholds leak | Those values live in server-only columns or restricted tables and never appear in a manager-facing DTO (`MAT-11`) | 3, 4, 10 |
| I-2 | Email/IP/device hashes exposed to other managers | Never serialized in player-facing responses; support-only access; hashed, not raw | 2, 10 |
| I-3 | Account enumeration via login/reset/registration | Generic responses and comparable timing for all three | 2 |
| I-4 | Tokens, passwords, cookies, raw email, or snapshots in logs | Structured logging with an explicit redaction list and tests asserting redaction (Stage 0 exit criterion) | 1, 2 |
| I-5 | Another manager's private data readable by ID guessing | Resource policies derive ownership from the authenticated tenure; UUIDv7 IDs; every manager endpoint tested against "other club" | 6, 14 |
| I-6 | Unpublished results visible before publication | Publication is atomic; staged fixtures are not readable through public endpoints | 6 |
| I-7 | Match payload grows until it is an abuse vector | Payload caps on match presentations, search, pagination, and request bodies; pathological payloads rejected | 6, 7 |
| I-8 | Diagnostics leak through error responses | RFC Problem Details with a stable code and safe detail; no exception text, stack trace, or SQL | 1 |

### 4.5 Denial of service

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| D-1 | Login or registration flood | Per-route and per-IP-prefix rate limits; cheap rejection before hashing where safe | 2, 14 |
| D-2 | Expensive global search queries | Mandatory filters or capped result sets, cursor pagination, index-only plans, statement timeouts | 10 |
| D-3 | Matchday thundering herd of polling clients | ETags, 60-second `/sync` while visible, back-off in hidden tabs | 7, 13 |
| D-4 | Oversized request bodies | Content-type and size validation on all mutable requests | 1 |
| D-5 | Auction contention in the final minutes | Bounded increment rules, one active bid per club per listing, reservations, indexed resolution ordering | 10 |
| D-6 | Job queue flooded by a hostile producer | Bounded handlers, per-type concurrency caps, dead-letter classification, alerts | 6 |
| D-7 | Worker outage during a matchday | Durable jobs, leases, retry, idempotent handlers, operator resume, SLO alerting on publication delay | 6, 14 |

### 4.6 Elevation of privilege

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| E-1 | Manager reaches an admin route | Role-based policies on every admin route; admin UI lazy-loaded and role-protected; API is authoritative | 14 |
| E-2 | AI code path bypasses a rule a human must obey | Humans and AI share the same validators; AI has no privileged finance or attributes; covered by tests (`INS-12`, `FIN-15`) | 8, 10, 11 |
| E-3 | Support role used to alter competitive outcomes | Support cannot mutate game state; only `operator`/`admin` with MFA, reason, and compensating-action tooling | 14 |
| E-4 | Emergency grant or repair used as a gameplay advantage | Grant/repair requires an incident reference, dry run, approval, audit, and player notification; alerted and tuned out through balancing (`FIN-16`) | 9, 14 |
| E-5 | Job payload tampering grants cross-module write | Job handlers call application use cases with the same authorization and invariant checks as the API; payloads are validated | 6 |

### 4.7 Game-specific integrity threats

| # | Threat | Mitigation | Stage |
|---|---|---|---|
| G-1 | Collusion: two accounts funneling players cheaply | Below-value transaction detection, reciprocal-pattern signals, account-overlap hashes, rapid resign/reclaim detection → review cases. Results are never silently altered (`INT-4`) | 10, 14 |
| G-2 | Multi-account farming to occupy a division and force provisioning | Inactivity thresholds, one tenure per account, cooldowns, and capacity evaluation based on active human tenures only | 3, 11 |
| G-3 | Sniping an auction at the last second | Fixed resolution windows, minimum increment, reservation from the moment a bid leads, no anti-sniping extension in MVP (explicitly out of scope) | 10 |
| G-4 | Refusing to play to avoid relegation | Matches resolve server-side regardless of manager presence; a locked snapshot is always produced by deterministic repair (`DIS-6`) | 6 |
| G-5 | Inheriting a strong club by timing a takeover | Takeover always inherits the club exactly as it exists; upper-tier vacancies are not claimable; promotion/relegation is the only path upward (`WORLD-8`, `WORLD-9`) | 3, 12 |
| G-6 | Exploiting the reset window after resignation | Seven-day cooldown and permanent tenure history (`OCC-4`) | 3 |
| G-7 | Bot-like speed advantage at a deadline | Deadlines are server-enforced; clients cannot queue offline mutations; rate limits apply per manager and club | 6, 10, 13 |
| G-8 | Repeated void/replay requests to fish for a better result | Void requires an operator reason, preserves the original record, reuses the original seed unless a versioned engine defect is proven, and notifies affected managers (`MAT-10`) | 14 |

---

## 5. Deadline-critical workflow review

Stage 0's exit criteria require a security and operations review of the workflows that decide
results and money. Each is reviewed here against: who may act, what the preconditions are,
what makes it atomic, what happens on retry, and how it is recovered.

| Workflow | Who may act | Atomicity | Retry safety | Recovery |
|---|---|---|---|---|
| **Fixture lock** (kickoff − 30 min) | Worker only | Snapshot insert + fixture status + repair notices + outbox in one transaction, optimistic version check | Unique `(fixture_id)` on snapshots and idempotent business key | Re-enqueue; kickoff waits for a valid snapshot rather than simulating from live tables |
| **Matchday simulation** | Worker only | Each fixture staged independently; no public projection changes | Re-running the same snapshot/seed/engine reproduces the same output hash; `simulation_attempts` records every attempt | Retry per fixture; never publish a partial matchday |
| **Matchday publication** | Worker only, operator-resume | One transaction updates fixtures, matches, standings, stats, discipline, injuries, state, gate revenue, and outbox | Publication is guarded by matchday status; re-entry is a no-op | Operator inspects `GET /admin/matchdays/{id}` and resumes; never fabricates forfeits |
| **Auction resolution** | Worker only | Serializable transaction: revalidation, payment, credit, registration, contracts | Unique business key plus `transfer_outcomes` uniqueness prevents double charging | Retry picks the next valid bid per documented rules; every outcome audited |
| **Club takeover** | Verified active manager | Serializable transaction locking manager, club, capacity, and tenure rows | Idempotency key on the claim; partial unique indexes make duplicate tenures impossible | Stable `409` codes: `CLUB_ALREADY_CLAIMED`, `MANAGER_HAS_ACTIVE_CLUB`, `CAPACITY_PROVISIONING` |
| **Division provisioning** | Worker only | Country advisory lock + `unique (country, target_tier)`; generation is checkpointed | Idempotent on the business key; activation only after validation | Failure leaves the tier non-claimable and the request in a diagnosable state; never partially assigns |
| **Season rollover** | Worker only, operator-preview and resume | Resumable state machine with per-step checkpoints; country-scoped transactions | Every step is idempotent on its checkpoint; a country failure does not corrupt others | Operator previews, then resumes from the last checkpoint; final world activation waits for all countries |
| **Weekly finance run** | Worker only | Per-club ledger postings with correlation keys | Idempotent per club per run; ledger replay reconciles exactly | Re-run the job; corrections use compensating entries only |
| **Inactivity evaluation** | Worker only | Status transition + AI takeover + notifications in one transaction | Idempotent per tenure per threshold | Re-run; thresholds are audited configuration |

**Cross-cutting answers**

- **Who may act:** only the worker (business deadlines), only the authenticated tenure owner
  (manager intents), only MFA-authenticated operators with a reason (repairs). No client ever
  decides an outcome.
- **Preconditions:** every workflow validates authorization, resource state, and invariants
  inside the transaction that performs its effect, not in a prior request.
- **Replay and repudiation:** every workflow stores its correlation/idempotency key, hashes
  its inputs, and writes an audit or attempt record.
- **Operator visibility:** every workflow has an admin read path and a resume/retry path with
  a required reason.
- **Worst case:** a missed matchday is an SLO breach with an operator runbook, not a silent
  data loss. `99%` of matchdays must publish within five minutes of kickoff, and no accepted
  bid, claim, published fixture, or finance posting may be lost.

---

## 6. Residual risks accepted for MVP

| Risk | Why accepted | Revisit |
|---|---|---|
| Shared households may be flagged as collusion | Detection is advisory and never auto-punishes (`INT-3`) | After beta signals are measured |
| No anti-sniping extension invites last-second bidding | Explicit MVP non-goal; fixed windows and reservations are the designed answer | Post-MVP |
| Admin MFA is a flag requirement, not enforced in staging | Staging holds no real data | Enforced from Stage 14 |
| Single region means a provider incident pauses the game | Deadline correctness beats degraded continuity; promotion rises from the incident runbook | Stage 21 |
| An operator with a systemic engine defect can only void/replay | Preserving history is more valuable than instant repair | Stage 14 exercises |

## 7. Open source / dependency and supply-chain posture

| Ref | Rule |
|---|---|
| SC-1 | Dependencies are restored from lockfiles; CI fails on unresolved or drifting versions. |
| SC-2 | Dependency, license, secret, and container vulnerability scanning run in the pull-request pipeline. |
| SC-3 | Secrets live in environment/provider secret storage and are never committed. A secret-scanning check runs on every change. |
| SC-4 | Container images are built from pinned base digests and scanned; production runs the scanned artifact. |
| SC-5 | Engine and rule builds that produced historical matches are retained so replays remain possible (ADR-0004). |
