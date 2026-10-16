import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CalendarEvent, CalendarWindow } from '../../core/calendar/calendar.models';
import { CalendarStore } from '../../core/calendar/calendar-store';
import { ResultGate } from '../../core/match/result-gate';
import { configurePresentation, resetPresentation } from '../../core/world/presentation';
import { Calendar } from './calendar';

/**
 * The calendar draws whatever the server sends it, by kind, without knowing the kinds.
 *
 * The month stacks a day's events in its cell and says "n more" when they do not fit; opening a day shows it
 * hour by hour, with events that share an hour sharing the width. A kind this build has never heard of is still
 * drawn, and a played match's result stays held back until the manager asks.
 */
describe('Calendar', () => {
  function event(
    overrides: Partial<CalendarEvent> & Pick<CalendarEvent, 'id' | 'kind' | 'at'>,
  ): CalendarEvent {
    return {
      state: 'upcoming',
      title: overrides.id,
      detail: null,
      link: null,
      amountMinor: null,
      resultMatchId: null,
      ...overrides,
    };
  }

  const today = '2026-10-17T12:00:00Z';

  function windowOf(events: readonly CalendarEvent[]): CalendarWindow {
    return {
      from: '2026-09-23T00:00:00Z',
      to: '2026-11-09T00:00:00Z',
      events,
      serverTime: today,
    };
  }

  let held: ReturnType<typeof signal<CalendarWindow | null>>;
  let store: {
    window: typeof held;
    loading: ReturnType<typeof signal<boolean>>;
    error: ReturnType<typeof signal<string | null>>;
    load: ReturnType<typeof vi.fn>;
    ensure: ReturnType<typeof vi.fn>;
    clear: ReturnType<typeof vi.fn>;
  };
  let hiddenMatches: Set<string>;
  let reveal: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Calendar>;

  async function render(events: readonly CalendarEvent[]): Promise<HTMLElement> {
    held.set(windowOf(events));
    fixture = TestBed.createComponent(Calendar);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  /** Some runtimes put a comma after the weekday and some do not; the tests do not care which. */
  function plain(text: string | null | undefined): string {
    return (text ?? '').replace(/,/g, '');
  }

  function dayButton(root: HTMLElement, startsWith: string): HTMLButtonElement {
    const found = [...root.querySelectorAll<HTMLButtonElement>('ol button')].find((button) =>
      plain(button.getAttribute('aria-label')).startsWith(startsWith),
    );

    if (found === undefined) {
      throw new Error(`No day button starts with "${startsWith}"`);
    }

    return found;
  }

  async function openDay(root: HTMLElement, label: string): Promise<void> {
    dayButton(root, label).click();
    await fixture.whenStable();
  }

  function buttonNamed(root: HTMLElement, text: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find(
      (button) =>
        button.textContent?.trim().startsWith(text) || button.getAttribute('aria-label') === text,
    );
  }

  /** The hourly view's blocks, by title. */
  function block(root: HTMLElement, title: string): HTMLElement {
    const found = [
      ...root.querySelectorAll<HTMLElement>('[aria-label="Hours of the day"] li'),
    ].find((item) => item.querySelector('p')?.textContent?.trim() === title);

    if (found === undefined) {
      throw new Error(`No block titled "${title}"`);
    }

    return found;
  }

  beforeEach(async () => {
    configurePresentation({ locale: 'en-GB', timeZone: 'UTC' });

    held = signal<CalendarWindow | null>(null);
    hiddenMatches = new Set();
    reveal = vi.fn((matchId: string | null) => {
      if (matchId !== null) {
        hiddenMatches.delete(matchId);
      }
    });
    store = {
      window: held,
      loading: signal(false),
      error: signal<string | null>(null),
      load: vi.fn(),
      ensure: vi.fn(),
      clear: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [Calendar],
      providers: [
        provideRouter([]),
        { provide: CalendarStore, useValue: store },
        {
          provide: ResultGate,
          useValue: {
            settled: signal(true),
            isHidden: (matchId: string | null) => matchId !== null && hiddenMatches.has(matchId),
            reveal,
            refresh: vi.fn(),
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => resetPresentation());

  it('reads a fresh window when it opens', async () => {
    await render([]);

    expect(store.clear).toHaveBeenCalled();
    expect(store.load).toHaveBeenCalled();
  });

  it('opens on the game clocks month with its day marked', async () => {
    const root = await render([]);

    expect(root.querySelector('h2')?.textContent).toContain('October 2026');
    expect(dayButton(root, 'Saturday 17 October 2026 today').getAttribute('aria-current')).toBe(
      'date',
    );
  });

  it('stacks a days events in its cell, each with its time and title', async () => {
    const root = await render([
      event({ id: 'Home v Vale', kind: 'match', at: '2026-10-17T15:00:00Z' }),
      event({ id: 'Ola Hansen injured', kind: 'injury', at: '2026-10-17T09:30:00Z' }),
    ]);

    const cell = dayButton(root, 'Saturday 17 October 2026 today');
    const chips = [...cell.querySelectorAll('span[class*="rounded border"]')];

    expect(chips).toHaveLength(2);
    expect(chips[0].textContent).toContain('09:30');
    expect(chips[0].textContent).toContain('Ola Hansen injured');
    expect(chips[1].textContent).toContain('15:00');
    expect(chips[1].textContent).toContain('Home v Vale');
    expect(cell.textContent).not.toContain('more');
  });

  it('gives the last row to "n more" when a day has too many events to stack', async () => {
    const root = await render(
      ['a', 'b', 'c', 'd', 'e'].map((id, index) =>
        event({ id, kind: 'match', at: `2026-10-17T0${index + 1}:00:00Z` }),
      ),
    );

    const cell = dayButton(root, 'Saturday 17 October 2026 today');

    expect(cell.querySelectorAll('span[class*="rounded border"]')).toHaveLength(2);
    expect(cell.textContent).toContain('3 more');
    expect(cell.getAttribute('aria-label')).toContain('5 events');
  });

  it('counts a day with nothing on it for a screen reader', async () => {
    const root = await render([event({ id: 'a', kind: 'match', at: '2026-10-20T10:00:00Z' })]);

    expect(plain(dayButton(root, 'Tuesday 20 October 2026').getAttribute('aria-label'))).toBe(
      'Tuesday 20 October 2026 1 event',
    );
    expect(dayButton(root, 'Wednesday 21 October 2026').getAttribute('aria-label')).toContain(
      'nothing happening',
    );
  });

  it('opens the hourly view of the day that is clicked', async () => {
    const root = await render([
      event({
        id: 'Weekly finances',
        kind: 'finance',
        at: '2026-10-18T23:00:00Z',
        amountMinor: -350_000,
      }),
      event({ id: 'Ola Hansen injured', kind: 'injury', at: '2026-10-20T10:00:00Z' }),
    ]);

    await openDay(root, 'Sunday 18 October 2026');

    expect(root.querySelector('ol'), 'the month grid is gone').toBeNull();
    expect(plain(root.querySelector('#day-heading')?.textContent)).toContain(
      'Sunday 18 October 2026',
    );
    expect(root.querySelectorAll('[aria-label="Hours of the day"] li')).toHaveLength(1);
    expect(root.textContent).toContain('Weekly finances');
    expect(root.textContent).not.toContain('Ola Hansen injured');
    expect(root.textContent).toContain('−3,500.00');
  });

  it('places an event at its hour', async () => {
    const root = await render([
      event({ id: 'Home v Vale', kind: 'match', at: '2026-10-17T15:30:00Z' }),
    ]);

    await openDay(root, 'Saturday 17 October 2026');

    // 15:30 is 15.5 hours of 64 pixels, plus the pixel of inset.
    expect(block(root, 'Home v Vale').style.top).toBe(`${15.5 * 64 + 1}px`);
    expect(block(root, 'Home v Vale').style.width).toBe('100%');
  });

  it('lets events in the same hour share the width, and leaves a lone event the whole of it', async () => {
    const root = await render([
      event({ id: 'Home v Vale', kind: 'match', at: '2026-10-17T14:00:00Z' }),
      event({ id: 'Ola Hansen injured', kind: 'injury', at: '2026-10-17T14:20:00Z' }),
      event({
        id: 'Main stand finished',
        kind: 'construction_finished',
        at: '2026-10-17T14:40:00Z',
      }),
      event({ id: 'Weekly finances', kind: 'finance', at: '2026-10-17T18:00:00Z' }),
    ]);

    await openDay(root, 'Saturday 17 October 2026');

    const widths = ['Home v Vale', 'Ola Hansen injured', 'Main stand finished'].map((title) =>
      parseFloat(block(root, title).style.width),
    );
    const lefts = ['Home v Vale', 'Ola Hansen injured', 'Main stand finished'].map((title) =>
      parseFloat(block(root, title).style.left),
    );

    expect(widths.every((width) => Math.abs(width - 100 / 3) < 0.01)).toBe(true);
    expect(lefts.map((left) => Math.round(left))).toEqual([0, 33, 67]);
    expect(block(root, 'Weekly finances').style.width).toBe('100%');
    expect(block(root, 'Weekly finances').style.left).toBe('0%');
  });

  it('draws a kind it has never heard of, with its own title, detail and label', async () => {
    const root = await render([
      event({
        id: 'Main stand finished',
        kind: 'construction_finished',
        at: '2026-10-17T09:00:00Z',
        detail: '500 new seats are open',
        link: '/stadium',
      }),
    ]);

    expect(root.textContent).toContain('Construction finished');

    await openDay(root, 'Saturday 17 October 2026');

    expect(root.textContent).toContain('Main stand finished');
    expect(root.textContent).toContain('500 new seats are open');
    expect(root.querySelector('a[href="/stadium"]')).not.toBeNull();
  });

  it('marks the game clocks now on today only', async () => {
    const root = await render([]);

    await openDay(root, 'Saturday 17 October 2026');

    expect(root.querySelector('.bg-accent.h-px')).not.toBeNull();

    buttonNamed(root, 'Next day')!.click();
    await fixture.whenStable();

    expect(root.querySelector('.bg-accent.h-px')).toBeNull();
    expect(root.textContent).toContain('Nothing is happening on this day.');
  });

  it('holds a result back until the manager asks for it', async () => {
    hiddenMatches.add('match-1');

    const root = await render([
      event({
        id: 'Home v Vale',
        kind: 'match',
        at: '2026-10-17T09:00:00Z',
        state: 'done',
        detail: 'Round 4 · 3–0 · Win',
        link: '/matches/match-1',
        resultMatchId: 'match-1',
      }),
    ]);

    await openDay(root, 'Saturday 17 October 2026');

    expect(root.textContent).toContain('Home v Vale');
    expect(root.textContent).not.toContain('3–0');
    expect(
      root.querySelector('a[href="/matches/match-1"]'),
      'the match itself is one click away',
    ).not.toBeNull();

    buttonNamed(root, 'Show result')!.click();
    await fixture.whenStable();

    expect(reveal).toHaveBeenCalledWith('match-1');
    expect(root.textContent).toContain('3–0');
  });

  it('goes back to the month, and follows a day into the next month', async () => {
    const root = await render([]);

    await openDay(root, 'Saturday 31 October 2026');
    buttonNamed(root, 'Next day')!.click();
    await fixture.whenStable();

    expect(plain(root.querySelector('#day-heading')?.textContent)).toContain(
      'Sunday 1 November 2026',
    );

    buttonNamed(root, 'Month')!.click();
    await fixture.whenStable();

    expect(root.querySelector('h2')?.textContent).toContain('November 2026');
    expect(root.querySelector('ol')).not.toBeNull();
  });

  it('moves between months, asking the store to cover each', async () => {
    const root = await render([]);

    buttonNamed(root, 'Next month')!.click();
    await fixture.whenStable();

    expect(root.querySelector('h2')?.textContent).toContain('November 2026');
    expect(store.ensure).toHaveBeenCalledTimes(2);

    buttonNamed(root, 'Today')!.click();
    await fixture.whenStable();

    expect(root.querySelector('h2')?.textContent).toContain('October 2026');
  });

  it('lets a kind be hidden from the month and the day', async () => {
    const root = await render([
      event({ id: 'Training ground visit', kind: 'visit', at: '2026-10-17T09:00:00Z' }),
      event({ id: 'Ola Hansen injured', kind: 'injury', at: '2026-10-17T10:00:00Z' }),
    ]);

    expect(root.textContent).toContain('Ola Hansen injured');

    const injury = [
      ...root.querySelectorAll<HTMLButtonElement>('ul[aria-label="Kinds of event"] button'),
    ].find((button) => button.textContent?.includes('Injury'))!;

    expect(injury.getAttribute('aria-pressed')).toBe('true');

    injury.click();
    await fixture.whenStable();

    expect(injury.getAttribute('aria-pressed')).toBe('false');
    expect(root.textContent).not.toContain('Ola Hansen injured');
    expect(root.textContent).toContain('Training ground visit');
  });

  it('offers a retry when the calendar could not be read', async () => {
    store.error.set('Your calendar could not be loaded.');

    const root = await render([]);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');

    buttonNamed(root, 'Try again')!.click();

    expect(store.load).toHaveBeenCalledTimes(2);
  });
});
