/**
 * Client-side presentation helpers for the competition module.
 *
 * Small and pure, so they can be unit tested without a component and without a server. The server already
 * sends the *codes* these turn into words, and each falls back to the raw code rather than showing nothing
 * when a new one appears.
 */

const FIXTURE_STATUS_LABELS: Record<string, string> = {
  scheduled: 'Scheduled',
  locked: 'Locked',
  simulating: 'Under way',
  staged: 'Awaiting publication',
  published: 'Played',
  void: 'Void',
};

const OUTCOME_LABELS: Record<string, string> = {
  win: 'Win',
  draw: 'Draw',
  loss: 'Loss',
};

const TEAM_SHEET_ISSUE_MESSAGES: Record<string, string> = {
  TEAM_SHEET_SLOT_NUMBER: 'A slot number is outside the range a side may use.',
  TEAM_SHEET_DUPLICATE_SLOT_NUMBER: 'Two entries name the same slot.',
  TEAM_SHEET_DUPLICATE_PLAYER: 'The same player is named twice.',
  TEAM_SHEET_PLAYER_NOT_ELIGIBLE: 'That player is not available to select for this club.',
  TEAM_SHEET_PLAYER_UNAVAILABLE: 'That player is injured or suspended.',
  TEAM_SHEET_INCOMPLETE: 'Pick all eleven starters before saving.',
  TEAM_SHEET_TOO_MANY_SUBSTITUTES: 'Name at most seven substitutes.',
};

/**
 * Words for the table's tie-break criteria, in the order the server lists them (`TBL-2`…`TBL-10`).
 *
 * The ordering is the server's; only the English is here, so the screen renders the sequence it is given
 * rather than a second, drifting copy of the rule.
 */
const TIE_BREAKER_LABELS: Record<string, string> = {
  points: 'Points',
  goal_difference: 'Goal difference',
  goals_scored: 'Goals scored',
  wins: 'Wins',
  head_to_head_points: 'Head-to-head points among the tied clubs',
  head_to_head_goal_difference: 'Head-to-head goal difference among the tied clubs',
  fewer_red_cards: 'Fewer red cards',
  fewer_yellow_cards: 'Fewer yellow cards',
  draw_key: 'The season draw, stored before the season began',
};

/** Names a fixture's lifecycle state, falling back to the code. */
export function fixtureStatusLabel(code: string): string {
  return FIXTURE_STATUS_LABELS[code] ?? code;
}

/** Names the manager's club's side: `home` or `away`, falling back to the code. */
export function venueLabel(venue: string): string {
  return venue === 'home' ? 'Home' : venue === 'away' ? 'Away' : venue;
}

/** Names a published result's outcome for the manager's club, falling back to the code. */
export function outcomeLabel(outcome: string | null): string {
  return outcome === null ? '' : (OUTCOME_LABELS[outcome] ?? outcome);
}

/** Formats a published scoreline, or an empty string while there is no result (`MAT-7`). */
export function scoreLabel(home: number | null, away: number | null): string {
  return home === null || away === null ? '' : `${home}\u2013${away}`;
}

/** A round's label, e.g. `Round 14`. */
export function roundLabel(roundNumber: number): string {
  return `Round ${roundNumber}`;
}

/**
 * Formats a goal difference, always signed (`TBL-3`).
 *
 * A positive difference reads `+7` rather than `7`, because the sign is the message. It is text rather
 * than a colour, so the column does not rely on a tint to be read (§11.3).
 */
export function goalDifferenceLabel(goalDifference: number): string {
  return goalDifference > 0 ? `+${goalDifference}` : `${goalDifference}`;
}

/**
 * Formats an average match rating to one decimal (`TRN-8`), or a dash before the player has one.
 *
 * A rating is read to a tenth of a point, so the number is fixed rather than left to the locale.
 */
export function averageRatingLabel(rating: number | null): string {
  return rating === null ? '\u2014' : rating.toFixed(1);
}

/** Names one of the table's tie-break criteria, falling back to the code (`TBL-2`…`TBL-10`). */
export function tieBreakerLabel(code: string): string {
  return TIE_BREAKER_LABELS[code] ?? code;
}

/** Words for one team-sheet validator issue, naming the slot it concerns when it concerns one. */
export function teamSheetIssueMessage(issue: {
  readonly code: string;
  readonly slotNumber: number | null;
}): string {
  const message = TEAM_SHEET_ISSUE_MESSAGES[issue.code] ?? 'That selection is not valid.';

  return issue.slotNumber === null ? message : `Slot ${issue.slotNumber}: ${message}`;
}

/**
 * How long until a fixture's team sheets lock (`CAL-3`).
 *
 * Returns a bounded phrase rather than a raw duration, because this is the countdown a manager reads on the
 * dashboard and the prepare screen, and the server's own deadline is the authority (`TIME-5`): the client
 * only counts down to it.
 */
export function lockCountdown(lockAt: string, now: Date): string {
  const target = new Date(lockAt).getTime();

  if (Number.isNaN(target)) {
    return 'Lock time unavailable';
  }

  const difference = target - now.getTime();

  if (difference <= 0) {
    return 'Team sheets are locked';
  }

  const totalMinutes = Math.floor(difference / 60_000);
  const days = Math.floor(totalMinutes / 1_440);
  const hours = Math.floor((totalMinutes % 1_440) / 60);
  const minutes = totalMinutes % 60;

  if (days > 0) {
    return `Locks in ${days}d ${hours}h`;
  }

  return hours > 0 ? `Locks in ${hours}h ${minutes}m` : `Locks in ${minutes}m`;
}

/** Whether a fixture is still ahead of publication, which is what "next fixture" means. */
export function isUpcoming(status: string): boolean {
  return status !== 'published' && status !== 'void';
}
