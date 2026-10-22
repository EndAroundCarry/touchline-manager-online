import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import {
  Player,
  PlayerMatchStat,
  PlayerSeasonStats,
  ReleaseQuote,
} from '../../core/squad/squad.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { MyFixtures } from '../../core/competition/competition.models';
import { SquadStore } from '../../core/squad/squad-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
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
    tacklesAttempted: 60,
    tacklesWon: 36,
    crossesAttempted: 30,
    crossesCompleted: 9,
    penaltiesTaken: 4,
    penaltiesScored: 3,
    penaltiesFaced: 5,
    penaltiesSaved: 2,
    freeKicksTaken: 8,
    freeKicksScored: 1,
    shotsOnTargetFaced: 40,
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
    tacklesAttempted: 7,
    tacklesWon: 4,
    crossesAttempted: 5,
    crossesCompleted: 2,
    penaltiesTaken: 1,
    penaltiesScored: 1,
    penaltiesFaced: 2,
    penaltiesSaved: 1,
    freeKicksTaken: 3,
    freeKicksScored: 0,
    shotsOnTargetFaced: 6,
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
  club: {
    id: 'club-1',
    name: 'Ashvale United',
    shortName: 'ASH',
    primaryColour: '#b11f2a',
    secondaryColour: '#f3efe6',
    crestShape: 'round',
    crestPattern: 'stripes',
  },
  isOwnClub: true,
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
  attributeProgress: { finishing: 0.85, stamina: -0.4 },
  state: { condition: 90, fatigue: 10, morale: 70, matchSharpness: 80 },
  experience: { points: 47.5, level: 2, maxLevel: 5, skillBonusPercent: 2, nextLevelPoints: 60 },
  contract: {
    id: 'contract-1',
    startSeasonNumber: 2,
    endSeasonNumber: 4,
    seasonsRemaining: 2,
    seasonWageMinor: 1_200_000,
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

const releaseQuote: ReleaseQuote = {
  contractId: 'contract-1',
  playerId: 'player-1',
  remainingWeeks: 20,
  payoutMinor: 12_000_000,
  seasonWageMinor: 1_200_000,
  availableMinor: 50_000_000,
  joinsPoolAt: '2026-10-26T12:00:00Z',
  blockedReason: null,
  contractVersion: 4,
  serverTime: '2026-10-20T09:00:00Z',
};

/** A manager's season in which nothing has been played, so there is no result to keep back. */
const NOTHING_PLAYED: MyFixtures = {
  clubId: 'club-1',
  clubName: 'Ashvale United',
  clubShortName: 'ASH',
  divisionId: 'division-1',
  divisionName: 'English Tier 1',
  tierNumber: 1,
  seasonNumber: 1,
  seasonLabel: '2026/27',
  nextFixtureId: null,
  fixtures: [],
  serverTime: '2026-10-07T12:00:00Z',
};

describe('PlayerProfile', () => {
  let fixture: ComponentFixture<PlayerProfile>;
  let root: HTMLElement;
  let mine: WritableSignal<MyFixtures | null>;
  let playerState: WritableSignal<Player>;
  let releaseQuoteState: WritableSignal<ReleaseQuote | null>;
  let quoteRelease: ReturnType<typeof vi.fn>;
  let releasePlayer: ReturnType<typeof vi.fn>;
  let trainingStore: {
    history: WritableSignal<null>;
    loading: WritableSignal<boolean>;
    error: WritableSignal<null>;
    load: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    localStorage.clear();
    mine = signal<MyFixtures | null>(NOTHING_PLAYED);

    playerState = signal(player);
    releaseQuoteState = signal<ReleaseQuote | null>(null);

    quoteRelease = vi.fn(() => {
      releaseQuoteState.set(releaseQuote);

      return of(releaseQuote);
    });
    releasePlayer = vi.fn(() => {
      releaseQuoteState.set(null);

      return of({
        contractId: 'contract-1',
        playerId: 'player-1',
        payoutMinor: 12_000_000,
        remainingWeeks: 20,
        seasonWageSavedMinor: 1_200_000,
        joinsPoolAt: '2026-10-26T12:00:00Z',
        serverTime: '2026-10-20T09:00:00Z',
      });
    });

    const store = {
      player: playerState,
      playerMatches: signal(matches),
      loadPlayer: () => of(player),
      loadPlayerMatches: () => of(matches),
      releaseQuote: releaseQuoteState.asReadonly(),
      quoteRelease,
      releasePlayer,
      clearReleaseQuote: () => releaseQuoteState.set(null),
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
        {
          provide: OnboardingStore,
          useValue: {
            state: signal({
              tenure: {
                clubId: 'club-1',
                primaryColour: '#b11f2a',
                secondaryColour: '#f3efe6',
                crestShape: 'round',
                crestPattern: 'stripes',
              },
            }),
          },
        },
        {
          provide: CompetitionStore,
          useValue: {
            fixtures: mine,
            fixturesLoading: signal(false),
            fixturesError: signal<string | null>(null),
            loadFixtures: vi.fn(),
          },
        },
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

  it('shows how close a skill is to its next point beside its value, as in Finishing 17 (0.85)', () => {
    const rows = Array.from(root.querySelectorAll('app-attribute-value'));
    const finishing = rows.find((row) => row.textContent?.includes('Finishing'))!;
    const stamina = rows.find((row) => row.textContent?.includes('Stamina'))!;
    const passing = rows.find((row) => row.textContent?.includes('Passing'))!;

    expect(finishing.querySelector('[data-testid="attribute-progress"]')?.textContent?.trim()).toBe(
      '(0.85)',
    );
    expect(stamina.querySelector('[data-testid="attribute-progress"]')?.textContent?.trim()).toBe(
      '(−0.40)',
    );
    expect(passing.querySelector('[data-testid="attribute-progress"]')).toBeNull();
  });

  it('shows the player’s portrait under their name, with the skills to the right of it, in the club’s shirt', () => {
    const portrait = root.querySelector<HTMLElement>('[data-testid="player-portrait"]');
    const attributes = root.querySelector<HTMLElement>('[data-testid="attribute-rows"]');
    const name = root.querySelector('h1');

    expect(portrait).not.toBeNull();
    expect(attributes).not.toBeNull();
    expect(
      name!.compareDocumentPosition(portrait!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    // The portrait's column and the tabs' column are the two halves of one row, portrait first.
    const row = portrait!.parentElement!.parentElement!;
    const tabs = root.querySelector('[data-testid="player-tabs"]')!;

    expect(row.children).toHaveLength(2);
    expect(row.children[0].contains(portrait)).toBe(true);
    expect(row.children[1].contains(tabs)).toBe(true);
    expect(row.children[1].contains(attributes)).toBe(true);
    expect(portrait?.getAttribute('role')).toBe('img');
    expect(portrait?.getAttribute('aria-label')).toBe(`Portrait of ${player.fullName}`);
    expect(portrait?.querySelector('svg')).not.toBeNull();
    expect(portrait?.querySelector('path[fill="#b11f2a"]')).not.toBeNull();
    expect(portrait?.querySelector('path[stroke="#f3efe6"]')).not.toBeNull();
  });

  it('keeps the state measures under the portrait, in the same column', () => {
    const portrait = root.querySelector<HTMLElement>('[data-testid="player-portrait"]')!;
    const state = root.querySelector<HTMLElement>('#player-state-heading')!;

    expect(portrait.parentElement!.contains(state)).toBe(true);
    expect(state.textContent).toContain('State');
    expect(portrait.parentElement!.querySelectorAll('dt')).toHaveLength(4);
  });

  it('shows the experience beside the player’s details, with the level and what it adds in matches', () => {
    const box = root.querySelector<HTMLElement>('[data-testid="player-experience"]')!;

    expect(box.querySelector('[data-testid="experience-headline"]')?.textContent).toContain(
      'Level 2 · 47.5 / 100',
    );
    expect(box.querySelector('[data-testid="experience-bonus"]')?.textContent).toContain(
      '+2% skills in matches',
    );
    expect(box.querySelectorAll('[data-testid="experience-pips"] > span')).toHaveLength(5);
  });

  it('lays the attributes out as four columns side by side', () => {
    const grid = root.querySelector('[data-testid="attribute-rows"]')!;

    expect(grid.classList.contains('grid-cols-4')).toBe(true);
    expect(grid.querySelectorAll(':scope > section')).toHaveLength(4);
  });

  describe('releasing a player', () => {
    function button(text: string): HTMLButtonElement | undefined {
      return Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((candidate) =>
        candidate.textContent?.includes(text),
      );
    }

    async function ask(): Promise<void> {
      await openTab('Contract');
      button('Release player')!.click();
      await fixture.whenStable();
    }

    it('offers the release below the negotiation, and asks nothing until the manager does', async () => {
      await openTab('Contract');

      const section = root.querySelector('[data-testid="release-section"]')!;
      const negotiation = root.querySelector('#negotiation-note')!;

      expect(
        negotiation.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
      expect(section.textContent).toContain('half the wages left');
      expect(button('Release player')).toBeDefined();
      expect(quoteRelease).not.toHaveBeenCalled();
    });

    it('shows the payout, the wage saved and when the player joins the pool before anything is paid', async () => {
      await ask();

      const group = root.querySelector(
        '[role="group"][aria-labelledby="release-confirm-heading"]',
      )!;

      expect(quoteRelease).toHaveBeenCalledWith('contract-1');
      expect(group.textContent).toContain(`Release ${player.fullName}?`);
      expect(group.textContent).toContain('20 weeks of wages');
      expect(group.textContent).toContain('Payout, paid now');
      expect(group.textContent).toContain('Wage saved a season');
      expect(group.textContent).toContain('Joins the free-agent pool');
      expect(releasePlayer).not.toHaveBeenCalled();
    });

    it('releases under the quoted version and says where the player goes', async () => {
      await ask();

      button('Confirm release')!.click();
      await fixture.whenStable();

      expect(releasePlayer).toHaveBeenCalledWith('contract-1', 4);

      const message = root.querySelector('[data-testid="release-section"] [role="status"]')!;

      expect(message.textContent).toContain(`${player.fullName} was released`);
      expect(message.textContent).toContain('free-agent pool');
      expect(root.querySelector('[data-testid="release-section"] a[href="/squad"]')).not.toBeNull();
      expect(button('Release player')).toBeUndefined();
    });

    it('lets the manager keep the player', async () => {
      await ask();

      button('Keep player')!.click();
      await fixture.whenStable();

      expect(releasePlayer).not.toHaveBeenCalled();
      expect(releaseQuoteState()).toBeNull();
      expect(button('Release player')).toBeDefined();
      expect(
        root.querySelector('[role="group"][aria-labelledby="release-confirm-heading"]'),
      ).toBeNull();
    });

    it('says why a release is refused and keeps the confirm button off', async () => {
      quoteRelease.mockImplementation(() => {
        const blocked = { ...releaseQuote, blockedReason: 'RELEASE_PLAYER_LISTED' };

        releaseQuoteState.set(blocked);

        return of(blocked);
      });

      await ask();

      const alert = root.querySelector('[role="group"] [role="alert"]')!;

      expect(alert.textContent).toContain('listed for transfer');
      expect(button('Confirm release')!.disabled).toBe(true);

      button('Confirm release')!.click();
      await fixture.whenStable();

      expect(releasePlayer).not.toHaveBeenCalled();
    });

    it('shows the server’s refusal and asks for a fresh quote next time when the contract has changed', async () => {
      releasePlayer.mockImplementation(() =>
        throwError(
          () =>
            new ApiError(
              412,
              'PRECONDITION_FAILED',
              'Reload the contract and try again.',
              null,
              new Map(),
            ),
        ),
      );

      await ask();

      button('Confirm release')!.click();
      await fixture.whenStable();

      expect(
        root.querySelector('[data-testid="release-section"] [role="alert"]')?.textContent,
      ).toContain('Reload the contract and try again.');
      expect(releaseQuoteState()).toBeNull();
      expect(button('Release player')).toBeDefined();
    });
  });

  it('hands the Training tab to the training component, which reads the player’s history', async () => {
    await openTab('Training');

    const panel = root.querySelector('#player-panel-training')!;

    expect(panel.querySelector('app-player-training')).not.toBeNull();
    expect(panel.textContent).not.toContain('Sample data');
    expect(trainingStore.load).toHaveBeenCalledWith('player-1');
  });

  describe("while a result of the manager's own is unseen", () => {
    beforeEach(() => {
      mine.set({
        ...NOTHING_PLAYED,
        fixtures: [
          {
            id: 'fixture-0',
            roundNumber: 1,
            venue: 'home',
            opponentClubId: 'club-2',
            opponentName: 'Vale Athletic',
            opponentShortName: 'VAL',
            opponentPrimaryColour: '#1f4e79',
            opponentSecondaryColour: '#d6e4f0',
            opponentCrestShape: 'round',
            opponentCrestPattern: 'sash',
            kickoffAt: '2026-10-06T19:00:00Z',
            lockAt: '2026-10-06T18:00:00Z',
            status: 'published',
            homeScore: 2,
            awayScore: 1,
            matchId: 'match-0',
            outcome: 'win',
          },
        ],
      });
    });

    it("holds the statistics back, since a player's figures count the goals of the match", async () => {
      await openTab('Statistics');

      expect(root.querySelector('[data-testid="result-hidden"]')).not.toBeNull();
      expect(root.querySelector('[data-testid="season-stats"]')).toBeNull();
      expect(root.querySelector('[data-testid="match-stats"]')).toBeNull();
    });

    it('shows them once the result has been asked for', async () => {
      await openTab('Statistics');

      Array.from(root.querySelectorAll('button'))
        .find((button) => button.textContent?.trim() === 'Show result')!
        .click();
      await fixture.whenStable();

      expect(root.querySelector('[data-testid="result-hidden"]')).toBeNull();
      expect(root.querySelector('[data-testid="season-stats"]')).not.toBeNull();
    });

    it('leaves the attributes and the contract alone', async () => {
      expect(root.querySelectorAll('app-attribute-value')).toHaveLength(28);

      await openTab('Contract');

      expect(root.querySelector('#player-panel-contract')).not.toBeNull();
    });
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

  it('shows an outfield player the tackles, crosses, penalties and free kicks, not the saves', async () => {
    await openTab('Statistics');

    const seasons = root.querySelector('[data-testid="season-stats"] table')!;
    const headings = Array.from(seasons.querySelectorAll('thead th')).map((cell) =>
      cell.textContent?.trim(),
    );

    expect(headings).toEqual(
      expect.arrayContaining(['Crosses', 'Tackles', 'Pens', 'Free kicks', 'Shots']),
    );
    expect(headings).not.toContain('Saves');
    expect(headings).not.toContain('Pens saved');

    const season = seasons.querySelector('tbody tr')!.textContent;

    expect(season).toContain('9/30');
    expect(season).toContain('36/60');
    expect(season).toContain('3/4');
    expect(season).toContain('1/8');

    const matchTable = root.querySelector('[data-testid="match-stats"]')!;
    const matchHeadings = Array.from(matchTable.querySelectorAll('th')).map((cell) =>
      cell.textContent?.trim(),
    );

    expect(matchHeadings).toEqual(expect.arrayContaining(['Crs', 'Tkl', 'Pen', 'FK', 'Shots']));
    expect(matchHeadings).not.toContain('Saves');

    const row = matchTable.querySelector('tbody tr')!.textContent;

    expect(row).toContain('2/5');
    expect(row).toContain('4/7');
    expect(row).toContain('1/1');
    expect(row).toContain('0/3');
  });

  it('shows a goalkeeper the saves out of the shots faced and the penalties saved', async () => {
    playerState.set({ ...player, primaryPosition: 'gk' });
    await openTab('Statistics');

    const seasons = root.querySelector('[data-testid="season-stats"] table')!;
    const headings = Array.from(seasons.querySelectorAll('thead th')).map((cell) =>
      cell.textContent?.trim(),
    );

    expect(headings).toEqual(expect.arrayContaining(['Saves', 'Save %', 'Pens saved']));
    expect(headings).not.toContain('Tackles');
    expect(headings).not.toContain('Dribbles');
    expect(headings).not.toContain('Shots');

    const season = seasons.querySelector('tbody tr')!.textContent;

    // Saves 0 of 40 shots on target, and 2 of 5 penalties saved.
    expect(season).toContain('0/40');
    expect(season).toContain('0%');
    expect(season).toContain('2/5');

    const matchTable = root.querySelector('[data-testid="match-stats"]')!;
    const matchHeadings = Array.from(matchTable.querySelectorAll('th')).map((cell) =>
      cell.textContent?.trim(),
    );

    expect(matchHeadings).toEqual(expect.arrayContaining(['Saves', 'Pens']));
    expect(matchHeadings).not.toContain('Dribbles');
    expect(matchHeadings).not.toContain('Shots');

    const row = matchTable.querySelector('tbody tr')!.textContent;

    expect(row).toContain('0/6');
    expect(row).toContain('1/2');
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
    expect(panel.textContent).toContain('a season');
    expect(button.disabled).toBe(true);
    expect(panel.textContent).toContain('coming soon');
  });
  describe('another club’s player', () => {
    const rival: Player = {
      ...player,
      clubId: 'club-9',
      club: {
        id: 'club-9',
        name: 'Vale Rovers',
        shortName: 'VAL',
        primaryColour: '#1f4e79',
        secondaryColour: '#d6e4f0',
        crestShape: 'shield',
        crestPattern: 'bands',
      },
      isOwnClub: false,
      state: null,
      contract: null,
      registration: null,
      availability: [],
      attributeProgress: {},
    };

    beforeEach(async () => {
      playerState.set(rival);
      await fixture.whenStable();
    });

    it('offers only the attributes and the statistics, never the training or the contract', () => {
      const tabs = Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));

      expect(tabs.map((tab) => tab.textContent?.trim())).toEqual(['Attributes', 'Statistics']);
      expect(root.querySelector('app-player-training')).toBeNull();
    });

    it('keeps the club’s state out of the page and dresses the portrait in the player’s own club’s shirt', () => {
      const portrait = root.querySelector<HTMLElement>('[data-testid="player-portrait"]')!;

      expect(root.querySelector('#player-state-heading')).toBeNull();
      expect(portrait.querySelector('path[fill="#1f4e79"]')).not.toBeNull();
      expect(portrait.querySelector('path[fill="#b11f2a"]')).toBeNull();
    });

    it('names the club with a link to its page, and goes back there rather than to the squad', () => {
      const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('a')).map((a) => [
        a.textContent?.trim(),
        a.getAttribute('href'),
      ]);

      expect(links).toContainEqual(['Back to Vale Rovers', '/clubs/club-9']);
      expect(links).toContainEqual(['Vale Rovers', '/clubs/club-9']);
      expect(links.some(([, href]) => href === '/squad')).toBe(false);
    });

    it('still shows the statistics, with the opponent of each match linked to its page', async () => {
      await openTab('Statistics');

      expect(root.querySelector('[data-testid="season-stats"]')).not.toBeNull();

      const opponent = Array.from(
        root.querySelectorAll<HTMLAnchorElement>('[data-testid="match-stats"] a'),
      ).find((a) => a.textContent?.trim() === 'Vale Rovers');

      expect(opponent?.getAttribute('href')).toBe('/clubs/club-2');
    });
  });

  describe('a free agent', () => {
    const freeAgent: Player = {
      ...player,
      clubId: null,
      club: null,
      isOwnClub: false,
      state: null,
      contract: null,
      registration: null,
      availability: [],
      attributeProgress: {},
    };

    beforeEach(async () => {
      playerState.set(freeAgent);
      await fixture.whenStable();
    });

    it('says free agent where the club would be, with no club link or crest', () => {
      const club = root.querySelector<HTMLElement>('[data-testid="player-club"]')!;

      expect(club.textContent?.trim()).toBe('Free agent');
      expect(club.querySelector('a')).toBeNull();
      expect(club.querySelector('app-club-crest')).toBeNull();
    });

    it('goes back to the transfers page rather than to a club or the squad', () => {
      const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('a')).map((a) => [
        a.textContent?.trim(),
        a.getAttribute('href'),
      ]);

      expect(links).toContainEqual(['Back to transfers', '/transfers']);
      expect(links.some(([, href]) => href === '/squad')).toBe(false);
    });

    it('offers the public tabs and dresses the portrait in the neutral shirt', () => {
      const tabs = Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));

      expect(tabs.map((tab) => tab.textContent?.trim())).toEqual(['Attributes', 'Statistics']);
      expect(root.querySelector('[data-testid="player-portrait"] path[fill="#b11f2a"]')).toBeNull();
    });
  });
});
