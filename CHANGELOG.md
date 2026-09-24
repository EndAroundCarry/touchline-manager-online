# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16.

## Stage 2 — Identity and authenticated walking skeleton

The auth module: the account schema, the full credential lifecycle, session rotation and reuse
detection, the request security the rest of the product will build on, the Angular screens that drive
it, and the browser journeys that prove it works end to end.

### Added

- `auth` schema: `users`, `user_roles`, `refresh_sessions`, `email_tokens`, `user_consents`, with
  unique indexes on normalized email and display name, a unique index on each token hash, a check
  constraint on the status lifecycle, and restrictive foreign keys to `users` (ADR-0002).
- `ops.audit_log` and an `IAuditWriter` that stages audit rows **in the same unit of work** as the
  change they describe, so an action without an audit trail is impossible rather than merely
  discouraged. Auth actions (register, verify, login outcomes, rotation, reuse, logout, reset,
  profile change, deletion) are recorded with actor, target, correlation ID, and a hashed IP.
- Domain auth aggregates with the lifecycle as behaviour: `User` (register, verify, lockout,
  suspend/restore, change password, display name, deletion), `RefreshSession` (issue, rotate,
  revoke), `EmailToken` (issue, consume, supersede), `UserConsent`, `UserStatusRules`, and a pure
  `AccountLockoutPolicy` escalation ladder.
- Use cases for the whole lifecycle: `RegisterUser`, `VerifyEmail`, `ResendVerificationEmail`,
  `Login`, `RefreshAccessToken`, `Logout`, `LogoutAll`, `ForgotPassword`, `ResetPassword`,
  `GetProfile`, `UpdateProfile`, `DeleteAccount`, plus the shared `EmailTokenIssuer` and
  `SessionIssuer`. Each returns an explicit outcome rather than throwing for expected results.
- FluentValidation request validators with shared email, display-name, and password rules, returning
  per-field Problem Details that the client can render against individual form controls.
- Infrastructure providers: `IdentityPasswordHasher` over ASP.NET Core's password hasher with an
  explicit work factor and transparent rehash, `SecureTokenService` (CSPRNG tokens, SHA-256 storage
  hashes, HMAC client fingerprints), `JwtAccessTokenIssuer` (HS256 with the security stamp embedded),
  `SmtpEmailSender` (MailKit), and the EF Core repositories implementing the auth ports.
- The endpoints from master plan §10.1, including `GET/PATCH/DELETE /api/v1/me`.
- Bearer authentication that re-reads the account on every authenticated request and compares the
  security stamp, which is what makes a stateless token revocable. Policies for a verified manager
  and for the `admin`, `operator`, and `support` roles — the groundwork for the Stage 14 MFA
  requirement.
- Response security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, a
  `default-src 'none'` CSP, and `Permissions-Policy`), plus rate limiting on the auth endpoints.
- The HttpOnly, `Secure`, `SameSite=Lax` refresh cookie, scoped to the refresh path, with `Secure`
  relaxed only in Development where the client is served over plain HTTP.
- A fake-email local workflow: `Email__Host`/`Email__Port` point at the Compose mail catcher, and the
  integration tests substitute a recording sender.
- The Angular auth screens — register, sign in, confirm email, forgotten password, reset password, and
  settings — as standalone components with reactive forms. Field messages are resolved in one place:
  the client's own rule wins while the manager is editing, and the server's per-field `errors` map
  takes over afterwards.
- The `SessionStore`, which holds the access token in memory and nowhere a template or another script
  can reach it, restores the session before the first route activates, and **shares one rotation
  between concurrent callers**. Without that sharing, a screen firing three requests on load would
  present an already-consumed refresh token and trip the server's reuse detection against its own
  legitimate user.
- The route guards in both directions, and a `returnUrl` that is honoured only when it is a
  same-origin absolute path — navigating to an arbitrary value would turn the sign-in screen into an
  open redirect.
- The token interceptor: attaches the bearer token, keeps auth endpoints out of both the token and the
  retry (a rejected sign-in is an answer, not an expiry), recovers from an expired token exactly once,
  and ends the session when the rotation fails instead of retrying forever.
