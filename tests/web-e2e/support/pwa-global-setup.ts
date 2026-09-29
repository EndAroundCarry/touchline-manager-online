import { execFileSync, execSync } from 'node:child_process';
import path from 'node:path';

/**
 * Prepares the PWA journey's stack.
 *
 * The database work is the main suite's exactly — the shared world, migrated and seeded — because the
 * journey only reads it. What this setup adds is the **production build** of the web client: the service
 * worker is disabled in development, so the app served by the other configs cannot register one, and the
 * offline and install assertions would have nothing to test.
 *
 * Every step is idempotent, so a partial local run costs a few seconds and never destroys anything.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..', '..');

export default function globalSetup(): void {
  // PostgreSQL and the mail catcher, waiting for their health checks (the API needs both).
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

  // Onboarding needs a world; the seeder is idempotent, so this is a no-op against an existing one.
  run('dotnet', ['run', '--project', 'tools/world-seeder']);

  // The production bundle, which emits `ngsw-worker.js` and `ngsw.json`. `npm` is a shell shim on Windows
  // (`npm.cmd`), which a direct spawn cannot execute, so it goes through the shell as one command string.
  execSync('npm --prefix apps/web run build', { cwd: repositoryRoot, stdio: 'inherit' });
}

function run(command: string, args: readonly string[]): void {
  execFileSync(command, [...args], { cwd: repositoryRoot, stdio: 'inherit' });
}
