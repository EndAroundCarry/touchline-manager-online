# ADR-0048: Telemetry dashboards and SLO alerting, over the instruments that exist

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** [ADR-0008](0008-deployment-topology.md), [ADR-0012](0012-daily-progression-materialised-job.md),
  [ADR-0041](0041-privacy-safe-operational-funnels.md), [ADR-0042](0042-operator-access-and-mfa.md),
  [ADR-0046](0046-load-supply-chain-and-restore-drills.md), [ADR-0047](0047-incident-read-only-mode.md),
  master plan §7.1, §13, §14.1, §16 Stage 14, `F-48`, `F-49`, `F-50`, `F-51`

## Context

Master plan §14.1 lists the metrics the game must expose, fixes four service-level objectives, and states
that "alerts must be actionable and linked to runbooks". §16's Stage 14 deliverable is "complete telemetry
dashboards, SLO alerts, maintenance/read-only mode, and runbooks". Read-only mode landed
([ADR-0047](0047-incident-read-only-mode.md)); the dashboards and the alerts did not, and both
[ADR-0046](0046-load-supply-chain-and-restore-drills.md) and ADR-0047 record `F-48` as deferred.

Both hosts already have an OpenTelemetry pipe that exports when `OTEL_EXPORTER_OTLP_ENDPOINT` is set
([ADR-0041](0041-privacy-safe-operational-funnels.md)) — and nothing has ever received it. There is no
metric backend, no scrape endpoint, no dashboard, and no alert rule anywhere in the repository, so the only
continuous evidence about a running game is whatever an operator happens to query by hand.

What exists to display is also much less than §14.1 lists. The instruments registered today are the
ASP.NET Core and HttpClient auto-instrumentation, plus the two `F-54` counters
(`touchline.analytics.onboarding`, `touchline.analytics.tenure`). §14.1's remaining names — job lag and dead
count, simulation duration, matchday publication delay, auction lag, login failures, active managers, club
occupancy, database pool saturation, payload sizes, and front-end errors — have no instrument at all, and
neither does §7.1's requirement to expose queue depth, oldest due age, failures, retries, and execution
duration. The load suite ([ADR-0046](0046-load-supply-chain-and-restore-drills.md)) asserts two of the
objectives by hand, once, and nothing observes them while the game runs.

## Decision

1. **The stack is the OTLP bridge, not a second exporter.** An OpenTelemetry Collector receives the hosts'
   OTLP and re-exposes the metrics for Prometheus to scrape; the traces are forwarded to Tempo. The
   application's contract is unchanged — `OTEL_EXPORTER_OTLP_ENDPOINT` and nothing else — which is what keeps
   the game free of a dashboard-driven dependency. This also matches
   `docs/architecture/context.md`, which already declares an external "Telemetry backend — OTLP collector".

2. **It runs locally and by hand, as its own compose project.** `infra/observability/`, named
   `touchline-observability`, brought up with `npm run obs:up`, with its own ports so it can never tear down
   the development stack. The precedent is the restore drill and the load suite
   ([ADR-0046](0046-load-supply-chain-and-restore-drills.md)); nothing is wired into CI, which is `F-50`'s.

3. **Every published port is bound to `127.0.0.1`, and the stack is never deployed.** This is deliberately
   unlike the backing services, which publish on every interface. Nothing here is authenticated — Grafana is
   provisioned for anonymous viewing so the dashboards are one command away — so a stack that answered the
   LAN would hand the game's operational state to anyone on the network.

4. **Tempo is included, so the trace export is not discarded.** Both hosts already export traces; a collector
   that accepted and dropped them would make "complete telemetry dashboards" false. Traces are read in
   Grafana's Explore view, and the latency runbook is built on them — that is where "why is this route slow"
   is actually answered.

5. **Only the objectives that can be computed are alarmed on, and the approximation is stated.** Availability
   and the two latency budgets come from the request-duration histogram. Reads are `GET`, `HEAD`, and
   `OPTIONS` and commands are every other method: the load suite tags the two kinds exactly, a dashboard can
   only infer them from the method, and the inference is recorded in `docs/operations/observability.md`
   rather than hidden in a query.

