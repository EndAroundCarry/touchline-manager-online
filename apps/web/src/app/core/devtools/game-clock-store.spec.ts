import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { GameClockApi } from './game-clock-api';
import { GameClockStore } from './game-clock-store';

/**
 * The non-production season-stepping toolbar's state (ADR-0049).
 *
 * The point is that the toolbar appears only where the API reports a stepped clock, and that a step is only
 * reported done once the clock has actually reached the target and the day has been given time to run — so a
 * refresh shows the new day rather than the previous one.
 */
describe('GameClockStore', () => {
  let api: { status: ReturnType<typeof vi.fn>; advance: ReturnType<typeof vi.fn> };

  function create(): GameClockStore {
    api = { status: vi.fn(), advance: vi.fn() };

    TestBed.configureTestingModule({
      providers: [{ provide: GameClockApi, useValue: api }],
    });

    return TestBed.inject(GameClockStore);
  }

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reports the clock available and shows the date when the API answers', () => {
    const store = create();

    api.status.mockReturnValue(
      of({ gameNow: '2026-10-06T19:00:00Z', nextMatchdayAt: '2026-10-08T19:00:00Z' }),
    );

    store.probe();

    expect(store.available()).toBe(true);
    expect(store.gameNow()).toBe('2026-10-06T19:00:00Z');
    expect(store.nextMatchdayAt()).toBe('2026-10-08T19:00:00Z');
  });

  it('stays hidden when the host has no stepped clock', () => {
    const store = create();

    api.status.mockReturnValue(throwError(() => new Error('404')));

    store.probe();

    expect(store.available()).toBe(false);
    expect(store.gameNow()).toBeNull();
  });

  it('reports a step done only once the clock has reached the target', () => {
    const store = create();

    api.advance.mockReturnValue(
      of({
        target: 'day',
        targetInstantUtc: '2026-10-07T00:00:00Z',
        businessKey: 'clock:day:2026-10-07T00:00:00.0000000Z',
        enqueued: true,
      }),
    );

    api.status
      .mockReturnValueOnce(of({ gameNow: '2026-10-06T19:00:00Z', nextMatchdayAt: null }))
      .mockReturnValue(of({ gameNow: '2026-10-07T00:00:00Z', nextMatchdayAt: null }));

    store.advance('day');

    expect(store.busy()).toBe(true);

    // First poll: the clock has not moved yet, so the store waits and asks again.
    vi.advanceTimersByTime(1_000);
    expect(store.completedTick()).toBe(0);

    // Second poll: the clock has reached the target, so the store lets the day settle.
    vi.advanceTimersByTime(1_000);
    expect(store.completedTick()).toBe(0);

    vi.advanceTimersByTime(1_500);

    expect(store.busy()).toBe(false);
    expect(store.completedTick()).toBe(1);
  });

  it('gives up and reports a failure when a step never lands', () => {
    const store = create();

    api.advance.mockReturnValue(
      of({
        target: 'day',
        targetInstantUtc: '2026-10-07T00:00:00Z',
        businessKey: 'clock:day:x',
        enqueued: true,
      }),
    );

    api.status.mockReturnValue(of({ gameNow: '2026-10-06T19:00:00Z', nextMatchdayAt: null }));

    store.advance('day');
    vi.advanceTimersByTime(1_000 * 60);

    expect(store.busy()).toBe(false);
    expect(store.error()).not.toBeNull();
    expect(store.completedTick()).toBe(0);
  });

  it('forgets everything when the session ends, including a pending poll', () => {
    const store = create();

    api.advance.mockReturnValue(
      of({
        target: 'matchday',
        targetInstantUtc: '2026-10-08T19:00:00Z',
        businessKey: 'clock:matchday:x',
        enqueued: true,
      }),
    );
    api.status.mockReturnValue(of({ gameNow: '2026-10-06T19:00:00Z', nextMatchdayAt: null }));

    store.advance('matchday');
    store.clear();

    expect(store.available()).toBe(false);
    expect(store.gameNow()).toBeNull();
    expect(store.busy()).toBe(false);

    // The pending poll was cancelled, so nothing fires after clear.
    vi.advanceTimersByTime(1_000 * 120);
    expect(store.completedTick()).toBe(0);
  });
});
