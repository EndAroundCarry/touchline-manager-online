import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StatusApi } from './status-api';
import { StatusStore } from './status-store';

/**
 * The public status store (`F-55`, ADR-0050).
 *
 * The point is a live page: a read on start and on each tick while the page is open, a read the moment the
 * tab comes back, and everything stopped when the page goes away. A failed read is remembered so the page can
 * say it could not reach the server, and the last status is kept rather than blanked.
 */
describe('StatusStore', () => {
  let api: { summary: ReturnType<typeof vi.fn> };

  const sample = {
    serverTime: '2026-10-06T18:00:00Z',
    readOnly: false,
    readOnlyMessage: null,
    seasonNumber: 1,
    nextMatchdayAt: '2026-10-06T19:00:00Z',
    documents: { termsVersion: '2026-01-01', privacyVersion: '2026-01-01' },
  };

  function create(): StatusStore {
    api = { summary: vi.fn().mockReturnValue(of(sample)) };

    TestBed.configureTestingModule({ providers: [{ provide: StatusApi, useValue: api }] });

    return TestBed.inject(StatusStore);
  }

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reads once on start and again on each interval', () => {
    const store = create();

    store.start();

    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(store.status()?.seasonNumber).toBe(1);

    vi.advanceTimersByTime(30_000);

    expect(api.summary).toHaveBeenCalledTimes(2);

    store.stop();

    vi.advanceTimersByTime(120_000);

    expect(api.summary).toHaveBeenCalledTimes(2);
  });

  it('refreshes directly for a one-shot read', () => {
    const store = create();

    store.refresh();

    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(store.status()).toEqual(sample);
    expect(store.loading()).toBe(false);
  });

  it('reads again when the tab becomes visible', () => {
    const store = create();

    setVisibility('hidden');
    store.start();
    expect(api.summary).toHaveBeenCalledTimes(1);

    setVisibility('visible');
    globalThis.document.dispatchEvent(new Event('visibilitychange'));

    expect(api.summary).toHaveBeenCalledTimes(2);

    store.stop();
  });

  it('remembers a failed read but keeps the last status', () => {
    const store = create();

    store.refresh();
    expect(store.failed()).toBe(false);

    api.summary.mockReturnValue(throwError(() => new Error('offline')));
    store.refresh();

    expect(store.failed()).toBe(true);
    expect(store.loading()).toBe(false);
    expect(store.status(), 'a moment-old status is more useful than a blank page').toEqual(sample);
  });

  it('stops listening when the page goes away', () => {
    const store = create();

    store.start();
    store.stop();

    globalThis.document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(120_000);

    expect(api.summary).toHaveBeenCalledTimes(1);
  });

  /** Overrides the document's visibility for one test. */
  function setVisibility(state: 'visible' | 'hidden'): void {
    Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
  }
});
