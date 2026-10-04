import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import {
  ClubFixture,
  DivisionPlayerStat,
  DivisionStatistics,
  DivisionTable,
  MyFixtures,
} from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
import { CompetitionStatistics } from './statistics';
import { CompetitionTable } from './table';

/**
 * The league table and the player statistics count every match in the division, the manager's own included, so
 * they are held back for the manager's own division while a result of theirs is unseen, and shown again the moment
 * it has been watched or asked for. Another division's table never contains the manager's matches.
 */
describe('The division aggregates', () => {
  const OWN_DIVISION = 'division-1';

  const OWN_FIXTURE: ClubFixture = {
    id: 'fixture-1',
    roundNumber: 1,
    venue: 'home',
    opponentClubId: 'club-2',
    opponentName: 'Vale Athletic',
    opponentShortName: 'VAL',
    kickoffAt: '2026-10-06T19:00:00Z',
    lockAt: '2026-10-06T18:00:00Z',
    status: 'published',
    homeScore: 4,
    awayScore: 0,
    matchId: 'match-1',
    outcome: 'win',
  };

  const FIXTURES: MyFixtures = {
    clubId: 'club-1',
    clubName: 'Ashfield Rovers',
    clubShortName: 'ASH',
    divisionId: OWN_DIVISION,
    divisionName: 'English Tier 1',
    tierNumber: 1,
    seasonNumber: 1,
    seasonLabel: '2026/27',
    nextFixtureId: null,
    fixtures: [OWN_FIXTURE],
    serverTime: '2026-10-07T12:00:00Z',
  };

  function table(divisionId: string): DivisionTable {
    return {
      divisionId,
      divisionName: 'English Tier 1',
      tierNumber: 1,
      countryId: 'country-1',
      countryCode: 'ENG',
      countryName: 'England',
      seasonNumber: 1,
      seasonLabel: '2026/27',
      serverTime: '2026-10-07T12:00:00Z',
      rows: [
        {
          rank: 1,
          clubId: 'club-1',
          clubName: 'Ashfield Rovers',
          clubShortName: 'ASH',
          played: 1,
          won: 1,
          drawn: 0,
          lost: 0,
          goalsFor: 4,
          goalsAgainst: 0,
          goalDifference: 4,
          points: 3,
          yellowCards: 0,
          redCards: 0,
        },
      ],
    };
  }

  function stat(): DivisionPlayerStat {
    return {
      playerId: 'player-1',
      playerName: 'A Scorer',
      clubId: 'club-1',
      clubName: 'Ashfield Rovers',
      clubShortName: 'ASH',
      appearances: 1,
      starts: 1,
      minutesPlayed: 90,
      goals: 3,
      assists: 0,
      shots: 5,
      shotsOnTarget: 4,
      saves: 0,
      yellowCards: 0,
      redCards: 0,
      averageRating: 8.1,
    };
  }

  function statistics(divisionId: string): DivisionStatistics {
    return {
      divisionId,
      divisionName: 'English Tier 1',
      tierNumber: 1,
      countryId: 'country-1',
      countryCode: 'ENG',
      countryName: 'England',
      seasonNumber: 1,
      seasonLabel: '2026/27',
      serverTime: '2026-10-07T12:00:00Z',
      rows: [stat()],
    };
  }

  let mine: ReturnType<typeof signal<MyFixtures | null>>;

  async function configure(divisionId: string | null, shown: string): Promise<void> {
    localStorage.clear();
    TestBed.resetTestingModule();
    mine = signal<MyFixtures | null>(FIXTURES);

    await TestBed.configureTestingModule({
      imports: [CompetitionTable, CompetitionStatistics],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (name: string) => (name === 'divisionId' ? divisionId : null) },
            },
          },
        },
        {
          provide: CompetitionStore,
          useValue: {
            fixtures: mine,
            fixturesLoading: signal(false),
            fixturesError: signal<string | null>(null),
            loadFixtures: vi.fn(),
            divisionTable: signal(table(shown)),
            tableLoading: signal(false),
            tableError: signal<string | null>(null),
            managedClubId: signal('club-1'),
            loadDivisionTable: vi.fn(),
            loadMyDivisionTable: vi.fn(),
            divisionStatistics: signal(statistics(shown)),
            statisticsLoading: signal(false),
            statisticsError: signal<string | null>(null),
            loadDivisionStatistics: vi.fn(),
          },
        },
      ],
    }).compileComponents();
  }

  async function render<T>(
    component: new () => T,
  ): Promise<{ root: HTMLElement; fixture: ComponentFixture<T> }> {
    const fixture = TestBed.createComponent(component);
    await fixture.whenStable();

    return { root: fixture.nativeElement as HTMLElement, fixture };
  }

  describe('the league table', () => {
    it("is held back for the manager's own division while a result is unseen", async () => {
      await configure(null, OWN_DIVISION);

      const { root } = await render(CompetitionTable);

      expect(root.querySelector('[data-testid="result-hidden"]')).not.toBeNull();
      expect(root.querySelector('table')).toBeNull();
      expect(root.textContent).toContain('The league table would give away the result');
    });

    it('comes back when the result is shown', async () => {
      await configure(null, OWN_DIVISION);

      const { root, fixture } = await render(CompetitionTable);

      [...root.querySelectorAll('button')]
        .find((button) => button.textContent?.trim() === 'Show result')!
        .click();
      await fixture.whenStable();

      expect(root.querySelector('[data-testid="result-hidden"]')).toBeNull();
      expect(root.querySelector('table')).not.toBeNull();
    });

    it('comes back for a match watched in the viewer', async () => {
      await configure(null, OWN_DIVISION);
      TestBed.inject(ResultRevealStore).reveal('match-1');

      const { root } = await render(CompetitionTable);

      expect(root.querySelector('table')).not.toBeNull();
    });

    it("is never held back for another division, which holds none of the manager's matches", async () => {
      await configure('division-9', 'division-9');

      const { root } = await render(CompetitionTable);

      expect(root.querySelector('[data-testid="result-hidden"]')).toBeNull();
      expect(root.querySelector('table')).not.toBeNull();
    });
  });

  describe('the player statistics', () => {
    it("are held back for the manager's own division while a result is unseen", async () => {
      await configure(OWN_DIVISION, OWN_DIVISION);

      const { root } = await render(CompetitionStatistics);

      expect(root.querySelector('[data-testid="result-hidden"]')).not.toBeNull();
      expect(root.textContent).not.toContain('A Scorer');
    });

    it('come back when the result is shown', async () => {
      await configure(OWN_DIVISION, OWN_DIVISION);

      const { root, fixture } = await render(CompetitionStatistics);

      [...root.querySelectorAll('button')]
        .find((button) => button.textContent?.trim() === 'Show result')!
        .click();
      await fixture.whenStable();

      expect(root.textContent).toContain('A Scorer');
    });

    it('are never held back for another division', async () => {
      await configure('division-9', 'division-9');

      const { root } = await render(CompetitionStatistics);

      expect(root.textContent).toContain('A Scorer');
    });
  });
});
