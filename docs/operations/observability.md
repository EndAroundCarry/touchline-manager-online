# Observability

The local telemetry stack: where the API's and the worker's OpenTelemetry export lands, what it can show,
and which objectives it can and cannot alarm on. It is the `F-48` deliverable of Stage 14, recorded in
[ADR-0048](../architecture/adr/0048-telemetry-dashboards-and-slo-alerting.md), and it makes §14.1's
observability promises real for the signals that already exist.

Master plan §14.1 fixes four service-level objectives. **Two of them are alarmable today and two are not**,
and this document is explicit about which is which rather than implying the dashboards cover everything.

## The stack

It is its own compose project, exactly like the restore drill, so it starts and stops independently of the
development backing services and can never tear them down.

```bash
npm run obs:up      # start it and wait for healthy
npm run obs:down    # stop it, keeping the data volumes
npm run obs:reset   # stop it and discard the metrics, traces, and dashboards state
npm run obs:check   # validate every configuration file (see below)
```

| Service | What it does | Where |
|---|---|---|
| OTLP collector | Receives the hosts' OTLP export; re-exposes metrics for scraping and forwards traces | OTLP on `14317` (gRPC) / `14318` (HTTP) |
| Prometheus | Scrapes the collector, evaluates the alert rules | `http://localhost:19090` |
| Alertmanager | Holds the firing alerts | `http://localhost:19093` |
| Grafana | The dashboards, reading Prometheus and Tempo | `http://localhost:13000` |
| Tempo | Stores the traces | internal only — read through Grafana |

Every host port is deliberately outside its standard range, so the stack cannot collide with something
already running on the machine; the compose file explains each choice. The images are pinned, and
`npm run scan:image` covers them alongside the development backing services.

## Pointing the hosts at it

The application's contract is unchanged: it exports OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and does
nothing when it is not. **The hosts read that from the environment, not from `.env`** — .NET does not load a
`.env` file, and compose only reads the `OBS_*` port variables.

```bash
# bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:14317 npm run dev
```

```powershell
# PowerShell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:14317'; npm run dev
```

With it unset, everything below is empty — which is what keeps the test suites and CI free of a collector.

## The objectives

§14.1's four SLOs, and their status here.

| Objective | Alarmable? | How |
|---|---|---|
| 99.9% monthly API availability, excluding announced maintenance | **Yes** | The ratio of `5xx` to all responses. The monthly window itself needs the production telemetry backend: this stack keeps fifteen days of samples, so the dashboard is range-selectable and the alert burns a short window. |
| p95 read under 500 ms, p95 command under 800 ms | **Yes** | The request-duration histogram, split by method. |
| 99% of matchdays published within five minutes of kickoff | **No** | The publication delay is not instrumented. Until it is, the load suite's matchday-publication scenario is the evidence. |
| No lost accepted bid, club claim, published fixture, or finance posting | **No** | This is a property, not a rate; it is held by the invariant and idempotency tests, not by a counter. |

### Reads and commands are approximated

The load suite tags each request `read` or `command` exactly. A dashboard can only infer the split from the
HTTP method, so **reads are `GET`, `HEAD`, and `OPTIONS` and commands are every other method**. That is the
approximation behind every read/command panel and both latency alerts, and it is recorded in ADR-0048 rather
than hidden. It is a good approximation for this API, where a read is always a `GET` and a command is always
`POST`/`PATCH`/`DELETE`, but it is an approximation.

## The rules

Four alerts, in `infra/observability/prometheus/rules/`. Each one names the runbook section that answers it,
and `npm run obs:check` fails if that link stops resolving — §14.1's "alerts must be actionable and linked to
runbooks", checked rather than promised.

