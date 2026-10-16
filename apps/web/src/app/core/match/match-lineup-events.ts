import { LineupIndex } from './match-lineups';
import { CommentaryLine } from './match.models';

/** The minutes a player was booked, sent off or hurt, each null when it did not happen or is not dated. */
export interface PlayerEventMinutes {
  readonly yellow: number | null;
  readonly red: number | null;
  readonly injury: number | null;
}

/** What the live panels may show when nothing is being played and nothing has been revealed. */
export const NOTHING_HAS_HAPPENED = -1;

/** What the live panels show once the whole match is known: every event, dated or not. */
export const EVERYTHING_HAS_HAPPENED = Number.POSITIVE_INFINITY;

const YELLOW_TEMPLATES: ReadonlySet<string> = new Set(['match.card.yellow']);
const RED_TEMPLATES: ReadonlySet<string> = new Set(['match.card.red', 'match.card.second_yellow']);
const INJURY_TEMPLATES: ReadonlySet<string> = new Set(['match.injury']);

/**
 * When each player was booked, sent off or hurt, read from the commentary.
 *
 * A lineup line says *that* a player carries a card or an injury, not when, and the lineup panels fill in as
 * the replay plays, so each event has to be placed on the match clock. The first event of a kind wins.
 */
export function playerEventMinutes(
  commentary: readonly CommentaryLine[],
  lineups: LineupIndex,
): ReadonlyMap<string, PlayerEventMinutes> {
  const byParticipant = new Map<
    string,
    { yellow: number | null; red: number | null; injury: number | null }
  >();

  for (const line of commentary) {
    const kind = YELLOW_TEMPLATES.has(line.templateKey)
      ? 'yellow'
      : RED_TEMPLATES.has(line.templateKey)
        ? 'red'
        : INJURY_TEMPLATES.has(line.templateKey)
          ? 'injury'
          : null;

    if (kind === null) {
      continue;
    }

    const playerId = line.parameters.find((parameter) => parameter.name === 'playerId')?.value;
    const participantId =
      playerId === undefined ? undefined : lineups.participantByPlayerId.get(playerId);

    if (participantId === undefined) {
      continue;
    }

    const events = byParticipant.get(participantId) ?? { yellow: null, red: null, injury: null };

    events[kind] = Math.min(events[kind] ?? line.minute, line.minute);
    byParticipant.set(participantId, events);
  }

  return byParticipant;
}

/**
 * Whether an event dated at a minute has happened by the shown minute.
 *
 * An event with no date is shown only once everything is, so it never appears early.
 */
export function hasHappened(minute: number | null, shownMinute: number): boolean {
  return minute === null ? shownMinute === EVERYTHING_HAS_HAPPENED : minute <= shownMinute;
}
