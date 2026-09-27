/**
 * The throwaway database the matchday journey plays in.
 *
 * The main journeys share one persistent world and never advance it; they give their club back rather than
 * consume it. The matchday journey is different: watching a fixture end to end means playing a round, and a
 * played round permanently advances a season. It therefore gets its own database, reset and reseeded by its
 * global setup every run, so the shared world can never be exhausted by repeated local runs.
 */
export const matchdayDatabase = process.env['E2E_MATCHDAY_DATABASE'] ?? 'touchline_e2e_matchday';

/**
 * The connection string the setup and the stack both use.
 *
 * It is derived once here rather than written twice, so the migrations, the seed, the API, and the worker
 * can never disagree about which database the journey is playing in.
 */
export const matchdayConnectionString =
  process.env['E2E_MATCHDAY_CONNECTION_STRING'] ??
  `Host=localhost;Port=55432;Database=${matchdayDatabase};Username=touchline_app;Password=local_dev_password_change_me`;
