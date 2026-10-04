import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Player, PlayerMatchStat, PlayerSeasonStats } from '../../core/squad/squad.models';
import { SquadStore } from '../../core/squad/squad-store';
import { PlayerTrainingStore } from '../../core/training/player-training-store';
import { PlayerProfile } from './player';

/**
 * The player profile's tabs.
 *
 * Attributes is the tab a manager lands on, the contract tab's negotiation is deliberately switched off
 * until its mechanics exist, and the statistics tab can be pointed at an earlier season.
 */

function stats(goals: number): PlayerSeasonStats {
  return {
    appearances: 10,
    starts: 9,
    minutesPlayed: 800,
    goals,
    assists: 2,
    shots: 20,
    shotsOnTarget: 9,
    saves: 0,
    passesAttempted: 400,
    passesCompleted: 320,
    dribblesAttempted: 50,
    dribblesCompleted: 20,
    yellowCards: 1,
    redCards: 0,
    averageRating: 7.1,
  };
}

function match(fixtureId: string, seasonNumber: number, round: number): PlayerMatchStat {
  return {
    fixtureId,
    seasonNumber,
    seasonLabel: `Season ${seasonNumber}`,
    round,
    playedAt: '2026-09-28T14:00:00Z',
    opponentClubId: 'club-2',
    opponentName: 'Vale Rovers',
    home: true,
    goalsFor: 2,
    goalsAgainst: 1,
    started: true,
    minutesPlayed: 90,
    goals: 1,
    assists: 0,
    shots: 3,
    shotsOnTarget: 2,
    saves: 0,
    passesAttempted: 40,
    passesCompleted: 32,
    dribblesAttempted: 6,
    dribblesCompleted: 3,
    yellowCards: 0,
    redCards: 0,
    rating: 7.5,
  };
}

const matches = {
  playerId: 'player-1',
  matches: [match('f3', 3, 2), match('f2', 3, 1), match('f1', 2, 5)],
  serverTime: '2026-09-28T00:00:00Z',
};

const player: Player = {
  id: 'player-1',
  clubId: 'club-1',
  fullName: 'Alaric Alderwick',
  shortName: 'A. Alderwick',
  nationalityCode: 'ENG',
  age: 24,
  birthGameYear: 2002,
  preferredFoot: 'right',
  heightCm: 180,
  weightKg: 75,
  primaryPosition: 'st',
  secondaryPositions: [],
  status: 'active',
  attributes: {
    technical: {
      finishing: 17,
      passing: 10,
      crossing: 8,
      dribbling: 12,
      firstTouch: 11,
      tackling: 5,
      marking: 5,
      heading: 9,
      technique: 12,
      setPieces: 6,
    },
    mental: {
      decisions: 11,
      vision: 10,
      positioning: 14,
      composure: 13,
      anticipation: 12,
      workRate: 9,
      aggression: 8,
      leadership: 7,
    },
    physical: {
      pace: 15,
      acceleration: 15,
      stamina: 12,
      strength: 10,
      agility: 13,
      jumpingReach: 9,
    },
    goalkeeping: { handling: 2, reflexes: 2, oneOnOnes: 2, aerialAbility: 2 },
  },
  state: { condition: 90, fatigue: 10, morale: 70, matchSharpness: 80 },
  contract: {
    id: 'contract-1',
    startSeasonNumber: 2,
    endSeasonNumber: 4,
    seasonsRemaining: 2,
    weeklyWageMinor: 1_200_000,
    squadStatus: 'first_team',
    status: 'active',
  },
  registration: null,
  availability: [],
  seasonStats: stats(7),
  careerStats: {
    totals: stats(12),
    seasonsPlayed: 1,
    seasons: [
      {
        seasonNumber: 2,
        seasonLabel: 'Season 2',
        clubId: 'club-1',
        clubName: 'Ashvale United',
        stats: stats(5),
      },
    ],
  },
  serverTime: '2026-09-28T00:00:00Z',
};

