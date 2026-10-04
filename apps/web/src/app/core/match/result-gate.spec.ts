import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ClubFixture, MyFixtures } from '../competition/competition.models';
import { CompetitionStore } from '../competition/competition-store';
import { ResultGate } from './result-gate';
import { ResultRevealStore } from './result-reveal-store';

/**
 * Which of the manager's own results are still to be seen.
 *
 * The gate is what every screen that could give a result away asks first, so the behaviour that matters is that
 * it hides while it does not know, hides while a result of the manager's own is unseen, and lets go the moment
 * the manager has watched the match or asked for it. Another club's match is never held back.
 */
describe('ResultGate', () => {
  const OWN_DIVISION = 'division-1';

  function fixture(overrides: Partial<ClubFixture>): ClubFixture {
    return {
      id: 'fixture-1',
      roundNumber: 1,
      venue: 'home',
      opponentClubId: 'club-2',
      opponentName: 'Vale Athletic',
      opponentShortName: 'VAL',
      kickoffAt: '2026-10-06T19:00:00Z',
      lockAt: '2026-10-06T18:00:00Z',
      status: 'published',
      homeScore: 2,
      awayScore: 1,
      matchId: 'match-1',
      outcome: 'win',
      ...overrides,
    };
  }

  function fixtures(list: readonly ClubFixture[]): MyFixtures {
    return {
      clubId: 'club-1',
      clubName: 'Ashfield Rovers',
      clubShortName: 'ASH',
      divisionId: OWN_DIVISION,
      divisionName: 'English Tier 1',
      tierNumber: 1,
      seasonNumber: 1,
      seasonLabel: '2026/27',
      nextFixtureId: null,
      fixtures: list,
      serverTime: '2026-10-07T12:00:00Z',
    };
  }

  let mine: ReturnType<typeof signal<MyFixtures | null>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let failure: ReturnType<typeof signal<string | null>>;
  let loadFixtures: ReturnType<typeof vi.fn>;

  function gate(): ResultGate {
    return TestBed.inject(ResultGate);
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();

    mine = signal<MyFixtures | null>(null);
    loading = signal(false);
    failure = signal<string | null>(null);
    loadFixtures = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        {
          provide: CompetitionStore,
          useValue: {
            fixtures: mine,
            fixturesLoading: loading,
            fixturesError: failure,
            loadFixtures,
          },
        },
      ],
    });
  });

  it('hides while the fixture list has not been read, so nothing is shown first and hidden after', () => {
    expect(gate().settled()).toBe(false);
    expect(gate().hiding()).toBe(true);
    expect(gate().holdsBack(OWN_DIVISION)).toBe(true);
    expect(gate().holdsBack('another-division')).toBe(true);
  });

  it('hides while the list is being read again, even when an older one is held', () => {
    mine.set(fixtures([]));
    loading.set(true);

    expect(gate().hiding()).toBe(true);
  });

  it('lets go when the list could not be read, rather than hiding for good', () => {
    failure.set('Your fixtures could not be loaded.');

    expect(gate().settled()).toBe(true);
    expect(gate().hiding()).toBe(false);
  });

  it('holds nothing back from a manager who has played nothing', () => {
    mine.set(
      fixtures([fixture({ status: 'scheduled', homeScore: null, awayScore: null, matchId: null })]),
    );

    expect(gate().hiding()).toBe(false);
    expect(gate().unseen()).toEqual([]);
  });

  it('holds back while a published match of the manager is unseen', () => {
    mine.set(fixtures([fixture({})]));

    expect(gate().hiding()).toBe(true);
    expect(gate().unseen()).toHaveLength(1);
    expect(gate().isHidden('match-1')).toBe(true);
  });

  it("never holds back another club's match", () => {
    mine.set(fixtures([fixture({})]));

    expect(gate().isHidden('someone-elses-match')).toBe(false);
    expect(gate().isHidden(null)).toBe(false);
  });

  it("holds back the manager's own division and not another one", () => {
    mine.set(fixtures([fixture({})]));

    expect(gate().holdsBack(OWN_DIVISION)).toBe(true);
    expect(gate().holdsBack(null)).toBe(true);
    expect(gate().holdsBack('another-division')).toBe(false);
  });

  it('lets go of one match when it has been shown, and of nothing else', () => {
    mine.set(
      fixtures([fixture({}), fixture({ id: 'fixture-2', roundNumber: 2, matchId: 'match-2' })]),
    );

    gate().reveal('match-1');

    expect(gate().isHidden('match-1')).toBe(false);
    expect(gate().isHidden('match-2')).toBe(true);
    expect(gate().hiding()).toBe(true);
  });

  it('lets go of everything when every unseen result is shown', () => {
    mine.set(
      fixtures([fixture({}), fixture({ id: 'fixture-2', roundNumber: 2, matchId: 'match-2' })]),
    );

    gate().revealAll();

    expect(gate().unseen()).toEqual([]);
    expect(gate().hiding()).toBe(false);
  });

  it('lets go of a match the manager watched, which the viewer records under the same id', () => {
    mine.set(fixtures([fixture({})]));

    TestBed.inject(ResultRevealStore).reveal('match-1');

    expect(gate().hiding()).toBe(false);
  });

  it('reads the fixture list again unless it is already being read', () => {
    gate().refresh();

    expect(loadFixtures).toHaveBeenCalledTimes(1);

    loading.set(true);
    gate().refresh();

    expect(loadFixtures).toHaveBeenCalledTimes(1);
  });
});
