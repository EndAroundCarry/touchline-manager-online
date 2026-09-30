#!/usr/bin/env node
//
// Supply-chain scans for the repository (master plan §16 Stage 14; threat model SC-2, SC-3).
//
// Everything here runs locally by hand. Nothing is wired into CI yet and no container images are
// built — that is the deployment milestone's work (ADR-0046). The root npm scripts wrap the
// individual checks; the procedure and triage notes live in docs/operations/supply-chain.md.
//
//   npm run scan               every check that can run on this machine
//   npm run scan:deps          .NET vulnerable packages
//   npm run scan:deps:web      npm vulnerable packages (web + e2e)
//   npm run scan:licenses      .NET licence inventory
//   npm run scan:licenses:web  npm licence inventory
//   npm run scan:secrets       gitleaks over the working tree (Docker)
//   npm run scan:image         trivy over the base images (Docker) — advisory; --strict fails on it
//   npm run scan:zap           OWASP ZAP baseline against a running API (Docker, optional)
//
// The checks are dependency-free on purpose: the .NET and npm inventories are read from the
// lockfiles and nuspecs the restores already produced, so a scan needs no extra tool and no network.

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const artifactsDirectory = join(repositoryRoot, 'infra', 'scan', '.artifacts');

// npm is a shell shim on Windows, so it is reached through the shell there and directly elsewhere.
const NPM_NEEDS_SHELL = process.platform === 'win32';

// Pinned so a scan is reproducible; a bump is a deliberate change.
const GITLEAKS_IMAGE = 'zricethezav/gitleaks:v8.21.2';
const TRIVY_IMAGE = 'aquasec/trivy:0.58.1';
const ZAP_IMAGE = 'ghcr.io/zaproxy/zaproxy:stable';

// The only images the stack ships with: the development backing services (infra/compose.yaml) and the
// local telemetry stack (infra/observability/, ADR-0048). API/worker images are Stage 14 deployment work
// and are deliberately not built here (ADR-0046).
const BASE_IMAGES = [
  'postgres:17-alpine',
  'axllent/mailpit:v1.27',
  'otel/opentelemetry-collector-contrib:0.161.0',
  'prom/prometheus:v3.15.0',
  'prom/alertmanager:v0.34.1',
  'grafana/grafana:13.2.3',
  'grafana/tempo:3.1.0',
];

// The npm projects whose lockfiles carry a licence inventory.
const NPM_PROJECTS = ['apps/web', 'tests/web-e2e'];

// Licences that must not enter the dependency graph. Matched case-insensitively against the whole
// declared licence string; `\b`-like boundaries keep LGPL (a weak copyleft we accept) out of the
// A?GPL branch.
const DENIED_LICENSES = /(?:^|[^A-Za-z0-9])(?:A?GPL|SSPL|EUPL|OSL-3|CC-BY-NC|BUSL-1|CPAL-1)(?:[^A-Za-z0-9]|$)/i;

// Build output and third-party code are never scanned as project content.
const SKIPPED_DIRECTORIES = new Set([
  'node_modules',
  '.git',
  'bin',
  'obj',
  'dist',
  '.angular',
  'TestResults',
  'playwright-report',
]);

const checks = new Map([
  ['deps', { label: 'deps           .NET vulnerable packages', run: scanDotnetDependencies }],
  ['deps-web', { label: 'deps-web       npm vulnerable packages', run: scanNpmDependencies }],
  ['licenses', { label: 'licenses       .NET licence inventory', run: scanDotnetLicenses }],
  ['licenses-web', { label: 'licenses-web   npm licence inventory', run: scanNpmLicenses }],
  ['secrets', { label: 'secrets        gitleaks (Docker)', run: scanSecrets }],
  ['image', { label: 'image          trivy over base images (Docker)', run: scanImages }],
  ['zap', { label: 'zap            OWASP ZAP baseline (Docker, optional)', run: scanZap }],
]);

const defaultChecks = ['deps', 'deps-web', 'licenses', 'licenses-web', 'secrets', 'image'];

const requested = process.argv.slice(2).filter((argument) => !argument.startsWith('-'));

// The base-image scan is advisory by default: the only image the stack ships with is a local
// development dependency, and production runs managed PostgreSQL (ADR-0008), so its CVEs are
// reported but do not gate. Strict mode fails on them, for when real images are built (ADR-0046).
const strictImages = process.argv.includes('--strict');

