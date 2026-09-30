#!/usr/bin/env node
//
// A point-in-time restore drill (master plan §16 Stage 14; `F-49`).
//
// It stands up an archive-enabled PostgreSQL, migrates and seeds a world, marks a point in time, takes
// a base backup, writes more data after that point, then recovers into a fresh cluster targeted at the
// point and proves the recovery landed there: the pre-target marker is present, the post-target one is
// absent, and every integrity invariant still holds. ADR-0008 requires restored copies to be safe to
// open, so the drill also clears job leases and asserts none remain.
//
//   npm run drill:restore
//
// It runs against Docker on a fixed set of names, so it tears down its own volumes when it is done. The
// procedure maps to the provider's managed PITR in docs/operations/backup-and-restore.md.

import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const here = fileURLToPath(new URL('.', import.meta.url));
const repositoryRoot = resolve(here, '..', '..');

const COMPOSE_FILE = resolve(here, 'compose.pitr.yaml');
const COMPOSE = ['compose', '-f', COMPOSE_FILE];

const PRIMARY = 'touchline-pitr-primary';
const RESTORE = 'touchline-pitr-restore';

const PORT = process.env.PITR_PRIMARY_PORT ?? '55433';
const PASSWORD = 'local_dev_password_change_me';
const CONNECTION = `Host=localhost;Port=${PORT};Database=touchline;Username=touchline_app;Password=${PASSWORD}`;

main()
  .then((ok) => {
    cleanup();

    process.exit(ok ? 0 : 1);
  })
  .catch((error) => {
    cleanup();
    process.stderr.write(`\nThe restore drill failed: ${error.message}\n`);
    process.exit(1);
  });

async function main() {
  cleanup();

  // Compose creates the declared volume, then the directories inside it are made with the image's own
  // `postgres` user, so their ownership matches whatever uid the image uses (it differs between the
  // Alpine and Debian variants) and the archiver can write to them.
  step('Preparing the archive volume');
  compose(['create', 'primary']);
  sh('docker run --rm -v touchline-pitr-archive:/archive postgres:17-alpine sh -c "mkdir -p /archive/wal /archive/base && chown -R postgres:postgres /archive"');

  step('Starting the archive-enabled primary');
  compose(['up', '-d', '--wait', 'primary']);

  step('Migrating and seeding a world into the drill database');
  run('dotnet', ['tool', 'restore']);
  run('dotnet', ['dotnet-ef', 'database', 'update',
    '--project', 'src/TouchlineManager.Infrastructure',
    '--startup-project', 'src/TouchlineManager.Infrastructure'], { env: { ConnectionStrings__Database: CONNECTION } });
  run('dotnet', ['run', '--project', 'tools/world-seeder'],
    { env: { ConnectionStrings__Database: CONNECTION } });

  step('Marking the pre-target state');
  primary(`
    create table if not exists ops.restore_drill_sentinel (
      label text primary key,
      marked_at timestamptz not null default clock_timestamp()
    );
    insert into ops.restore_drill_sentinel (label) values ('before') on conflict (label) do nothing;
  `);

  step('Taking the base backup');
  docker(['exec', '-e', `PGPASSWORD=${PASSWORD}`, '-u', 'postgres', PRIMARY,
    'pg_basebackup', '-h', 'localhost', '-U', 'touchline_app',
    '-D', '/archive/base', '--wal-method=stream', '--checkpoint=fast']);

  // The recovery target must be after the base backup's start, so it is recorded now, and the data that
  // must be excluded is written after it.
  const target = primary('select clock_timestamp()').trim();

  step(`Writing the post-target state (target ${target})`);
  primary(`insert into ops.restore_drill_sentinel (label) values ('after') on conflict (label) do nothing;`);

  step('Archiving the WAL past the target');
  primary('select pg_switch_wal();');
  await waitForArchive();

  step('Recovering into a fresh cluster at the target');
  compose(['--profile', 'restore', 'up', '-d', 'restore'], { env: { PITR_TARGET_TIME: target } });
  const promoted = await waitForPromotion();

  if (!promoted) {
    process.stderr.write(`${restoreLogs()}\n`);

    return false;
  }

  step('Applying the restored-environment adjustments (ADR-0008)');
  if (!restoreScript('/sanitize.sql')) {
    return false;
  }

  step('Running the integrity checks');
  if (!restoreScript('/checks.sql')) {
    return false;
  }

  step('Done');
  process.stdout.write(
    `\nThe restored cluster promoted at ${target}, its integrity checks passed, and the post-target `
    + 'marker was correctly absent.\n',
  );

  return true;
}

