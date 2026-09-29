import { TestBed } from '@angular/core/testing';
import { SwUpdate, UnrecoverableStateEvent, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { UpdateStore } from './update-store';

/**
 * The service-worker update prompt (`F-45`, ADR-0007).
 *
 * The store's contract is narrow: report a downloaded version, keep quiet otherwise, and reload onto
 * the new version only when the manager asks. A disabled worker must make it inert rather than throw,
 * because development and the unit suite run without one.
 */
describe('UpdateStore', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<UnrecoverableStateEvent>;
  let activateUpdate: ReturnType<typeof vi.fn>;
  let reload: ReturnType<typeof vi.fn>;

  function create(isEnabled = true, provideSwUpdate = true): UpdateStore {
    versionUpdates = new Subject<VersionEvent>();
    unrecoverable = new Subject<UnrecoverableStateEvent>();
    activateUpdate = vi.fn().mockResolvedValue(true);

    TestBed.configureTestingModule({
      providers: provideSwUpdate
        ? [
            {
              provide: SwUpdate,
              useValue: { isEnabled, versionUpdates, unrecoverable, activateUpdate },
            },
          ]
        : [],
    });

    return TestBed.inject(UpdateStore);
  }

  beforeEach(() => {
    reload = vi.fn();
    vi.stubGlobal('location', { reload });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('offers the update once a new version is ready', () => {
    const store = create();

    expect(store.updateReady()).toBe(false);

    versionUpdates.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'old' },
      latestVersion: { hash: 'new' },
    });

    expect(store.updateReady()).toBe(true);
  });

  it('does not offer an update that is only detected or has failed to install', () => {
    const store = create();

    versionUpdates.next({ type: 'VERSION_DETECTED', version: { hash: 'new' } });
    versionUpdates.next({
      type: 'VERSION_INSTALLATION_FAILED',
      version: { hash: 'new' },
      error: 'network',
    });

    expect(store.updateReady()).toBe(false);
  });

  it('defers the update when the manager chooses later', () => {
    const store = create();

    versionUpdates.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'old' },
      latestVersion: { hash: 'new' },
    });
    store.dismiss();

    expect(store.updateReady()).toBe(false);
  });

  it('activates the new version and reloads onto it', async () => {
    const store = create();

    store.reload();

    await vi.waitFor(() => expect(reload).toHaveBeenCalledOnce());
    expect(activateUpdate).toHaveBeenCalledOnce();
  });

  it('reloads even when activating the new version fails', async () => {
    const store = create();
    activateUpdate.mockRejectedValue(new Error('activation failed'));

    store.reload();

    await vi.waitFor(() => expect(reload).toHaveBeenCalledOnce());
  });

  it('reports an unrecoverable state, which the banner offers to fix with a reload', () => {
    const store = create();

    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'broken cache' });

    expect(store.unrecoverable()).toBe(true);
  });

  it('stays inert when the worker is disabled', () => {
    const store = create(false);

    versionUpdates.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'old' },
      latestVersion: { hash: 'new' },
    });

    expect(store.updateReady()).toBe(false);
  });

  it('constructs without a service worker at all', () => {
    const store = create(true, false);

    expect(store.updateReady()).toBe(false);
    expect(() => store.reload()).not.toThrow();
  });
});