| Alert | Severity | Rule | Runbook |
|---|---|---|---|
| `ApiAvailabilityBurnFast` | critical | `5xx` ratio over 5 m above the 0.1% budget, for 2 m | [The API is failing requests](runbook.md#runbook--the-api-is-failing-requests) |
| `ApiReadLatencyHigh` | warning | p95 read over 500 ms for 10 m | [The API is slow](runbook.md#runbook--the-api-is-slow) |
| `ApiCommandLatencyHigh` | warning | p95 command over 800 ms for 10 m | [The API is slow](runbook.md#runbook--the-api-is-slow) |
| `TelemetryPipelineDown` | warning | `up{job="otel-collector"} == 0` for 5 m | [Telemetry is not arriving](runbook.md#runbook--telemetry-is-not-arriving) |

The last one is the guard on the pipeline itself: if the collector stops answering, every other alert here
goes quiet, and a silent graph is not evidence that the game is healthy.

Routing these anywhere other than Alertmanager's own UI is the deployment milestone's work. On this machine,
**Alertmanager's UI is the notification.**

## The dashboards

- **SLO overview** — availability over the selected range, the error ratio behind it, p95 read against the
  500 ms objective and p95 command against the 800 ms one, and throughput by status code.
- **API (RED)** — request rate, `5xx` rate, and p95 duration broken down by **route template** (for example
  `/api/v1/clubs/{id}`, never an identifier), plus in-flight requests and outbound client latency.
- **Product funnels** — the two counters of the privacy-safe operational funnels (ADR-0041), which are the
  same numbers `GET /ops/analytics/funnels` returns.

Traces are not a dashboard; they are read in Grafana's **Explore** view against the Tempo datasource, which
is where the latency runbook sends you.

## Checking the configuration

Configuration fails quietly — a mistyped metric name yields a panel that reads "No data" forever. So the
stack's configuration is checked:

```bash
npm run obs:check
```

| Check | Guards against | Needs Docker |
|---|---|---|
| `dashboards` | Invalid JSON, or a panel naming a datasource that is not provisioned | no |
| `metrics` | A rule or dashboard naming a metric the application does not emit — including the OTLP-to-Prometheus name translation drifting on an upgrade | no |
| `runbooks` | An alert with no runbook annotation, or one pointing at a section that no longer exists | no |
| `collector` | A pipeline the collector cannot parse — validated by its own `validate` | yes |
| `tempo` | A storage field the trace store rejects — validated by its own `-config.verify` | yes |
| `rules` | A malformed expression — validated by Prometheus's own `promtool` | yes |
| `alertmanager` | An unroutable config — validated by Alertmanager's own `amtool` | yes |
| `compose` | An invalid or unresolvable stack file | yes |

A Docker-based check reports `SKIP` when no daemon is reachable rather than failing, matching
[`supply-chain.md`](supply-chain.md). The whole suite runs locally and by hand; wiring it into CI is the
deployment milestone's work (`F-50`).

## What is deliberately not here

This milestone builds the stack, the dashboards, and the alerts for the signals that exist. §14.1's metric
list is only partly met, and the gap is recorded rather than papered over (ADR-0048). Missing, in rough order
of how much they matter:

- **Matchday publication delay**, and therefore the "99% within five minutes" objective. It needs an
  instrument in the publication use case.
- **Job queue depth, oldest-due age, retries, dead letters, and execution duration**. §7.1 asks for these and
  they were never built, so the queue is visible only through `GET /api/v1/admin/health/game` and the
  operator console.
- **Match simulation duration, login failures, auction lag, active managers, club occupancy, database pool
  saturation, payload sizes, and front-end errors.**
- **Logs.** The hosts register no OpenTelemetry logging exporter, so there is nothing for a log store to
  receive.
- **Custom spans** for database calls, worker jobs, email dispatch, and simulation. Only the ASP.NET Core and
  HttpClient auto-instrumentation exists, so a trace shows the request and its outbound calls, nothing inside.

## Never publish this stack

It has no authentication beyond Grafana's admin account, and it is meant for a developer's machine. It also
carries no `C2`-or-higher value: counts, latencies, route templates, and funnel step names, with no address,
no manager identity, and no hidden game attribute. That is what makes it safe locally — and why it must never
be exposed, since nothing in it is protected.
