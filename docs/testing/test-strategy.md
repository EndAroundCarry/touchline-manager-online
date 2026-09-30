# Test Strategy

> Companion to master plan §15. This document describes the layers that exist and what each one is
> for. Per-stage gates are named in
> [`docs/product/mvp-traceability.md`](../product/mvp-traceability.md).

Every test layer answers a question the layer above it cannot. The point of the split is to make
failures cheap to localise: a broken rule should fail a fast unit test, not a Playwright journey.

---

## Layer 1 — Domain unit tests (`tests/TouchlineManager.Domain.Tests`)

Pure logic with no dependencies: invariant calculations, ordering rules, retry schedules, tie-break
comparators, money arithmetic, schedule generation properties.

*Today:* the job retry policy (43 assertions over backoff, caps, jitter bounds, and argument
validation). Stage 14 added the second factor's pure core: the TOTP algorithm pinned to the RFC 6238
Appendix B vectors and its acceptance window (`TotpTests`), the base32 encoding and decoding RFC 4648
defines (`Base32Tests`), and the credential's lifecycle — confirmation, secret replacement,
single-use recovery codes (`MfaCredentialTests`).

## Layer 2 — Application unit tests (`tests/TouchlineManager.Application.Tests`)

Use cases and policies with ports replaced by hand-written doubles. No database, no HTTP.

*Today:* job handler registration, including that two handlers claiming one job type fails at
construction rather than silently ignoring work, and that the no-op use case derives its business
key from domain identity. Stage 14 added the operator's access paths: granting and revoking a role
with its audit and its actor (`RoleAdministrationTests`), and the multi-factor flows — enrolment,
confirmation, the login challenge, a spent recovery code, and that a role requiring a second factor
cannot disable it (`MfaUseCasesTests`). Stage 14 added the operator console's page cursors: the job and
audit cursors round-trip, treat no cursor as the first page, and refuse a value the server did not produce
(`AdminCursorTests`).

## Layer 3 — Architecture tests (`tests/TouchlineManager.ArchitectureTests`)

Assert the dependency rules from [`docs/architecture/modules.md`](../architecture/modules.md) §2 by
inspecting each assembly's real references. These are the guardrail that keeps a modular monolith
from decaying into a ball of mud.

*Today:* DEP-1 (Domain depends on nothing), DEP-2/DEP-7 (MatchEngine purity), DEP-3 (Application
never reaches outward), DEP-4/DEP-8 (composition roots are leaves), DEP-6 (Contracts stay transport
only).

## Layer 4 — Infrastructure integration tests against real PostgreSQL (`tests/TouchlineManager.Infrastructure.Tests`)

Testcontainers PostgreSQL 17. **Never** an in-memory EF provider: the behaviour under test is
partial unique indexes, check constraints, `FOR UPDATE SKIP LOCKED`, and lease expiry, none of which
an in-memory provider reproduces.

*Today:* redaction filter behaviour (the security control in
[`data-classification.md`](../security/data-classification.md) §4), and the durable queue —
enqueue idempotency, claim and lease recording, a leased job not being re-claimable early, recovery
from an expired lease, future-due jobs not being claimed early, completion being terminal, transient
reschedule within the retry budget, permanent dead-lettering, attempt-budget exhaustion, claim
ordering, and the lease-consistency check constraint. Stage 13 added the operational funnels
(`F-54`, ADR-0041): the counters pinned by instrument name and step/event tag with nothing else on
them (`OperationalMetricsTests`), and the onboarding and retention counts asserted as deltas over real
rows as a manager walks the funnel and then resigns (`OperationalAnalyticsQueriesTests`). Stage 14
added the second factor's storage contract (`MfaPersistenceTests`): the raw column is asserted to be
ciphertext — version-prefixed, and containing neither the base32 nor the raw secret — recovery codes
are rows that cascade with the credential, and one account has one authenticator at the database.

## Layer 5 — API integration tests (`tests/TouchlineManager.Api.IntegrationTests`)

The real composition root over a real database, driven through `WebApplicationFactory<Program>`.
These catch wiring faults that unit tests cannot: option validation, middleware order, endpoint
mapping, and — as happened in Stage 1 — a service registered with the wrong lifetime.

