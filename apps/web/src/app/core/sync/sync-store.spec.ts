import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionStore } from '../auth/session-store';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { MaintenanceStore } from '../maintenance/maintenance-store';
import { SyncApi } from './sync-api';
import { SyncStore } from './sync-store';

/**
 * The shell's synchronization poll (`§11.2`, ADR-0007, `F-41`).
 *
 * The point is that the badge stays fresh on a visible, online, signed-in tab and stays quiet everywhere
 * else: a hidden tab, an offline browser, or a signed-out device must not keep asking. It also reads
 * again the moment the tab becomes visible or the connection returns, and it remembers a failed refresh
 * so the shell can label stale data instead of pretending the read is current.
 */
describe('SyncStore', () => {
  let api: { summary: ReturnType<typeof vi.fn> };
  let authenticated: boolean;
  let online: ReturnType<typeof signal<boolean>>;

  function create(): SyncStore {
    api = {
      summary: vi.fn().mockReturnValue(
        of({
          serverTime: '2026-10-06T19:00:00Z',
          unreadInboxCount: 3,
          readOnly: false,
          readOnlyMessage: null,
        }),
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: SyncApi, useValue: api },
        { provide: SessionStore, useValue: { isAuthenticated: () => authenticated } },
        { provide: ConnectivityStore, useValue: { isOnline: online } },
      ],
    });

    return TestBed.inject(SyncStore);
  }

  beforeEach(() => {
    authenticated = true;
    online = signal(true);
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
    online.set(false);
    vi.advanceTimersByTime(60_000);
    expect(api.summary).not.toHaveBeenCalled();

    online.set(true);
    setVisibility('hidden');
    vi.advanceTimersByTime(60_000);
    expect(api.summary).not.toHaveBeenCalled();

    setVisibility('visible');
    vi.advanceTimersByTime(60_000);
    expect(api.summary).toHaveBeenCalledTimes(1);

    store.stop();
  });

  it('reads again immediately when the tab becomes visible', () => {
    const store = create();

    setVisibility('hidden');
    store.start();
    expect(api.summary).not.toHaveBeenCalled();

    setVisibility('visible');
    globalThis.document.dispatchEvent(new Event('visibilitychange'));

    expect(api.summary).toHaveBeenCalledTimes(1);

    store.stop();
  });

  it('reads again immediately when the connection returns', () => {
    const store = create();

    online.set(false);
    store.start();
    expect(api.summary).not.toHaveBeenCalled();

    online.set(true);
    globalThis.dispatchEvent(new Event('online'));

    expect(api.summary).toHaveBeenCalledTimes(1);

    store.stop();
  });

  it('stops listening when the shell goes away', () => {
    const store = create();

    store.start();
    store.stop();

    globalThis.dispatchEvent(new Event('online'));
    globalThis.document.dispatchEvent(new Event('visibilitychange'));

    expect(api.summary).toHaveBeenCalledTimes(1);
  });

  it('labels the read stale after a failed refresh and clears it on the next success', () => {
    const store = create();

    store.start();
    expect(store.isStale()).toBe(false);
    expect(store.lastRefreshedAt()).not.toBeNull();

    api.summary.mockReturnValue(throwError(() => new Error('offline')));
    store.refresh();

    expect(store.refreshFailed()).toBe(true);
    expect(store.isStale()).toBe(true);

    api.summary.mockReturnValue(
      of({
        serverTime: '2026-10-06T19:00:00Z',
        unreadInboxCount: 1,
        readOnly: false,
        readOnlyMessage: null,
      }),
    );
    store.refresh();

    expect(store.refreshFailed()).toBe(false);
    expect(store.isStale()).toBe(false);

    store.stop();
  });

  it('is stale while the browser is offline, before any poll has failed', () => {
    const store = create();

    online.set(false);

    expect(store.isStale()).toBe(true);
    expect(store.refreshFailed()).toBe(false);
  });

  it('carries the read-only switch from the summary into the maintenance store', () => {
    const store = create();
    const maintenance = TestBed.inject(MaintenanceStore);

    api.summary.mockReturnValue(
      of({
        serverTime: '2026-10-06T19:00:00Z',
        unreadInboxCount: 3,
        readOnly: true,
        readOnlyMessage: 'Read-only while we repair the ledger.',
      }),
    );

    store.start();

    expect(maintenance.readOnly()).toBe(true);
    expect(maintenance.message()).toBe('Read-only while we repair the ledger.');

    store.clear();

    expect(maintenance.readOnly()).toBe(false);
    expect(maintenance.message()).toBeNull();

    store.stop();
  });

  it('drops the count and the freshness when the session ends', () => {
    const store = create();

    store.start();
    store.clear();

    expect(store.unreadInboxCount()).toBe(0);
    expect(store.lastRefreshedAt()).toBeNull();
    expect(store.refreshFailed()).toBe(false);

    store.stop();
  });

  it('swallows a failed poll rather than surfacing it', () => {
    const store = create();

    api.summary.mockReturnValue(throwError(() => new Error('offline')));

    store.start();

    expect(store.unreadInboxCount()).toBe(0);

    store.stop();
  });

  /** Overrides the document's visibility for one test. */
  function setVisibility(state: 'visible' | 'hidden'): void {
    Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
  }
});
