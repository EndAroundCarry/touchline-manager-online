/**
 * The throwaway database the market journey trades in.
 *
 * Like the matchday journey's, it gets its own database, reset and reseeded by its global setup every run:
 * the journey resolves an auction, and a resolved auction permanently moves a player between two clubs, so
 * it cannot share the persistent world the other journeys read.
 */
export const marketDatabase = process.env['E2E_MARKET_DATABASE'] ?? 'touchline_e2e_market';

/**
 * The connection string the setup and the stack both use.
 *
 * Derived once here rather than written twice, so the migrations, the seed, the API, and the worker can
 * never disagree about which database the journey is trading in.
 */
export const marketConnectionString =
  process.env['E2E_MARKET_CONNECTION_STRING'] ??
  `Host=localhost;Port=55432;Database=${marketDatabase};Username=touchline_app;Password=local_dev_password_change_me`;
