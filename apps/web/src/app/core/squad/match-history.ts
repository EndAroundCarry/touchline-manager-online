/**
 * One match in a player's per-match statistics table.
 *
 * PLACEHOLDER: the server does not yet expose a player's match-by-match lines (it stores them inside each
 * match's statistics document and only sums them into the season line), and the engine does not count passes
 * at all. {@link sampleMatchStats} stands in with deterministic dummy rows so the Statistics tab can be built
 * and reviewed. When a real read exists the tab keeps this shape and swaps the source.
 */
export interface PlayerMatchStatRow {
  /** A stable key for the row. */
  readonly id: string;

  /** The matchday round. */
  readonly round: number;

  /** The opponent's name. */
  readonly opponent: string;

  /** Whether the match was played at home. */
  readonly home: boolean;

  /** The player's side's goals. */
  readonly goalsFor: number;

  /** The opposing side's goals. */
  readonly goalsAgainst: number;

  readonly minutes: number;
  readonly goals: number;
  readonly assists: number;
  readonly passesCompleted: number;
  readonly passesAttempted: number;
  readonly shots: number;
  readonly shotsOnTarget: number;
  readonly saves: number;
  readonly yellowCards: number;
  readonly redCards: number;

  /** The match rating on a 0.0–10.0 scale. */
  readonly rating: number;
}

const OPPONENTS = [
  'Ashvale United',
  'Vale Rovers',
  'Northgate Town',
  'Harbour City',
  'Millbrook Athletic',
  'Stonebridge',
  'Kingsmere',
  'Redfield Wanderers',
  'Eastwick',
  'Oakham Rangers',
  'Fairhaven',
  'Dunmore Albion',
];

/** A small stable generator, so a player always gets the same sample rows. */
function generator(text: string): () => number {
  let state = 2166136261;

  for (let index = 0; index < text.length; index++) {
    state = Math.imul(state ^ text.charCodeAt(index), 16777619) >>> 0;
  }

  return () => {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;

    return state / 4294967296;
  };
}

/**
 * Builds deterministic, clearly-sample match rows for a player, newest first.
 *
 * @param playerId The player's identity.
 * @param seasonKey The season the rows belong to, so each season differs.
 * @param matches How many rows to produce, normally the player's appearances.
 */
export function sampleMatchStats(
  playerId: string,
  seasonKey: string,
  matches: number,
): readonly PlayerMatchStatRow[] {
  const next = generator(`${playerId}|${seasonKey}`);
  const rows: PlayerMatchStatRow[] = [];

  for (let round = 1; round <= matches; round++) {
    const minutes = next() < 0.8 ? 90 : 45 + Math.floor(next() * 40);
    const attempted = 15 + Math.floor(next() * 45);
    const shots = Math.floor(next() * 4);
    const goalsFor = Math.floor(next() * 4);
    const goalsAgainst = Math.floor(next() * 3);

    rows.push({
      id: `${playerId}-${seasonKey}-${round}`,
      round,
      opponent: OPPONENTS[Math.floor(next() * OPPONENTS.length)],
      home: next() < 0.5,
      goalsFor,
      goalsAgainst,
      minutes,
      goals: next() < 0.12 ? 1 : 0,
      assists: next() < 0.1 ? 1 : 0,
      passesCompleted: Math.floor(attempted * (0.65 + next() * 0.3)),
      passesAttempted: attempted,
      shots,
      shotsOnTarget: Math.floor(shots * next()),
      saves: 0,
      yellowCards: next() < 0.1 ? 1 : 0,
      redCards: next() < 0.01 ? 1 : 0,
      rating: Math.round((5.5 + next() * 3.5) * 10) / 10,
    });
  }

  return rows.reverse();
}
