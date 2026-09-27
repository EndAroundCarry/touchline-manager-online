import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';
import { matchdayConnectionString } from './support/matchday-database';

/**
 * The stack the end-to-end "prepare and watch" journey runs against (master plan §15.5 journey 3).
 *
 * It is deliberately separate from the main config, and it differs in three ways:
 *
 *   1. It starts the **worker**, which the main stack does not, because watching a fixture end to end means
 *      a real round has to play, and only the worker advances a matchday (ADR-0001, ADR-0003).
 *   2. It points at a **throwaway database**, reset and reseeded each run, because the journey plays a
 *      round and a played round permanently advances a season.
 *   3. It enables the **matchday trigger** — a non-production diagnostic that enqueues a round's real lock
 *      and resolution jobs (ADR-0016) — so the round plays on demand rather than days from now.
 *
 * The ports are shared with the main config on purpose: the two suites never run at the same time (separate
 * CI steps; one at a time locally), and sharing them keeps the existing dev-server proxy and CORS settings
 * valid. `reuseExistingServer: false` means a stray `npm run dev` stack fails loudly rather than letting the
 * journey test the wrong database.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..');

const isContinuousIntegration = process.env['CI'] === 'true';

export default defineConfig({
  testDir: './matchday',
  outputDir: './.artifacts-matchday',

  // One worker, as in the main config: the journey mutates the shared world it is given.
  workers: 1,
  fullyParallel: false,

  forbidOnly: isContinuousIntegration,
  retries: isContinuousIntegration ? 1 : 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },

  reporter: isContinuousIntegration
    ? [['github'], ['html', { open: 'never', outputFolder: 'playwright-report-matchday' }]]
    : [['list']],

  // Applied before the servers start, so the stack always finds a freshly seeded database.
  globalSetup: './support/matchday-global-setup.ts',

  use: {
    baseURL: process.env['E2E_WEB_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  webServer: [
    {
      command: 'dotnet run --project apps/api/TouchlineManager.Api',
      cwd: repositoryRoot,
      url: 'http://localhost:5080/health/live',
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: 'http://localhost:5080',
        // The suite signs in far more often from one address than a person would (see the main config).
        RateLimiting__AuthPermitLimit: '5000',
        ConnectionStrings__Database: matchdayConnectionString,
        // The trigger is what lets the round play without waiting for its calendar deadline (ADR-0016).
        Diagnostics__EnableMatchdayTrigger: 'true',
      },
    },
    {
      // The worker has no HTTP surface, so this entry has no `url` and no `port`: Playwright starts the
      // process and moves on rather than waiting for a readiness it cannot observe. The journey polls for
      // the published result, so a worker that is a second late costs a second and nothing else.
      command: 'dotnet run --project apps/worker/TouchlineManager.Worker',
      cwd: repositoryRoot,
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        DOTNET_ENVIRONMENT: 'Development',
        ConnectionStrings__Database: matchdayConnectionString,
        // The trigger supplies the round's jobs, due now. The scheduler must not pre-create them with their
        // real deadlines, or the trigger's due-now enqueue would be an idempotent no-op and nothing would play.
        Matchday__EnableMatchdayWorker: 'false',
        Worker__PollIntervalSeconds: '1',
        Worker__MaxIdlePollIntervalSeconds: '1',
      },
    },
    {
      command: 'npm --prefix apps/web start',
      cwd: repositoryRoot,
      url: 'http://localhost:4200',
      reuseExistingServer: false,
      timeout: 240_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
