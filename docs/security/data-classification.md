# Data Classification and Handling

> **Companion to** [`threat-model.md`](threat-model.md). Defines what each class of data is,
> where it may appear, and how long it lives. Where this conflicts with a habit, this wins.

---

## 1. Classes

| Class | Meaning | Examples |
|---|---|---|
| **C0 — Public game data** | Intended to be seen by any visitor or manager | Competition names, tables, results, fixture lists, public player profiles and attributes, completed transfer history |
| **C1 — Internal game data** | Correctness-critical, not user-private, but not meant for arbitrary exposure | Standings projections, discipline records, engine configuration hashes, job metadata, audit references |
| **C2 — Restricted game data** | Must never be exposed to any manager; operator/support only | Hidden potential, internal valuations, collusion risk flags, raw match snapshots, RNG seed material, simulation diagnostics, AI policy internals |
| **C3 — Personal data** | Identifies or can locate a natural person | Email, display name, password hash, consent records, session records, IP hashes, user-agent hash, support correspondence |
| **C4 — Secrets and credentials** | Compromise enables access or forgery | Refresh and email token hashes, password hashes, signing keys, world seed secret, provider API keys, database credentials |

---

## 2. Handling matrix

| Class | At rest | In API responses | In logs/metrics | Retention |
|---|---|---|---|---|
| **C0** | Plain, backed up | Public or authenticated read | Full values allowed | Permanent (competition history) |
| **C1** | Plain, backed up | Only where the viewer is authorized (for example own club, own division) | Values allowed with identifiers; no cross-tenant leakage | Season-linked; permanent for history |
| **C2** | Plain tables/columns with restricted access, or `server_only` columns never mapped to DTOs | **Never** in a manager-facing response | Counts and durations only, never values | Engine/rule artifacts retained while their matches are replayable |
| **C3** | Plain where functionally necessary; hashed where the hash suffices | Own data only; never another manager's | **Redacted by default**; identifiers/hashes only when operationally required | See §3 |
| **C4** | Hashed where a hash suffices (tokens, passwords); encrypted where reversible (seed material) | **Never** | **Never**; tokens and secrets are on the log redaction list | See §3 |

### 2.1 Restricted-column convention

Data in class C2 lives in one of two places, never both:

1. A separate restricted table (for example hidden player generation values), readable only
   by the modules that need it; or
2. Columns explicitly marked server-only and excluded from every DTO mapping, with a test
   asserting they are absent from manager-facing payloads.

**Rule:** if a value is C2, there is a test that fails when it appears in a manager-facing
response. Relying on a mapper being correct is not sufficient.

---

## 3. Retention

| Data | Retention | Rationale |
|---|---|---|
| Match snapshots, matches, events, highlights | Permanent while the season history matters (indefinite at MVP scale) | Result reproducibility and dispute handling |
| Standings, season entries, promotion/relegation history | Permanent | Competition integrity |
| Transfer listings, bids, outcomes | Permanent | Market transparency and collusion review |
| Finance ledger entries | Permanent | Balances must remain reconstructible |
| Audit log | Permanent, access-restricted | Repudiation defence |
| Job records (completed) | Retained per operational policy; attempt history kept for the season | Incident investigation |
| Raw email delivery events | Short, provider-defined (days) | Deliverability debugging only |
| Security/IP and device hashes | Bounded period, documented before launch | Abuse detection without indefinite tracking |
| Support correspondence | Bounded period, documented before launch | Support continuity |
| Refresh sessions | Until expiry or revocation, then purged | Session hygiene |
| Email tokens (verification/reset) | Consumed, then purged after expiry | Least data |
| Account identity after deletion | Anonymized after a cooling period | Privacy right vs competition integrity |

**Deletion principle.** Deleting an account closes the tenure, revokes sessions, and
anonymizes identity. It does **not** delete the manager's historical effect on the world:
matches, transfers, tables, finance postings, and audit entries remain, now attached to an
anonymized persona. This is stated in the privacy policy in plain language.

---

## 4. Logging and telemetry redaction list

The following must never appear in logs, traces, metrics, or error responses. A test asserts
this list is enforced by the logging pipeline.

- Passwords, password hashes, and password-reset tokens
- Access tokens, refresh tokens, and refresh cookie values
- Email verification tokens and any raw email token
- Email addresses (log a hash or a user ID instead)
- Raw IP addresses (store and log a prefix hash only)
- Raw user-agent strings where a hash suffices
- Full match input snapshots and full event payload bodies
- RNG seed material and world seed secret
- Provider API keys, signing keys, and database credentials
- Admin MFA secrets and recovery codes

**What should appear instead:** correlation ID, user ID, manager ID, club ID, tenure ID,
job ID, fixture ID, matchday ID, division ID, season ID, module name, outcome, duration, and
error category. Structured logs carry these as fields, not as interpolated prose.

---

## 5. Environment rules

| Rule | Statement |
|---|---|
| ENV-1 | Local, test, staging, and production have separate databases, email settings, secrets, domains, and generated worlds. |
| ENV-2 | Production personal data is never used in local or test fixtures. Test worlds are generated fiction. |
| ENV-3 | A restored environment has outgoing email disabled, job leases cleared, differing secrets, and must not be able to execute production deadlines. |
| ENV-4 | Read-only mode and maintenance banners are available to operators as an emergency control. |
| ENV-5 | Anonymous database access is never granted; runtime and migration roles are separate where the host permits it. |

---

## 6. Data subject capabilities (MVP)

| Capability | Behaviour |
|---|---|
| Export | Returns account, profile, tenure history, and own transactional history in a machine-readable form |
| Deletion | Closes tenure → revokes sessions → cooling period → anonymizes identity; competition records retained anonymized |
| Consent | Terms and privacy versions are recorded per acceptance with a timestamp |
| Correction | Display name and timezone are user-editable; game data is not user-editable |

---

## 7. Verification checklist

Stage 0 exit criteria require that the classification is actionable, not aspirational:

- [ ] The redaction list in §4 is implemented as a logging filter with an automated test (Stage 1).
- [ ] Every C2 field has a test asserting absence from manager-facing DTOs (Stages 3, 4, 10).
- [ ] Retention values in §3 with a "documented before launch" note are fixed before Stage 15.
- [ ] The export and deletion flows are exercised by integration tests (Stage 13).
- [ ] Restore drills confirm ENV-3 (Stage 14).
