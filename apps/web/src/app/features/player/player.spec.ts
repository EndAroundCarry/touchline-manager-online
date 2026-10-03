import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Player, PlayerSeasonStats } from '../../core/squad/squad.models';
import { SquadStore } from '../../core/squad/squad-store';
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
    yellowCards: 1,
    redCards: 0,
    averageRating: 7.1,
  };
}

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

  beforeEach(async () => {
    const store = { player: signal(player), loadPlayer: () => of(player) };

    await TestBed.configureTestingModule({
      imports: [PlayerProfile],
      providers: [provideRouter([]), { provide: SquadStore, useValue: store }],
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
      'Training report',
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

  it('shows the training report with its placeholder notice', async () => {
    await openTab('Training report');

    expect(root.textContent).toContain('Sample data');
    expect(root.querySelectorAll('#player-panel-training tbody tr').length).toBeGreaterThan(0);
  });

  it('shows the current season first and an earlier season on request', async () => {
    await openTab('Statistics');

    const select = root.querySelector<HTMLSelectElement>('#statistics-season')!;

    expect(select.options).toHaveLength(2);
    expect(select.options[0].textContent).toContain('Current season');
    expect(root.querySelector('#player-panel-statistics dl')?.textContent).toContain('7');

    const rowCount = () => root.querySelectorAll('[data-testid="match-stats"] tbody tr').length;

    expect(rowCount()).toBe(10);
    expect(
      Array.from(root.querySelectorAll('[data-testid="match-stats"] th')).map((cell) =>
        cell.textContent?.trim(),
      ),
    ).toEqual(expect.arrayContaining(['Goals', 'Passes', 'Yellow', 'Red', 'Rating']));

    select.value = '2';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(rowCount()).toBe(10);

    expect(root.querySelector('#player-panel-statistics dl')?.textContent).toContain('5');
    expect(root.querySelector('#player-panel-statistics dl')?.textContent).not.toContain('Goals7');
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
