import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';
import { marketConnectionString } from './support/market-database';

/**
 * The stack the end-to-end seller/bidder/outbid/winner journey runs against (master plan §15.5 journey 4,
 * §16 Stage 10).
 *
 * It follows the matchday stack's design and differs in the same three ways:
 *
 *   1. It starts the **worker**, because only the worker settles an auction (ADR-0001, ADR-0003).
 *   2. It points at a **throwaway database**, reset and reseeded each run, because a resolved auction
 *      permanently moves a player between two clubs.
 *   3. It enables the **auction trigger** — the non-production diagnostic that enqueues a listing's real
 *      resolution job (ADR-0016) — so the auction settles on demand rather than at its daily window.
 *
 * The worker's own schedulers are switched off: the AI market would otherwise bid against the managers the
 * journey is driving, and the auction scheduler would pre-create the resolution row with its real deadline,
 * turning the trigger's due-now enqueue into a no-op. Both are the same reasoning the matchday stack applies
 * to its own scheduler.
 *
 * The ports are shared with the other configs on purpose: the suites never run at the same time, and sharing
 * them keeps the dev-server proxy and CORS settings valid. `reuseExistingServer: false` means a stray
 * `npm run dev` stack fails loudly rather than letting the journey trade in the wrong database.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..');

const isContinuousIntegration = process.env['CI'] === 'true';

export default defineConfig({
  testDir: './market',
  outputDir: './.artifacts-market',

  // One worker, as in the other configs: the journey mutates the world it is given.
  workers: 1,
  fullyParallel: false,

  forbidOnly: isContinuousIntegration,
  retries: isContinuousIntegration ? 1 : 0,
  timeout: 240_000,
  expect: { timeout: 15_000 },

  reporter: isContinuousIntegration
    ? [['github'], ['html', { open: 'never', outputFolder: 'playwright-report-market' }]]
    : [['list']],

  // Applied before the servers start, so the stack always finds a freshly seeded database.
  globalSetup: './support/market-global-setup.ts',

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
        ConnectionStrings__Database: marketConnectionString,
        // The trigger is what lets the auction settle without waiting for its window (ADR-0016).
        Diagnostics__EnableMatchdayTrigger: 'true',
      },
    },
    {
      // The worker has no HTTP surface, so this entry has no `url` and no `port`: Playwright starts the
      // process and moves on rather than waiting for a readiness it cannot observe. The journey polls for the
      // completed transfer, so a worker that is a second late costs a second and nothing else.
      command: 'dotnet run --project apps/worker/TouchlineManager.Worker',
      cwd: repositoryRoot,
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        DOTNET_ENVIRONMENT: 'Development',
        ConnectionStrings__Database: marketConnectionString,
        // The scheduler must not pre-create the resolution row, or the trigger's due-now enqueue would be an
        // idempotent no-op and nothing would settle.
        Auctions__EnableAuctions: 'false',
        // And the AI market must not bid against the managers this journey is driving.
        AiMarket__EnableEvaluation: 'false',
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
