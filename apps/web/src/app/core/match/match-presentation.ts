import { CommentaryLine, MatchStatistics, Passage } from './match.models';

/**
 * Client-side presentation helpers for the match center.
 *
 * Small and pure, so they can be unit tested without a component and without a server. The server sends
 * *codes* and the screen turns them into words; every lookup falls back to the raw code rather than showing
 * nothing when a new one appears, exactly as the competition helpers do.
 */

const OUTCOME_LABELS: Record<string, string> = {
  goal: 'Goal',
  penalty_goal: 'Penalty goal',
  penalty_missed: 'Penalty missed',
  free_kick_shot: 'Free kick',
  woodwork: 'Woodwork',
  saved: 'Saved',
  blocked: 'Blocked',
  off_target: 'Off target',
  chance: 'Chance',
  play: 'Play',
};

/** The outcome codes that mean the ball finished in the net. */
const GOAL_OUTCOMES = new Set(['goal', 'penalty_goal']);

/**
 * A match minute as it is written on a scoreboard: `67'`, or `90+3'` in stoppage time.
 *
 * The stoppage minute is a separate fact rather than folded into the minute, because "the third minute of
 * stoppage" and "the ninety-third minute" are different moments to a reader even when the clock agrees.
 */
export function matchClockLabel(minute: number, stoppageMinute: number): string {
  return stoppageMinute > 0 ? `${minute}+${stoppageMinute}'` : `${minute}'`;
}

/** Regulation time, in minutes, that the continuous clock counts up to before stoppage. */
const REGULATION_MINUTES = 90;

/**
 * A continuous scoreboard label for a match second (`replay-v3`).
 *
 * The engine records a match second as `(minute + stoppage) * 60`, so a passage's window can be walked
 * continuously: the label ticks over as the film plays rather than jumping at each passage boundary. Past
 * ninety minutes the label becomes `90+N'`, which is how a manager reads stoppage time.
 */
export function matchClockFromSeconds(matchSecond: number): {
  readonly minute: number;
  readonly stoppageMinute: number;
} {
  const regulationSeconds = REGULATION_MINUTES * 60;

  if (matchSecond >= regulationSeconds) {
    return {
      minute: REGULATION_MINUTES,
      stoppageMinute: Math.floor((matchSecond - regulationSeconds) / 60) + 1,
    };
  }

  return { minute: Math.max(1, Math.floor(Math.max(0, matchSecond) / 60) + 1), stoppageMinute: 0 };
}

