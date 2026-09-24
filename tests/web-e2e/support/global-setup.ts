import { execFileSync } from 'node:child_process';
import path from 'node:path';

/**
 * Prepares the backing services before any server starts.
 *
 * Playwright runs this before it launches the `webServer` entries, which is the only ordering that
 * works: the API cannot serve a request until PostgreSQL is listening and the schema is current.
 *
 * Both steps are idempotent, so running them against an already-healthy local stack costs a few
 * seconds and never destroys anything.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..', '..');

export default function globalSetup(): void {
  // PostgreSQL and the mail catcher, waiting for their health checks so the API's first connection
  // does not race the database's startup.
  run('docker', ['compose', '-f', 'infra/compose.yaml', 'up', '-d', '--wait']);

  // The EF tool is a local tool (see .config/dotnet-tools.json), so restore it before invoking it.
  run('dotnet', ['tool', 'restore']);

  run('dotnet', [
    'dotnet-ef',
    'database',
    'update',
    '--project',
    'src/TouchlineManager.Infrastructure',
    '--startup-project',
    'src/TouchlineManager.Infrastructure',
  ]);
}

function run(command: string, args: readonly string[]): void {
  // No shell: `docker` and `dotnet` are real executables on every supported platform, and spawning
  // them directly avoids the argument-quoting hazards that come with passing through a shell.
  execFileSync(command, [...args], { cwd: repositoryRoot, stdio: 'inherit' });
}
