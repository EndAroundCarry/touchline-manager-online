# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16.

## Stage 3 — World generation, six countries, clubs, and onboarding

A world you can onboard into. Six fictional national pyramids are generated from one seed, a manager
creates a profile, chooses a country, takes over an AI club in its lowest active tier, and inherits it
exactly as it stands. A country that fills with humans queues the generation of the next tier underneath
it.

### Added

- **The schema the rest of the stage writes into** (shipped first, in its own commit): the `world` schema
  (game worlds, countries, manager profiles, clubs, club tenures, division provisioning requests,
  generation runs), the `competition` shell (seasons, divisions, division-seasons, club season entries),
  and the `finance` shell (club accounts), in one migration applied against real PostgreSQL 17 before
  being committed. The versioned `WorldRuleSet` holds every constant from game rules §3, and its version is
  stamped onto the world and each season, so a historical season is interpreted against the rules that were
  actually in force when it was played.
- **The world aggregates with the rules as behaviour**: `GameWorld` (freeze/resume), `Country`, `Manager`
  (the resignation cooldown), `Club` (tier-scaled baselines and a slug derived from the name rather than
  supplied beside it), `ClubTenure` (the whole of the ownership model), `DivisionProvisioningRequest`,
  `GenerationRun`, `SeasonCalendar` (a pure function from a first matchday to a season's whole window),
  and a `CountryCapacity` value object that answers "does this country have room, and should it grow?" in
  one place instead of in an endpoint.
- **Mappings that put the plan's constraints in the database rather than in a convention**: partial unique
  indexes for one open tenure per club and per manager (`OCC-9`), unique `(country_id, target_tier)` for
  provisioning (`PYR-3`), unique per-world club name and slug, one club per season, and the `FIN-13`
  checks that neither balance can go negative and reservations cannot exceed the cash behind them.
- **Deterministic world generation.** `Pcg32`, a pinned, explicitly tested PRNG, plus versioned fictional
  name pools for all six locales (`england`, `spain`, `germany`, `italy`, `france`, `romania`), a curated
  blocklist of real football identities, and `ClubIdentityGenerator`. Every generated value is a pure
  function of the seed, the country's pool, and the club's ordinal within its country, so the same seed
  reproduces the same pyramid and different seeds produce visibly different ones.
- **Club names that cannot collide, without a retry loop.** Places and suffixes are woven by ordinal rather
  than multiplied, so a division alternates both instead of naming eighteen clubs after one place; the
  combination cycle is their least common multiple, which makes the pairing injective and the names unique
  by construction. Past the cycle the generator adds a qualifier and then a numeral, so a deep pyramid runs
  out of names only by running out of integers (`PYR-11`).
- **The world seeder**, as a use case and as `tools/world-seeder`. `--seed` and `--first-matchday` override
  the configuration; everything else comes from `World:*`. It is idempotent — running it against an
  existing world reports that world and writes nothing (`WORLD-1`) — and it records the seed, generator
  version, and a digest of the non-seed inputs in `world.generation_runs` (`PYR-14`).
- **The onboarding commands**: `CreateManagerProfile`, `ClaimClub`, and `ResignClub`. The takeover validates
  the club is an active, AI-controlled member of the country's lowest active tier, disables a manager who
  already holds a club or is serving the resignation cooldown, and returns the inherited club.
- **`CapacityEvaluator`**, which answers "should this country grow?" in one place. It runs after every
  successful takeover and after every resignation, and creates the next tier's
  `DivisionProvisioningRequest` exactly once (`PYR-1`, `PYR-2`).
- **Advisory locks**: `IAdvisoryLock` and `AdvisoryLockKey`, implemented over
  `pg_advisory_xact_lock`. A takeover takes the manager's lock and then the country's, always in that
  order, which is what keeps the pair deadlock-free (`PYR-3`).
- **Explicit transactions** on `IUnitOfWork` (`BeginTransactionAsync`, `IDatabaseTransaction`,
  `TransactionIsolation`), so a workflow that must hold a lock across more than one save can.
- **The onboarding reads**: world, countries, per-country capacity, available clubs, onboarding state, and
  the inherited-club dashboard, each one query shaped for one screen.
- **The API** from master plan §10.2, plus `GET /clubs/{clubId}/dashboard`. Claim refusals carry the stable
  codes §7.6 requires — `CLUB_ALREADY_CLAIMED`, `MANAGER_HAS_ACTIVE_CLUB`, `MANAGER_PROFILE_REQUIRED`,
  `MANAGER_IN_COOLDOWN`, and `CAPACITY_PROVISIONING` — and the last two carry the moment the cooldown
  lapses and the provisioning request's state and polling hint (`PYR-10`).
- **The Angular onboarding screens** (`/onboarding/manager`, `/onboarding/country`, `/onboarding/club`) and
  a dashboard that routes on state: no profile, no club, or a club. `OnboardingStore` holds the reference
  data and the claim's idempotency key; `requireVerifiedEmail` keeps a manager who cannot claim away from
  the screen that would ask them to.
- 109 new domain tests (175 total) covering the PRNG's pinned sequences, the generator's determinism,
  uniqueness, deep-pyramid behaviour, and the blocklist; 38 world infrastructure tests over real
  PostgreSQL, including the concurrent-takeover and fill-a-tier races; 5 API onboarding tests; 14 new web
  tests (57 total); and a Playwright onboarding journey.
- **ADR-0010**, on why a takeover serialises with an advisory lock rather than with `SERIALIZABLE`.

### Fixed

- **The worker's composition root could not resolve `IRequestContext`.** The API registered an HTTP-backed
  implementation and nothing registered a default, so any use case that writes an audit row would have
  failed the moment the worker ran one. `ServiceRequestContext` is now the default, and the API's own
  registration overrides it — which is the arrangement `IRequestContext`'s own documentation described and
  nothing implemented.

### Notes

- **The onboarding endpoints sit at the version root, not under `/api/v1/world`.** Master plan §10.2
  addresses them as resources (`/countries`, `/club-claims`, `/club-tenure`), and the committed path is the
  contract. It is the same reasoning that puts `/me` at the root.
- **A takeover holds an advisory lock in a read-committed transaction rather than a serializable one.**
  §7.6 asks for serializable, but a snapshot-isolation transaction fixes its snapshot at its first
  statement — which in this workflow is the lock request — so it would still be reading pre-lock state
  after the lock was granted, and the losing manager would get a constraint violation instead of the
  documented refusal. ADR-0010 records the decision; the partial unique indexes are untouched and remain
  the backstop.
- **The deterministic PRNG in `Domain` will not be the match engine's.** The engine ships its own
  `Pcg32` in Stage 5, and that is deliberate: a generated world's reproducibility and a played match's
  reproducibility are separate versioned contracts, and the engine's output hashes are pinned per engine
  version (ADR-0004). The dependency rules settle the question anyway — `Domain` depends on nothing (DEP-1)
  and the engine may not depend on it (DEP-2).
- **Ordinals are per country and never restart per tier.** Tier 1 takes 0–17, tier 2 takes 18–35. If each
  tier restarted at zero, every provisioned tier would propose the same eighteen names and the unique name
  index would reject the second one. A test generates twelve tiers and asserts the names never repeat.
- **Name pools use invented places only, and never a real club's home town.** Generation is where licensed
  identity would enter the product, so the pools are the primary defence and the blocklist is the backstop
  (`FIC-5`). A test runs the blocklist check across every pool and every generated name.
- **A club's founding game year is the first season's game year.** A plausible older founding date would
  have to be invented from the seed, and inventing history is not what a reproducibility rule should be
  doing with its randomness.
- **The tier-1 money, stadium, and reputation baselines are provisional.** They halve per tier, which is the
  shape Stage 9 needs, and they are recorded in `game-rules.md` as balancing values rather than as rules.
  Calibrating them is Stage 9's multi-season simulation work, not a guess made now.
- **Provisioning is requested, not executed.** The takeover creates the `DivisionProvisioningRequest` and
  returns `CAPACITY_PROVISIONING` with its state; generating the tier, its squads, and its backfilled
  results is Stage 11. Until then a full country stays full, on purpose: a half-built tier that a manager
  could claim would be worse than a wait.
- **Only a club claim requires a verified address.** Master plan §12.1 lists club claims, bidding, listing,
  and display-name changes; creating a manager profile and resigning are authenticated but not
  verification-gated, because an unverified account can reach neither a club nor a bid.
- **The dashboard shows only what exists yet.** Identity, competition placement, control, and money. Squad,
  contracts, fixtures, and history join the response in the stages that create them rather than appearing
  now as permanently-empty fields.
- **The Playwright journey gives its club back.** It resigns at the end, which exercises the resignation
  path and returns the club to the pool. Without that, each run would consume one of the 108 clubs and the
  suite would start failing after eighteen runs for a reason that is not a defect.
- **`appsettings.json` had to travel with the seeder tool.** The generic host does not copy it the way the
  web SDK does, so the project declares it explicitly and the tool sets its content root to the binary's
  directory rather than the process's working directory.
- **`game_worlds` has no JSONB feature-flags column**, although master plan §6.3 lists one. Feature flags
  belong in `ops.feature_flags`, where they are queryable and versioned; a JSONB blob on the world row is
  precisely the unfinished modelling the JSONB policy forbids (`JSN-5`).
- **`club_season_entries` carries `season_id` as well as the division-season.** Without it, "one club
  appears in exactly one division per season" is not expressible as a database constraint: a promotion bug
  could enter one club into two divisions in the same season and every standings query would double-count
  it.
- **A failed provisioning request can be retried, and the retry reuses the recorded seed.**
  `unique (country_id, target_tier)` means a second request for the same tier cannot exist, so without that
  transition one failed run would block the tier permanently. Reusing the seed is what keeps `PYR-14` true
  across attempts. A domain test caught the gap.
- **Tier names are descriptive rather than evocative** — "England Top Division", "Spain Division 2" — so no
  generated name can drift towards a real competition's branding (`WORLD-3`).
- **A seeded division starts active and a provisioned one does not.** Stage 11's tier stays in
  `provisioning` until its generation, validation, and backfill all complete (`PYR-8`); the tier the seeder
  creates is claimable the moment it exists, because there is nothing left to backfill.

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