*Today:* liveness/readiness/detailed health semantics (including that liveness does **not** depend on
the database), correlation ID propagation and substitution of unsafe values, RFC 9457 Problem
Details shape, that error bodies contain no server internals, that unimplemented modules expose no
endpoints, and the job probe's gating and idempotency. Stage 13 added the operator-only funnels read
(`F-54`, ADR-0041): `AnalyticsTests` covers the product's first role-gated endpoint from both sides —
anonymous is refused, a plain manager is forbidden, an operator reads — and asserts the body is counts
with no account address anywhere in it, which is the disclosure boundary `MAT-11` and
data-classification §4 require. Stage 14 added the second factor and the admin gate (`F-46`, ADR-0042):
`MfaTests` drives the whole journey through the real stack — enrol, confirm with a computed code, be
challenged at sign-in, complete it — and asserts the `mfa` claim is earned, is kept across a refresh,
is spent once when a recovery code is used, and that the challenge token is refused at `/me` because
it carries a purpose. `AdminEndpointsTests` asserts the gate from every side: anonymous, a plain
manager, an operator that never completed a second factor, an operator that did but sent no fresh
code, and the operator that did both — then that suspending an account closes its sessions immediately
and records the reason in `ops.audit_log`. `AdminConsoleTests` then covers the console's reads from every
side — anonymous, a plain manager, an operator with a completed factor, and a read-only `support`
operator — and asserts the contract each read holds: the job page filters by status and withholds the
payload, a keyset walk over thirty rows neither skips nor repeats, a bad cursor or status filter is
refused by name, a matchday returns its nine fixtures with their club names, an unknown matchday is
`404`, and the audit search returns a recorded suspension without its hashed IP (`F-46`, `F-47`,
ADR-0043).

## Layer 6 — Worker integration tests (`tests/TouchlineManager.Worker.IntegrationTests`)

The worker's own composition over a real database, asserting that a job enqueued by one component is
claimed, executed, and completed by another **without API involvement**. This is the walking
skeleton that every real deadline will reuse.

*Today:* the no-op walking skeleton, the matchday worker's lock/simulate/publish chain, and the season
rollover driven by the calendar's scheduler. Stage 12 added the **five-season staging run**:
`StagingSeasonRunTests` plays five consecutive seasons over a seeded, two-tier world with a human/AI mix,
the worker rolls each one over, and the test asserts every season reconciles and opens a complete next
season (squad legality, ledger replay, movement totals, and history immutability at the end) — Stage 12's
"at least five consecutive automated staging seasons" gate ([ADR-0035](../architecture/adr/0035-five-season-staging-run.md)).

## Layer 7 — Frontend unit tests (`apps/web`, Vitest)

Signal stores, state transitions, and pure view logic. Component tests run in a DOM environment.

*Today:* `ApiError` mapping from Problem Details and the application root; the session store
(including that concurrent callers share one rotation and that a failed restore is never fatal), the
route guards in both directions, the token interceptor (attach, rotate-and-replay exactly once,
no retry loop, end the session when the rotation fails), and the client-before-server field-message
precedence in `controlError`. Stage 4 added the squad store (that a second read reaches the server rather
than answering from the first, because nothing here carries a version to revalidate against), the squad
presentation helpers (attribute and state bands, filters), and the attribute display — the last of these
being the F-17 gate: a value renders its number *and* the word for its band, so colour is never the only
signal (§11.3). The tactics milestone added the plan-draft transforms (a formation change keeps the
lineup by slot number; a partial lineup is sent rather than dropped, so the server can refuse it), the
tactics presentation helpers (labels, pitch geometry, and the words for every validator code), and the
tactics store — the conditional-save contract in unit form: a save carries the version it read, a `412`
keeps the draft and pulls the server's state, and a reapply then goes out against the version that just
arrived (`CONC-1`, §11.2). Stage 13 added the service-worker update store (a ready version is offered, a detected or failed one is not, and a disabled or absent worker makes it inert), the sync store's freshness and refetch (stale after a failed refresh and cleared on the next success, an immediate read when the tab becomes visible or the connection returns, and listeners removed on stop), and the system notices that render the offline, stale, and update banners. The guided-help milestone added the help topics themselves (the five promised subjects are covered, every link is an absolute in-app route, and no point discloses a value `MAT-11` keeps server-side), the dashboard's first steps (offered for a claimed club, without the team-sheet step while no fixture waits, and dismissed with focus moved to the heading), and the deadline formatter that names its zone where `formatInstant` deliberately does not.

