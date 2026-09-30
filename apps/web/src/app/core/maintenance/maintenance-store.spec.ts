import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { MaintenanceStore } from './maintenance-store';

/**
 * The read-only incident state (`F-51`).
 *
 * It carries the operator's switch and folds it together with connectivity into one `canMutate`, so a
 * command is disabled for either reason. The tests pin that composition from both sides: read-only blocks
 * an online client, and offline blocks a game that is not read-only.
 */
describe('MaintenanceStore', () => {
  let online: ReturnType<typeof signal<boolean>>;

  function create(): MaintenanceStore {
    TestBed.configureTestingModule({
      providers: [{ provide: ConnectivityStore, useValue: { isOnline: online } }],
    });

    return TestBed.inject(MaintenanceStore);
  }

  beforeEach(() => {
    online = signal(true);
  });

  it('starts writable, with no reason', () => {
    const store = create();

    expect(store.readOnly()).toBe(false);
    expect(store.message()).toBeNull();
    expect(store.canMutate()).toBe(true);
  });

  it('refuses writes while the game is read-only, even online', () => {
    const store = create();

    store.set(true, 'Read-only while we repair the ledger.');

    expect(store.readOnly()).toBe(true);
    expect(store.message()).toBe('Read-only while we repair the ledger.');
    expect(store.canMutate()).toBe(false);
  });

  it('refuses writes while offline, even when the game is not read-only', () => {
    const store = create();

    online.set(false);

    expect(store.readOnly()).toBe(false);
    expect(store.canMutate()).toBe(false);
  });

  it('drops the state and the reason on clear', () => {
    const store = create();

    store.set(true, 'Read-only.');
    store.clear();

    expect(store.readOnly()).toBe(false);
    expect(store.message()).toBeNull();
    expect(store.canMutate()).toBe(true);
  });
});
