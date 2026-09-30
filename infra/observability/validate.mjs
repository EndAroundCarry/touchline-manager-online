#!/usr/bin/env node
//
// Checks the local telemetry stack (master plan §14.1, §16 Stage 14; `F-48`).
//
//   npm run obs:check          every check that can run on this machine
//   npm run obs:check -- list  the check names
//
// The stack is configuration, and configuration fails quietly: a mistyped metric name produces a panel that
// renders "No data" forever, and an alert whose runbook link points at a section that no longer exists is
// exactly the "actionable" property §14.1 asks for, lost without a sound. These checks are what make those
// two failures loud. They are dependency-free — the YAML is validated by the tools that will actually read
// it (promtool, amtool, docker compose), each in its own pinned image.

import { spawnSync } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const stackDirectory = resolve(fileURLToPath(new URL('.', import.meta.url)));
const repositoryRoot = resolve(stackDirectory, '..', '..');

const rulesDirectory = join(stackDirectory, 'prometheus', 'rules');
const dashboardsDirectory = join(stackDirectory, 'grafana', 'dashboards');
const datasourcesFile = join(stackDirectory, 'grafana', 'provisioning', 'datasources', 'datasources.yaml');
const runbookFile = join(repositoryRoot, 'docs', 'operations', 'runbook.md');
const composeFile = join(stackDirectory, 'compose.observability.yaml');

// The images the stack pins. Only ever used to run the tools that validate the configuration, never to
// serve traffic, so a stale tag here cannot affect a running stack. `npm run scan:image` covers their CVEs.
const OTEL_IMAGE = 'otel/opentelemetry-collector-contrib:0.161.0';
const PROMETHEUS_IMAGE = 'prom/prometheus:v3.15.0';
const ALERTMANAGER_IMAGE = 'prom/alertmanager:v0.34.1';
const TEMPO_IMAGE = 'grafana/tempo:3.1.0';

// Every metric the application can produce, in the form the collector's prometheus exporter gives it: `.`
// becomes `_`, a histogram gains its unit as a suffix, and a monotonic sum gains `_total`. A rule or a
// dashboard that names anything else is a typo, and it is the failure this check exists to catch —
// including the OTLP-to-Prometheus name translation drifting when OpenTelemetry is upgraded.
const KNOWN_METRICS = [
  'http_server_request_duration_seconds',
  'http_server_active_requests',
  'http_client_request_duration_seconds',
  'touchline_analytics_onboarding_total',
  'touchline_analytics_tenure_total',
];

// A metric reference in PromQL or in a dashboard expression: the exporter's own families plus the two
// custom instruments. Anchored on those prefixes so PromQL's functions and operators are never mistaken
// for a metric name.
const METRIC_REFERENCE = /\b(?:http_server|http_client|touchline)_[a-z0-9_]+\b/g;

// The suffix the prometheus exporter adds for a histogram's parts and a counter's `_total`.
const SUFFIXES = /_(?:bucket|count|sum|total)$/;

const normalizeMetric = (name) => name.replace(SUFFIXES, '');

const knownMetrics = new Set(KNOWN_METRICS.map(normalizeMetric));

const checks = [
  { label: 'dashboards     JSON parses; every datasource is provisioned', run: checkDashboards },
  { label: 'metrics        every referenced metric is one the app emits', run: checkMetricNames },
  { label: 'runbooks       every alert links to a real runbook section', run: checkRunbookLinks },
  { label: 'collector      the collector config validates (Docker)', run: checkCollectorConfig },
  { label: 'tempo          the trace store config validates (Docker)', run: checkTempoConfig },
  { label: 'rules          promtool validates the alert rules (Docker)', run: checkPrometheusRules },
  { label: 'alertmanager   amtool validates the routing config (Docker)', run: checkAlertmanagerConfig },
  { label: 'compose        docker compose validates the stack (Docker)', run: checkCompose },
];

if (process.argv.includes('--list') || process.argv.includes('list')) {
  for (const check of checks) {
    process.stdout.write(`${check.label}\n`);
  }

  process.exit(0);
}

const results = [];

for (const check of checks) {
  process.stdout.write(`\n> ${check.label}\n`);

  let result;

  try {
    result = check.run();
  } catch (error) {
    result = { status: 'fail', findings: [`the check could not run: ${error.message}`] };
  }

  results.push({ ...check, ...result });
}

report(results);

process.exit(results.some((result) => result.status === 'fail') ? 1 : 0);

// ---------------------------------------------------------------------------------------------
// Checks
// ---------------------------------------------------------------------------------------------

/**
 * Every dashboard must be valid JSON, and every datasource it names must actually be provisioned — a
 * dashboard pointing at an unprovisioned UID loads as empty panels with no error anywhere.
 */
