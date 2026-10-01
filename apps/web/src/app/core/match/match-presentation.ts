import { CommentaryLine, Highlight, MatchStatistics } from './match.models';

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
  woodwork: 'Woodwork',
  saved: 'Saved',
  blocked: 'Blocked',
  off_target: 'Off target',
  chance: 'Chance',
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

/** Names a highlight's outcome, falling back to the code. */
export function outcomeLabel(code: string): string {
  return OUTCOME_LABELS[code] ?? code;
}

/** Whether an outcome code means a goal, which is what the score reconciles against (`MAT-5`). */
export function isGoalOutcome(code: string): boolean {
  return GOAL_OUTCOMES.has(code);
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

/** A one-line title for a highlight, e.g. `Goal — 67'`. */
export function highlightTitle(highlight: Highlight): string {
  return `${outcomeLabel(highlight.outcomeCode)} \u2014 ${matchClockLabel(highlight.minute, highlight.stoppageMinute)}`;
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

/**
 * The highlight a commentary line can be shown as, or -1 when the line has none.
 *
 * A line narrates an event; only some events are worth replaying (§9.2), so the timeline offers "show me
 * this" only where there is something to show rather than a button that does nothing.
 */
export function highlightIndexForLine(
  highlights: readonly Highlight[],
  line: CommentaryLine,
): number {
  return highlights.findIndex((highlight) => highlight.sourceEventSequence === line.sequence);
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