if (process.argv.includes('--list')) {
  for (const [name, check] of checks) {
    process.stdout.write(`${name.padEnd(14)} ${check.label}\n`);
  }

  process.exit(0);
}

const selected = requested.length === 0 || requested.includes('all')
  ? defaultChecks
  : requested;

for (const name of selected) {
  if (!checks.has(name)) {
    process.stderr.write(`Unknown check '${name}'. Run with --list to see the names.\n`);
    process.exit(2);
  }
}

const results = [];

for (const name of selected) {
  const check = checks.get(name);

  process.stdout.write(`\n> ${check.label}\n`);

  let result;

  try {
    result = check.run();
  } catch (error) {
    result = { status: 'fail', findings: [`the check could not run: ${error.message}`] };
  }

  results.push({ name, label: check.label, ...result });
}

report(results);

const failed = results.some((result) => result.status === 'fail');

process.exit(failed ? 1 : 0);

// ---------------------------------------------------------------------------------------------
// Checks
// ---------------------------------------------------------------------------------------------

/** Reads `dotnet list package --vulnerable` and reports every advisary the restore surfaced. */
function scanDotnetDependencies() {
  const result = run('dotnet', [
    'list',
    'TouchlineManager.slnx',
    'package',
    '--vulnerable',
    '--include-transitive',
    '--format',
    'json',
  ]);

  if (result.error) {
    return { status: 'fail', findings: [`dotnet could not be run: ${result.error.message}`] };
  }

  const report = parseJson(result.stdout);

  if (report === null) {
    return { status: 'fail', findings: ['the dotnet report was not JSON', tail(result.stderr || result.stdout)] };
  }

  const findings = [];

  for (const project of report.projects ?? []) {
    for (const framework of project.frameworks ?? []) {
      const packages = [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])];

      for (const pkg of packages) {
        for (const vulnerability of pkg.vulnerabilities ?? []) {
          findings.push(
            `${pkg.id} ${pkg.resolvedVersion} (${project.path}) — `
            + `${vulnerability.severity} ${vulnerability.advisoryurl}`,
          );
        }
      }
    }
  }

  return { status: findings.length === 0 ? 'pass' : 'fail', findings };
}

