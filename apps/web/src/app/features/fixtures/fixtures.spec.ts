import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
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

  async function render(fixtures: readonly ClubFixture[]): Promise<HTMLElement> {
    mine.set(list(fixtures));
    component = TestBed.createComponent(Fixtures);
    await component.whenStable();

    return component.nativeElement as HTMLElement;
  }

  function button(root: HTMLElement, text: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.trim().startsWith(text),
    );
  }

  beforeEach(async () => {
    localStorage.clear();
    mine = signal<MyFixtures | null>(null);

    await TestBed.configureTestingModule({
      imports: [Fixtures],
      providers: [
        provideRouter([]),
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

    button(root, 'Show result')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('3–0');
    expect(root.textContent).not.toContain('0–2');
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
});
