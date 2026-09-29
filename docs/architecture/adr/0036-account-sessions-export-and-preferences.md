# ADR-0036: Account sessions, machine-readable export, and formatting preferences

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 13
- **Related:** [ADR-0002](0002-auth-and-session-model.md), [ADR-0007](0007-pwa-first-delivery.md), [ADR-0009](0009-time-identity-and-concurrency.md), [ADR-0011](0011-squad-schema-and-hidden-player-values.md), master plan §10.1, §10.2, §12.4, §16 Stage 13, data classification §6, game rules `CAL-4`, `CONC-1`

## Context

Stage 13 completes the account's own surface. Three pieces of it touch identity, ownership, or privacy,
so they are decisions rather than incidental UI:

- **Session management.** ADR-0002 records rotating refresh sessions in `auth.refresh_sessions` and gives
  the account `logout` (this device) and `logout-all` (every device). It has no way to *see* the sessions
  and no way to end one of them without ending the others. A manager who suspects a lost laptop can only
  blow away every device, including the one they are holding.
- **Account export.** Master plan §12.4 and data classification §6 promise that an account can obtain its
  own data in a machine-readable form. Nothing implemented it.
- **Formatting preferences.** The manager profile has carried `locale` and `time_zone` since onboarding
  (`WORLD-7`), the read returns them, and the database validates the zone — but nothing can change them
  after creation, and the client formats every date in the browser's zone, ignoring the stored one
  (`CAL-4` says deadlines render in the manager's local time, which need not be the device's).

Two constraints shape the session decision. First, the refresh cookie is scoped to the path
`/api/v1/auth` (`AuthOptions.RefreshCookiePath`, ADR-0002), so it is only presented to auth endpoints; any
resource under `/me` cannot see which session is asking. Second, a session has no client-visible
identifier today: the opaque refresh token is the only thing that ties a request to a session, and the
access token carries a per-token `jti`, not the session id.

## Decision

**1. The manager's sessions are an account resource, read and revoked under `/auth`.** `GET
/api/v1/auth/sessions` returns the active sessions; `DELETE /api/v1/auth/sessions/{id}` revokes one. They
sit under `/api/v1/auth` rather than `/api/v1/me` because the path-scoped refresh cookie must travel with
the request for the server to mark which listed session is the current one. They still authenticate with
the bearer token and go through the ordinary 401 handling; they are credentialed so the cookie is sent,
but they are not "auth endpoints" in the client's retry sense.

**2. The current session cannot be revoked from the list.** Ending the session you are using is what
sign-out means. Revoking it here would leave the manager holding an access token valid for up to fifteen
more minutes while the screen claimed the session was gone, so it is refused with a stable
`CURRENT_SESSION` code and the manager is told to sign out. A session is only ever matched to the current
one by hashing the presented refresh cookie and comparing it with the stored token hash — the same fact
the refresh path already relies on.

**3. Revoking one session is a set-based, owner-scoped conditional update.** `RevokeByIdAsync` sets the
revocation and bumps the version only where the row is the named session, belongs to the caller, and is
still active, returning whether it acted. Like the family and account revocations (ADR-0002), it is one
statement, so two concurrent revocations of the same session cannot both report success, and an account
can never revoke a session it does not own — even by knowing its id.

**4. The account export is one JSON document, scoped from the authenticated account.** `GET
/api/v1/me/export` returns `AccountExportResponse`: the account and its consents, the manager profile,
every tenure the account has held, the active sessions, and the current club's ledger as the "own
transactional history". It takes no target, so it can only ever be of the caller's own data. It is sent
with `Cache-Control: no-store`.

**5. The export carries no secret or hidden value.** No password hash, no access or refresh token, no
token hash, no client-fingerprint (IP, user-agent) hash, no security stamp, and nothing from a server-only
column (`VOI-4`, data classification §4). A test asserts the raw response omits each of these. The IP and
user-agent hashes the server keeps for support (`INT-3`) are class C3 and are deliberately absent from
both the session list and the export.

**6. "Own transactional history" is read as the current club's ledger, not the world's record of the
club.** The ledger is bounded (newest-first, capped, with a `truncated` flag), is data the manager already
reads through the finances screen, and is the only finance record tied to the manager's current tenure. A
club's matches, results, and past ledgers are world data that outlive the managers who ran it; they are
not copied into the departing manager's personal export. The first page of the tenure history records the
clubs the account has moved on from.

**7. Formatting preferences live on the manager profile and change under `If-Match`.** `PATCH
/api/v1/manager-profile` takes the locale and time zone and the profile's current entity tag, reusing
`Manager.ChangePreferences` (already in the domain) and the `world.manager_profile.preferences_changed`
audit action. The time zone is validated against the runtime's zone database, exactly as at creation, so a
zone that does not exist is a validation failure rather than a silent UTC fallback (`CAL-4`). The client
adopts the stored locale and zone as its defaults and re-renders on the change, so every deadline screen
follows without a reload.

## Consequences

**Positive**

- A manager can see their sessions and end one lost device without disturbing the one in use, which is the
  point of revocation being per-session rather than all-or-nothing.
- The export satisfies the data-subject promise with an endpoint that is scoped by construction and
  provably free of secrets, and its scope is written down rather than implied.
- `CAL-4` becomes true: deadlines render in the manager's chosen zone, and changing it changes the whole
  application's rendering.
- Ownership is enforced in the database query, not in the handler, so no caller can widen it.

**Negative**

- The session list cannot show a human-readable device: the server stores only hashes, and returning them
  (or the user agent behind the hash) would be a C3 disclosure. A manager recognises a session by its
  issued and last-used times, which is enough to end a suspicious one but is coarser than a device list.
- The export is a synchronous read that assembles several queries and walks the ledger; large ledgers are
  capped and reported truncated rather than streamed. A very large account gets a partial transactional
  history in one response.
- The export writes one audit row (`auth.account.exported`) inside a `GET`. It is a security-relevant
  action worth recording, but it means the read is not side-effect free.
- Session management adds a second credentialed call path beside refresh; the cookie path is what keeps it
  correct, so moving the endpoints would silently lose the current-session marker.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Put the sessions under `/me` and identify the current session from a new `sid` claim in the access token | Adding a session claim changes the token contract that ADR-0002 fixed and every issuer/validator with it, to solve a placement problem the path-scoped cookie already solves. It remains available if a later client needs the session id without a cookie. |
| Return the stored IP and user-agent hashes as a device label | They are class C3 and non-reversible by design; returning them defeats the reason they are hashed, and they cannot be shown as a recognisable device anyway. |
| Make `logout` revoke by session id and drop `logout-all` | `logout` revokes the token the caller holds, which is what a client without a session list needs; removing the all-devices control for a listing that already complements it would lose the "something is wrong" escape hatch. |
| Allow revoking the current session from the list | It is sign-out with a confusing name and a fifteen-minute stale-token tail. |
| A streaming or ZIP export | The document is small (one account, bounded ledger); a single JSON body is simpler, cacheable-by-nobody, and testable. If the ledger cap is ever hit routinely, streaming can be added without changing the payload shape. |
| Put locale and time zone in a separate user-preference table | The manager profile already owns them, `ChangePreferences` already exists, and a second table would split one preference set across two rows that must agree. |
| Format dates with the browser zone and treat the stored zone as advisory | That is the current behaviour and it is what `CAL-4` was written to avoid: a device in another zone would move every deadline a manager planned around. |
