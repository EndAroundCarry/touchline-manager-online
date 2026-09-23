# ADR-0002: Authentication and Session Model

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §4.6, §6.2, §10.1, §12.1

## Context

Accounts gate competitive actions (club claims, bids, lineups) and must resist enumeration, credential stuffing, session theft, and refresh-token replay. Email verification is required before any public or competitive action. Admin/support/operator access must be stronger than player access.

## Decision

- **Credentials:** email + password only for MVP. ASP.NET Core's supported password hasher with an explicitly configured work factor; transparent rehash-on-login for upgraded hashes. Never store or log raw passwords or raw tokens.
- **Sessions:** short-lived **access token held in memory** on the client (never localStorage); rotating **HttpOnly, Secure refresh cookie** against `auth.refresh_sessions`.
- **Rotation & reuse detection:** every refresh issues a new token and marks the old session replaced; presenting a revoked/replaced refresh token revokes the entire **token family** and forces re-login.
- **Server-side state:** `auth.refresh_sessions` stores only hashed tokens with family ID, issued/expires/last-used/revoked instants, replacement session ID, and salted IP-prefix/user-agent hashes.
- **Email tokens:** `auth.email_tokens` for `verify` and `reset` purposes; hashed, single-active-per-user-purpose, expiring; raw token only ever leaves via the email provider.
- **Authorization:** roles in `auth.user_roles` (`player`, `support`, `operator`, `admin`); MFA mandatory for every privileged role before production launch. Verified email is a precondition for club claim, bidding, listing, and display-name change.
- **Anti-abuse:** generic login/reset responses (no account enumeration), progressive lockout via `failed_login_count`/`lockout_until`, route/IP-prefix/user rate limits, exact per-environment CORS allowlist, CSRF/origin checks on cookie-authenticated mutations.
- **Transport of authority:** access token TTL is short; refresh is the only long-lived credential and is bound to the rotation family above.

## Consequences

- Replay of a stolen refresh token is detectable and self-healing (family revocation).
- Client code must hold the access token in memory and re-bootstrap it on page load from the refresh cookie.
- Native clients later swap the cookie for platform secure storage through a versioned auth adapter; server contract does not change.

## Alternatives considered

- **Server sessions + cookies only:** rejected — couples every API call to session lookup and complicates the future native adapter; plan mandates token model.
- **Long-lived JWT in localStorage:** rejected — XSS-exfiltratable, no server-side revocation.
- **Third-party IdP:** rejected for MVP — adds cost/dependency for a self-contained game identity; revisit post-MVP.
