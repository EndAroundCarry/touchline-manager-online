# ADR-0002: Authentication and session model

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (implemented in Stage 2)
- **Related:** [`../security/threat-model.md`](../../security/threat-model.md), [ADR-0007](0007-pwa-first-delivery.md)

## Context

Accounts belong to real people who hold one club in a persistent competitive world. Session
compromise is not just a privacy problem: it is result-integrity and money-integrity
degradation, because a hijacked account can bid, list, claim, and rename.

The first client is a browser PWA served from a different origin than the API, and later
clients are Capacitor-hosted native shells that cannot use browser cookie handling the same
way.

## Decision

**Credentials**

- Email + password only for MVP; social login deferred.
- Hash with ASP.NET Core's supported password hasher at an explicitly configured work
  factor, with transparent rehash on successful login when the stored format is stale.
- Emails are compared on a normalized form; display names likewise; both carry unique
  indexes.
- Verified email is required before club claim, bidding, listing, and display-name change.

**Tokens**

- **Access token:** short-lived JWT (15 minutes), held **in memory only** by the client,
  never in `localStorage` or `sessionStorage`.
- **Refresh token:** opaque random value in an `HttpOnly`, `Secure`, `SameSite=Lax` cookie,
  scoped to the refresh endpoint path. Stored server-side only as a hash.
- Refresh rotation is mandatory: every refresh issues a new token, records the replacement,
  and revokes the consumed one.
- **Reuse detection:** presenting an already-rotated token revokes the entire token family
  and forces re-authentication. This is the primary defence against stolen refresh cookies.
- `logout` revokes the current session; `logout-all` revokes every session for the user and
  bumps the security stamp.

**Email tokens**

- Verification and password-reset tokens are single-use, purpose-scoped, hashed at rest, and
  expire (verification 24 h, reset 60 min). Only the newest active token per user/purpose is
  valid.

**Protection against enumeration and brute force**

- Login and forgot-password return generic responses regardless of whether the account
  exists, and take comparable time.
- Progressive lockout after repeated failures, with rate limiting by route, IP prefix,
  account, and status.
- Suspended accounts lose write access immediately at the authorization layer, independent of
  token validity.

**Roles and MFA**

- Roles: `player`, `support`, `operator`, `admin`, stored as rows in `auth.user_roles`.
- TOTP MFA is **required** for `support`, `operator`, and `admin` before production launch,
  and required again for every admin mutation.

**Transport and cookie rules**

- Exact CORS allowlist per environment with credentials enabled; no wildcard origins.
- CSP, HSTS, `frame-ancestors`, MIME sniffing protection, and referrer policy set at the edge
  and re-asserted by the API.
- Refresh cookie is `SameSite=Lax` and the API rejects state-changing requests whose `Origin`
  is not in the allowlist.

**Native clients later**

Capacitor builds do not receive the browser refresh cookie. They use the same endpoints
through a versioned auth adapter that stores the refresh token in platform secure storage and
sends it explicitly. The server-side session, rotation, and reuse-detection model is
identical, so integrity behaviour does not fork per client.

## Consequences

**Positive**

- Refresh tokens are never readable by JavaScript, so XSS cannot exfiltrate long-lived
  credentials. Access tokens live only for minutes and only in memory.
- Session revocation is immediate and auditable, and reuse detection converts a
  stolen-cookie incident into a contained event.
- Account enumeration and credential-stuffing pressure are both reduced.

**Negative**

- The SPA must bootstrap a session refresh on load before rendering authenticated routes —
  handled by a single app initializer, not per-feature code.
- Cross-origin cookies require careful CORS, CSRF, and same-site configuration per
  environment; misconfiguration is a stage gate, not a runtime discovery.
- Rotation writes to `auth.refresh_sessions` on every refresh; index and retention policy
  must keep the table small.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Access token in `localStorage` | Any XSS becomes a full account takeover. |
| Single long-lived JWT | Cannot be revoked; guarantees that a compromise persists. |
| Refresh token without rotation | Stolen tokens stay valid indefinitely; no reuse signal. |
| Server-side session cookie for everything | Simpler, but loses the stateless hot path for read APIs and complicates the worker's authorization story. |
| Third-party identity provider | Adds an external dependency on the critical path of first login and of support tooling, without removing the need for our own roles/audit. |
