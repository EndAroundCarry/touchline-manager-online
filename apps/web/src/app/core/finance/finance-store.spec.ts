import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { FinanceApi } from './finance-api';
import { FinanceLedgerEntry, FinanceLedgerPage, FinanceSummary } from './finance.models';
import { FinanceStore } from './finance-store';

/**
 * The finance store's guarantees.
 *
 * The screen shows what the ledger says: the summary is one read, the ledger is grown a page at a time by
 * cursor, and nothing is ever computed optimistically. A refused read is reported rather than shown as an
 * empty club.
 */

function summary(overrides: Partial<FinanceSummary> = {}): FinanceSummary {
  return {
    cashMinor: 50_000_000,
    reservedMinor: 1_000_000,
    availableMinor: 49_000_000,
    weeklyWageMinor: 1_200_000,
    contractedPlayers: 22,
    weeklySponsorshipMinor: 3_000_000,
    weeklyOperatingCostMinor: 1_000_000,
    tierNumber: 1,
    seasonLabel: '2026/27',
    warnings: [],
    totals: [
      { category: 'opening_balance', amountMinor: 50_000_000 },
      { category: 'wages', amountMinor: -1_200_000 },
    ],
    serverTime: '2026-09-28T00:00:00Z',
    ...overrides,
  };
}

function entry(sequence: number): FinanceLedgerEntry {
  return {
    id: `e${sequence}`,
    sequence,
    category: 'wages',
    cashDeltaMinor: -1_200_000,
    reservedDeltaMinor: 0,
    resultingCashMinor: 48_800_000,
    resultingReservedMinor: 0,
    description: 'Weekly player wages (22 players)',
    createdAt: '2026-09-28T00:00:00Z',
  };
}

function page(entries: readonly FinanceLedgerEntry[], nextCursor: string | null): FinanceLedgerPage {
  return { entries, nextCursor, serverTime: '2026-09-28T00:00:00Z' };
}

function createApiStub() {
  return { getSummary: vi.fn(), getLedger: vi.fn() };
}

describe('FinanceStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: FinanceStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: FinanceApi, useValue: api }] });

    store = TestBed.inject(FinanceStore);
  });

  it('reads the summary and the first page of the ledger', () => {
    api.getSummary.mockReturnValue(of(summary()));
    api.getLedger.mockReturnValue(of(page([entry(2), entry(1)], null)));

    store.load();

    expect(api.getLedger).toHaveBeenCalledWith(null);
    expect(store.summary()?.cashMinor).toBe(50_000_000);
    expect(store.ledger().map((row) => row.sequence)).toEqual([2, 1]);
    expect(store.hasMore()).toBe(false);
    expect(store.loading()).toBe(false);
  });

  it('grows the ledger by cursor on load more', () => {
    api.getSummary.mockReturnValue(of(summary()));
    api.getLedger.mockReturnValueOnce(of(page([entry(3)], '3')));
    api.getLedger.mockReturnValueOnce(of(page([entry(2), entry(1)], null)));

    store.load();
    store.loadMore();

    expect(api.getLedger).toHaveBeenLastCalledWith('3');
    expect(store.ledger().map((row) => row.sequence)).toEqual([3, 2, 1]);
    expect(store.hasMore()).toBe(false);
  });

  it('does not ask for another page when the ledger has ended', () => {
    api.getSummary.mockReturnValue(of(summary()));
    api.getLedger.mockReturnValue(of(page([entry(1)], null)));

    store.load();
    store.loadMore();

    expect(api.getLedger).toHaveBeenCalledTimes(1);
  });

  it('reports a refused read rather than showing an empty club', () => {
    api.getSummary.mockReturnValue(
      throwError(
        () => new ApiError(403, 'NO_CLUB', 'You do not manage a club yet.', null, new Map()),
      ),
    );

    store.load();

    expect(store.error()).toBe('You do not manage a club yet.');
    expect(store.summary()).toBeNull();
    expect(store.loading()).toBe(false);
  });

  it("forgets everything on clear, so one manager never sees another club's money", () => {
    api.getSummary.mockReturnValue(of(summary()));
    api.getLedger.mockReturnValue(of(page([entry(1)], null)));

    store.load();
    expect(store.summary()).not.toBeNull();

    store.clear();

    expect(store.summary()).toBeNull();
    expect(store.ledger()).toEqual([]);
    expect(store.hasMore()).toBe(false);
  });
});
