# ADR-0041: Privacy-safe operational funnels

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 13
- **Related:** master plan §16 Stage 13, §14.1, §15, §12.4, `F-54`, `F-48`, `LGL-5`, `MAT-11`,
  `VOI-4`, `CON-2`, `FIN-7`, `FIN-16`, `OCC-1`–`OCC-8`, `WORLD-8`, ADR-0002, ADR-0036, ADR-0040,
  data-classification §2/§4, threat-model I-1/I-2/I-4.

## Context

Stage 13 promises, at master plan §16, to _"add product analytics limited to privacy-safe operational
funnels."_ It is the last of the stage's six deliverables: the account surface (ADR-0036), PWA hardening
(ADR-0037), responsive layouts (ADR-0038), the accessibility baseline (ADR-0039), and guided help
(ADR-0040) have all landed. ADR-0040 recorded the sixth explicitly: _"product analytics is the sixth and
remains deferred"_, and rejected instrumenting even a help page _"before the privacy-safe funnel
decisions exist"_. This ADR makes those decisions.

Three constraints fixed the shape before any code was written.

**The collection boundary is already drawn, in policy.** `LGL-5` permits analytics _"limited to
privacy-safe operational funnels"_ and forbids device fingerprinting beyond a risk hash, third-party
advertising trackers, and the sale of personal data. The data-classification policy is sharper still: for
restricted game data (C2), logs and metrics carry _"counts and durations only, never values"_; for
personal data (C3), _"redacted by default; identifiers/hashes only when operationally required"_ (§2). §4
lists what must never reach a log, trace, metric, or error response, and §2.1 requires a test that fails
when a C2 value reaches a manager-facing payload. Any design that collects per-manager events contradicts
its own policy.

**Nothing collects today.** An audit of the repository found no analytics of any kind: the web client
emits nothing (no vendor, no beacon, no log sink, and — by ADR-0002 and ADR-0040 — deliberately keeps
nothing in browser storage); the backend has an OpenTelemetry pipe that is registered in both hosts and
exports only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, but it carries **zero custom instruments and no
metric-name registry**. The only telemetry convention is structured logging behind a hard redaction
filter (`LogRedactor`).

**The stage asks for funnels, and a funnel is a count.** Master plan §15 names the funnels it wants
instrumented — onboarding completion and retention among them — and a conversion funnel is a set of step
counts, not a stream of individual events. The server already stores every row those counts need:
`auth.users`, `world.managers`, and `world.club_tenures`.

## Decision

**1. "Product analytics" here means two things and nothing else.** (a) Operator-visible counts of two
operational funnels — onboarding completion and retention — computed from rows the game already writes;
and (b) live counters for the same transitions on the existing OpenTelemetry meter pipe. There is no
event stream, no session or screen tracking, no per-manager dimension, no client beacon, and no vendor.

**2. The counts are derived, so the surface adds no personal data.** `IOperationalAnalyticsQueries` reads
account, manager, and tenure rows and returns counts; it writes nothing and needs no table, no migration,
and no retention or deletion path of its own. Counting the auth module's accounts is a cross-module read,
which `MOD-3` permits — the same arrangement onboarding already uses to measure occupancy. Anonymized
accounts are excluded from the account steps, because they are no longer a manager walking the funnel.

**3. The onboarding funnel is `registered → verified → profile → club`, and retention is
`active | inactive | closed` tenures plus recent logins.** These are the transitions the game already
records (`WORLD-8`, `OCC-1`–`OCC-8`, `CAL-*`) and the steps a manager actually walks. The response
carries the world and season it was read against and a server instant, so a funnel is never shown without
its context (`VOI-4`).

**4. Runtime signal rides the existing meter, and carries no value.** `IOperationalMetrics` is a port
whose `System.Diagnostics.Metrics` implementation exposes two counters — an onboarding step tagged only
by `step`, and a tenure event tagged by `event`/`reason` — with no identity and no per-manager dimension.
Each host registers the meter with the OTel pipeline; with no collector the calls are a no-op, so tests
and local runs are unaffected. Recording happens **after** the transaction commits, so a failed write
leaves no phantom step in the funnel.

