import { Component, input, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { MyFixtures } from '../../core/competition/competition.models';
import { CompetitionStore } from '../../core/competition/competition-store';
import { FanBase } from '../../core/fans/fan.models';
import { FanStore } from '../../core/fans/fan-store';
import {
  clubFixture,
  expectation,
  fanBase,
  fanEvent,
  recentMatch,
} from '../../core/fans/fan-test-data';
import { configurePresentation, resetPresentation } from '../../core/world/presentation';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { LineChart, LineChartSeries } from '../../shared/ui/line-chart/line-chart';
import { FanBasePage } from './fan-base';

/**
 * The Fan Base page.
 *
 * What it must get right: the numbers are the server's, with the floor and cap beside the count; the next match shows
 * what the fans expect and whether that is final; the recent matches set what was expected beside what happened; the
 * history can be switched between the manager's tenure and the whole club; and, because a fan's reaction is a result in
 * disguise, nothing that follows a match the manager has not seen is shown until they have seen it.
 */

/**
 * Stands in for the chart so the specs read what the page hands it instead of what Chart.js paints; jsdom has no
 * canvas, and the chart's own spec covers the drawing.
 */
@Component({
  selector: 'app-line-chart',
  template: '<canvas role="img" [attr.aria-label]="ariaLabel()"></canvas>',
})
class StubLineChart {
  readonly labels = input.required<readonly string[]>();
  readonly series = input.required<readonly LineChartSeries[]>();
  readonly ariaLabel = input.required<string>();
  readonly yTitle = input('');
  readonly yMin = input<number | null>(null);
  readonly yMax = input<number | null>(null);
}

function myFixtures(...unseen: readonly string[]): MyFixtures {
  return {
    clubId: 'club-1',
    clubName: 'Harbour Town',
    clubShortName: 'HAR',
    divisionId: 'division-1',
    divisionName: 'Premier',
    tierNumber: 1,
    seasonNumber: 1,
    seasonLabel: '2026/27',
    nextFixtureId: 'fixture-1',
    fixtures: unseen.map((id) =>
      clubFixture({
        id,
        status: 'published',
        matchId: `match-${id}`,
        homeScore: 2,
        awayScore: 0,
        outcome: 'win',
      }),
    ),
    serverTime: '2026-10-15T09:00:00Z',
  };
}

describe('FanBasePage', () => {
  let fixture: ComponentFixture<FanBasePage>;
  let fans: ReturnType<typeof signal<FanBase | null>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let error: ReturnType<typeof signal<string | null>>;
  let mine: ReturnType<typeof signal<MyFixtures | null>>;
  let store: { fans: unknown; loading: unknown; error: unknown; load: ReturnType<typeof vi.fn> };
  let loadFixtures: ReturnType<typeof vi.fn>;

  async function render(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(FanBasePage);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  const text = (root: HTMLElement, testId: string) =>
    root.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    localStorage.clear();
    configurePresentation({ locale: 'en-GB', timeZone: 'UTC' });

    fans = signal<FanBase | null>(fanBase());
    loading = signal(false);
    error = signal<string | null>(null);
    mine = signal<MyFixtures | null>(myFixtures());
    loadFixtures = vi.fn();
    store = { fans, loading, error, load: vi.fn() };

    TestBed.overrideComponent(FanBasePage, {
      remove: { imports: [LineChart] },
      add: { imports: [StubLineChart] },
    });

    await TestBed.configureTestingModule({
      imports: [FanBasePage],
      providers: [
        provideRouter([]),
        { provide: FanStore, useValue: store },
        { provide: OnboardingStore, useValue: { state: signal(null) } },
        {
          provide: CompetitionStore,
          useValue: {
            fixtures: mine,
            fixturesLoading: signal(false),
            fixturesError: signal<string | null>(null),
            loadFixtures,
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => resetPresentation());

  it('reads the fans and the fixture list when it opens', async () => {
    await render();

    expect(store.load).toHaveBeenCalledWith('tenure');
    expect(loadFixtures).toHaveBeenCalled();
  });

  it('shows the count with its floor and cap, the mood word and the expected crowd', async () => {
    const root = await render();

    expect(text(root, 'fan-count')).toBe('10,450');
    expect(root.querySelector('[data-testid="fan-summary"]')?.textContent).toContain('4,000');
    expect(root.querySelector('[data-testid="fan-summary"]')?.textContent).toContain('183,333');
    expect(text(root, 'fan-mood')).toContain('Calm');
    expect(text(root, 'fan-mood')).toContain('53 / 100');
    expect(text(root, 'fan-crowd')).toBe('3,200');
    expect(text(root, 'fan-season-change')).toContain('+450 fans');
  });

  it('says what the fans expect from the next match, and that it is final once the sheets locked', async () => {
    const root = await render();
    const next = root.querySelector('[data-testid="fan-next"]')!;

    expect(next.textContent).toContain('Vale Athletic');
    expect(next.textContent).toContain('Should win');
    expect(next.textContent).toContain('58% win, 24% draw, 18% loss');
    expect(next.textContent).toContain('Fixed when the team sheets locked');
  });

  it('says a live expectation can still move', async () => {
    fans.set(
      fanBase({
        nextFixture: { fixture: clubFixture(), expectation: expectation(), frozen: false },
      }),
    );

    const root = await render();

    expect(root.querySelector('[data-testid="fan-next"]')?.textContent).toContain(
      'fixed when the team sheets lock',
    );
  });

  it('says so when the season has no match left', async () => {
    fans.set(fanBase({ nextFixture: null }));

    const root = await render();

    expect(root.querySelector('[data-testid="fan-next"]')?.textContent).toContain(
      'no fixtures left',
    );
  });

  it('lists the recent matches with what was expected beside what happened', async () => {
    const root = await render();
    const row = root.querySelector('[data-testid="fan-recent-matches"] tbody tr')!;

    expect(row.textContent).toContain('Northfield');
    expect(row.textContent).toContain('Should win');
    expect(row.textContent).toContain('2–0');
    expect(row.textContent).toContain('Win');
    expect(row.textContent).toContain('+2.8');
    expect(row.textContent).toContain('+450');
  });

  it('shows a dash where no change was recorded for a match locked before fans existed', async () => {
    fans.set(
      fanBase({
        recentMatches: [
          recentMatch({
            expectation: null,
            fansBefore: null,
            fansAfter: null,
            moodBeforeBp: null,
            moodAfterBp: null,
          }),
        ],
      }),
    );

    const root = await render();
    const row = root.querySelector('[data-testid="fan-recent-matches"] tbody tr')!;

    expect(row.textContent).toContain('Not recorded');
    expect(row.querySelectorAll('td')[2].textContent?.trim()).toBe('—');
  });

  it('draws both charts with a data table of the points behind them', async () => {
    const root = await render();

    const charts = [...root.querySelectorAll('app-line-chart')];

    expect(charts).toHaveLength(2);
    expect(
      root.querySelector('[data-testid="fan-satisfaction-chart"] app-line-chart'),
    ).not.toBeNull();
    expect(root.querySelector('[data-testid="fan-count-chart"] app-line-chart')).not.toBeNull();
    expect(root.querySelectorAll('[data-testid="fan-points"] tbody tr')).toHaveLength(2);
    expect(root.querySelector('[data-testid="fan-points"]')?.textContent).toContain('New manager');
  });

  it('hands the charts the season’s satisfaction on a 0–100 scale and the fans with their floor', async () => {
    await render();

    const [satisfaction, count] = fixture.debugElement
      .queryAll(By.directive(StubLineChart))
      .map((chart) => chart.componentInstance as StubLineChart);

    expect(satisfaction.yMin()).toBe(0);
    expect(satisfaction.yMax()).toBe(100);
    expect(satisfaction.series().map((line) => line.label)).toEqual([
      'Satisfaction',
      'Calm from 40',
      'Content from 60',
    ]);
    expect(satisfaction.series()[0].data).toEqual([50, 52.8]);
    expect(count.series().map((line) => line.label)).toEqual(['Fans', 'Floor', 'New manager']);
    expect(count.series()[1].data).toEqual([4_000, 4_000]);
  });

  it('asks for the whole club history on request, keeping the page on screen', async () => {
    const root = await render();

    [...root.querySelectorAll<HTMLButtonElement>('[data-testid="fan-scope"] button')]
      .find((button) => button.textContent?.includes('Whole club history'))!
      .click();
    await fixture.whenStable();

    expect(store.load).toHaveBeenLastCalledWith('club', false);
    expect(
      root
        .querySelector('[data-testid="fan-scope"] button[aria-pressed="true"]')
        ?.textContent?.trim(),
    ).toBe('Whole club history');
  });

  it('reports a failed read and offers another go', async () => {
    fans.set(null);
    error.set('Your fans could not be loaded.');

    const root = await render();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    expect(root.textContent).toContain('Try again');
  });

  describe('holding back a result the manager has not seen', () => {
    const history = fanBase({
      fans: 10_900,
      moodBp: 5_600,
      moodPhrase: 'Calm',
      homeDemand: 3_300,
      seasonFansChange: 900,
      events: [
        fanEvent({ id: 'e1', kind: 'takeover', fansAfter: 10_000, moodAfterBp: 5_000 }),
        fanEvent({
          id: 'e2',
          occurredAt: '2026-10-14T20:00:00Z',
          fixtureId: 'fixture-0',
          fansAfter: 10_450,
          moodAfterBp: 5_280,
          expectedPointsCentis: 198,
          pointsWon: 3,
        }),
        fanEvent({
          id: 'e3',
          occurredAt: '2026-10-15T20:00:00Z',
          fixtureId: 'fixture-9',
          fansAfter: 10_900,
          moodAfterBp: 5_600,
          expectedPointsCentis: 150,
          pointsWon: 3,
        }),
      ],
      recentMatches: [
        recentMatch(),
        recentMatch({
          fixture: clubFixture({
            id: 'fixture-9',
            status: 'published',
            matchId: 'match-fixture-9',
            homeScore: 3,
            awayScore: 1,
            outcome: 'win',
            opponentName: 'Eastgate',
          }),
        }),
      ],
    });

    beforeEach(() => {
      fans.set(history);
      mine.set(myFixtures('fixture-9'));
    });

    it('ends the history before the match and shows the fans and mood as they were', async () => {
      const root = await render();

      expect(text(root, 'fan-count')).toBe('10,450');
      expect(text(root, 'fan-mood')).toContain('53 / 100');
      expect(root.querySelectorAll('[data-testid="fan-points"] tbody tr')).toHaveLength(2);
      expect(root.querySelector('[data-testid="fan-points"]')?.textContent).not.toContain('10,900');
    });

    it('holds back the crowd and the change this season, which come from the same result', async () => {
      const root = await render();

      expect(text(root, 'fan-crowd')).toContain('Held back');
      expect(text(root, 'fan-season-change')).toContain('Held back');
      expect(root.textContent).not.toContain('3,300');
    });

    it('does not show the score, the outcome or the reaction of the unseen match', async () => {
      const root = await render();
      const rows = [...root.querySelectorAll('[data-testid="fan-recent-matches"] tbody tr')];
      const unseen = rows.find((row) => row.textContent?.includes('Eastgate'))!;
      const seen = rows.find((row) => row.textContent?.includes('Northfield'))!;

      expect(unseen.textContent).toContain('Held back until you have seen the result');
      expect(unseen.textContent).not.toContain('3–1');
      expect(unseen.textContent).toContain('Should win');
      expect(seen.textContent).toContain('2–0');
    });

    it('offers the way out the rest of the game offers, and shows everything once it is taken', async () => {
      const root = await render();

      expect(root.querySelector('[data-testid="fan-held-back"]')).not.toBeNull();

      [...root.querySelectorAll('button')]
        .find((button) => button.textContent?.trim() === 'Show result')!
        .click();
      await fixture.whenStable();

      expect(root.querySelector('[data-testid="fan-held-back"]')).toBeNull();
      expect(text(root, 'fan-count')).toBe('10,900');
      expect(text(root, 'fan-crowd')).toBe('3,300');
    });
  });

  it('waits to know which results are unseen before showing anything that could give one away', async () => {
    mine.set(null);

    const root = await render();

    expect(root.querySelector('[data-testid="fan-summary"]')).toBeNull();
    expect(root.textContent).toContain('Checking your results');
  });
});
