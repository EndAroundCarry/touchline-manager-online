import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

/**
 * The stack the PWA journey runs against (master plan §15.5, §16 Stage 13, `F-45`).
 *
 * The main config serves the app with `ng serve`, where the service worker is deliberately disabled
 * (`enabled: !isDevMode()`), so no journey there can exercise install, offline reads, or the update path.
 * This stack builds the **production** web bundle and serves it from a static origin that proxies `/api`
 * to the API, which is the only configuration in which the worker registers.
 *
 * It is separate for the same reason the matchday stack is: it needs a different server (the production
 * build, not the dev server) and a different port, and it never runs at the same time as the others.
 */

const repositoryRoot = path.resolve(__dirname, '..', '..');

const isContinuousIntegration = process.env['CI'] === 'true';

export default defineConfig({
  testDir: './pwa',
  outputDir: './.artifacts-pwa',

  // One worker: the journey signs in and reads the shared world.
  workers: 1,
  fullyParallel: false,

  forbidOnly: isContinuousIntegration,
  retries: isContinuousIntegration ? 1 : 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },

  reporter: isContinuousIntegration
    ? [['github'], ['html', { open: 'never', outputFolder: 'playwright-report-pwa' }]]
    : [['list']],

  // Applied before the servers start: the database is migrated and seeded, and the production bundle is
  // built (the worker only exists in a production build).
  globalSetup: './support/pwa-global-setup.ts',

  use: {
    baseURL: process.env['E2E_PWA_WEB_URL'] ?? 'http://localhost:4201',
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
      },
    },
    {
      // The production bundle, from a static origin that proxies `/api` to the API above. The service
      // worker is only enabled here, which is what makes the offline assertions meaningful.
      command: 'node tests/web-e2e/support/pwa-server.mjs',
      cwd: repositoryRoot,
      url: 'http://localhost:4201/index.html',
      reuseExistingServer: false,
      timeout: 60_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        PWA_PORT: '4201',
        PWA_API_ORIGIN: 'http://localhost:5080',
      },
    },
  ],
});
