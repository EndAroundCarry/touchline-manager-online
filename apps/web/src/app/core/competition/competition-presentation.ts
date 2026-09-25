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