## Layer 8 — End-to-end journeys (Playwright)

A real browser against the real API and the real database. The suite owns its stack: `globalSetup`
brings up PostgreSQL and the mail catcher and applies migrations, then the config starts the API and
the Angular dev server. Nothing is stubbed, so a journey that passes has gone through the same code
paths a manager will.

Playwright was introduced in Stage 2, because master plan §16 makes the full auth lifecycle a Stage 2
exit criterion and the traceability table (F-01, F-05) asks for a journey there. The remaining master
plan journeys — squad → team sheet → version conflict, matchday → highlights,
seller/bidder/outbid/winner, rollover — arrive with the features they cover, since a journey for a
screen that does not exist tests nothing.

*Today:* register → confirm → sign in → the session survives a reload → sign out; the unconfirmed
account's restriction; account closure; renaming with the shell following; sign out everywhere;
forgotten-password replacement with the old password refused and the link single-use; the route guards
in both directions, including that an external `returnUrl` is ignored rather than navigated to; and
onboarding — manager profile → country → club → the inherited dashboard → resignation.

The onboarding journey **gives its club back** by resigning at the end. A world holds 108 clubs and a
country that fills with humans waits for the next tier to be generated, so a journey that kept its club
would exhaust the world after eighteen runs and start failing for a reason that is not a defect. The
seeder runs in `globalSetup` for the same reason: onboarding needs a world, and the world is an operator
tool rather than something the stack creates for itself.

Stage 4 added the squad journey: a manager onboards, opens the squad the club was generated with, sorts
and filters it, and opens a player profile — asserting that twenty-two players are there to inherit and
that an attribute shows its number **and** its band word. It gives its club back too.

Stage 4 also added the tactics journey: a manager shapes an eleven through the board's accessible,
non-drag assignment table, creates the plan, and then — with the API acting as a second device that
revises the same plan — survives a version conflict by reapplying rather than overwriting (`CONC-1`,
§11.2). It gives its club back too.

Stage 7 added the matchday journey, on a stack of its own (`playwright.matchday.config.ts`,
`npm run test:e2e:matchday`). It is separate for two reasons: it starts the **worker**, because only the
worker advances a matchday (ADR-0001), and it points at a **throwaway database** reset and reseeded every
run, because it plays a round and a played round permanently advances a season. A manager onboards,
prepares a side for the next fixture through the prepare screen, asks a non-production trigger to play the
round (ADR-0016), waits for the worker to lock, simulate, and publish it, and watches the replay on the
match center. The main journeys keep their persistent shared world and their worker-free stack.

Stage 13 added a third stack (`playwright.pwa.config.ts`, `npm run test:pwa`) that serves the **production** build from a small static origin, because the service worker is disabled under `ng serve`. It asserts the manifest is served, the worker takes control, an offline reload still loads the shell, and a mutation control is disabled offline and enabled again on reconnection.

Stage 13 also made the core suite **breakpoint-aware** (`F-44`, ADR-0038). `playwright.config.ts` declares a `desktop` and a `mobile` project, each running every journey, and a `tablet` project that selects only the `@responsive` spec by `grep`. A shared `support/navigation.ts` opens the shell's mobile disclosure when it is present, so a journey that reaches a destination through the sidebar also runs on a phone. The responsive spec (`journeys/responsive.spec.ts`) asserts the disclosure-versus-sidebar switch at the `md` boundary, that a converted screen shows its card list below it and its table above it, that the shown controls are at least 44px tall, and that no screen makes the page itself scroll sideways. The journeys on the converted screens branch on the layout they are given — squad and training assert their card list on a phone and their table on a desktop — so the same journey proves both.

Stage 13 also added the accessibility gate (`F-52`, ADR-0039). An `@a11y` journey runs axe, filtered to the WCAG 2.2 A/AA tags, over every public screen and every manager screen a claimed club reaches, at the desktop and mobile projects — so the card layouts are audited on a phone and the tables on a desktop — collecting each screen's violations so one run names every offending page. The Canvas viewer is scanned in the matchday journey, where a real replay exists. The same journey guards the focus ring the shared controls depend on, and the milestone fixes what axe found: the squad roster is named, a focused tactics slot moves with the arrow keys and its state and position are in its accessible name, every horizontal-scroll table region is focusable and named, and focus moves to `<main>` on navigation.

