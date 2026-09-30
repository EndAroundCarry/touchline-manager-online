# ADR-0042: Operator access — role administration, TOTP MFA, and the gated admin surface

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** master plan §6.2, §10.8, §12.1, §13, §16 Stage 14, `F-46`, `F-47`, ADR-0002,
  ADR-0010, ADR-0034, data-classification §2/§4, threat-model `S-6`, `E-1`, `E-3`, `R-2`

## Context

Stage 14 opens with the admin and operator surface. Master plan §10.8 fixes the rule for it:
_"All admin mutations require MFA-authenticated role, explicit reason, idempotency key, and audit
event."_ §13 lists what the console must show (world, season, matchday and worker status; queue
depth, dead jobs, retries, leases; account suspension and restoration) and requires runbooks that
define who may act.

The repository has the opposite of that surface today:

- **No role can be granted in production.** The only caller of `User.GrantRole` is registration,
  which grants `player`; the integration tests reach into the `DbContext` to make an operator. No
  administrator could exist.
- **No second factor exists.** `UserRoles.MfaRequired` and ADR-0002's "TOTP MFA is required for
  `support`, `operator`, and `admin` before production launch, and required again for every admin
  mutation" are the only trace of it.
- **No admin route group exists.** The only always-mapped operator read is
  `GET /api/v1/ops/analytics/funnels` (`F-54`, ADR-0041).

What is already built and reused: the append-only audit trail (`IAuditWriter`/`ops.audit_log`
with `before_metadata`, `after_metadata`, and `reason`), the role-gated-endpoint pattern
(`OperationalAnalyticsEndpoints`), the audited/reason-required/idempotent command pattern
(`ResumeSeasonRollover`, ADR-0034), and `User.Suspend`/`Restore`, which had no production caller.

## Decision

**1. Roles are administered by an operator tool, not an endpoint.** §10.8 lists no roles endpoint,
and §16 Stage 16 provisions "admin accounts" — so the first administrator cannot be granted by an
administrator. `tools/access-admin` (grant, revoke, reset-mfa, list) is the bootstrap and the
break-glass path, modelled on `tools/world-seeder`: a console host over the same composition roots,
audited as `AuditActorTypes.Service`. When the console later grows a roles command, the tool remains
the path that cannot lock the last operator out.

**2. TOTP (RFC 6238) is implemented pure, in the domain.** Twenty years of authenticator
applications default to HMAC-SHA1 with six digits and a thirty-second step, so that is what this
uses, and it is verified against the RFC's Appendix B vectors. No third-party OTP dependency: the
algorithm is small, the repository's ethos is deterministic and testable pure logic, and a dependency
here would be a supply-chain surface for a function of thirty lines.

**3. The second factor is a claim, carried by the session.** A password success for an account with a
confirmed credential does not issue a session; it returns a short-lived **challenge token** and the
caller completes with `POST /auth/mfa/login`. The session records when it completed the factor, and
the access token carries `mfa=true`. This mirrors how roles and the security stamp already work, and
means a refresh keeps the assurance the session was granted.

**4. A challenge is a token with a purpose, and the access-token validator rejects any purpose.**
The challenge is signed with the same key, so without that check it would be accepted as a session
that never completed a second factor. The purpose claim is the guard, and there is a test that the
challenge is refused at `/me`.

**5. The secret is encrypted at rest; recovery codes are hashed.** A TOTP secret is recoverable by
design, so it cannot be hashed the way a refresh token is. It is stored under AES-256-GCM with
`Auth:EncryptionKey` (validated at startup like `Auth:SigningKey`), prefixed with a scheme version so
the algorithm can change without a data migration. Recovery codes are shown once and stored as
hashes, like every other opaque secret.

**6. Reads require the claim; mutations require a fresh code.** ADR-0002 requires the second factor
to be re-asserted for every admin mutation, so `AdminMutate` is enforced twice: the policy requires
the role and the `mfa` claim, and a step-up filter requires a current code in the `X-MFA-Code`
header. `support` is excluded from mutations (`E-3`).

**7. An account that holds a role requiring a second factor cannot disable it.** The requirement is a
property of the role, so the recovery path for a lost authenticator is `access-admin reset-mfa`, not
the account removing its own factor.

**8. A mutation that changes state is idempotent by state, and still requires an idempotency key.**
Suspend only suspends, restore only restores, so a retry returns a stable `409` rather than applying
twice. The `Idempotency-Key` header is required for uniformity with every other command and so a
client's retry is explicit. This milestone adds no `ops.idempotency_records` table: the existing
per-command convention stores a key on the affected row, and these commands have no such row.

## Consequences

**Positive**

- The stage's exit criterion — "an on-call operator can diagnose and resume failed matchday,
  auction, provisioning, and rollover workflows" — now has the surface it needs: a role-gated,
  second-factor-protected read of the live game and an audited way to act on it.
- `S-6`, `E-1`, and `E-3` move from "Stage 14 will" to enforced: role policies on every admin route,
  MFA for the roles that require it, and support excluded from mutation.
- The gate is testable from every side, so a future admin route cannot be added without the same
  policies and filter, and the tests would fail if it were.

**Negative**

- The login contract gains a branch. An account holding a confirmed credential receives `202` with a
  challenge instead of `200` with a session. The web client's sign-in screen does not yet handle it,
  which is the next milestone; no account can hold such a credential until one is granted, so the
  existing client is unaffected in the meantime.
- A lost authenticator for an `operator`/`admin` needs the tool and shell access. That is deliberate —
  a self-service reset would defeat the factor — but it is an operational dependency.
- `Auth:EncryptionKey` is a new required secret in every non-development environment, and rotating it
  invalidates enrolled authenticators (they must re-enrol). The versioned prefix leaves room for a
  key id if that becomes a problem.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A production endpoint that grants roles | It is itself a privilege-escalation surface, and the first administrator cannot be granted by one. §10.8 lists no roles endpoint. |
| SMS or email one-time codes | Both add a delivery channel, a cost, and a new abuse path (SIM swap, mailbox compromise), and neither is offline. TOTP is already in every manager's pocket. |
| One-step login that accepts an optional code | It invites a client to retry without the code and would need a stored "half-authenticated" state. The challenge token has nothing to revoke and expires in minutes. |
| A per-request database check instead of a claim | The claim is what makes the policy and the filter declarative; the stamp check already re-reads the account on every request, so a second lookup would buy nothing. |
| Store the secret in plaintext, or hash it | Plaintext fails data-classification §2; hashing is impossible because the secret must be recovered to compute a code. AES-GCM is authenticated, so a tampered value is rejected rather than decrypting to a wrong secret. |
| Accept a code once per session instead of per mutation | ADR-0002 requires it per mutation, and one long-lived code is exactly the window S-6 describes. |
| Build `ops.idempotency_records` now | These commands are idempotent by state; the table is a later milestone's concern (a compensating finance entry is the first command that needs it). |
