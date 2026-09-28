import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { marketConnectionString } from './market-database';

/**
 * Prepares a freshly seeded, throwaway world for the market journey.
 *
 * The market journey resolves an auction, and a resolved auction permanently moves a player between two
 * clubs, so it cannot run against the shared world the other journeys read. It resets its own database every
 * run instead, which means the journey always begins from a clean, fully-staffed market and can be run
 * repeatedly.
 *
 * Every step is idempotent against an already-healthy stack, so a partial local run costs a few seconds and
 * never touches the shared database.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..', '..');

export default function globalSetup(): void {
  // PostgreSQL and the mail catcher, waiting for their health checks (the API needs the former, and
  // registration needs the latter).
  run('docker', ['compose', '-f', 'infra/compose.yaml', 'up', '-d', '--wait']);

  // The EF tool is a local tool (see .config/dotnet-tools.json), so restore it before invoking it.
  run('dotnet', ['tool', 'restore']);

  const marketEnvironment = { ConnectionStrings__Database: marketConnectionString };

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
    marketEnvironment,
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
    marketEnvironment,
  );

  run('dotnet', ['run', '--project', 'tools/world-seeder'], marketEnvironment);
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
