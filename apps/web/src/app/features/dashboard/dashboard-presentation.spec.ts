import {
  DivisionPlayerStat,
  DivisionTableRow,
  MyFixtures,
} from '../../core/competition/competition.models';
import { SquadPlayer } from '../../core/squad/squad.models';
import { MyMarketBid, TransferListing } from '../../core/transfers/transfers.models';
import { calendarEvents, squadStatus, statLeaders, tableWindow } from './dashboard-presentation';

/**
 * The dashboard's widgets as pure functions: which table rows to show, who is a concern, what is next on the
 * calendar, and who leads the division.
 */

function row(rank: number, clubId = `club-${rank}`): DivisionTableRow {
  return {
    rank,
    clubId,
    clubName: `Club ${rank}`,
    clubShortName: `C${rank}`,
    played: 5,
    won: 0,
    drawn: 0,
    lost: 0,
    goalsFor: 0,
    goalsAgainst: 0,
    goalDifference: 0,
    points: 0,
    yellowCards: 0,
    redCards: 0,
  };
}

const TABLE = Array.from({ length: 18 }, (_, index) => row(index + 1));

describe('tableWindow', () => {
  it('centres the window on the club', () => {
    const window = tableWindow(TABLE, 'club-10', 9);

    expect(window.map((r) => r.rank)).toEqual([6, 7, 8, 9, 10, 11, 12, 13, 14]);
  });

  it('pins to the top when the club is near it', () => {
    expect(tableWindow(TABLE, 'club-2', 9).map((r) => r.rank)[0]).toBe(1);
    expect(tableWindow(TABLE, 'club-2', 9)).toHaveLength(9);
  });

  it('pins to the bottom when the club is near it', () => {
    const window = tableWindow(TABLE, 'club-17', 9);

    expect(window[window.length - 1].rank).toBe(18);
    expect(window).toHaveLength(9);
  });

  it('shows the whole table when it is shorter than the window, and the top when there is no club', () => {
    expect(tableWindow(TABLE.slice(0, 5), 'club-3', 9)).toHaveLength(5);
    expect(tableWindow(TABLE, null, 9)[0].rank).toBe(1);
  });
});

function player(
  id: string,
  overrides: Partial<{
    condition: number;
    availability: SquadPlayer['availability'];
    seasonsRemaining: number | null;
  }> = {},
): SquadPlayer {
  const seasonsRemaining =
    overrides.seasonsRemaining === undefined ? 3 : overrides.seasonsRemaining;

  return {
    id,
    fullName: `Player ${id}`,
    shortName: `P. ${id}`,
    nationalityCode: 'ESP',
    age: 24,
    preferredFoot: 'right',
    primaryPosition: 'CM',
    secondaryPositions: [],
    state: { condition: overrides.condition ?? 95, fatigue: 5, morale: 50, matchSharpness: 50 },
    contract:
      seasonsRemaining === null
        ? null
        : {
            id: `contract-${id}`,
            startSeasonNumber: 1,
            endSeasonNumber: 1 + seasonsRemaining,
            seasonsRemaining,
            weeklyWageMinor: 1000,
            squadStatus: 'first_team',
            status: 'active',
          },
    availability: overrides.availability ?? [],
    attributeAverages: { goalkeeping: 1, technical: 10, mental: 10, physical: 10 },
  };
}

