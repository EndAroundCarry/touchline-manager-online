# Threat Model and Data Classification

Stage 0 deliverable: threat model + data classification (plan §16 Stage 0, §12, §15.6). Re-review required before closed beta and before public launch. Trust boundaries: [context.md](context.md).

## Scope and assets

| Asset | Why it matters | Primary adversary |
|---|---|---|
| Account credentials & sessions | Full account takeover = club/bid control | External attacker, credential stuffing |
| Competitive integrity (snapshots, seeds, fixtures, tables) | A manipulated result poisons the whole world | Cheating player, insider, compromised worker |
| Money (`club_accounts`, `ledger_entries`, reservations) | Direct economic harm, auction unfairness | Fraud, race conditions, buggy retry |
| Auction state (`transfer_listings`, `transfer_bids`) | Collusion, sniping, double-charge | Multi-account rings, timing attacks |
| Club ownership (`club_tenures`, claims) | Duplicate clubs, capacity bypass | Race conditions, suspended users |
| PII & secrets (emails, IP hashes, world secret, password hashes) | Privacy, regulatory, seed derivation | Breach, log leakage |
| Availability of deadline pipelines (locks, matchdays, rollover) | Missed deadlines = unfair seasons | DoS, job-queue failure, bad deploy |

## STRIDE checks per boundary

| Boundary | Threat | Mitigation (plan ref) |
|---|---|---|
| Spoofing — login | Account enumeration, stuffing | Generic login/reset responses; progressive lockout; rate limits by route/IP-prefix/user; bot challenge after suspicious attempts (§12.1) |
| Spoofing — sessions | Stolen/replayed refresh token | Hashed rotating refresh with **family reuse detection → whole-family revocation**; access token memory-only (ADR-0002) |
| Spoofing — admin | Privileged action without strong auth | MFA mandatory for support/operator/admin before launch (§12.1) |
| Tampering — client payloads | Forged scores, balances, deadlines, ownership | Clients submit decisions only; server derives tenure/club from session; never trust client money/seller/deadline (§7.7, §12.3) |
| Tampering — SQL | Injection | EF parameterization only; no string-built SQL from request data (§12.2) |
| Tampering — match outcome | Simulating from live tables, seed grinding | Snapshot-only simulation on worker; seed = HMAC(world secret, fixture, snapshot hash, engine version); outputs hashed & immutable; no public simulate endpoint (ADR-0004, §7.4) |
| Tampering — money | Negative balance, double charge | Nonnegative checks; transactional cash+reserved; append-only ledger with compensating entries; serializable auction resolution; idempotency keys (§3.12, §7.7) |
| Repudiation — disputes | "I never bid / I never claimed" | `ops.audit_log` (actor, tenure, correlation, version, source); bid IP/device **risk hashes** for support only; idempotency records (§7.7, §12.3) |
| Information disclosure — logs | Tokens/passwords/emails/snapshots in logs | Structured logging with redaction of tokens, passwords, email tokens, raw email, JSON snapshots (§12.2) |
| Information disclosure — API | Hidden potential, seeds, internal valuations, other managers' private data leaking into DTOs | DTO filter rules: never serialize EF entities; hidden fields excluded from player-facing DTOs; admin-protected diagnostics (§10.9, §9.5) |
| Information disclosure — data at rest | DB dump, backup leak | Managed encrypted storage + PITR; separate migration/runtime roles; restricted audit access (§12.2, §14.3) |
| Denial of service — matchday | Lock/publication missed, auction window missed | Durable queue + leases + retries; 99% matchdays published ≤5 min of kickoff SLO; operational status display without inventing forfeits (§7.4, §14.1) |
| Denial of service — API | Login bursts, polling storms | Rate limits; ETag/`/sync` polling cadence with backoff; request/pagination/presentation size caps (§10, §11.2) |
| Elevation of privilege | Player hitting admin routes; cross-club writes | Resource policies on tenure ownership; role checks on `/admin`; feature-inaccessible routes behind flags (§10.8, §17.12) |

## Game-integrity abuse cases (beyond STRIDE)

- **Collusion / multi-accounting:** repeated below-value transfers, reciprocal bid patterns, shared signals → review cases in admin queue; thresholds never exposed; never silently alter results (§7.7, §12.3).
- **Claim races:** concurrent claims of last club → serializable transaction + partial unique indexes guarantee exactly one winner (§15.2).
- **Deadline gaming:** client clock manipulation → all deadlines from server time; `If-Match`/ETag rejects stale writes (§4.6).
- **Botting:** AI and humans use identical constraints; no purchasable advantage exists in MVP (§12.3).
- **Insider access:** audit append-only + restricted; admin mutations require reason + idempotency key + audit event; repairs are compensating, never destructive (§13).

## Data classification

| Class | Definition | Examples | Handling |
|---|---|---|---|
| **P0 — Public** | Anyone may read, forever | Published fixtures, results, tables, competition rules, published transfer history, player attributes (public scouting data) | Cacheable/CDN-friendly; immutable after publication |
| **P1 — Game-public** | Visible to all logged-in managers | Market listings/bids amounts & counts, news, club identities, manager display names | Authenticated read; no cross-tenant private data |
| **P2 — Private (manager)** | Owner + server only | Pre-lock lineups/team sheets, shortlists & notes, own finances/ledger, inbox, sessions, account settings | Resource policy = owning tenure/user only; excluded from any public list |
| **P3 — PII** | Personally identifying or linkable | Email, IP-prefix hashes, user-agent hashes, consent records, support data | Minimize; hash where possible; retention periods documented; never in logs unredacted; export/deletion workflows (§12.4) |
| **P4 — Secret** | Compromising it breaks security or fairness | Password hashes, refresh/email raw tokens (never stored), world HMAC secret, protected match seeds pre-reveal, DB credentials | Env/secret manager only, never committed, never serialized to DTOs, redacted from logs; seed reveal only per ops/security policy (§8.2) |
| **P5 — Restricted internal** | Server-only competitive internals | Hidden player potential, internal valuations, risk flags, engine snapshots/diagnostics, detection thresholds | Server-only columns/tables; admin role for any access; explicitly banned from player-facing DTOs (§10.9) |

Classification drives serialization: any new DTO field must state its class; a P4/P5 value in a player-facing response is a release-blocking defect.

## Security review gates

- Threat model review before closed beta and before public launch (§15.6).
- No unresolved critical/high finding to exit Stage 14 (§16 Stage 14).
- Deadline-critical workflows (lock, publication, auction, rollover, claim) receive explicit security + operations review at Stage 0 sign-off (§16 Stage 0 exit).