- The Playwright suite in `tests/web-e2e`. It owns its stack — `globalSetup` brings up PostgreSQL and
  the mail catcher and applies migrations, then the config starts the API and the web client — and it
  reads the confirmation and reset links out of the mail catcher rather than being handed a token.
  Ten journeys cover registration and confirmation, the session surviving a reload, the unconfirmed
  account's restriction, account closure, renaming with the shell following, signing out everywhere,
  password replacement with a single-use link, and the guards including the external-`returnUrl` case.
- Frontend unit coverage for the session store, the guards, the interceptor, and the field-message
  precedence (43 web tests in total, up from 8).
- An `e2e` CI job that installs Chromium, typechecks the suite, runs the journeys, and uploads the
  report on failure.

### Notes

- **Access tokens carry a security stamp that is checked against the database on every authenticated
  request.** This costs one indexed primary-key lookup and is what makes suspension and
  "sign out everywhere" take effect immediately instead of after the token expires. A cache belongs
  here only when profiling shows it is needed.
- **`DELETE /api/v1/me` takes the confirming password in a JSON body.** ASP.NET Core refuses to infer
  a body parameter for `DELETE`, so the binding is explicit.
- **Only the auth endpoints are rate limited for now.** The broader per-route and per-user limits are
  Stage 14 work, where they can be tuned from load evidence instead of guessed at. The permit limit is
  configuration, so load and functional tests raise it rather than tripping it.
- **`refresh_sessions.revocation_reason`** is an addition to the column list in master plan §6.2:
  reuse detection has to distinguish "rotated" from "signed out" to know whether a presented token is
  a replay, and the reason is also what an operator needs when investigating a session incident.
- `Auth:SigningKey` is validated at startup in every environment. Development has a committed
  dev-only value, following the precedent of the local database password; every other environment
  must supply its own.
- **Playwright arrives in Stage 2, not Stage 7.** `docs/testing/test-strategy.md` originally deferred
  journeys until the match viewer, but master plan §16 makes the full auth lifecycle a Stage 2 exit
  criterion and `docs/product/mvp-traceability.md` asks for a registration journey at F-01. The test
  strategy has been corrected. The remaining master plan journeys still arrive with the screens they
  exercise, because a journey for a screen that does not exist tests nothing.
- **The journey stack raises `RateLimiting:AuthPermitLimit`.** The suite signs in far more often from
  one address than a person would, and a throttle that fired halfway through would look like a bug in
  the feature under test. The limiter is still tested — by the API integration tests, which build a
  host with a deliberately tiny limit and can therefore assert on it directly.
- **The suite is one worker, deliberately.** Every journey shares one database and one mail catcher,
  and a test that read another test's message out of the mailbox would fail for a reason that is not
  real.
- Anonymization after the deletion cooling period, session listing on the settings screen, and TOTP
  enrolment remain outstanding; they belong to the privacy workflow (Stage 14) and the settings
  screen (Stage 13).

### Fixed

- **The settings screen told managers the manager-name field was unavailable while leaving it
  editable.** The input carried both `formControlName` and a `[disabled]` binding. A reactive form
  owns the disabled property — `FormControlName` re-applies the control's own state on every change —
  so the binding was silently ignored. An unconfirmed manager could type a new name into a field the
  same screen said was locked, and only the save button stopped them. The control's own state is now
  set from the profile, and an end-to-end journey asserts the field really is disabled.
- The migration for the auth tables initially omitted the foreign keys to `auth.users`; the migration
  was regenerated rather than patched, since it had not been applied anywhere.

## Stage 1 — Monorepo scaffold and engineering guardrails

### Added

- Solution `TouchlineManager.slnx` with `Domain`, `Application`, `Infrastructure`, `Contracts`,
  `MatchEngine`, the `Api` and `Worker` composition roots, seven test projects, and two tool
  projects. Identifier naming uses the real product name throughout.
- Central package management (`Directory.Packages.props`), lockfiles, and a pinned SDK
  (`global.json`). `TreatWarningsAsErrors` with .NET analyzers at `Recommended`.
- Durable job queue on PostgreSQL: `ops.jobs` with enqueue idempotency on
  `(job_type, business_key)`, `FOR UPDATE SKIP LOCKED` claims, leases with expiry, exponential
  backoff with jitter, and dead-lettering for permanent domain failures (ADR-0003).
- Job poller as an Infrastructure hosted service, registered only by the worker composition root so
  the API can never execute a deadline.
