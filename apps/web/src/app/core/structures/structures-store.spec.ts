import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { StructuresApi } from './structures-api';
import { StructuresStore } from './structures-store';
import { Structure, Structures } from './structures.models';

/**
 * The structures store's guarantees.
 *
 * Nothing is optimistic: levels and the purse come from the server, so an upgrade shows only once the server
 * has charged for it. An order is made against the version the server last returned for that building, and a
 * stale one reloads both buildings and says nothing was charged.
 */

function building(overrides: Partial<Structure> = {}): Structure {
  return {
    kind: 'snack_bar',
    level: 1,
    maxLevel: 10,
    version: 1,
    spendMinor: 100,
    upkeepWeeklyMinor: 30_000,
    next: {
      level: 2,
      spendMinor: 125,
      upkeepWeeklyMinor: 45_000,
      costMinor: 2_500_000,
      buildDays: 1,
      estWeeklyProfitChangeMinor: 5_000,
    },
    building: null,
    bonuses: [],
    recentWeeks: [],
    recentMatches: [],
    seasonTakingsMinor: 0,
    seasonUpkeepMinor: 0,
    ...overrides,
  };
}

function structures(
  snackBar: Partial<Structure> = {},
  merchandise: Partial<Structure> = {},
  overrides: Partial<Structures> = {},
): Structures {
  return {
    availableCashMinor: 50_000_000,
    snackBar: building(snackBar),
    merchandise: building({ kind: 'merchandise', version: 10, ...merchandise }),
    serverTime: '2026-10-21T00:00:00Z',
    ...overrides,
  };
}

function problem(status: number, code: string, detail: string): ApiError {
  return new ApiError(status, code, detail, null, new Map());
}

describe('StructuresStore', () => {
  let api: { get: ReturnType<typeof vi.fn>; upgrade: ReturnType<typeof vi.fn> };
  let store: StructuresStore;

  beforeEach(() => {
    api = { get: vi.fn(), upgrade: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: StructuresApi, useValue: api }] });

    store = TestBed.inject(StructuresStore);
  });

  it('reads both buildings', () => {
    api.get.mockReturnValue(of(structures()));

    store.load();

    expect(store.structures()?.snackBar.level).toBe(1);
    expect(store.structures()?.merchandise.kind).toBe('merchandise');
    expect(store.loaded()).toBe(true);
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
  });

  it('reports a refused read instead of showing empty buildings', () => {
    api.get.mockReturnValue(
      throwError(() => problem(403, 'NO_CLUB', 'You do not manage a club yet.')),
    );

    store.load();

    expect(store.structures()).toBeNull();
    expect(store.error()).toBe('You do not manage a club yet.');
    expect(store.loading()).toBe(false);
  });

  it("upgrades against the building's own version and adopts what the server returns", () => {
    api.get.mockReturnValue(of(structures({ version: 4 }, { version: 9 })));
    api.upgrade.mockReturnValue(
      of(
        structures(
          {
            version: 5,
            building: { targetLevel: 2, dueAt: '2026-10-22T10:00:00Z' },
            next: null,
          },
          { version: 9 },
          { availableCashMinor: 47_500_000 },
        ),
      ),
    );

    store.load();
    store.upgrade('snack_bar');

    expect(api.upgrade).toHaveBeenCalledWith('snack_bar', 4);
    expect(store.structures()?.snackBar.version).toBe(5);
    expect(store.structures()?.availableCashMinor).toBe(47_500_000);
    expect(store.upgradedMessage()).toContain('Snack bar upgrade to level 2 ordered');
    expect(store.upgrading()).toBeNull();
  });

  it('uses the shop version for the shop, not the snack bar one', () => {
    api.get.mockReturnValue(of(structures({ version: 4 }, { version: 9 })));
    api.upgrade.mockReturnValue(of(structures({ version: 4 }, { version: 10 })));

    store.load();
    store.upgrade('merchandise');

    expect(api.upgrade).toHaveBeenCalledWith('merchandise', 9);
    expect(store.upgradedMessage()).toContain('Merchandise shop upgrade');
  });

  it('makes a second order against the version the first one produced', () => {
    api.get.mockReturnValue(of(structures({ version: 1 })));
    api.upgrade
      .mockReturnValueOnce(of(structures({ version: 2 })))
      .mockReturnValueOnce(of(structures({ version: 3 })));

    store.load();
    store.upgrade('snack_bar');
    store.upgrade('snack_bar');

    expect(api.upgrade).toHaveBeenNthCalledWith(1, 'snack_bar', 1);
    expect(api.upgrade).toHaveBeenNthCalledWith(2, 'snack_bar', 2);
  });

  it('does not send an order before the buildings have been read', () => {
    store.upgrade('snack_bar');

    expect(api.upgrade).not.toHaveBeenCalled();
  });

  it('does not send a second order while one is in flight', () => {
    const pending = new (class {
      subscribe() {
        return { unsubscribe: () => undefined };
      }
    })();

    api.get.mockReturnValue(of(structures()));
    api.upgrade.mockReturnValue(pending);

    store.load();
    store.upgrade('snack_bar');
    store.upgrade('merchandise');

    expect(api.upgrade).toHaveBeenCalledTimes(1);
    expect(store.upgrading()).toBe('snack_bar');
  });

  it('reloads the buildings and says nothing was charged when the version was stale', () => {
    api.get
      .mockReturnValueOnce(of(structures({ version: 1 })))
      .mockReturnValueOnce(
        of(structures({ version: 2, building: { targetLevel: 2, dueAt: '2026-10-22T10:00:00Z' } })),
      );
    api.upgrade.mockReturnValue(
      throwError(() =>
        problem(412, 'PRECONDITION_FAILED', 'The building changed since you read it.'),
      ),
    );

    store.load();
    store.upgrade('snack_bar');

    expect(api.get).toHaveBeenCalledTimes(2);
    expect(store.structures()?.snackBar.version).toBe(2);
    expect(store.upgradeError()).toContain('nothing was charged');
    expect(store.loading()).toBe(false);
  });

  it('shows the server reason when it refuses an order for money or work in progress', () => {
    api.get.mockReturnValue(of(structures()));
    api.upgrade.mockReturnValue(
      throwError(() =>
        problem(409, 'INSUFFICIENT_FUNDS', 'The club does not hold that much cash.'),
      ),
    );

    store.load();
    store.upgrade('snack_bar');

    expect(store.upgradeError()).toBe('The club does not hold that much cash.');
    expect(store.structures()?.snackBar.version).toBe(1);
    expect(store.upgradedMessage()).toBeNull();
    expect(store.upgrading()).toBeNull();
  });

  it('drops a stale confirmation when the messages are dismissed', () => {
    api.get.mockReturnValue(of(structures()));
    api.upgrade.mockReturnValue(of(structures({ version: 2 })));

    store.load();
    store.upgrade('snack_bar');
    store.dismissMessages();

    expect(store.upgradedMessage()).toBeNull();
    expect(store.upgradeError()).toBeNull();
  });

  it('clears what it holds when the session ends', () => {
    api.get.mockReturnValue(of(structures()));

    store.load();
    store.clear();

    expect(store.structures()).toBeNull();
    expect(store.upgradedMessage()).toBeNull();
    expect(store.upgradeError()).toBeNull();
  });
});
