# Supply-chain scans

The dependency, licence, secret, and base-image checks for the repository (threat model `SC-2`,
`SC-3`; master plan §16 Stage 14). They run locally by hand through `infra/scan/scan.mjs`; nothing is
wired into CI yet — that is the deployment milestone's work ([ADR-0046](../architecture/adr/0046-load-supply-chain-and-restore-drills.md)).

## Running it

```bash
npm run scan               # every check that can run on this machine
npm run scan:deps          # .NET vulnerable packages (dotnet list --vulnerable)
npm run scan:deps:web      # npm vulnerable packages (web + e2e)
npm run scan:licenses      # .NET licence inventory
npm run scan:licenses:web  # npm licence inventory
npm run scan:secrets       # gitleaks over the working tree (Docker)
npm run scan:image         # trivy over the base images (Docker)
npm run scan:image:strict  # the same, failing on a finding
npm run scan:zap           # OWASP ZAP baseline against a running API (Docker, optional)
```

The run prints one line per check and exits non-zero when a blocking check fails. `scan:deps*` and
`scan:licenses*` need only the .NET SDK and npm; the Docker-based checks skip with a clear line when no
daemon is reachable rather than failing.

## What each check does, and what fails it

| Check | Source of truth | Fails when |
|---|---|---|
| `deps` | `dotnet list TouchlineManager.slnx package --vulnerable --include-transitive --format json` | any package carries an advisory |
| `deps-web` | `npm audit --json` in `apps/web` and `tests/web-e2e` | any `high` or `critical` advisory |
| `licenses` | each resolved package's `.nuspec` from the NuGet global packages folder | a licence on the denylist |
| `licenses-web` | the `license` field in each `package-lock.json` entry | a licence on the denylist |
| `secrets` | `gitleaks detect --no-git` over the working tree | any finding the allowlist does not cover |
| `image` | `trivy image` on `postgres:17-alpine` and `axllent/mailpit:v1.27` | advisory by default; a finding under `--strict` |
| `zap` | OWASP ZAP baseline against a running API | a baseline failure (optional; needs a live stack) |

The licence denylist is a single named constant in `infra/scan/scan.mjs` (`GPL`/`AGPL`/`SSPL` and the
close relatives). A licence-policy change is one edit there.

The .NET scan and both licence inventories are read from the lockfiles and nuspecs a restore already
produced, so a scan needs no extra tool and no network. Only `deps-web`, `secrets`, `image`, and `zap`
reach out.

## Why the base-image scan is advisory

The stack ships no container images yet, and per [ADR-0008](../architecture/adr/0008-deployment-topology.md)
production runs **managed** PostgreSQL rather than this image. The only image scanned is a local
development dependency, so a finding is reported as `WARN` and does not fail the run. When the
deployment milestone builds API, worker, and web images, `scan:image:strict` becomes the gate.

The `postgres:17-alpine` image currently reports fixable `HIGH`/`CRITICAL` advisories in the Go standard
library bundled into its `gosu` binary — the same set is present in `postgres:18-alpine`, so it is
upstream and not ours to patch. The remediation is to bump to a rebuilt tag when the provider publishes
one.

## Triaging a secret finding

`.gitleaks.toml` allowlists two things by value or path, never by whole file, so a real secret committed
into the same file is still caught:

- build output, third-party code, and the scan's own report;
- the deliberate development-only secrets ([ADR-0002](../architecture/adr/0002-auth-and-session-model.md),
  [ADR-0042](../architecture/adr/0042-operator-access-and-mfa.md)) and public test vectors.

When a finding appears, decide which it is:

- **a real secret** — rotate it, remove it from history, and treat it as an incident; the scan has done
  its job;
- **a new known-safe literal** — add it to the `regexes` list with a comment saying why, in the same
  change that introduces it, so the allowlist stays reviewable.

Never allowlist a path to silence a finding in it. The value-based allowlist exists precisely so a test
file can contain fake secrets without becoming a blind spot.
