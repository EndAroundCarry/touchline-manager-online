import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { FanNextFixture } from '../../core/fans/fan.models';
import { FanStore } from '../../core/fans/fan-store';
import { clubFixture, expectation } from '../../core/fans/fan-test-data';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { Fixtures } from './fixtures';

/**
 * The fixtures screen keeps a played match's result back until the manager has watched it or asks for it.
 *
 * Each result row names the match and offers the result; the scoreline and the outcome appear only on
 * "Show result", and a match watched in the viewer is shown without asking. "Show all results" clears a backlog.
 */
describe('Fixtures', () => {
  function played(
    id: string,
    round: number,
    opponent: string,
    home: number,
    away: number,
  ): ClubFixture {
    return {
      id,
      roundNumber: round,
      venue: 'home',
      opponentClubId: `club-${id}`,
      opponentName: opponent,
      opponentShortName: opponent.slice(0, 3).toUpperCase(),
      opponentPrimaryColour: '#1f4e79',
      opponentSecondaryColour: '#d6e4f0',
      opponentCrestShape: 'round',
      opponentCrestPattern: 'sash',
      kickoffAt: '2026-10-06T19:00:00Z',
      lockAt: '2026-10-06T18:00:00Z',
      status: 'published',
      homeScore: home,
      awayScore: away,
      matchId: `match-${id}`,
      outcome: home > away ? 'win' : home < away ? 'loss' : 'draw',
    };
  }

  function list(fixtures: readonly ClubFixture[]): MyFixtures {
    return {
      clubId: 'club-1',
      clubName: 'Ashfield Rovers',
      clubShortName: 'ASH',
      divisionId: 'division-1',
      divisionName: 'English Tier 1',
      tierNumber: 1,
      seasonNumber: 1,
      seasonLabel: '2026/27',
      nextFixtureId: null,
      fixtures,
      serverTime: '2026-10-07T12:00:00Z',
    };
  }

  let component: ComponentFixture<Fixtures>;
  let mine: ReturnType<typeof signal<MyFixtures | null>>;
  let fanNext: ReturnType<typeof signal<FanNextFixture | null>>;

  /** Renders the screen with the Results tab open, which is where the played matches are. */
  async function render(fixtures: readonly ClubFixture[]): Promise<HTMLElement> {
    const root = await renderOnUpcoming(fixtures);

    tab(root, 'Results')!.click();
    await component.whenStable();

    return root;
  }

  async function renderOnUpcoming(fixtures: readonly ClubFixture[]): Promise<HTMLElement> {
    mine.set(list(fixtures));
    component = TestBed.createComponent(Fixtures);
    await component.whenStable();

    return component.nativeElement as HTMLElement;
  }

  function tab(root: HTMLElement, text: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((candidate) =>
      candidate.textContent?.trim().startsWith(text),
    );
  }

  function scheduled(id: string, round: number, opponent: string): ClubFixture {
    return {
      ...played(id, round, opponent, 0, 0),
      status: 'scheduled',
      homeScore: null,
      awayScore: null,
      matchId: null,
      outcome: null,
    };
  }

  function button(root: HTMLElement, text: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.trim().startsWith(text),
    );
  }

  beforeEach(async () => {
    localStorage.clear();
    mine = signal<MyFixtures | null>(null);
    fanNext = signal<FanNextFixture | null>(null);

    await TestBed.configureTestingModule({
      imports: [Fixtures],
      providers: [
        provideRouter([]),
        { provide: OnboardingStore, useValue: { state: signal(null) } },
        { provide: FanStore, useValue: { nextFixture: fanNext, load: vi.fn() } },
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
  });

  it('names a played match without giving its result', async () => {
    const root = await render([played('a', 1, 'Vale Athletic', 3, 0)]);

    expect(root.textContent).toContain('Vale Athletic');
    expect(root.textContent).not.toContain('3–0');
    expect(root.textContent).not.toContain('Win');
    expect(button(root, 'Show result'), 'the result is one click away').toBeDefined();
  });

  it('keeps the way to watch the match beside the held-back result', async () => {
    const root = await render([played('a', 1, 'Vale Athletic', 3, 0)]);
    const watch = root.querySelector('a[href="/matches/match-a"]');

    expect(watch).not.toBeNull();
  });

  it('shows the result on request, and no longer offers to', async () => {
    const root = await render([played('a', 1, 'Vale Athletic', 3, 0)]);

    button(root, 'Show result')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('3–0');
    expect(button(root, 'Show result')).toBeUndefined();
  });

  it('shows one result without showing another', async () => {
    const root = await render([
      played('a', 1, 'Vale Athletic', 3, 0),
      played('b', 2, 'Northfield', 0, 2),
    ]);

    // The latest round is listed first, so the first button is Northfield's.
    button(root, 'Show result')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('0–2');
    expect(root.textContent).not.toContain('3–0');
    expect(button(root, 'Show result'), 'the other match still waits').toBeDefined();
  });

  it('shows a match already watched in the viewer without asking', async () => {
    TestBed.inject(ResultRevealStore).reveal('match-a');

    const root = await render([played('a', 1, 'Vale Athletic', 3, 0)]);

    expect(root.textContent).toContain('3–0');
    expect(button(root, 'Show result')).toBeUndefined();
  });

  it('offers to show every result at once when more than one is held back', async () => {
    const root = await render([
      played('a', 1, 'Vale Athletic', 3, 0),
      played('b', 2, 'Northfield', 0, 2),
    ]);

    button(root, 'Show all results')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('3–0');
    expect(root.textContent).toContain('0–2');
    expect(button(root, 'Show all results')).toBeUndefined();
  });

  it('does not offer "show all" for a single result', async () => {
    const root = await render([played('a', 1, 'Vale Athletic', 3, 0)]);

    expect(button(root, 'Show all results')).toBeUndefined();
  });

  describe('what the fans expect from the next fixture', () => {
    async function renderWithNext(): Promise<HTMLElement> {
      mine.set({ ...list([scheduled('b', 2, 'Northfield')]), nextFixtureId: 'b' });
      component = TestBed.createComponent(Fixtures);
      await component.whenStable();

      return component.nativeElement as HTMLElement;
    }

    it('shows the label, the chances and the predicted score in the next fixture card', async () => {
      fanNext.set({
        fixture: clubFixture({ id: 'b' }),
        expectation: expectation(),
        frozen: false,
      });

      const root = await renderWithNext();
      const card = root.querySelector('[data-testid="next-fixture"]')!;

      expect(card.textContent).toContain('Should win');
      expect(card.textContent).toContain('58% win, 24% draw, 18% loss');
      expect(card.textContent).toContain('can still change');
    });

    it('shows nothing before the fans are read, or for a different fixture', async () => {
      const root = await renderWithNext();
      expect(root.querySelector('app-fan-expectation-chip')).toBeNull();

      fanNext.set({
        fixture: clubFixture({ id: 'not-the-next-one' }),
        expectation: expectation(),
        frozen: true,
      });
      await component.whenStable();

      expect(root.querySelector('app-fan-expectation-chip')).toBeNull();
    });
  });

  it('opens on the upcoming fixtures, with the results one tab away and counted', async () => {
    const root = await renderOnUpcoming([
      played('a', 1, 'Vale Athletic', 3, 0),
      scheduled('b', 2, 'Northfield'),
    ]);

    expect(tab(root, 'Upcoming')!.getAttribute('aria-selected')).toBe('true');
    expect(tab(root, 'Results')!.textContent).toContain('1');
    expect(root.textContent).toContain('Northfield');
    expect(root.textContent).not.toContain('Vale Athletic');
  });

  it('lists the latest result first', async () => {
    const root = await render([
      played('a', 1, 'Vale Athletic', 3, 0),
      played('b', 2, 'Northfield', 0, 2),
    ]);
    const text = root.querySelector('[role="tabpanel"]')!.textContent!;

    expect(text.indexOf('Northfield')).toBeLessThan(text.indexOf('Vale Athletic'));
  });

  it('moves between the tabs with the arrow keys', async () => {
    const root = await renderOnUpcoming([played('a', 1, 'Vale Athletic', 3, 0)]);

    tab(root, 'Upcoming')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    await component.whenStable();

    expect(tab(root, 'Results')!.getAttribute('aria-selected')).toBe('true');
  });
});