- API composition root: validated options that fail startup when misconfigured, correlation
  middleware, RFC 9457 Problem Details, liveness/readiness/detailed health endpoints, CORS
  allowlist, OpenAPI in Development, and one route group per bounded module.
- Log redaction filter shared by both composition roots, enforcing the redaction list in
  `docs/security/data-classification.md` §4, with tests.
- Angular 22 PWA: standalone components, strict TypeScript and strict templates, PrimeNG 22 with
  Aura and Tailwind 4, documented CSS layer ordering (`tailwind-base, primeng, tailwind-utilities`),
  service worker and manifest, responsive shell with safe-area insets, skip link, reduced-motion
  handling, connectivity banner and correlation-ID footer.
- `IClock` seam with a fake clock in tests; no code path uses server local time.
- Docker Compose providing PostgreSQL 17 and a mail catcher, plus `npm run dev` as the single
  command that starts the backing services, the API, the worker and the web client.
- GitHub Actions pull-request pipeline: locked restore, `dotnet format` verification, Release build
  with warnings as errors, all test projects, generated migration SQL as a review artefact, and the
  Angular typecheck/build/test job.
- Architecture tests that fail the build when the dependency rules in
  `docs/architecture/modules.md` §2 are crossed.

### Fixed

- `global.json` pinned an SDK version the installed preview sorts below, so no SDK resolved.
- `EnqueueNoOpJob` and the job handler registry were registered as singletons while consuming the
  scoped `DbContext`. Use cases are now scoped, and the worker resolves handlers from each job's own
  scope. Caught by the API integration tests at startup.
- The log redactor missed `refresh_token`-style keys, because `_` is a word character and therefore
  provides no word boundary for a plain `\btoken\b` pattern.
- The private-field naming rule also applied to `const` and `static` fields, which are PascalCase by
  convention. Only private instance fields take the `_` prefix now.
- Local PostgreSQL publishes host port **55432**, not 5432. Docker Desktop will happily bind a second
  listener on an already-occupied host port, after which connections land unpredictably — observed
  here when 5433 was chosen and a second process bound it too. A port outside the standard and
  ephemeral ranges removes the class of problem.
- Both `package.json` files carry an `.npmrc` setting `include=dev`. A shell exporting
  `NODE_ENV=production` otherwise silently omits devDependencies, producing an install that cannot
  build.

### Notes

- EF Core is pinned to 10.0.4 to match the Npgsql provider's floor, so the graph contains exactly
  one EF Core version.
- FluentAssertions is pinned to 7.2.0. Version 8 changed to a licence that is not free for
  commercial use.
- Container images for the API, worker and web, and separate database roles for migrations and
  runtime, are deferred to Stage 14 where they are built and exercised for real rather than
  half-implemented now.
- `docs/product/match-engine.md` and the operations runbooks are intentionally absent; they are
  Stage 5 and Stage 14 deliverables.

## Stage 0 — Product rules, architecture, and executable specifications

### Added

- `docs/product/master-plan.md` adopting the approved plan.
- `docs/product/game-rules.md`: the normative rule set, with stable references for every
  configurable value, a consolidated constant table, and an explicit list of values deliberately
  left to balancing.
- ADR-0001 … ADR-0009 covering the modular monolith, authentication and sessions, the PostgreSQL
  job queue, the deterministic match engine, dynamic pyramid expansion, semantic highlights,
  PWA-first delivery, deployment topology, and time/identity/concurrency conventions.
- C4 context and container diagrams, the module map, and the first ER diagrams.
- Glossary; UI tone and fictional-data policy; disaster/recovery-relevant threat model and data
  classification; MVP traceability for 51 features plus a guardrail per non-goal.
- `docs/product/stage-0-review.md`: the consistency review, the resolved contradiction about where
  shortlists live, and the three decisions requiring a product owner's answer.

### Fixed

- Master plan §5.2 and §6.5 disagreed about the schema for shortlists. Resolved to
  `market.shortlists` and recorded, because placing it in `squad` while the market module writes it
  would violate the module-ownership rule.
- Closed two gaps that would otherwise have produced two implementations: what happens at the top of
  a pyramid (`PR-9`, `PR-10`) and whether an unserved suspension carries across rollover (`DIS-8`).
