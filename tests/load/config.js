// Shared configuration for the Stage 14 load suite (master plan §14 SLOs, §16 Stage 14).
//
// The population model is the MVP world itself. Six countries of eighteen clubs is 108 clubs, so one
// human manager per club is 108 concurrent users; the stage requires three times headroom, which is
// 324 virtual users. Every scenario scales from these numbers rather than hard-coding them, so a
// change to the model moves the whole suite.
//
// `LOAD_SMOKE=1` shrinks every scenario and window so the scripts can be checked in seconds against a
// small seeded world. A smoke run proves the plumbing, not the SLOs.

export const POPULATION = {
  countries: 6,
  clubsPerCountry: 18,
  managers: 108,
  headroom: 3,
  virtualUsers: 324,
};

// The service-level objectives the run is measured against (master plan §14).
export const SLO = {
  readP95Ms: 500,
  commandP95Ms: 800,
  errorRate: 0.001,
};

export const SMOKE = __ENV.LOAD_SMOKE === '1';

/** The steady-state window a scenario runs for. */
export const WINDOW = SMOKE ? '5s' : (__ENV.LOAD_WINDOW || '2m');

/** Scales a virtual-user count down for a smoke run. */
export function vus(count) {
  return SMOKE ? Math.max(1, Math.round(count / 50)) : count;
}

/** Scales a request rate down for a smoke run. */
export function rate(perSecond) {
  return SMOKE ? Math.max(1, Math.round(perSecond / 10)) : perSecond;
}

/** The account pool the seeder wrote. Every read-only scenario authenticates as these. */
export const credentials = JSON.parse(open('./.artifacts/credentials.json'));