describe('PlayerProfile', () => {
  let fixture: ComponentFixture<PlayerProfile>;
  let root: HTMLElement;
  let trainingStore: {
    history: WritableSignal<null>;
    loading: WritableSignal<boolean>;
    error: WritableSignal<null>;
    load: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    const store = {
      player: signal(player),
      playerMatches: signal(matches),
      loadPlayer: () => of(player),
      loadPlayerMatches: () => of(matches),
    };

    trainingStore = {
      history: signal(null),
      loading: signal(false),
      error: signal(null),
      load: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [PlayerProfile],
      providers: [
        provideRouter([]),
        { provide: SquadStore, useValue: store },
        { provide: PlayerTrainingStore, useValue: trainingStore },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlayerProfile);
    fixture.componentRef.setInput('id', 'player-1');
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  });

  async function openTab(name: string): Promise<void> {
    const tab = Array.from(root.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(
      (button) => button.textContent?.trim() === name,
    );

    tab?.click();
    await fixture.whenStable();
  }

  it('offers the four tabs, with Attributes selected by default', () => {
    const tabs = Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));

    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual([
      'Attributes',
      'Training',
      'Statistics',
      'Contract',
    ]);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(root.querySelectorAll('app-attribute-value')).toHaveLength(28);
  });

  it('lays the attributes out as four columns side by side', () => {
    const grid = root.querySelector('[data-testid="attribute-rows"]')!;

    expect(grid.classList.contains('grid-cols-4')).toBe(true);
    expect(grid.querySelectorAll(':scope > section')).toHaveLength(4);
  });

  it('hands the Training tab to the training component, which reads the player’s history', async () => {
    await openTab('Training');

    const panel = root.querySelector('#player-panel-training')!;

    expect(panel.querySelector('app-player-training')).not.toBeNull();
    expect(panel.textContent).not.toContain('Sample data');
    expect(trainingStore.load).toHaveBeenCalledWith('player-1');
  });

  it('shows the seasons as a table with the career total and the passes and dribbles', async () => {
    await openTab('Statistics');

    const table = root.querySelector('[data-testid="season-stats"] table')!;
    const headings = Array.from(table.querySelectorAll('thead th')).map((cell) =>
      cell.textContent?.trim(),
    );

    expect(headings).toEqual(
      expect.arrayContaining([
        'Season',
        'Club',
        'Goals',
        'Assists',
        'Passes',
        'Pass %',
        'Dribbles',
      ]),
    );
    expect(table.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(table.querySelector('tbody tr')!.textContent).toContain('Ashvale United');
    expect(table.querySelector('tbody tr')!.textContent).toContain('320/400');
    expect(table.querySelector('tbody tr')!.textContent).toContain('80%');
    expect(table.querySelector('tfoot tr')!.textContent).toContain('Career');
    expect(table.querySelector('tfoot tr')!.textContent).toContain('20/50');
  });

  it('shows the current season first and an earlier season on request', async () => {
    await openTab('Statistics');

    const select = root.querySelector<HTMLSelectElement>('#statistics-season')!;

    expect(select.options).toHaveLength(2);
    expect(select.options[0].textContent).toContain('Current season');

    const rowCount = () => root.querySelectorAll('[data-testid="match-stats"] tbody tr').length;

    expect(rowCount()).toBe(2);
    expect(
      Array.from(root.querySelectorAll('[data-testid="match-stats"] th')).map((cell) =>
        cell.textContent?.trim(),
      ),
    ).toEqual(expect.arrayContaining(['Goals', 'Passes', 'Dribbles', 'Yellow', 'Red', 'Rating']));

    // The engine counts passes and take-ons now, so a row shows them as completed out of attempted.
    const firstRow = root.querySelector('[data-testid="match-stats"] tbody tr')!.textContent;

    expect(firstRow).toContain('32/40');
    expect(firstRow).toContain('3/6');
    expect(root.querySelector('[data-testid="match-stats"]')?.textContent).not.toContain(
      'not tracked yet',
    );

    select.value = '2';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(rowCount()).toBe(1);
  });

  it('shows the contract with the negotiation switched off and marked as coming soon', async () => {
    await openTab('Contract');

    const panel = root.querySelector('#player-panel-contract')!;
    const button = Array.from(panel.querySelectorAll('button')).find((item) =>
      item.textContent?.includes('Start negotiation'),
    )!;

    expect(panel.textContent).toContain('End of season 4');
    expect(panel.textContent).toContain('a week');
    expect(button.disabled).toBe(true);
    expect(panel.textContent).toContain('coming soon');
  });
});
