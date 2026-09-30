import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { FinanceStore } from '../../core/finance/finance-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { ClubDashboard, OnboardingState } from '../../core/world/world.models';
import { Dashboard } from './dashboard';

/**
 * The dashboard's first-steps guidance (`F-53`, master plan §16 Stage 13).
 *
 * The guidance is a helper, not a gate, so the claims worth pinning are that it is there for a manager who
 * holds a club, that its team-sheet step only appears when a fixture is actually waiting, and that
 * dismissing it removes it without stranding the keyboard.
 */

const STATE: OnboardingState = {
  manager: {
    id: 'manager-1',
    reputation: 70,
    takeoverCooldownUntil: null,
    locale: 'en-GB',
    timeZone: 'Europe/London',
    version: 1,
  },
  tenure: {
    id: 'tenure-1',
    clubId: 'club-1',
    clubName: 'Ashfield Rovers',
    clubShortName: 'ASH',
    countryDisplayName: 'England',
    divisionName: 'English Tier 1',
    tierNumber: 1,
    controlStatus: 'active',
    startedAt: '2026-09-01T10:00:00Z',
    lastActiveAt: '2026-09-02T10:00:00Z',
    version: 1,
  },
  serverTime: '2026-09-30T12:00:00Z',
};

const DASHBOARD: ClubDashboard = {
  club: {
    id: 'club-1',
    name: 'Ashfield Rovers',
    shortName: 'ASH',
    slug: 'ashfield-rovers',
    city: 'Ashfield',
    region: 'Midlands',
    badgeSeed: 'badge-1',
    foundingGameYear: 1901,
    status: 'active',
    reputation: 70,
    stadiumBaseline: 25_000_000,
  },
  country: { id: 'country-1', code: 'ENG', displayName: 'England', locale: 'en-GB', sortOrder: 1 },
  division: {
    id: 'division-1',
    tierNumber: 1,
    displayName: 'English Tier 1',
    status: 'active',
    capacity: 18,
  },
  season: {
    id: 'season-1',
    sequenceNumber: 1,
    displayLabel: '2026/27',
    gameYear: 1,
    status: 'active',
    ruleSetVersion: '2',
    startsAt: '2026-08-01T00:00:00Z',
    endsAt: '2026-12-01T00:00:00Z',
    rolloverEndsAt: '2026-12-08T00:00:00Z',
  },
  control: {
    status: 'active',
    tenureId: 'tenure-1',
    startedAt: '2026-09-01T10:00:00Z',
    lastActiveAt: '2026-09-02T10:00:00Z',
  },
  finances: { cashMinor: 50_000_000, reservedMinor: 0, availableMinor: 50_000_000 },
  serverTime: '2026-09-30T12:00:00Z',
};

const NEXT_FIXTURE: ClubFixture = {
  id: 'fixture-1',
  roundNumber: 1,
  venue: 'home',
  opponentClubId: 'club-2',
  opponentName: 'Barrow Town',
  opponentShortName: 'BAR',
  kickoffAt: '2026-10-06T19:00:00Z',
  lockAt: '2026-10-06T18:30:00Z',
  status: 'scheduled',
  homeScore: null,
  awayScore: null,
  matchId: null,
  outcome: null,
};

const FIXTURES: MyFixtures = {
  clubId: 'club-1',
  clubName: 'Ashfield Rovers',
  clubShortName: 'ASH',
  divisionId: 'division-1',
  divisionName: 'English Tier 1',
  tierNumber: 1,
  seasonNumber: 1,
  seasonLabel: '2026/27',
  nextFixtureId: 'fixture-1',
  fixtures: [NEXT_FIXTURE],
  serverTime: '2026-09-30T12:00:00Z',
};

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let fixtures: WritableSignal<MyFixtures | null>;

  beforeEach(async () => {
    fixtures = signal<MyFixtures | null>(FIXTURES);

    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [
        provideRouter([]),
        {
          provide: OnboardingStore,
          useValue: {
            state: signal(STATE),
            loadState: () => of(STATE),
            dashboard: () => of(DASHBOARD),
            resign: () => of(DASHBOARD),
          },
        },
        { provide: CompetitionStore, useValue: { fixtures, loadFixtures: vi.fn() } },
        { provide: FinanceStore, useValue: { summary: signal(null), loadSummary: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
  });

  async function render(): Promise<HTMLElement> {
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  function dismissButton(element: HTMLElement): HTMLButtonElement {
    const match = [...element.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Hide these first steps',
    );

    if (match === undefined) {
      throw new Error('No dismiss control for the first steps');
    }

    return match;
  }

  it('offers the first steps to a manager who holds a club', async () => {
    const element = await render();

    expect(element.textContent).toContain('Your first steps');
    expect(element.textContent).toContain('Read how the game works');
    expect(element.textContent).toContain('Check your squad is legal');
    expect(element.textContent).toContain('Set your formation and instructions');
    expect(element.textContent).toContain('Prepare your next team sheet');
  });

  it('drops the team-sheet step when no fixture is waiting', async () => {
    fixtures.set(null);

    const element = await render();

    expect(element.textContent).toContain('Your first steps');
    expect(element.textContent).not.toContain('Prepare your next team sheet');
  });

  it('hides the guidance on dismissal and moves focus off the removed control', async () => {
    const before = await render();

    dismissButton(before).click();

    const after = await render();

    expect(after.textContent).not.toContain('Your first steps');
    // The card held the control that was activated, so focus has to land somewhere deliberate rather than
    // on the document body.
    expect(document.activeElement).toBe(after.querySelector('h1'));
  });
});