function checkDashboards() {
  const provisioned = readProvisionedDatasourceUids();
  const findings = [];
  const files = listFiles(dashboardsDirectory, '.json');

  if (files.length === 0) {
    return { status: 'fail', findings: ['no dashboards found'] };
  }

  for (const file of files) {
    const name = file.slice(dashboardsDirectory.length + 1);
    const text = readFileSync(file, 'utf8');

    let dashboard;

    try {
      dashboard = JSON.parse(text);
    } catch (error) {
      findings.push(`${name} — not valid JSON: ${error.message}`);

      continue;
    }

    if (typeof dashboard.uid !== 'string' || dashboard.uid.length === 0) {
      findings.push(`${name} — no uid, so the file-based provider cannot address it`);
    }

    if (typeof dashboard.title !== 'string' || dashboard.title.length === 0) {
      findings.push(`${name} — no title`);
    }

    if (!Array.isArray(dashboard.panels) || dashboard.panels.length === 0) {
      findings.push(`${name} — no panels`);
    }

    for (const uid of collectDatasourceUids(dashboard)) {
      if (!provisioned.has(uid)) {
        findings.push(`${name} — references datasource '${uid}', which is not provisioned`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${files.length} dashboard(s) against ${provisioned.size} provisioned datasource(s)`,
  };
}

/**
 * Every metric named in a rule or a dashboard must be one the application emits. `promtool` proves an
 * expression parses; only this proves it parses to something that can ever have data.
 */
function checkMetricNames() {
  const findings = [];
  let referenced = 0;

  const sources = [
    ...listFiles(rulesDirectory, '.yml'),
    ...listFiles(dashboardsDirectory, '.json'),
    datasourcesFile,
  ];

  for (const file of sources) {
    const text = readFileSync(file, 'utf8');
    const name = file.slice(stackDirectory.length + 1).replace(/\\/g, '/');

    for (const token of new Set(text.match(METRIC_REFERENCE) ?? [])) {
      referenced += 1;

      if (!knownMetrics.has(normalizeMetric(token))) {
        findings.push(`${name} — '${token}' is not a metric the application emits`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${referenced} distinct metric reference(s) against ${knownMetrics.size} known instrument(s)`,
  };
}

/**
 * Every alert must name its runbook, and the section it names must exist. This is §14.1's "alerts must be
 * actionable and linked to runbooks", checked rather than promised.
 */
function checkRunbookLinks() {
  const anchors = readRunbookAnchors();
  const findings = [];
  let rules = 0;

  for (const file of listFiles(rulesDirectory, '.yml')) {
    const name = file.slice(rulesDirectory.length + 1);
    const text = readFileSync(file, 'utf8');

    for (const rule of splitAlertRules(text)) {
      rules += 1;

      if (!/^\s*expr:/m.test(rule.body)) {
        findings.push(`${name} — '${rule.alert}' has no expression`);
      }

      if (!/^\s*severity:/m.test(rule.body)) {
        findings.push(`${name} — '${rule.alert}' has no severity label`);
      }

      const link = /^\s*runbook:\s*"?([^"\n]+)"?\s*$/m.exec(rule.body);

      if (link === null) {
        findings.push(`${name} — '${rule.alert}' has no runbook annotation`);

        continue;
      }

      const value = link[1].trim();

      if (!value.startsWith('docs/operations/runbook.md#')) {
        findings.push(`${name} — '${rule.alert}' links to '${value}', not to docs/operations/runbook.md`);

        continue;
      }

      const anchor = value.slice(value.indexOf('#') + 1);

      if (!anchors.has(anchor)) {
        findings.push(`${name} — '${rule.alert}' links to '#${anchor}', which is not a heading in the runbook`);
      }
    }
  }

  return {
    status: findings.length === 0 ? 'pass' : 'fail',
    findings,
    detail: `${rules} alert rule(s) against ${anchors.size} runbook section(s)`,
  };
}

/** The collector ships a `validate` subcommand, so an unparseable pipeline fails here, not at startup. */
function checkCollectorConfig() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const directory = join(stackDirectory, 'otel');

  const result = run('docker', [
    'run',
    '--rm',
    '-v',
    `${directory}:/config:ro`,
    '--entrypoint',
    '/otelcol-contrib',
    OTEL_IMAGE,
    'validate',
    '--config=/config/collector.yaml',
  ]);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stderr || result.stdout)],
    detail: 'collector.yaml verified by the collector itself',
  };
}

/** Tempo is distroless, so its binary's `-config.verify` is the only pre-flight check it offers. */
function checkTempoConfig() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const directory = join(stackDirectory, 'tempo');

  const result = run('docker', [
    'run',
    '--rm',
    '-v',
    `${directory}:/config:ro`,
    '--entrypoint',
    '/tempo',
    TEMPO_IMAGE,
    '-config.file=/config/tempo.yaml',
    '-config.verify=true',
  ]);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stderr || result.stdout)],
    detail: 'tempo.yaml verified by Tempo itself',
  };
}