**5. The read is operator-only, behind a role the product had not yet enforced.** `GET
/api/v1/ops/analytics/funnels` requires the `operator` or `admin` role — the first role-gated endpoint in
the product, using the policies Stage 2 defined but nothing had applied. The two single-role policies are
not hierarchical, so the read names both. It is always mapped, unlike the test-only `/diagnostics/*`
probes, and answers `Cache-Control: no-store` because it is live operator data (the account export's
precedent, ADR-0036).

**6. The boundary is enforced by tests, not review.** A Layer 4 test pins the metric names and tags; a
Layer 4 test asserts the counts move by exactly one step as a manager walks the funnel, over real
PostgreSQL; and a Layer 5 test asserts the gate from both sides (anonymous `401`, plain manager `403`,
operator `200`) and that the raw response body carries counts and **no** account address — the executable
form of the disclosure boundary `MAT-11` and data-classification §4 require.

## Consequences

**Positive**

- The stage's analytics deliverable is met without collecting a single new personal datum: every figure
  is a count over rows the game already had, so the surface has no table, no migration, and nothing to
  redact, retain, or delete.
- Operators get the two funnels that matter for running the game — how far the membership walks
  onboarding, and who is still holding a club — as one cache-free read that Stage 14's console consumes
  unchanged (`F-46`).
- The counters give the telemetry backend a rate for the same transitions, so Stage 14's dashboards and
  SLOs have real instruments instead of the empty meter the pipeline carried before.
- The disclosure boundary is now executable: a count that turned into a value, or an address that reached
  the payload, fails the build.

**Negative**

- The funnels are counts, not cohorts. They answer "how many" and not "who", which is the privacy
  guarantee and also the limit: no per-manager retention curve, no funnel by country or tenure age, and
  no time-bucketed conversion without another decision.
- `ops` now exposes an operator read with no operator UI until Stage 14, so until then it is a
  machine-readable surface only.
- The endpoint is the first role-gated route, and no user can be granted a role through the product yet:
  an operator is made by a database write until the Stage 14 admin surface exists. The test grants it the
  same way, deliberately.
- "Seen recently" is a fixed seven-day window, not a configurable rule. It is a reasonable default and a
  deliberate non-rule: extending the retention funnel's dimensions is Stage 14/15 work.
- Match distributions, financial health, auction liquidity, AI behavior, job lag, and support volume —
  the rest of master plan §15's list — are **not** built here. They belong to Stage 14/15 under `F-48`.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A first-party client beacon for screen reach and frontend errors | It adds the one thing the product has avoided — an outbound collection path from the browser — for data the server can mostly derive anyway, and it invites the storage and fingerprinting questions ADR-0002 and ADR-0040 already answered "no" to. The promised "frontend error reporting with PII scrubbing" is a Stage 14 concern. |
| A third-party analytics vendor | `LGL-5` forbids third-party advertising trackers and the sale of personal data, and `docs/architecture/context.md` records "no analytics vendor on the critical path". |
| A new `ops.analytics_events` append-only table | Storing per-event rows is exactly the per-manager collection the policy forbids, and it would need a retention rule and a deletion path for data the counts can be derived from instead. |
| Instrument the counters from a scheduled job rather than at each transition | A job that recomputed deltas would be a second source of truth that can drift from the rows, and it would need its own deadline and idempotency key for no benefit over recording the transition where it happens. |
| Expose the funnels to any authenticated manager | The figures describe the whole membership, not one manager's club; they are operational data, and the role gate is the smallest exposure the stage allows. |
| Put the read under `/api/v1/admin/...` | No admin route group exists before Stage 14, and `ops` already owns the observability surface (`F-48`). The path can be re-homed under the admin console's group then if the console needs it. |
| Reuse an existing `F-NN` row instead of adding `F-54` | Analytics maps to no single requirement in master plan §20; `F-52` and `F-53` set the precedent that a cross-cutting requirement traced to a plan section gets its own row and its own evidence. |
