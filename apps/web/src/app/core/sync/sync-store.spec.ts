import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionStore } from '../auth/session-store';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { SyncApi } from './sync-api';
import { SyncStore } from './sync-store';

/**
 * The shell's synchronization poll (`§11.2`, ADR-0007, `F-41`).
 *
 * The point is that the badge stays fresh on a visible, online, signed-in tab and stays quiet everywhere
 * else: a hidden tab, an offline browser, or a signed-out device must not keep asking.
 */
describe('SyncStore', () => {
  let api: { summary: ReturnType<typeof vi.fn> };
  let authenticated: boolean;
  let online: boolean;

  function create(): SyncStore {
    api = {
      summary: vi
        .fn()
        .mockReturnValue(of({ serverTime: '2026-10-06T19:00:00Z', unreadInboxCount: 3 })),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: SyncApi, useValue: api },
        { provide: SessionStore, useValue: { isAuthenticated: () => authenticated } },
        { provide: ConnectivityStore, useValue: { isOnline: () => online } },
      ],
    });

    return TestBed.inject(SyncStore);
  }

  beforeEach(() => {
    authenticated = true;
    online = true;
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reads once on start and again on each interval', () => {
    const store = create();

    store.start();

    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(store.unreadInboxCount()).toBe(3);

    vi.advanceTimersByTime(60_000);

    expect(api.summary).toHaveBeenCalledTimes(2);

    store.stop();

    vi.advanceTimersByTime(180_000);

    expect(api.summary).toHaveBeenCalledTimes(2);
  });

  it('stays quiet when the session has ended, the browser is offline, or the tab is hidden', () => {
    const store = create();

    authenticated = false;
    store.start();
    expect(api.summary).not.toHaveBeenCalled();

    authenticated = true;
    online = false;
    vi.advanceTimersByTime(60_000);
    expect(api.summary).not.toHaveBeenCalled();

    online = true;
    setVisibility('hidden');
    vi.advanceTimersByTime(60_000);
    expect(api.summary).not.toHaveBeenCalled();

    setVisibility('visible');
    vi.advanceTimersByTime(60_000);
    expect(api.summary).toHaveBeenCalledTimes(1);

    store.stop();
  });

  it('swallows a failed poll rather than surfacing it', () => {
    const store = create();

    api.summary.mockReturnValue(throwError(() => new Error('offline')));

    store.start();

    expect(store.unreadInboxCount()).toBe(0);

    store.stop();
  });

  it('drops the count when the session ends', () => {
    const store = create();

    store.start();
    store.clear();

    expect(store.unreadInboxCount()).toBe(0);

    store.stop();
  });

  /** Overrides the document's visibility for one test. */
  function setVisibility(state: 'visible' | 'hidden'): void {
    Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
  }
});