/** Waits until the archiver has caught up and has no failed segment. */
async function waitForArchive() {
  for (let attempt = 0; attempt < 60; attempt++) {
    const archived = primary(
      "select (last_failed_wal is null and last_archived_time > now() - interval '20 seconds') from pg_stat_archiver;",
    ).trim();

    if (archived === 't') {
      return;
    }

    await sleep(1000);
  }

  throw new Error('the WAL archive did not catch up');
}

/** Waits until the restored cluster has finished recovery and is serving. */
async function waitForPromotion() {
  for (let attempt = 0; attempt < 60; attempt++) {
    const state = spawnSync('docker', ['exec', RESTORE, 'pg_isready', '-U', 'touchline_app', '-d', 'touchline'], { encoding: 'utf8' });

    if (state.status === 0) {
      const recovering = spawnSync('docker', ['exec', RESTORE, 'psql', '-U', 'touchline_app', '-d', 'touchline', '-tAc', 'select pg_is_in_recovery()'], { encoding: 'utf8' });

      if (recovering.stdout.trim() === 'f') {
        return true;
      }
    }

    if (containerExited()) {
      return false;
    }

    await sleep(1000);
  }

  return false;
}

/** Runs SQL in the primary and returns stdout. */
function primary(sql) {
  const result = docker(['exec', PRIMARY, 'psql', '-U', 'touchline_app', '-d', 'touchline', '-tAc', sql]);

  return result.stdout;
}

/** Runs a SQL script in the restored cluster, showing its output, and returns whether it succeeded. */
function restoreScript(file) {
  const result = docker(['exec', RESTORE, 'psql', '-U', 'touchline_app', '-d', 'touchline', '-v', 'ON_ERROR_STOP=1', '-f', file]);

  if (result.stdout) {
    process.stdout.write(result.stdout);
  }

  if (result.stderr) {
    process.stderr.write(result.stderr);
  }

  return result.ok;
}

function restoreLogs() {
  return spawnSync('docker', ['logs', '--tail', '40', RESTORE], { encoding: 'utf8' }).stdout;
}

function containerExited() {
  const state = spawnSync('docker', ['inspect', '-f', '{{.State.Running}}', RESTORE], { encoding: 'utf8' });

  return state.status !== 0 || state.stdout.trim() === 'false';
}

/** Prints a labelled step. */
function step(message) {
  process.stdout.write(`\n== ${message}\n`);
}

/** Runs docker and returns the captured result. */
function docker(args) {
  return run('docker', args, { capture: true });
}

/** Runs docker compose with the drill's environment merged in. */
function compose(args, options = {}) {
  return run('docker', [...COMPOSE, ...args], options);
}

/** Runs a command, inheriting stdio unless the output is captured. */
function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    env: { ...process.env, ...(options.env ?? {}) },
    stdio: options.capture ? 'pipe' : 'inherit',
  });

  if (result.error) {
    throw new Error(`${command} could not be run: ${result.error.message}`);
  }

  if (!options.capture && result.status !== 0) {
    throw new Error(`${command} ${args.slice(0, 2).join(' ')} exited with ${result.status}`);
  }

  return { ok: result.status === 0, stdout: result.stdout ?? '', stderr: result.stderr ?? '' };
}

/** Runs a shell one-liner (used for the archive-volume setup). */
function sh(command) {
  execFileSync(command, { cwd: repositoryRoot, stdio: 'inherit', shell: true });
}

function cleanup() {
  spawnSync('docker', [...COMPOSE, '--profile', 'restore', 'down', '-v', '--remove-orphans'], {
    cwd: repositoryRoot,
    stdio: 'ignore',
  });

  // The archive volume holds a base backup; a stale one would make the next run's pg_basebackup refuse
  // a non-empty target, so it is removed explicitly rather than left to Compose.
  spawnSync('docker', ['volume', 'rm', '-f', 'touchline-pitr-archive'], { stdio: 'ignore' });
}

function sleep(milliseconds) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}
