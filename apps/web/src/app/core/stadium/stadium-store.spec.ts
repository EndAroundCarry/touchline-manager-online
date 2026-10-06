import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { StadiumApi } from './stadium-api';
import { Stadium } from './stadium.models';
import { StadiumStore } from './stadium-store';

/**
 * The stadium store's guarantees.
 *
 * Nothing is optimistic: the ground and the purse come from the server, so a build shows only once the
 * server has charged for it. An order is always made against the version the server last returned, and a
 * stale one reloads the ground and says nothing was charged.
 */

function stadium(overrides: Partial<Stadium> = {}): Stadium {
  return {
    clubId: 'c1',
    level: 1,
    maxLevel: 10,
    capacity: 5_000,
    maxCapacity: 50_000,
    seatsPerLevel: 5_000,
    seatsToNextLevel: 1,
    primaryColour: '#8c2f39',
    secondaryColour: '#f2e3e5',
    stands: [
      {
        stand: 'standing',
        seats: 3_000,
        ticketPriceMinor: 800,
        buildCostMinor: 30_000,
        expectedSold: 3_000,
      },
      {
        stand: 'seating',
        seats: 1_000,
        ticketPriceMinor: 1_500,
        buildCostMinor: 60_000,
        expectedSold: 1_000,
      },
      {
        stand: 'covered_seating',
        seats: 900,
        ticketPriceMinor: 2_500,
        buildCostMinor: 100_000,
        expectedSold: 900,
      },
      {
        stand: 'vip',
        seats: 100,
        ticketPriceMinor: 9_000,
        buildCostMinor: 400_000,
        expectedSold: 100,
      },
    ],
    expectedDemand: 9_000,
    fullHouseMinor: 7_050_000,
    expectedGateMinor: 7_050_000,
    availableMinor: 50_000_000,
    version: 1,
    serverTime: '2026-10-06T00:00:00Z',
    ...overrides,
  };
}

function problem(status: number, code: string, detail: string): ApiError {
  return new ApiError(status, code, detail, null, new Map());
}

describe('StadiumStore', () => {
  let api: { get: ReturnType<typeof vi.fn>; build: ReturnType<typeof vi.fn> };
  let store: StadiumStore;

  beforeEach(() => {
    api = { get: vi.fn(), build: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: StadiumApi, useValue: api }] });

    store = TestBed.inject(StadiumStore);
  });

  it('reads the stadium', () => {
    api.get.mockReturnValue(of(stadium()));

    store.load();

    expect(store.stadium()?.capacity).toBe(5_000);
    expect(store.loaded()).toBe(true);
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
  });

  it('reports a refused read instead of showing an empty ground', () => {
    api.get.mockReturnValue(
      throwError(() => problem(403, 'NO_CLUB', 'You do not manage a club yet.')),
    );

    store.load();

    expect(store.stadium()).toBeNull();
    expect(store.error()).toBe('You do not manage a club yet.');
    expect(store.loading()).toBe(false);
  });

  it('builds against the version it last read and adopts the ground the server returns', () => {
    api.get.mockReturnValue(of(stadium({ version: 4 })));
    api.build.mockReturnValue(
      of(stadium({ version: 5, capacity: 5_010, availableMinor: 49_700_000 })),
    );

    store.load();
    store.build('standing', 10, '10 standing places');

    expect(api.build).toHaveBeenCalledWith({ stand: 'standing', count: 10 }, 4);
    expect(store.stadium()?.version).toBe(5);
    expect(store.stadium()?.availableMinor).toBe(49_700_000);
    expect(store.builtMessage()).toBe('10 standing places built.');
    expect(store.building()).toBeNull();
  });

  it('says so when an order lifts the ground to the next level', () => {
    api.get.mockReturnValue(of(stadium()));
    api.build.mockReturnValue(of(stadium({ version: 2, level: 2, capacity: 5_001 })));

    store.load();
    store.build('seating', 1, '1 seat');

    expect(store.builtMessage()).toBe('1 seat built. The stadium is now level 2.');
  });

  it('makes a second order against the ground the first one produced', () => {
    api.get.mockReturnValue(of(stadium({ version: 1 })));
    api.build
      .mockReturnValueOnce(of(stadium({ version: 2 })))
      .mockReturnValueOnce(of(stadium({ version: 3 })));

    store.load();
    store.build('standing', 5, 'standing places');
    store.build('standing', 5, 'standing places');

    expect(api.build).toHaveBeenNthCalledWith(1, { stand: 'standing', count: 5 }, 1);
    expect(api.build).toHaveBeenNthCalledWith(2, { stand: 'standing', count: 5 }, 2);
  });

  it('refuses to send an order for nothing, or before the ground has been read', () => {
    store.build('standing', 5, 'standing places');

    api.get.mockReturnValue(of(stadium()));
    store.load();
    store.build('standing', 0, 'standing places');
    store.build('standing', -3, 'standing places');
    store.build('standing', 2.5, 'standing places');

    expect(api.build).not.toHaveBeenCalled();
  });

  it('does not send a second order while one is in flight', () => {
    const pending = new (class {
      subscribe() {
        return { unsubscribe: () => undefined };
      }
    })();

    api.get.mockReturnValue(of(stadium()));
    api.build.mockReturnValue(pending);

    store.load();
    store.build('standing', 5, 'standing places');
    store.build('vip', 1, 'VIP seats');

    expect(api.build).toHaveBeenCalledTimes(1);
    expect(store.building()).toBe('standing');
  });

  it('reloads the ground and says nothing was charged when the version was stale', () => {
    api.get
      .mockReturnValueOnce(of(stadium({ version: 1 })))
      .mockReturnValueOnce(of(stadium({ version: 2, capacity: 5_100 })));
    api.build.mockReturnValue(
      throwError(() =>
        problem(412, 'PRECONDITION_FAILED', 'The stadium changed since you read it.'),
      ),
    );

    store.load();
    store.build('standing', 5, 'standing places');

    expect(api.get).toHaveBeenCalledTimes(2);
    expect(store.stadium()?.version).toBe(2);
    expect(store.stadium()?.capacity).toBe(5_100);
    expect(store.buildError()).toContain('nothing was charged');
    expect(store.loading()).toBe(false);
  });

  it('shows the server reason when it refuses an order for money or room', () => {
    api.get.mockReturnValue(of(stadium()));
    api.build.mockReturnValue(
      throwError(() =>
        problem(400, 'INSUFFICIENT_FUNDS', 'The club does not hold that much cash.'),
      ),
    );

    store.load();
    store.build('vip', 500, 'VIP seats');

    expect(store.buildError()).toBe('The club does not hold that much cash.');
    expect(store.stadium()?.version).toBe(1);
    expect(store.builtMessage()).toBeNull();
  });

  it('clears what it holds when the session ends', () => {
    api.get.mockReturnValue(of(stadium()));

    store.load();
    store.clear();

    expect(store.stadium()).toBeNull();
    expect(store.builtMessage()).toBeNull();
    expect(store.buildError()).toBeNull();
  });
});
