# ADR-0046: Load, supply-chain, and restore drills, run locally

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 14
- **Related:** [ADR-0008](0008-deployment-topology.md), [ADR-0015](0015-compressed-test-clock.md),
  [ADR-0016](0016-non-production-matchday-trigger.md), [ADR-0042](0042-operator-access-and-mfa.md),
  [ADR-0044](0044-operator-recovery-commands.md), [ADR-0045](0045-administrative-repairs-and-broadcasts.md)

## Context

Stage 14's exit criteria require load targets and the matchday publication SLO to pass with headroom, a
restore drill to meet documented integrity checks, and no unresolved critical/high security finding.
Test-strategy layer 10 names the deliverables: k6 scenarios for login bursts, dashboard reads, matchday
polling, and concurrent bids; dependency and container scans; and a backup/PITR restore drill.

None of it existed. The backlog scans are named as a supply-chain rule (`SC-2`, `SC-3`) but were never
run; no load scenario existed; the deployment topology ([ADR-0008](0008-deployment-topology.md)) promised
managed PITR but nothing proved a restore; and the stack ships no container images of its own, because
image building is deployment work.

Three decisions bound this milestone, taken with the user:

1. **No container images.** Scans cover dependencies, licences, secrets, and the one base image the
   local stack uses; API/worker/web Dockerfiles stay with the deployment milestone.
2. **Real PITR**, against PostgreSQL 17: a base backup, WAL archiving, and a replay to a chosen instant.
3. **Local scripts only.** Every drill is runnable by hand and documented; nothing is wired into CI.

## Decision

1. **The population model is the product's own world.** Six countries of eighteen clubs is 108 clubs, so
   one human manager per club is 108 concurrent users; the stage's three-times headroom is **324 virtual
   users**. The model lives in `tests/load/config.js` and every scenario scales from it, so changing the
   world's shape moves the whole suite.

2. **k6, run through its container, is the load tool.** The scenarios are plain k6 scripts; the runner
   (`tests/load/run.mjs`) invokes the pinned `grafana/k6` image and arranges the network for the host, so
   no local k6 install is needed. Each request carries a `kind` tag (`read`/`command`) so the read and
   command SLOs are asserted separately, per endpoint, from one metrics stream. A losing bid is a `409`
   and is declared expected, so `http_req_failed` keeps its meaning: a `5xx` fails.

3. **The load fixture is created through the public API.** `tests/load/seed.mjs` registers, confirms,
   signs in, onboards, and claims through the real endpoints, because a fixture that bypassed them would
   not exercise what it measures. It writes the account pool the scenarios authenticate with.

4. **Supply-chain scans read the lockfiles, and the image scan is advisory.** The .NET vulnerability
   scan and both licence inventories come from `dotnet list --vulnerable`, `packages.lock.json`, the
   nuspecs a restore already wrote, and the npm lockfiles, so a scan needs no extra tool and no network.
   gitleaks and trivy run through pinned images. Because no image is shipped yet and production runs
   managed PostgreSQL, a base-image finding is a `WARN`; `scan:image:strict` is the gate for when real
   images exist. The secret scan runs over the working tree and allowlists by value, never by whole file.

5. **The restore drill is real PITR.** It archives WAL from a primary, takes a `pg_basebackup`, writes an
   `after` marker past a chosen target instant, recovers a fresh cluster to that instant with
   `recovery.signal`, `restore_command`, and `recovery_target_time`, promotes it, and then proves the
   outcome: the ledger replays to every stored balance, publication stays atomic, the job queue is
   sound, leases are cleared ([ADR-0008](0008-deployment-topology.md)), the `before` marker is present,
   the `after` marker is absent, and the world is intact. That last pair is what proves the restore
   stopped where it was told to; a restore that fails a check is not a recovery. The provider's
   managed-PITR procedure is the same shape, documented in `docs/operations/backup-and-restore.md`.

6. **Nothing is wired into CI in this milestone, and no application code changes.** The drills are npm
   scripts and documents. Wiring them, building images, and making the deployment rollback and
   API/PWA-compatibility drill are the deployment milestone's work.

## Consequences

**Positive**

- The load, security, and restore evidence the stage requires can now be produced on demand, from the
  repository, without a staging environment.
- The scenarios and the drill are executable specifications of the SLOs and the invariants, not prose.
- The scan suite is dependency-free for everything but secrets and images, so it runs anywhere the SDK
  and npm do.

**Negative**

- The evidence is manual. Until CI runs the scans and the scheduled drills, a regression is caught only
  when somebody runs them by hand.
- The container scan is advisory, so nothing enforces the base image's hygiene yet.
- The matchday publication scenario needs a round at its deadline, which the seeded world does not have;
  it is honest about that (it fails fast and says so) but it means the publication SLO is really proven
  against the compressed-clock stack ([ADR-0015](0015-compressed-test-clock.md)), not the default one.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Build API/worker images now so container scanning is meaningful | Image building is the deployment milestone's work; doing it here would pull Dockerfiles, a deploy compose, and a registry into a drill milestone (agreed with the user). |
| A third-party .NET licence tool (`dotnet-project-licenses`) | Adds a tool and a network dependency to a scan that the lockfiles and nuspecs already answer; the self-contained reader is more reliable and reviews as one file. |
| Logical `pg_dump`/`pg_restore` instead of PITR | Faster, but it does not exercise point-in-time recovery, which is the property a deadline-critical game actually relies on (agreed with the user). |
| Wire everything into CI now | The scans and drills are environment-heavy and slow; gating every pull request on them before images and staging exist would make CI flaky for no coverage (agreed with the user). |
| Load-test with a bespoke .NET harness | k6 is the plan's named tool, is script-based, and is the least code to maintain; a harness would be application code for a non-application concern. |
| Seed the load fixture by writing rows directly | It would skip the write path the load is meant to exercise and could mask an API-level bottleneck. |

## Deferred

- Production Dockerfiles and image build/scan/publish; CI jobs, scheduled workflows, and gating.
- Deployment rollback and API/PWA-compatibility drill (`F-50`); maintenance/read-only mode (`F-51`).
- Telemetry dashboards and SLO alerting (`F-48`); the cost model; front-end performance budgets.
- Secret **rotation** automation (scanning is in scope; rotation is not).
- Tuning indexes, pools, or concurrency from the load evidence — the drills produce the evidence; acting
  on it is follow-up work with its own measurement.
