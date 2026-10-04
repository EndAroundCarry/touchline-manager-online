import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
import { NewsItem } from '../../core/news/news.models';
import { NewsStore } from '../../core/news/news-store';
import { News } from './news';

/**
 * The news feed keeps a played match's score back until the manager has watched it or asks for it.
 *
 * Only the manager's own matches are protected. A result that names its match is held back while that match is
 * one of the manager's own and unseen; a result that does not name its match cannot be told from one of theirs, so
 * it waits for a request. Everything else in the feed is shown as it was.
 */
describe('News', () => {
  const OWN_MATCH = 'match-own';

  function result(overrides: Partial<NewsItem>): NewsItem {
    return {
      id: 'news-1',
      category: 'result',
      countryId: null,
      divisionId: 'division-1',
      title: 'Round 4: Ashfield Rovers v Vale Athletic',
      body: 'The result is in.',
      publishedAt: '2026-10-06T19:00:00Z',
      spoiler: 'Ashfield Rovers 2–1 Vale Athletic',
      matchId: OWN_MATCH,
      ...overrides,
    };
  }

  function ownFixture(): ClubFixture {
    return {
      id: 'fixture-own',
      roundNumber: 4,
      venue: 'home',
      opponentClubId: 'club-2',
      opponentName: 'Vale Athletic',
      opponentShortName: 'VAL',
      kickoffAt: '2026-10-06T19:00:00Z',
      lockAt: '2026-10-06T18:00:00Z',
      status: 'published',
      homeScore: 2,
      awayScore: 1,
      matchId: OWN_MATCH,
      outcome: 'win',
    };
  }

  let component: ComponentFixture<News>;
  let items: ReturnType<typeof signal<readonly NewsItem[]>>;
  let mine: ReturnType<typeof signal<MyFixtures | null>>;

  async function render(feed: readonly NewsItem[]): Promise<HTMLElement> {
    items.set(feed);
    component = TestBed.createComponent(News);
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
    items = signal<readonly NewsItem[]>([]);
    mine = signal<MyFixtures | null>({
      clubId: 'club-1',
      clubName: 'Ashfield Rovers',
      clubShortName: 'ASH',
      divisionId: 'division-1',
      divisionName: 'English Tier 1',
      tierNumber: 1,
      seasonNumber: 1,
      seasonLabel: '2026/27',
      nextFixtureId: null,
      fixtures: [ownFixture()],
      serverTime: '2026-10-07T12:00:00Z',
    });

    await TestBed.configureTestingModule({
      imports: [News],
      providers: [
        provideRouter([]),
        {
          provide: NewsStore,
          useValue: {
            items,
            loading: signal(false),
            loadingMore: signal(false),
            error: signal<string | null>(null),
            hasMore: signal(false),
            load: vi.fn(),
            loadMore: vi.fn(),
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
  });

  it('names the match of the manager without its score', async () => {
    const root = await render([result({})]);

    expect(root.textContent).toContain('Round 4: Ashfield Rovers v Vale Athletic');
    expect(root.textContent).toContain('The result is in.');
    expect(root.textContent).not.toContain('2–1');
    expect(button(root, 'Show result')).toBeDefined();
  });

  it('shows the score on request, and remembers it under the match for the viewer', async () => {
    const root = await render([result({})]);

    button(root, 'Show result')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('Ashfield Rovers 2–1 Vale Athletic');
    expect(TestBed.inject(ResultRevealStore).isRevealed(OWN_MATCH)).toBe(true);
  });

  it('shows the score of a match the manager has watched', async () => {
    TestBed.inject(ResultRevealStore).reveal(OWN_MATCH);

    const root = await render([result({})]);

    expect(root.textContent).toContain('Ashfield Rovers 2–1 Vale Athletic');
    expect(button(root, 'Show result')).toBeUndefined();
  });

  it("does not hold back another club's result, which the manager is not waiting to watch", async () => {
    const root = await render([
      result({
        id: 'news-2',
        title: 'Round 4: Northfield v Harbour Town',
        spoiler: 'Northfield 0–0 Harbour Town',
        matchId: 'match-other',
      }),
    ]);

    expect(root.textContent).toContain('Northfield 0–0 Harbour Town');
    expect(button(root, 'Show result')).toBeUndefined();
  });

  it("holds back a result that does not name its match, since it could be the manager's own", async () => {
    const root = await render([result({ id: 'legacy', matchId: null })]);

    expect(root.textContent).not.toContain('2–1');

    button(root, 'Show result')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('2–1');
  });

  it("holds back every result until the manager's fixtures are known", async () => {
    mine.set(null);

    const root = await render([
      result({ id: 'news-2', matchId: 'match-other', spoiler: 'Northfield 0–0 Harbour Town' }),
    ]);

    expect(root.textContent).not.toContain('Northfield 0–0 Harbour Town');
  });

  it('shows every held-back result at once', async () => {
    const root = await render([
      result({}),
      result({ id: 'legacy', matchId: null, spoiler: 'Vale Athletic 1–1 Northfield' }),
    ]);

    button(root, 'Show all results')!.click();
    await component.whenStable();

    expect(root.textContent).toContain('Ashfield Rovers 2–1 Vale Athletic');
    expect(root.textContent).toContain('Vale Athletic 1–1 Northfield');
  });

  it('leaves an item with nothing to hide alone', async () => {
    const root = await render([
      {
        id: 'transfer',
        category: 'transfer',
        countryId: null,
        divisionId: null,
        title: 'Ion Popescu moves',
        body: 'Vale Athletic has signed Ion Popescu from Northfield for 12,500,000.',
        publishedAt: '2026-10-06T19:00:00Z',
        spoiler: null,
        matchId: null,
      },
    ]);

    expect(root.textContent).toContain('Ion Popescu moves');
    expect(button(root, 'Show result')).toBeUndefined();
  });
});