/** `promtool` is Prometheus's own parser, so this is the definition of a valid rule, not an approximation. */
function checkPrometheusRules() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const files = listFiles(rulesDirectory, '.yml').map((file) => `/rules/${file.slice(rulesDirectory.length + 1)}`);

  if (files.length === 0) {
    return { status: 'fail', findings: ['no rule files found'] };
  }

  const result = run('docker', [
    'run',
    '--rm',
    '-v',
    `${rulesDirectory}:/rules:ro`,
    '--entrypoint',
    '/bin/promtool',
    PROMETHEUS_IMAGE,
    'check',
    'rules',
    ...files,
  ]);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stderr || result.stdout)],
    detail: `${files.length} rule file(s) through promtool`,
  };
}

/** `amtool` is Alertmanager's own parser, so an unroutable config fails here rather than at startup. */
function checkAlertmanagerConfig() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const directory = join(stackDirectory, 'alertmanager');

  const result = run('docker', [
    'run',
    '--rm',
    '-v',
    `${directory}:/config:ro`,
    '--entrypoint',
    '/bin/amtool',
    ALERTMANAGER_IMAGE,
    'check-config',
    '/config/alertmanager.yml',
  ]);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stderr || result.stdout)],
    detail: 'alertmanager.yml through amtool',
  };
}

/** `docker compose config` resolves the file the way the stack will, variables and all. */
function checkCompose() {
  if (!hasDocker()) {
    return { status: 'skip', findings: [], detail: 'Docker is not available' };
  }

  const result = run('docker', ['compose', '-f', composeFile, 'config', '-q']);

  return {
    status: result.code === 0 ? 'pass' : 'fail',
    findings: result.code === 0 ? [] : [tail(result.stderr || result.stdout)],
    detail: 'compose.observability.yaml resolved',
  };
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

/** The datasource UIDs the provisioning file declares, which is what a dashboard is allowed to reference. */
function readProvisionedDatasourceUids() {
  const text = readFileSync(datasourcesFile, 'utf8');

  return new Set([...text.matchAll(/^\s*uid:\s*([A-Za-z0-9_-]+)\s*$/gm)].map((match) => match[1]));
}

/** Walks a dashboard object for every `datasource` reference, at panel and target level. */
function collectDatasourceUids(node, found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) {
      collectDatasourceUids(item, found);
    }

    return found;
  }

  if (node === null || typeof node !== 'object') {
    return found;
  }

  for (const [key, value] of Object.entries(node)) {
    if (
      key === 'datasource'
      && value !== null
      && typeof value === 'object'
      && typeof value.uid === 'string'
    ) {
      found.add(value.uid);

      continue;
    }

    collectDatasourceUids(value, found);
  }

  return found;
}

/**
 * The anchors GitHub derives from the runbook's headings, so a link is compared against what a reader's
 * browser would actually resolve. Punctuation (including the em dash in "Runbook — …") is dropped and
 * spaces become hyphens.
 */
function readRunbookAnchors() {
  const text = readFileSync(runbookFile, 'utf8');
  const anchors = new Set();

  for (const match of text.matchAll(/^#{1,6}\s+(.+?)\s*$/gm)) {
    anchors.add(
      match[1]
        .toLowerCase()
        .replace(/[^\w\- ]+/g, '')
        .trim()
        .replace(/ /g, '-'),
    );
  }

  return anchors;
}

/** Splits a Prometheus rule file into one entry per alert, so each is checked on its own. */
function splitAlertRules(text) {
  return text
    .split(/\n\s*- alert:/)
    .slice(1)
    .map((body) => ({ alert: body.split(/\r?\n/, 1)[0].trim(), body }));
}

/** Lists the files in a directory with the given extension. */
function listFiles(directory, extension) {
  return readdirSync(directory)
    .filter((name) => name.endsWith(extension))
    .map((name) => join(directory, name))
    .sort();
}

/** Whether a Docker daemon is reachable, so a Docker-based check can skip instead of failing. */
function hasDocker() {
  const result = run('docker', ['version', '--format', '{{.Server.Version}}']);

  return result.error === null && result.code === 0;
}

/** Runs a command without a shell. */
function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    shell: false,
  });

  return {
    code: result.status,
    stdout: result.stdout ?? '',
    stderr: result.stderr ?? '',
    error: result.error ?? null,
  };
}

/** The last non-empty line of a tool's output, for a one-line failure reason. */
function tail(text) {
  const lines = text.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0);

  return lines.at(-1) ?? 'no output';
}

/** Prints the run's summary and the findings of every failed check. */
function report(results) {
  process.stdout.write('\nObservability stack\n-------------------\n');

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
  const skipped = results.filter((result) => result.status === 'skip').length;

  if (failed > 0) {
    process.stdout.write(
      `\n${failed} check(s) failed. See docs/operations/observability.md for what each one guards.\n`,
    );

    return;
  }

  process.stdout.write(
    skipped > 0
      ? `\nAll blocking checks passed; ${skipped} check(s) skipped (Docker is not available).\n`
      : '\nAll checks passed.\n',
  );
}
