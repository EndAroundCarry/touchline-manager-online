import {
  DivisionPlayerStat,
  DivisionTableRow,
  MyFixtures,
} from '../../core/competition/competition.models';
import {
  isUpcoming,
  roundLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { SquadPlayer } from '../../core/squad/squad.models';
import { MyMarketBid, TransferListing } from '../../core/transfers/transfers.models';

/**
 * The dashboard's widgets, as plain functions over the data the stores already hold.
 *
 * Each widget on the dashboard answers one question a manager asks on arriving (who do I play, where do I stand,
 * who is hurt, what is closing). Keeping the answers here, away from the component, means they are tested without a
 * component and without a server, and the template stays a plain loop over what these return.
 */

/**
 * The rows of the table to show around the manager's club.
 *
 * A dashboard widget has room for a handful of rows, not a division, so it shows the club and its neighbours: the
 * window is centred on the club and pinned to the top or the bottom of the table when the club is near an end.
 * With no club to centre on it is the top of the table.
 */
export function tableWindow(
  rows: readonly DivisionTableRow[],
  clubId: string | null,
  size = 9,
): readonly DivisionTableRow[] {
  if (rows.length <= size) {
    return rows;
  }

  const at = rows.findIndex((row) => row.clubId === clubId);
  const centre = at < 0 ? 0 : at;
  const start = Math.min(Math.max(centre - Math.floor(size / 2), 0), rows.length - size);

  return rows.slice(start, start + size);
}

/** Why a player is listed in the squad-status widget. */
export type SquadStatusKind = 'injured' | 'suspended' | 'tired' | 'expiring';

/** One group of players with the same concern. */
export interface SquadStatusGroup {
  readonly kind: SquadStatusKind;

  /** The group's name. */
  readonly label: string;

  readonly players: readonly {
    readonly id: string;
    readonly name: string;
    readonly detail: string;
  }[];
}

/** Condition below this makes a player "tired" for the widget: a manager would think twice about starting them. */
export const TIRED_BELOW_CONDITION = 70;

/** A contract with this many seasons or fewer left is "expiring": the club is about to lose the player or renew. */
export const EXPIRING_WITHIN_SEASONS = 1;

const GROUP_LABELS: Record<SquadStatusKind, string> = {
  injured: 'Injured',
  suspended: 'Suspended',
  tired: 'Tired',
  expiring: 'Contract ending',
};

/**
 * The squad's concerns, grouped, with empty groups left out.
 *
 * A player can appear under more than one concern (an injured player whose contract is ending is both), because
 * each is a separate decision. The order is the order of urgency for the next matchday.
 */
export function squadStatus(players: readonly SquadPlayer[]): readonly SquadStatusGroup[] {
  const groups: Record<SquadStatusKind, { id: string; name: string; detail: string }[]> = {
    injured: [],
    suspended: [],
    tired: [],
    expiring: [],
  };

  for (const player of players) {
    for (const item of player.availability) {
      const unit = item.remainingFixtures === 1 ? 'fixture' : 'fixtures';

      groups[item.type === 'suspension' ? 'suspended' : 'injured'].push({
        id: player.id,
        name: player.shortName,
        detail: `${item.remainingFixtures} ${unit}`,
      });
    }

    if (player.availability.length === 0 && player.state.condition < TIRED_BELOW_CONDITION) {
      groups.tired.push({
        id: player.id,
        name: player.shortName,
        detail: `${player.state.condition}% condition`,
      });
    }

    if (player.contract !== null && player.contract.seasonsRemaining <= EXPIRING_WITHIN_SEASONS) {
      groups.expiring.push({
        id: player.id,
        name: player.shortName,
        detail:
          player.contract.seasonsRemaining <= 0
            ? 'ends this season'
            : `${player.contract.seasonsRemaining} season left`,
      });
    }
  }

  return (Object.keys(groups) as SquadStatusKind[])
    .filter((kind) => groups[kind].length > 0)
    .map((kind) => ({ kind, label: GROUP_LABELS[kind], players: groups[kind] }));
}

/** What a calendar entry is. */
export type CalendarEventKind = 'match' | 'deadline' | 'auction';

/** One dated entry in the dashboard's calendar. */
export interface CalendarEvent {
  /** An ISO instant. */
  readonly at: string;
  readonly kind: CalendarEventKind;
  readonly title: string;
  readonly detail: string;

  /** The router link the entry opens. */
  readonly link: readonly string[];
}

/**
 * The next things on the club's calendar, soonest first.
 *
 * Three kinds of dated thing exist in the game: a match, the deadline before it for the team sheet, and a transfer
 * auction the club is part of closing. Anything already past is dropped, so the list is always "what is next".
 */
export function calendarEvents(
  fixtures: MyFixtures | null,
  bids: readonly MyMarketBid[],
  listings: readonly TransferListing[],
  now: Date,
  limit = 7,
): readonly CalendarEvent[] {
  const events: CalendarEvent[] = [];

  for (const fixture of fixtures?.fixtures ?? []) {
    if (!isUpcoming(fixture.status)) {
      continue;
    }

    const opponent = `${venueLabel(fixture.venue)} v ${fixture.opponentShortName}`;

    events.push({
      at: fixture.kickoffAt,
      kind: 'match',
      title: opponent,
      detail: roundLabel(fixture.roundNumber),
      link: ['/fixtures', fixture.id, 'prepare'],
    });

    if (fixture.id === fixtures?.nextFixtureId) {
      events.push({
        at: fixture.lockAt,
        kind: 'deadline',
        title: 'Team sheet locks',
        detail: opponent,
        link: ['/fixtures', fixture.id, 'prepare'],
      });
    }
  }

  for (const bid of bids) {
    events.push({
      at: bid.endsAt,
      kind: 'auction',
      title: `Auction closes: ${bid.playerName}`,
      detail: 'Your bid',
      link: ['/transfers'],
    });
  }

  for (const listing of listings) {
    events.push({
      at: listing.endsAt,
      kind: 'auction',
      title: `Auction closes: ${listing.playerShortName}`,
      detail: 'Your listing',
      link: ['/transfers'],
    });
  }

  return events
    .filter((event) => new Date(event.at).getTime() >= now.getTime())
    .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
    .slice(0, limit);
}

/** One line of the player-stats widget. */
export interface StatLeader {
  readonly label: string;
  readonly player: string;
  readonly value: string;
}

/**
 * The division's leaders, for the player-stats widget.
 *
 * Only a stat somebody has actually earned is shown (a "top scorer" with no goals would be a lie), and each leader
 * is the first row with the highest value, which on the server's goals-then-assists-then-name order is the stable
 * choice.
 */
export function statLeaders(rows: readonly DivisionPlayerStat[]): readonly StatLeader[] {
  const leaders: StatLeader[] = [];

  const top = (
    label: string,
    value: (row: DivisionPlayerStat) => number | null,
    show: (n: number) => string,
  ): void => {
    let best: DivisionPlayerStat | null = null;
    let bestValue = 0;

    for (const row of rows) {
      const current = value(row);

      if (current !== null && current > bestValue) {
        best = row;
        bestValue = current;
      }
    }

    if (best !== null) {
      leaders.push({ label, player: best.playerName, value: show(bestValue) });
    }
  };

  top('Top goalscorer', (row) => row.goals, String);
  top('Most assists', (row) => row.assists, String);
  top(
    'Best average rating',
    (row) => row.averageRating,
    (n) => n.toFixed(2),
  );
  top('Most yellow cards', (row) => row.yellowCards, String);
  top('Most red cards', (row) => row.redCards, String);

  return leaders;
}