describe('squadStatus', () => {
  it('leaves out the groups nobody is in', () => {
    expect(squadStatus([player('a'), player('b')])).toEqual([]);
  });

  it('groups injuries, suspensions, tired players and ending contracts, most urgent first', () => {
    const groups = squadStatus([
      player('hurt', {
        availability: [
          { id: 'x', type: 'injury', severity: 'minor', remainingFixtures: 1, startedAt: '' },
        ],
      }),
      player('banned', {
        availability: [
          { id: 'y', type: 'suspension', severity: 'none', remainingFixtures: 2, startedAt: '' },
        ],
      }),
      player('spent', { condition: 60 }),
      player('leaving', { seasonsRemaining: 0 }),
      player('fine'),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['injured', 'suspended', 'tired', 'expiring']);
    expect(groups[0].players).toEqual([{ id: 'hurt', name: 'P. hurt', detail: '1 fixture' }]);
    expect(groups[1].players[0].detail).toBe('2 fixtures');
    expect(groups[2].players[0].detail).toBe('60% condition');
    expect(groups[3].players[0].detail).toBe('ends this season');
  });

  it('does not call an injured player tired as well', () => {
    const groups = squadStatus([
      player('hurt', {
        condition: 40,
        availability: [
          { id: 'x', type: 'injury', severity: 'minor', remainingFixtures: 3, startedAt: '' },
        ],
      }),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['injured']);
  });

  it('lists a player in each group that applies, because each is a separate decision', () => {
    const groups = squadStatus([
      player('both', {
        seasonsRemaining: 1,
        availability: [
          { id: 'x', type: 'injury', severity: 'minor', remainingFixtures: 1, startedAt: '' },
        ],
      }),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['injured', 'expiring']);
    expect(groups[1].players[0].detail).toBe('1 season left');
  });
});

const FIXTURES: MyFixtures = {
  clubId: 'club-1',
  clubName: 'Ashfield Rovers',
  clubShortName: 'ASH',
  divisionId: 'division-1',
  divisionName: 'Tier 1',
  tierNumber: 1,
  seasonNumber: 1,
  seasonLabel: '2026/27',
  nextFixtureId: 'f2',
  serverTime: '2026-10-04T12:00:00Z',
  fixtures: [
    {
      id: 'f1',
      roundNumber: 1,
      venue: 'home',
      opponentClubId: 'a',
      opponentName: 'Alpha',
      opponentShortName: 'ALP',
      kickoffAt: '2026-10-01T19:00:00Z',
      lockAt: '2026-10-01T18:30:00Z',
      status: 'published',
      homeScore: 1,
      awayScore: 0,
      matchId: 'm1',
      outcome: 'win',
    },
    {
      id: 'f2',
      roundNumber: 2,
      venue: 'away',
      opponentClubId: 'b',
      opponentName: 'Bravo',
      opponentShortName: 'BRA',
      kickoffAt: '2026-10-06T19:00:00Z',
      lockAt: '2026-10-06T18:30:00Z',
      status: 'scheduled',
      homeScore: null,
      awayScore: null,
      matchId: null,
      outcome: null,
    },
    {
      id: 'f3',
      roundNumber: 3,
      venue: 'home',
      opponentClubId: 'c',
      opponentName: 'Charlie',
      opponentShortName: 'CHA',
      kickoffAt: '2026-10-08T19:00:00Z',
      lockAt: '2026-10-08T18:30:00Z',
      status: 'scheduled',
      homeScore: null,
      awayScore: null,
      matchId: null,
      outcome: null,
    },
  ],
};

const BID: MyMarketBid = {
  listingId: 'l1',
  playerId: 'p1',
  playerName: 'Silas Loxley',
  amountMinor: 100,
  status: 'leading',
  endsAt: '2026-10-05T15:00:00Z',
};

const LISTING = {
  listingId: 'l2',
  playerShortName: 'D. Mora',
  endsAt: '2026-10-07T15:00:00Z',
} as TransferListing;

describe('calendarEvents', () => {
  const now = new Date('2026-10-04T12:00:00Z');

  it('orders matches, the next team-sheet deadline and auctions by time, and drops what is past', () => {
    const events = calendarEvents(FIXTURES, [BID], [LISTING], now);

    expect(events.map((e) => [e.kind, e.title])).toEqual([
      ['auction', 'Auction closes: Silas Loxley'],
      ['deadline', 'Team sheet locks'],
      ['match', 'Away v BRA'],
      ['auction', 'Auction closes: D. Mora'],
      ['match', 'Home v CHA'],
    ]);
  });

  it('adds a deadline only for the next fixture, and links a match to its team sheet', () => {
    const events = calendarEvents(FIXTURES, [], [], now);

    expect(events.filter((e) => e.kind === 'deadline')).toHaveLength(1);
    expect(events.find((e) => e.kind === 'match')?.link).toEqual(['/fixtures', 'f2', 'prepare']);
  });

  it('is bounded, and is empty with nothing to show', () => {
    expect(calendarEvents(FIXTURES, [BID], [LISTING], now, 2)).toHaveLength(2);
    expect(calendarEvents(null, [], [], now)).toEqual([]);
  });
});

function stat(name: string, over: Partial<DivisionPlayerStat>): DivisionPlayerStat {
  return {
    playerId: name,
    playerName: name,
    clubId: 'c',
    clubName: 'Club',
    clubShortName: 'CLB',
    appearances: 5,
    starts: 5,
    minutesPlayed: 450,
    goals: 0,
    assists: 0,
    shots: 0,
    shotsOnTarget: 0,
    saves: 0,
    yellowCards: 0,
    redCards: 0,
    averageRating: null,
    ...over,
  };
}

describe('statLeaders', () => {
  it('names the leader of each stat somebody has earned', () => {
    const leaders = statLeaders([
      stat('Ana', { goals: 4, assists: 1, averageRating: 7.1, yellowCards: 2 }),
      stat('Bea', { goals: 2, assists: 3, averageRating: 7.456, yellowCards: 1 }),
    ]);

    expect(leaders).toEqual([
      { label: 'Top goalscorer', player: 'Ana', value: '4' },
      { label: 'Most assists', player: 'Bea', value: '3' },
      { label: 'Best average rating', player: 'Bea', value: '7.46' },
      { label: 'Most yellow cards', player: 'Ana', value: '2' },
    ]);
  });

  it('leaves out a stat nobody has, rather than naming a leader with none', () => {
    expect(statLeaders([stat('Ana', {})])).toEqual([]);
  });
});
