import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

/**
 * The end-to-end stack.
 *
 * A journey is only worth running against the real thing, so this config starts the real API over
 * the real database and the real web client, and drives them through a browser. Nothing is stubbed:
 * a journey that passes here has passed through the same code paths a manager will use.
 *
 * The suite owns its stack so that one command is enough locally and in CI. `reuseExistingServer`
 * keeps `npm run dev` sessions usable during development instead of fighting them for the ports.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..');

const isContinuousIntegration = process.env['CI'] === 'true';

export default defineConfig({
  testDir: './journeys',
  outputDir: './.artifacts',

  // One worker, deliberately. Every test shares one database and one mail catcher, and a journey
  // that read another journey's message from the mailbox would fail for a reason that is not real.
  workers: 1,
  fullyParallel: false,

  forbidOnly: isContinuousIntegration,
  retries: isContinuousIntegration ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },

  reporter: isContinuousIntegration ? [['github'], ['html', { open: 'never' }]] : [['list']],

  // Applied before the servers start, so the API always finds a migrated database.
  globalSetup: './support/global-setup.ts',

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
      reuseExistingServer: !isContinuousIntegration,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        // Development, so the API reads the same local configuration the developer does.
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: 'http://localhost:5080',
        // The suite signs in far more often from one address than a person would. The limiter is
        // a security control worth testing, but it is tested by the API integration tests, which
        // can pick their own permit limit; here it would only produce flaky failures.
        RateLimiting__AuthPermitLimit: '5000',
      },
    },
    {
      command: 'npm --prefix apps/web start',
      cwd: repositoryRoot,
      url: 'http://localhost:4200',
      reuseExistingServer: !isContinuousIntegration,
      timeout: 240_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
