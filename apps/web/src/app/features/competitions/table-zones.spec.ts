import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import {
  DivisionTable,
  DivisionTableRow,
  MyFixtures,
} from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { CompetitionTable } from './table';

/**
 * The table names what each place leads to at the end of the season, in words (`PR-1`, §11.3).
 *
 * The server decides the zone from the pair of leagues the division belongs to; the screen only prints it, so a place
 * that leads nowhere prints nothing and a place that leads somewhere is never told by a colour alone.
 */
describe('The table zones', () => {
  function row(rank: number, zone: string, zoneLabel: string): DivisionTableRow {
    return {
      rank,
      clubId: `club-${rank}`,
      clubName: `Club ${rank}`,
      clubShortName: `C${rank}`,
      played: 30,
      won: 10,
      drawn: 10,
      lost: 10,
      goalsFor: 40,
      goalsAgainst: 40,
      goalDifference: 0,
      points: 40,
      yellowCards: 0,
      redCards: 0,
      zone,
      zoneLabel,
    };
  }

  const TABLE: DivisionTable = {
    divisionId: 'division-2',
    divisionName: 'English Tier 2',
    tierNumber: 2,
    countryId: 'country-1',
    countryCode: 'ENG',
    countryName: 'England',
    seasonNumber: 1,
    seasonLabel: '2026/27',
    serverTime: '2026-10-07T12:00:00Z',
    rows: [
      row(1, 'promotion', 'Promotion'),
      row(2, 'promotion', 'Promotion'),
      row(3, 'playoff', 'Play-off'),
      row(4, 'playoff', 'Play-off'),
      row(5, 'none', ''),
    ],
  };

  const NO_FIXTURES: MyFixtures = {
    clubId: 'club-9',
    clubName: 'Elsewhere',
    clubShortName: 'ELS',
    divisionId: 'division-9',
    divisionName: 'English Tier 1',
    tierNumber: 1,
    seasonNumber: 1,
    seasonLabel: '2026/27',
    nextFixtureId: null,
    fixtures: [],
    serverTime: '2026-10-07T12:00:00Z',
  };

  async function render(): Promise<HTMLElement> {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [CompetitionTable],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (name: string) => (name === 'divisionId' ? 'division-2' : null) },
            },
          },
        },
        {
          provide: CompetitionStore,
          useValue: {
            fixtures: signal(NO_FIXTURES),
            fixturesLoading: signal(false),
            fixturesError: signal<string | null>(null),
            loadFixtures: vi.fn(),
            divisionTable: signal(TABLE),
            tableLoading: signal(false),
            tableError: signal<string | null>(null),
            managedClubId: signal(null),
            loadDivisionTable: vi.fn(),
            loadMyDivisionTable: vi.fn(),
            divisionStatistics: signal(null),
            statisticsLoading: signal(false),
            statisticsError: signal<string | null>(null),
            loadDivisionStatistics: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CompetitionTable);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  it('has a column for the zone', async () => {
    const root = await render();
    const headers = [...root.querySelectorAll('thead th')].map((th) => th.textContent?.trim());

    expect(headers).toContain('Zone');
  });

  it('names promotion and the play-off in words, and leaves the rest blank', async () => {
    const root = await render();
    const zones = [...root.querySelectorAll<HTMLElement>('tbody td[data-zone]')];

    expect(zones.map((cell) => cell.textContent?.trim())).toEqual([
      'Promotion',
      'Promotion',
      'Play-off',
      'Play-off',
      '',
    ]);
    expect(zones.map((cell) => cell.dataset['zone'])).toEqual([
      'promotion',
      'promotion',
      'playoff',
      'playoff',
      'none',
    ]);
  });
});