Stage 13 also added the guided-help journey (`F-53`, ADR-0040). `journeys/help.spec.ts` carries no tag, so it rides the desktop and mobile projects the suite already has: a manager takes over a club, is met by the first-steps guidance and puts it away, reaches `/help` through the shell navigation rather than by URL, finds all five promised subjects, and follows a topic to the screen that owns it. `/help` is added to the `@a11y` route list, so the new screen is covered by the accessibility gate rather than sitting outside it.

## Layer 9 — Match-engine validation — Stage 5

Golden output hashes per engine version, byte-identical repetition across supported platforms,
changed-seed behaviour, a 100,000-simulation distribution suite against documented ranges, and
fuzz/property tests. These live in `tests/TouchlineManager.MatchEngine.Tests` plus the benchmark
tool in `tools/simulation-benchmarks`.

## Layer 10 — Load, security, and restore — Stage 14

Stage 14 delivered all three, runnable locally and by hand; wiring them into CI is the deployment
milestone's work ([ADR-0046](../architecture/adr/0046-load-supply-chain-and-restore-drills.md)).

**Load** (`tests/load/`, `npm run load:*`): k6 scenarios for a sign-in burst, dashboard reads, the
matchday polling cadence, and auction contention, plus a matchday-publication scenario. The population
model is the world itself — 108 clubs, so 108 concurrent managers and 324 virtual users at the required
three-times headroom. Each request is tagged `read` or `command`, so the read p95 (500 ms) and command
p95 (800 ms) objectives are asserted per endpoint; an expected `409` on a losing bid is excluded so
`http_req_failed` still means "a 5xx". The publication scenario needs a due round and fails fast with
that reason on a real-time stack, so it is run against the compressed-clock matchday stack.

**Scans** (`infra/scan/`, `npm run scan*`): a dependency-vulnerability scan for .NET and npm, a licence
inventory for both read from the lockfiles and nuspecs a restore already produced, a secret scan
(gitleaks), and a base-image scan (trivy). Dependency, licence, and secret findings block; the image
scan is advisory while no image is shipped, with `scan:image:strict` as the future gate.

**Restore** (`infra/restore-drill/`, `npm run drill:restore`): a real point-in-time drill — base backup,
WAL archive, replay to a chosen instant, promote — whose integrity checks prove the ledger replays to
every stored balance, publication stays atomic, the job queue is sound, leases are cleared, and the
post-target marker is absent. It tears itself down when it finishes.

---

## Gates

| Gate | When | Blocking |
|---|---|---|
| `dotnet format --verify-no-changes` | Every pull request | Yes |
| Release build, warnings as errors | Every pull request | Yes |
| All .NET test projects | Every pull request | Yes |
| Angular typecheck, build, unit tests | Every pull request | Yes |
| End-to-end journeys (Playwright) | Every pull request | Yes |
| Responsive breakpoints (desktop + mobile journeys, tablet `@responsive`) | Every pull request | Yes |
| Automated accessibility (axe, WCAG 2.2 AA) on core routes | Every pull request | Yes |
| Generated migration SQL | Every pull request, uploaded as an artefact | Review |
| Bundle size budgets | Every frontend build | Yes (Angular budgets) |
| Dependency, licence, secret, container scans | Every pull request (Stage 14 for containers) | Yes |
| Restore drill | Before beta and before launch | Yes |

## Rules

1. A bug fix adds the test that would have caught it. A retried job, a duplicate tenure, or a
   partially published matchday is a test, not a ticket.
2. A concurrency guarantee is proven by a test that runs the contending operations for real.
3. `FakeClock`, never a `Thread.Sleep`, for anything time-dependent in a unit or application test.
4. Integration tests are allowed to be slow; unit tests are not. If a unit test needs a database, it
   is in the wrong project.
5. Assertions state the guarantee: `.Should().BeFalse("re-enqueueing the same business action must be
   a no-op (ADR-0003)")` beats `.Should().BeFalse()`.
