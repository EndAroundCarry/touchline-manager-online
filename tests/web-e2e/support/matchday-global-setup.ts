import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { matchdayConnectionString } from './matchday-database';

/**
 * Prepares a freshly seeded, throwaway world for the matchday journey.
 *
 * The main suite's setup leaves the shared world alone; this one resets its own database every run, because
 * the journey plays a round and a played round is permanent. Resetting means the journey always begins from an
 * unplayed season, so it can be run repeatedly without the shared world ever being consumed and without the
 * suite failing after enough runs for a reason that is not a defect.
 *
 * Every step is idempotent against an already-healthy stack, so a partial local run costs a few seconds and
 * never destroys the shared database.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..', '..');

export default function globalSetup(): void {
  // PostgreSQL and the mail catcher, waiting for their health checks (the worker and the API both need the
  // former, and registration needs the latter).
  run('docker', ['compose', '-f', 'infra/compose.yaml', 'up', '-d', '--wait']);

  // The EF tool is a local tool (see .config/dotnet-tools.json), so restore it before invoking it.
  run('dotnet', ['tool', 'restore']);

  // Drop and recreate the throwaway database. `drop --force` on a database that does not exist is a no-op,
  // and `update` creates it and applies every migration, so the journey always starts from the committed
  // schema. The connection string is passed in the environment so the same value reaches the EF host that
  // the API and worker will read.
  const matchdayEnvironment = { ConnectionStrings__Database: matchdayConnectionString };

  run(
    'dotnet',
    [
      'dotnet-ef',
      'database',
      'drop',
      '--force',
      '--project',
      'src/TouchlineManager.Infrastructure',
      '--startup-project',
      'src/TouchlineManager.Infrastructure',
    ],
    matchdayEnvironment,
  );

  run(
    'dotnet',
    [
      'dotnet-ef',
      'database',
      'update',
      '--project',
      'src/TouchlineManager.Infrastructure',
      '--startup-project',
      'src/TouchlineManager.Infrastructure',
    ],
    matchdayEnvironment,
  );

  // A world to onboard into: six countries, one 18-club tier each, squads, and the fixture calendar. Fresh
  // from the recreate above, so this seeds rather than reporting an existing world.
  run('dotnet', ['run', '--project', 'tools/world-seeder'], matchdayEnvironment);
}

function run(command: string, args: readonly string[], env?: Readonly<Record<string, string>>): void {
  // No shell: `docker` and `dotnet` are real executables on every supported platform, and spawning them
  // directly avoids the argument-quoting hazards that come with passing through a shell.
  execFileSync(command, [...args], {
    cwd: repositoryRoot,
    stdio: 'inherit',
    env: env ? { ...process.env, ...env } : process.env,
  });
}