/** Runs `npm audit` in every npm project and reports high/critical advisories. */
function scanNpmDependencies() {
  const findings = [];
  let audited = 0;
  let skipped = 0;

  for (const project of NPM_PROJECTS) {
    const directory = join(repositoryRoot, project);

    if (!existsSync(join(directory, 'package-lock.json'))) {
      skipped++;

      continue;
    }

    const result = run('npm', ['audit', '--json', '--audit-level=high'], {
      cwd: directory,
      shell: NPM_NEEDS_SHELL,
    });

    if (result.error) {
      return { status: 'fail', findings: [`npm could not be run: ${result.error.message}`] };
    }

    const report = parseJson(result.stdout);

    if (report === null) {
      // `npm audit` answers non-zero when it finds something, so a null report means it did not run to
      // completion — most often no registry access from this machine. Skip rather than fail a check
      // that needs the network.
      skipped++;

      continue;
    }

    audited++;

    for (const [name, advisory] of Object.entries(report.vulnerabilities ?? {})) {
      if (advisory.severity === 'high' || advisory.severity === 'critical') {
        findings.push(`${name} — ${advisory.severity} (${project})`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${audited} project(s) audited, ${skipped} could not be reached`,
  };
}

/** Inventories every resolved NuGet package's declared licence and flags a denylisted one. */
function scanDotnetLicenses() {
  const nugetRoot = process.env.NUGET_PACKAGES ?? join(homedir(), '.nuget', 'packages');
  const packages = new Map();

  for (const lockfile of findFiles(repositoryRoot, 'packages.lock.json')) {
    const lock = parseJson(readFileSync(lockfile, 'utf8'));

    if (lock === null) {
      continue;
    }

    for (const framework of Object.values(lock.dependencies ?? {})) {
      for (const [id, meta] of Object.entries(framework ?? {})) {
        if (meta.type === 'Project' || !meta.resolved) {
          continue;
        }

        packages.set(`${id}@${meta.resolved}`, { id, version: meta.resolved });
      }
    }
  }

  const findings = [];
  let unknown = 0;

  for (const { id, version } of packages.values()) {
    const license = readNuGetLicense(nugetRoot, id, version);

    if (license === null) {
      unknown++;

      continue;
    }

    if (DENIED_LICENSES.test(license)) {
      findings.push(`${id} ${version} — ${license}`);
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${packages.size} package(s), ${unknown} with no machine-readable licence`,
  };
}

/** Inventories npm licences from the lockfiles and flags a denylisted one. */
function scanNpmLicenses() {
  const findings = [];
  let counted = 0;
  let unknown = 0;

  // The root lockfile is included: the task runner's one dependency is part of the graph too.
  for (const lockfile of [join(repositoryRoot, 'package-lock.json'), ...NPM_PROJECTS.map((project) => join(repositoryRoot, project, 'package-lock.json'))]) {
    if (!existsSync(lockfile)) {
      continue;
    }

    const lock = parseJson(readFileSync(lockfile, 'utf8'));

    if (lock === null) {
      continue;
    }

    for (const [path, meta] of Object.entries(lock.packages ?? {})) {
      if (!path.startsWith('node_modules/')) {
        continue;
      }

      const license = normaliseNpmLicense(meta.license);

      if (license === null) {
        unknown++;

        continue;
      }

      counted++;

      if (DENIED_LICENSES.test(license)) {
        findings.push(`${path.slice('node_modules/'.length)} — ${license}`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${counted} package(s), ${unknown} with no declared licence`,
  };
}

/** Scans the working tree for committed secrets, excluding build output and third-party code. */
function scanSecrets() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  mkdirSync(artifactsDirectory, { recursive: true });

  const report = join(artifactsDirectory, 'gitleaks.json');
  const result = run('docker', [
    'run',
    '--rm',
    '-v',
    `${repositoryRoot}:/repo`,
    '-w',
    '/repo',
    GITLEAKS_IMAGE,
    'detect',
    '--no-git',
    '--source=/repo',
    '--config=/repo/.gitleaks.toml',
    '--report-format',
    'json',
    '--report-path=/repo/infra/scan/.artifacts/gitleaks.json',
  ]);

  const findings = readGitleaksReport(report);

  if (findings === null) {
    return { status: 'fail', findings: ['gitleaks did not produce a report', tail(result.stderr || result.stdout)] };
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings: findings.slice(0, 20),
    detail: `${findings.length} finding(s); the full report is at infra/scan/.artifacts/gitleaks.json`,
  };
}

/** Scans the base images the stack ships with for high/critical CVEs. */
function scanImages() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  mkdirSync(artifactsDirectory, { recursive: true });

  const findings = [];

  for (const image of BASE_IMAGES) {
    const result = run('docker', [
      'run',
      '--rm',
      '-v',
      'touchline-trivy-cache:/root/.cache/',
      TRIVY_IMAGE,
      'image',
      '--severity',
      'HIGH,CRITICAL',
      '--ignore-unfixed',
      '--format',
      'json',
      '--quiet',
      image,
    ]);

    const report = parseJson(result.stdout);

    if (report === null) {
      findings.push(`${image} — trivy could not scan it: ${tail(result.stderr || result.stdout)}`);

      continue;
    }

    for (const imageResult of report.Results ?? []) {
      for (const vulnerability of imageResult.Vulnerabilities ?? []) {
        findings.push(`${image} — ${vulnerability.VulnerabilityID} ${vulnerability.Severity} ${vulnerability.PkgName}`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : strictImages ? 'fail' : 'warn',
    findings: findings.slice(0, 20),
    detail: `${BASE_IMAGES.length} image(s) scanned`
      + (findings.length > 0 && !strictImages
        ? ' — advisory: no image is shipped yet, production runs managed PostgreSQL (ADR-0008)'
        : ''),
  };
}

/** Runs an OWASP ZAP baseline against a running API. Optional: it needs a live stack. */
function scanZap() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const target = process.env.ZAP_TARGET ?? 'http://host.docker.internal:5080';

  const result = run('docker', [
    'run',
    '--rm',
    '--add-host=host.docker.internal:host-gateway',
    ZAP_IMAGE,
    'zap-baseline.py',
    '-t',
    target,
    '-I',
  ]);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stdout || result.stderr)],
    detail: `baseline against ${target}`,
  };
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

/** Runs a command without a shell, except where the platform requires one (npm on Windows). */
function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: options.cwd ?? repositoryRoot,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    shell: options.shell ?? false,
  });

  return {
    code: result.status,
    stdout: result.stdout ?? '',
    stderr: result.stderr ?? '',
    error: result.error ?? null,
  };
}

/** Whether a Docker daemon is reachable, so a Docker-based check can skip instead of failing. */
function hasDocker() {
  const result = run('docker', ['version', '--format', '{{.Server.Version}}']);

  return result.error === null && result.code === 0;
}

/** Parses the first JSON value in a string, tolerating a BOM or a leading log line. */
function parseJson(text) {
  const start = text.search(/[[{]/);

  if (start < 0) {
    return null;
  }

  try {
    return JSON.parse(text.slice(start).replace(/^\uFEFF/, ''));
  } catch {
    return null;
  }
}

/** Reads the declared licence of a resolved NuGet package from its nuspec. */
function readNuGetLicense(nugetRoot, id, version) {
  const nuspec = join(nugetRoot, id.toLowerCase(), version, `${id.toLowerCase()}.nuspec`);

  if (!existsSync(nuspec)) {
    return null;
  }

  const xml = readFileSync(nuspec, 'utf8');

  const expression = /<license[^>]*\btype="expression"[^>]*>([^<]+)<\/license>/i.exec(xml);

  if (expression) {
    return expression[1].trim();
  }

  const file = /<license[^>]*\btype="file"[^>]*>([^<]+)<\/license>/i.exec(xml);

  if (file) {
    return `file: ${file[1].trim()}`;
  }

  const url = /<licenseUrl>([^<]+)<\/licenseUrl>/i.exec(xml);

  return url ? url[1].trim() : null;
}

/** Normalises an npm `license` field, which is a string or a legacy `{ type }` object. */
function normaliseNpmLicense(license) {
  if (typeof license === 'string') {
    return license.trim().length === 0 ? null : license.trim();
  }

  if (license && typeof license === 'object' && typeof license.type === 'string') {
    return license.type;
  }

  return null;
}

/** Recursively finds files by name, skipping build output and third-party code. */
function findFiles(root, name) {
  const found = [];

  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (SKIPPED_DIRECTORIES.has(entry.name)) {
        continue;
      }

      found.push(...findFiles(join(root, entry.name), name));

      continue;
    }

    if (entry.isFile() && entry.name === name) {
      found.push(join(root, entry.name));
    }
  }

  return found;
}

/** Reads the gitleaks JSON report, or null when there is none. */
function readGitleaksReport(path) {
  if (!existsSync(path)) {
    return null;
  }

  const report = parseJson(readFileSync(path, 'utf8'));

  if (report === null) {
    return null;
  }

  return (Array.isArray(report) ? report : []).map(
    (finding) => `${finding.File ?? '?'}:${finding.StartLine ?? '?'} — ${finding.RuleID ?? finding.Description ?? 'finding'}`,
  );
}

/** The last non-empty line of a tool's output, for a one-line failure reason. */
function tail(text) {
  const lines = text.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0);

  return lines.at(-1) ?? 'no output';
}

/** Prints the run's summary and the findings of every failed check. */
function report(results) {
  process.stdout.write('\nSupply-chain scan\n-----------------\n');

  for (const result of results) {
    const marker = { pass: 'PASS', warn: 'WARN', skip: 'SKIP', fail: 'FAIL' }[result.status] ?? 'FAIL';

    process.stdout.write(`${marker}  ${result.label.trim()}\n`);

    if (result.detail) {
      process.stdout.write(`      ${result.detail}\n`);
    }

    for (const finding of result.findings ?? []) {
      process.stdout.write(`      - ${finding}\n`);
    }
  }

  const failed = results.filter((result) => result.status === 'fail').length;
  const warned = results.filter((result) => result.status === 'warn').length;

  if (failed > 0) {
    process.stdout.write(`\n${failed} check(s) failed. See docs/operations/supply-chain.md for how to triage.\n`);

    return;
  }

  process.stdout.write(
    warned > 0
      ? `\nAll blocking checks passed; ${warned} advisory warning(s) above.\n`
      : '\nAll checks passed.\n',
  );
}
