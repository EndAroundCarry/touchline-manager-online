import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ApiError } from '../../core/api/api-error';
import { CompetitionApi } from '../../core/competition/competition-api';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import { ResultGate } from '../../core/match/result-gate';
import { SquadApi } from '../../core/squad/squad-api';
import { ClubOverview, ClubPlayer, PlayerAttributes } from '../../core/squad/squad.models';
import { ClubPage } from './club';

/**
 * Any club's page: its squad and its season, for studying a rival.
 *
 * What matters is that the public squad lists every player as a link to their profile, that the fixtures are read
 * from the club's own side with every opponent a link to their page and every played match one to watch, that a
 * result of the manager's own that they have not seen stays behind the gate, and that moving from one club to an
 * opponent on the same route reads the opponent rather than keeping the first club.
 */

function attributes(value: number): PlayerAttributes {
  const fill = (keys: readonly string[]) => Object.fromEntries(keys.map((key) => [key, value]));

  return {
    technical: fill([
      'finishing',
      'passing',
      'crossing',
      'dribbling',
      'firstTouch',
      'tackling',
      'marking',
      'heading',
      'technique',
      'setPieces',
    ]),
    mental: fill([
      'decisions',
      'vision',
      'positioning',
      'composure',
      'anticipation',
      'workRate',
      'aggression',
      'leadership',
    ]),
    physical: fill(['pace', 'acceleration', 'stamina', 'strength', 'agility', 'jumpingReach']),
    goalkeeping: fill(['handling', 'reflexes', 'oneOnOnes', 'aerialAbility']),
  } as unknown as PlayerAttributes;
}

function player(id: string, name: string, position: string): ClubPlayer {
  return {
    id,
    fullName: name,
    shortName: name.slice(0, 3),
    nationalityCode: 'ENG',
    age: 26,
    preferredFoot: 'right',
    primaryPosition: position,
    secondaryPositions: [],
    attributes: attributes(12),
    experience: { points: 47.5, level: 2, maxLevel: 5, skillBonusPercent: 2, nextLevelPoints: 60 },
  };
}

function club(
  id: string,
  name: string,
  isOwnClub = false,
  managerName: string | null = 'Sam Rivera',
): ClubOverview {
  return {
    clubId: id,
    name,
    shortName: name.slice(0, 3).toUpperCase(),
    city: 'Ashford',
    region: 'Kent',
    countryCode: 'ENG',
    foundingGameYear: 1901,
    primaryColour: '#1f4e79',
    secondaryColour: '#d6e4f0',
    crestShape: 'shield',
    crestPattern: 'stripes',
    divisionId: 'division-1',
    divisionName: 'English Tier 1',
    tierNumber: 1,
    isOwnClub,
    managerName,
    seasonNumber: 1,
    players: [player('p-gk', 'Gil Keeper', 'gk'), player('p-st', 'Stan Striker', 'st')],
    serverTime: '2026-10-20T00:00:00Z',
  };
}

function fixture(
  id: string,
  round: number,
  status: string,
  venue: 'home' | 'away',
  opponent: { id: string; name: string },
  matchId: string | null,
): ClubFixture {
  return {
    id,
    roundNumber: round,
    venue,
    opponentClubId: opponent.id,
    opponentName: opponent.name,
    opponentShortName: opponent.name.slice(0, 3).toUpperCase(),
    opponentPrimaryColour: '#8c2f39',
    opponentSecondaryColour: '#f2e3e5',
    opponentCrestShape: 'round',
    opponentCrestPattern: 'stripes',
    kickoffAt: '2026-10-20T14:00:00Z',
    lockAt: '2026-10-20T12:00:00Z',
    status,
    homeScore: status === 'published' ? 3 : null,
    awayScore: status === 'published' ? 1 : null,
    matchId,
    outcome: status === 'published' ? (venue === 'home' ? 'win' : 'loss') : null,
    kind: 'league',
    stageLabel: null,
  };
}

