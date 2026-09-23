/**
 * Runtime configuration.
 *
 * The API base URL is relative by default so that the PWA works from the same origin in production
 * (Cloudflare proxies `/api` to the API service, ADR-0008) and relies on the dev-server proxy
 * locally. Keeping it relative also avoids a build-time environment swap for the same reason.
 */
export const appEnvironment = {
  apiBaseUrl: '/api/v1',

  /** Polling cadence for `/sync` while the tab is visible (ADR-0007). */
  syncIntervalMs: 60_000,

  /** Cadence used near a deadline, where freshness is worth the extra requests. */
  syncIntervalNearDeadlineMs: 15_000,
} as const;