/** A playback position as a clock, e.g. `1:05` or `10:35`. */
export function playbackClockLabel(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1_000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

/** Names a passage's outcome, falling back to the code. */
export function outcomeLabel(code: string): string {
  return OUTCOME_LABELS[code] ?? code;
}

/** Whether an outcome code means a goal, which is what the score reconciles against (`MAT-5`). */
export function isGoalOutcome(code: string): boolean {
  return GOAL_OUTCOMES.has(code);
}

/** The commentary template keys that report a goal. */
const GOAL_TEMPLATES = new Set(['match.goal', 'match.penalty.goal']);

/** Whether a commentary line reports a goal, which the report highlights and the scoreboard lists. */
export function isGoalCommentary(templateKey: string): boolean {
  return GOAL_TEMPLATES.has(templateKey);
}

/** Formats a published scoreline, e.g. `2–1`. */
export function scoreLine(home: number, away: number): string {
  return `${home}\u2013${away}`;
}

/** Formats a possession share, which travels as basis points. */
export function possessionPercent(basisPoints: number): string {
  return `${Math.round(basisPoints / 100)}%`;
}

/** Names which end a commentary line belongs to. */
export function commentarySideLabel(side: string): string {
  return side === 'home' ? 'Home' : side === 'away' ? 'Away' : side;
}

/** A one-line title for a passage, e.g. `Goal — 67'`. */
export function passageTitle(passage: Passage): string {
  return `${outcomeLabel(passage.outcomeCode)} \u2014 ${matchClockLabel(passage.minute, passage.stoppageMinute)}`;
}

/** One row of the statistics panel, with both sides already formatted. */
export interface MatchStatisticRow {
  readonly label: string;
  readonly home: string;
  readonly away: string;
}

/**
 * The statistics panel's rows, home first.
 *
 * Ordered the way a match report reads — the score, then possession, then the attacking counts, then
 * discipline — rather than the order the engine happens to declare its fields in.
 */
export function matchStatisticRows(
  home: MatchStatistics,
  away: MatchStatistics,
): readonly MatchStatisticRow[] {
  const row = (label: string, value: (side: MatchStatistics) => string): MatchStatisticRow => ({
    label,
    home: value(home),
    away: value(away),
  });

  const count = (pick: (side: MatchStatistics) => number) => (side: MatchStatistics) =>
    `${pick(side)}`;

  return [
    row(
      'Goals',
      count((side) => side.goals),
    ),
    row('Possession', (side) => possessionPercent(side.possessionBasisPoints)),
    row(
      'Shots',
      count((side) => side.shots),
    ),
    row(
      'On target',
      count((side) => side.shotsOnTarget),
    ),
    row(
      'Off target',
      count((side) => side.shotsOffTarget),
    ),
    row(
      'Blocked',
      count((side) => side.shotsBlocked),
    ),
    row(
      'Woodwork',
      count((side) => side.woodworkHits),
    ),
    row(
      'Saves',
      count((side) => side.saves),
    ),
    row(
      'Corners',
      count((side) => side.corners),
    ),
    row(
      'Offsides',
      count((side) => side.offsides),
    ),
    row(
      'Fouls',
      count((side) => side.fouls),
    ),
    row(
      'Yellow cards',
      count((side) => side.yellowCards),
    ),
    row(
      'Red cards',
      count((side) => side.redCards),
    ),
    row(
      'Penalties awarded',
      count((side) => side.penaltiesAwarded),
    ),
    row(
      'Penalties scored',
      count((side) => side.penaltiesScored),
    ),
    row(
      'Injuries',
      count((side) => side.injuries),
    ),
    row(
      'Substitutions',
      count((side) => side.substitutions),
    ),
  ];
}

/** The outcome codes that mean the ball was struck at goal. */
const SHOT_OUTCOMES = new Set([
  'goal',
  'penalty_goal',
  'penalty_missed',
  'free_kick_shot',
  'woodwork',
  'saved',
  'blocked',
  'off_target',
  'chance',
]);

/** The action tags the engine writes on the keyframe where the ball is struck at goal. */
const STRIKE_ACTIONS = new Set(['shot', 'penalty', 'free_kick']);

/** Whether an outcome code means a shot at goal, which is what the shot map plots. */
export function isShotOutcome(code: string): boolean {
  return SHOT_OUTCOMES.has(code);
}

/** One shot on the shot map: where it was struck from and who struck it. */
export interface ShotMapEntry {
  readonly sourceEventSequence: number;
  readonly side: 'home' | 'away';
  readonly x: number;
  readonly y: number;
  readonly outcomeCode: string;
  readonly minute: number;
  readonly stoppageMinute: number;
}

/**
 * Every shot worth plotting, derived from the film passages themselves.
 *
 * The shooter's own track carries a keyframe tagged with the strike, and its coordinates are the exact spot
 * the ball was struck from — so the map is the replay's own geometry rather than an estimate. A tagged
 * presentation is what `replay-v3` always sends; the fallback to where the ball came to rest exists only for
 * a vintage payload whose action tags are absent.
 *
 * The engine's axis is fixed: the home side attacks towards the higher X, so a fallback shot in the
 * defending half would be read as the wrong side. That is the honest trade — a map that omits a shot would
 * be emptier than one that can occasionally colour it wrong.
 */
export function shotMapEntries(passages: readonly Passage[]): readonly ShotMapEntry[] {
  const entries: ShotMapEntry[] = [];

  for (const passage of passages) {
    if (!isShotOutcome(passage.outcomeCode)) {
      continue;
    }

    const strike = strikePoint(passage);

    if (strike === null) {
      continue;
    }

    entries.push({
      sourceEventSequence: passage.sourceEventSequence,
      side: strike.side,
      x: strike.x,
      y: strike.y,
      outcomeCode: passage.outcomeCode,
      minute: passage.minute,
      stoppageMinute: passage.stoppageMinute,
    });
  }

  return entries;
}

/** Where a passage's shot was struck from, or null when nothing in it says. */
function strikePoint(passage: Passage): { side: 'home' | 'away'; x: number; y: number } | null {
  const sides = new Map<string, 'home' | 'away'>();

  for (const entity of passage.entities) {
    if (entity.side === 'home' || entity.side === 'away') {
      sides.set(entity.entityId, entity.side);
    }
  }

  for (const track of passage.tracks) {
    const side = sides.get(track.entityId);

    if (side === undefined) {
      continue;
    }

    const strike = track.keyframes.find(
      (keyframe) =>
        keyframe.action !== null &&
        keyframe.action !== undefined &&
        STRIKE_ACTIONS.has(keyframe.action),
    );

    if (strike !== undefined) {
      return { side, x: strike.x, y: strike.y };
    }
  }

  const ball = passage.tracks.find((track) => track.entityId === 'ball')?.keyframes.at(-1);

  return ball === undefined
    ? null
    : { side: ball.x >= 5_000 ? 'home' : 'away', x: ball.x, y: ball.y };
}

/** Returns the Football Manager style colour class for a player's condition, e.g. a green bar for a fresh player. */
export function conditionColorClass(basisPoints: number): string {
  const percent = Math.max(0, Math.min(100, basisPoints / 100));

  if (percent >= 75) {
    return 'bg-emerald-500';
  }

  if (percent >= 55) {
    return 'bg-lime-500';
  }

  if (percent >= 35) {
    return 'bg-amber-500';
  }

  return 'bg-rose-500';
}

/**
 * The passage a commentary line can be shown in, or -1 when the line has none.
 *
 * A line narrates an event; a passage carries the events it produced, so the report offers "watch" only
 * where a passage actually presents the event rather than a button that does nothing.
 */
export function passageIndexForLine(passages: readonly Passage[], line: CommentaryLine): number {
  return passages.findIndex(
    (passage) =>
      passage.sourceEventSequence === line.sequence ||
      passage.eventSequences.includes(line.sequence),
  );
}

/** The commentary template keys that report a booking, and the card each one means. */
const CARD_TEMPLATES: Record<string, 'yellow' | 'red'> = {
  'match.card.yellow': 'yellow',
  'match.card.second_yellow': 'red',
  'match.card.red': 'red',
};

/**
 * The card a commentary line reports, or null when it reports no booking.
 *
 * The pitch draws a booking above the offending player's token from the same timeline the report reads, so
 * the two can never disagree about who was booked or when.
 */
export function cardKindFor(templateKey: string): 'yellow' | 'red' | null {
  return CARD_TEMPLATES[templateKey] ?? null;
}

/** Formats a player's rating in basis points (e.g., 7200 -> "7.2"). */
export function formatMatchRating(basisPoints: number): string {
  if (basisPoints <= 0) {
    return '-';
  }

  return (basisPoints / 1000).toFixed(1);
}

/** Formats player condition as percentage (e.g., 9500 -> "95%"). */
export function formatConditionPercent(basisPoints: number): string {
  const percent = Math.round(Math.max(0, Math.min(10000, basisPoints)) / 100);

  return `${percent}%`;
}

/** Returns the Football Manager style color badge class for a player rating. */
export function ratingColorClass(basisPoints: number): string {
  if (basisPoints <= 0) {
    return 'bg-slate-800 text-slate-400 border-slate-700';
  }

  const rating = basisPoints / 1000;

  if (rating >= 8.0) {
    return 'bg-emerald-500/20 text-emerald-400 border-emerald-500/40 font-semibold';
  }

  if (rating >= 7.0) {
    return 'bg-green-500/20 text-green-400 border-green-500/40';
  }

  if (rating >= 6.5) {
    return 'bg-yellow-500/20 text-yellow-300 border-yellow-500/40';
  }

  if (rating >= 6.0) {
    return 'bg-amber-500/20 text-amber-300 border-amber-500/40';
  }

  return 'bg-rose-500/20 text-rose-400 border-rose-500/40';
}