const SEASON: MyFixtures = {
  clubId: 'club-1',
  clubName: 'Ashvale United',
  clubShortName: 'ASH',
  divisionId: 'division-1',
  divisionName: 'English Tier 1',
  tierNumber: 1,
  seasonNumber: 1,
  seasonLabel: '2026/27',
  nextFixtureId: 'f3',
  fixtures: [
    fixture('f1', 1, 'published', 'home', { id: 'club-2', name: 'Vale Rovers' }, 'm1'),
    fixture('f2', 2, 'published', 'away', { id: 'club-3', name: 'Brook Town' }, 'm2'),
    fixture('f3', 3, 'scheduled', 'home', { id: 'club-4', name: 'Moor Athletic' }, null),
  ],
  serverTime: '2026-10-20T00:00:00Z',
};

describe('ClubPage', () => {
  let page: ComponentFixture<ClubPage>;
  let root: HTMLElement;
  let hiddenMatches: Set<string>;
  let squadApi: { club: ReturnType<typeof vi.fn> };
  let competitionApi: { clubFixtures: ReturnType<typeof vi.fn> };
  let gate: {
    refresh: ReturnType<typeof vi.fn>;
    isHidden: (id: string | null) => boolean;
    reveal: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    hiddenMatches = new Set();
    squadApi = { club: vi.fn(() => of(club('club-1', 'Ashvale United'))) };
    competitionApi = { clubFixtures: vi.fn(() => of(SEASON)) };
    gate = {
      refresh: vi.fn(),
      isHidden: (id) => id !== null && hiddenMatches.has(id),
      reveal: vi.fn((id: string) => hiddenMatches.delete(id)),
    };

    await TestBed.configureTestingModule({
      imports: [ClubPage],
      providers: [
        provideRouter([]),
        { provide: SquadApi, useValue: squadApi },
        { provide: CompetitionApi, useValue: competitionApi },
        { provide: ResultGate, useValue: gate },
      ],
    }).compileComponents();
  });

  async function open(id = 'club-1'): Promise<void> {
    page = TestBed.createComponent(ClubPage);
    page.componentRef.setInput('id', id);
    await page.whenStable();
    root = page.nativeElement as HTMLElement;
  }

  async function openTab(name: string): Promise<void> {
    const tab = Array.from(root.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(
      (button) => button.textContent?.trim() === name,
    );

    tab?.click();
    await page.whenStable();
  }

  function hrefs(selector: string): string[] {
    return Array.from(root.querySelectorAll<HTMLAnchorElement>(`${selector} a`)).map(
      (a) => a.getAttribute('href') ?? '',
    );
  }

  it('names the club, its division and its crest in its own colours', async () => {
    await open();

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('Ashvale United');
    expect(root.textContent).toContain('Ashford, Kent');
    expect(
      Array.from(root.querySelectorAll('a'))
        .find((a) => a.textContent?.trim() === 'English Tier 1')
        ?.getAttribute('href'),
    ).toBe('/competitions/division-1/table');
    expect(root.querySelector('[data-testid="club-crest"] rect')?.getAttribute('fill')).toBe(
      '#1f4e79',
    );
  });

  it('names the manager and offers to write to them', async () => {
    await open();

    expect(root.querySelector('[data-testid="club-manager"]')?.textContent).toContain(
      'Manager: Sam Rivera',
    );

    const link = Array.from(root.querySelectorAll<HTMLAnchorElement>('a')).find(
      (a) => a.textContent?.trim() === 'Send a message',
    );

    expect(link?.getAttribute('href')).toBe('/inbox/managers/new?club=club-1');
  });

  it('says AI manager and offers no message link for a club the game runs (COM-11)', async () => {
    squadApi.club.mockReturnValue(of(club('club-1', 'Ashvale United', false, null)));

    await open();

    expect(root.querySelector('[data-testid="club-manager"]')?.textContent?.trim()).toBe(
      'AI manager',
    );
    expect(root.textContent).not.toContain('Send a message');
  });

  it('offers no message link on the manager’s own club (COM-11)', async () => {
    squadApi.club.mockReturnValue(of(club('club-1', 'Ashvale United', true)));

    await open();

    expect(root.querySelector('[data-testid="club-manager"]')?.textContent).toContain(
      'Manager: Sam Rivera',
    );
    expect(root.textContent).not.toContain('Send a message');
  });

  it('opens on the squad, goalkeeper first, each player a link to their profile', async () => {
    await open();

    const tabs = Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));

    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual(['Squad', 'Fixtures']);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(hrefs('[data-testid="club-squad"] tbody')).toEqual(['/players/p-gk', '/players/p-st']);
    expect(root.querySelector('[data-testid="club-squad"] tbody th')?.textContent).toContain(
      'Gil Keeper',
    );
  });

  it('offers nothing private on another club’s squad: no condition, no contract, no wage', async () => {
    await open();

    const headings = Array.from(root.querySelectorAll('[data-testid="club-squad"] thead th')).map(
      (th) => th.textContent?.trim(),
    );

    expect(headings).toEqual([
      'Player',
      'Position',
      'Age',
      'Nationality',
      'GK',
      'Tech',
      'Mental',
      'Phys',
      'Experience',
    ]);
  });

  it('lists the results newest first, each opponent a link to their page and each match one to watch', async () => {
    await open();
    await openTab('Fixtures');

    const results = root.querySelector('[data-testid="club-results"]')!;
    const rows = Array.from(results.querySelectorAll('li'));

    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Round 2');
    expect(hrefs('[data-testid="club-results"]')).toEqual([
      '/clubs/club-3',
      '/matches/m2',
      '/clubs/club-2',
      '/matches/m1',
    ]);
  });

  it('orders the two names by who was at home, with the home side’s goals first', async () => {
    await open();
    await openTab('Fixtures');

    const rows = Array.from(root.querySelectorAll('[data-testid="club-results"] li')).map((li) =>
      li.textContent?.replace(/\s+/g, ' ').trim(),
    );

    // Round 2 was played away at Brook Town: they are the home side, and the home side's 3 goals come first.
    expect(rows[0]).toMatch(/Brook Town v ASH.*3–1/);
    expect(rows[1]).toMatch(/ASH v Vale Rovers.*3–1/);
  });

  it('lists what is still to play with its opponent linked', async () => {
    await open();
    await openTab('Fixtures');

    expect(hrefs('[data-testid="club-upcoming"]')).toEqual(['/clubs/club-4']);
  });

  it('keeps back the result of one of the viewer’s own unseen matches until it is asked for', async () => {
    hiddenMatches.add('m2');
    await open();
    await openTab('Fixtures');

    const rows = Array.from(root.querySelectorAll('[data-testid="club-results"] li'));

    expect(rows[0].textContent).not.toContain('3–1');
    expect(rows[0].textContent).toContain('Show result');
    expect(rows[1].textContent).toContain('3–1');

    Array.from(rows[0].querySelectorAll('button'))
      .find((button) => button.textContent?.trim() === 'Show result')
      ?.click();
    await page.whenStable();

    expect(gate.reveal).toHaveBeenCalledWith('m2');
    expect(root.querySelector('[data-testid="club-results"] li')?.textContent).toContain('3–1');
  });

  it('points a manager’s own club to the private squad', async () => {
    squadApi.club.mockReturnValue(of(club('club-1', 'Ashvale United', true)));
    await open();

    const manage = Array.from(root.querySelectorAll('a')).find(
      (a) => a.textContent?.trim() === 'Manage the squad',
    );

    expect(manage?.getAttribute('href')).toBe('/squad');
  });

  it('says why a club could not be loaded', async () => {
    squadApi.club.mockReturnValue(
      throwError(
        () => new ApiError(404, 'CLUB_NOT_FOUND', 'That club does not exist.', null, new Map()),
      ),
    );
    await open('nowhere');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'That club does not exist.',
    );
  });

  it('reads the opponent when the same page is opened on another club, not the first one again', async () => {
    const second = new Subject<ClubOverview>();

    await open('club-1');
    squadApi.club.mockReturnValue(second);

    page.componentRef.setInput('id', 'club-2');
    await page.whenStable();

    expect(squadApi.club).toHaveBeenLastCalledWith('club-2');
    expect(competitionApi.clubFixtures).toHaveBeenLastCalledWith('club-2');
    expect(root.querySelector('h1')).toBeNull();

    second.next(club('club-2', 'Vale Rovers'));
    await page.whenStable();

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('Vale Rovers');
  });
});