6. **Both halves of §14.1's alerting sentence are checked, not promised.** `npm run obs:check` fails when a
   rule or a dashboard names a metric the application does not emit — which is what pins the
   OTLP-to-Prometheus name translation against an OpenTelemetry upgrade — and when an alert's `runbook`
   annotation stops resolving to a section of `docs/operations/runbook.md`. The stack's own configuration is
   validated by the tools that will read it: `promtool`, `amtool`, the collector's `validate`, Tempo's
   `-config.verify`, and `docker compose config`.

7. **No application instrument is added, and the gap is recorded rather than implied.** The scope, settled
   with the user, is the stack, the dashboards, and the alerts over the signals that already exist. So this
   ADR's Deferred list and `F-48`'s traceability row name exactly what remains, and neither reads as more
   than it is.

## Consequences

**Positive**

- The two objectives that can be measured now have dashboards, alerts, and a runbook procedure behind each
  alert, and the alerts are read in Alertmanager on the same machine an operator is already working from.
- The milestone is configuration only: no application code, no schema change, no migration, so nothing about
  the game's behaviour moved and the existing test suites are unaffected.
- The two `F-54` funnels are now visible continuously, not only through one operator read.
- The name-translation and runbook-link checks are the part that decays quietly; both are now build failures.

**Negative**

- The stack is unauthenticated and must never be exposed, which is a rule rather than a mechanism.
- The objective that matters most operationally — 99% of matchdays published within five minutes — is the one
  that **cannot** be alarmed on, because the instrument does not exist. The load suite's publication scenario
  remains the only evidence for it.
- The evidence is manual and local: nothing runs in CI and there is no production telemetry backend, so this
  proves the wiring, not production behaviour.
- Four containers is real weight on a developer machine for a stack that shows two objectives. It is opt-in
  (`npm run obs:up`) and nothing else depends on it.
- Traces are only as good as the instrumentation behind them, which today is the request and its outbound
  calls — no database, job, or simulation spans, so a trace explains latency less often than it would with
  them.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` in the API, scraped directly | It is application code, it adds a second export path beside the OTLP one, and the worker is not ASP.NET Core so it would need its own anyway. One contract at the collector is smaller. |
| Prometheus's native OTLP write receiver, without a collector | Fewer containers, but no trace store and no single place to add processing later; the C4 context already names a collector as the ingress, and it is what makes the two hosts' telemetry identical to handle. |
| Grafana's own alert engine instead of Prometheus rules | Rules in a database are not reviewable in a pull request, and `promtool` could not validate them. Prometheus rule files are the portable artefact. |
| Metrics only, no Tempo | The hosts already export traces, so dropping them would be the exact "half-built" shape the plan's §17.12 keeps out — and the latency runbook would have nothing to read. |
| Loki for logs now | Neither host registers an OpenTelemetry logging exporter, so a log store would receive nothing without an application change. |
| Routing the alerts to email or chat in this milestone | There is no receiver to point at for a local stack, and a template with nothing to render into is dead configuration. Alertmanager's UI is the notification, and routing is `F-50`'s. |
| Building the missing instruments here so the dashboards are complete | Settled with the user as out of scope. It would touch the publication, queue, simulation, login, and auction paths and turn a configuration change into a different, much larger one; the honest alternative is to record the gap. |
| Binding the stack to every interface like the backing services | Consistency is not worth publishing an unauthenticated operational surface; the loopback prefix costs nothing because the hosts push to loopback. |

## Deferred

- **Matchday publication delay**, and therefore the "99% published within five minutes" objective. It needs
  an instrument in `PublishMatchday` measuring the publish instant against `Matchday.KickoffAt`.
- **Job queue depth, oldest due age, failures, retries, and execution duration** (§7.1 item 9, and §14.1's
  "job lag/retry/dead count"). It needs instruments on `PostgresJobQueue` and `JobQueueWorker`; today the
  queue is visible only through `GET /api/v1/admin/health/game` and the operator console.
- **Match simulation duration, login failures, auction lag, active managers, club occupancy, database pool
  saturation, payload sizes, and front-end errors** — the rest of §14.1's metric list.
- **"No lost accepted bid, club claim, published fixture, or finance posting."** This is a property rather
  than a rate: it is held by the invariant and idempotency tests, not by a counter.
- **OTLP log export (Loki), and custom `ActivitySource` spans** for database calls, worker jobs, email
  dispatch, and simulation (§14.1). Only ASP.NET Core and HttpClient auto-instrumentation exists today.
- **Routing the alerts to a real receiver, dashboards on the production telemetry backend, and CI wiring** —
  `F-50` and Stage 16.
