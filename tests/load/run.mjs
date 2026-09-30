#!/usr/bin/env node
//
// Runs one k6 scenario against a running stack, on any platform.
//
// k6 runs in the official container so nothing has to be installed locally. A container cannot reach
// the host's loopback the way the host can, so the network is arranged per platform and the scenario's
// base URL is pointed at the host accordingly.
//
//   npm run load:reads
//   npm run load:reads -- --smoke
//
// `--smoke` (or LOAD_SMOKE=1) shrinks the scenario so the plumbing can be checked in seconds.

import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const repositoryRoot = resolve(here, '..', '..');

const K6_IMAGE = 'grafana/k6:0.57.0';

const scenarios = ['login-burst', 'dashboard-reads', 'matchday-polling', 'auction-contention', 'matchday-publication'];

const args = process.argv.slice(2);
const scenario = args.find((argument) => !argument.startsWith('-'));
const smoke = args.includes('--smoke') || process.env.LOAD_SMOKE === '1';
const passthrough = args.filter((argument) => argument !== '--smoke' && argument !== scenario);

if (!scenario || !scenarios.includes(scenario)) {
  process.stderr.write(`Usage: npm run load -- <${scenarios.join('|')}> [--smoke] [k6 args]\n`);
  process.exit(2);
}

if (!existsSync(join(here, '.artifacts', 'credentials.json'))) {
  process.stderr.write('No credentials.json yet: run `npm run load:seed` first.\n');
  process.exit(2);
}

const linux = process.platform === 'linux';
const host = linux ? 'localhost' : 'host.docker.internal';
const network = linux ? ['--network', 'host'] : ['--add-host', 'host.docker.internal:host-gateway'];
const baseUrl = process.env.LOAD_BASE_URL ?? `http://${host}:5080/api/v1`;

process.stdout.write(`k6 ${scenario}${smoke ? ' (smoke)' : ''} against ${baseUrl}\n`);

execFileSync(
  'docker',
  [
    'run', '--rm', '-i',
    ...network,
    '-v', `${here}:/scripts`,
    '-w', '/scripts',
    '-e', `BASE_URL=${baseUrl}`,
    '-e', `LOAD_SMOKE=${smoke ? '1' : '0'}`,
    ...(process.env.LOAD_WINDOW ? ['-e', `LOAD_WINDOW=${process.env.LOAD_WINDOW}`] : []),
    K6_IMAGE,
    'run', `/scripts/${scenario}.js`,
    ...passthrough,
  ],
  { cwd: repositoryRoot, stdio: 'inherit' },
);
