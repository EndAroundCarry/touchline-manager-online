import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { TransfersApi } from './transfers-api';
import {
  MyMarketActivity,
  TransferHistoryEntry,
  TransferHistoryPage,
  TransferListing,
  TransferListingsPage,
} from './transfers.models';
import { TransfersStore } from './transfers-store';

/**
 * The transfers store's guarantees.
 *
 * The listings are grown a page at a time by cursor, and every command re-reads the market rather than
 * guessing: a listing, a bid, and a cancellation are server decisions (`§11.2`).
 */

function listing(id: string, status = 'open'): TransferListing {
  return {
    listingId: id,
    playerId: `player-${id}`,
    playerName: `Player ${id}`,
    playerShortName: id.toUpperCase(),
    primaryPosition: 'Striker',
    age: 24,
    ability: 12,
    kind: 'club',
    sellerClubId: 'club-1',
    sellerClubName: 'Somewhere FC',
    minimumFeeMinor: 1_000_000,
    generatedBuyerSeasonWageMinor: 50_000,
    generatedContractSeasons: 2,
    opensAt: '2026-09-28T00:00:00Z',
    endsAt: '2026-09-30T12:00:00Z',
    status,
    leadingAmountMinor: null,
    bidderCount: 0,
    minimumAcceptableBidMinor: 1_000_000,
    yourBidMinor: null,
    version: 1,
  };
}

function freeAgent(id: string): TransferListing {
  return {
    ...listing(id),
    kind: 'free_agent',
    sellerClubId: null,
    sellerClubName: null,
    ability: 8,
  };
}

function listingsPage(
  listings: readonly TransferListing[],
  nextCursor: string | null,
): TransferListingsPage {
  return { listings, nextCursor, serverTime: '2026-09-28T00:00:00Z' };
}

function activity(listings: readonly TransferListing[]): MyMarketActivity {
  return { myListings: listings, myBids: [], serverTime: '2026-09-28T00:00:00Z' };
}

function historyPage(transfers: readonly TransferHistoryEntry[]): TransferHistoryPage {
  return { transfers, nextCursor: null, serverTime: '2026-09-28T00:00:00Z' };
}

function createApiStub() {
  return {
    getListings: vi.fn(),
    getListing: vi.fn(),
    getMine: vi.fn(),
    getHistory: vi.fn(),
    create: vi.fn(),
    cancel: vi.fn(),
    bid: vi.fn(),
  };
}

describe('TransfersStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: TransfersStore;

  beforeEach(() => {
    api = createApiStub();

    api.getListings.mockImplementation((_cursor: string | null, filter?: { kind?: string }) =>
      of(
        filter?.kind === 'free_agent'
          ? listingsPage([freeAgent('f1')], 'fa-cursor-1')
          : listingsPage([listing('l1')], 'cursor-1'),
      ),
    );
    api.getMine.mockReturnValue(of(activity([listing('l1')])));
    api.getHistory.mockReturnValue(of(historyPage([])));

    TestBed.configureTestingModule({ providers: [{ provide: TransfersApi, useValue: api }] });

    store = TestBed.inject(TransfersStore);
  });

  it('reads the listings, the club activity, and the first history page', () => {
    store.load();

    expect(store.listings()).toHaveLength(1);
    expect(store.myListings()).toHaveLength(1);
    expect(store.hasMore()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('appends the next page of listings and clears the cursor at the end', () => {
    store.load();

    api.getListings.mockReturnValueOnce(of(listingsPage([listing('l2')], null)));
    store.loadMore();

    expect(store.listings().map((row) => row.listingId)).toEqual(['l1', 'l2']);
    expect(store.hasMore()).toBe(false);
  });

  it('asks for the clubs’ listings and the free-agent pool as separate kinds', () => {
    store.load();

    expect(api.getListings).toHaveBeenCalledWith(null, { kind: 'club' });
    expect(api.getListings).toHaveBeenCalledWith(null, { kind: 'free_agent', family: '' });
    expect(store.listings().map((row) => row.kind)).toEqual(['club']);
    expect(store.freeAgents().map((row) => row.kind)).toEqual(['free_agent']);
    expect(store.freeAgents()[0].sellerClubId).toBeNull();
    expect(store.hasMoreFreeAgents()).toBe(true);
  });

  it('narrows the pool to a position family without touching the clubs’ listings', () => {
    store.load();
    api.getListings.mockClear();

    store.setFreeAgentFamily('defence');

    expect(api.getListings).toHaveBeenCalledOnce();
    expect(api.getListings).toHaveBeenCalledWith(null, { kind: 'free_agent', family: 'defence' });
    expect(store.freeAgentFamily()).toBe('defence');
    expect(store.listings()).toHaveLength(1);
  });

  it('appends the next page of free agents with the family still applied', () => {
    store.load();
    store.setFreeAgentFamily('attack');

    api.getListings.mockReturnValueOnce(of(listingsPage([freeAgent('f2')], null)));
    store.loadMoreFreeAgents();

    expect(api.getListings).toHaveBeenLastCalledWith('fa-cursor-1', {
      kind: 'free_agent',
      family: 'attack',
    });
    expect(store.freeAgents().map((row) => row.listingId)).toEqual(['f1', 'f2']);
    expect(store.hasMoreFreeAgents()).toBe(false);
  });

  it('keeps a family’s answer from landing on a later family’s list', () => {
    const slow = new Subject<TransferListingsPage>();

    store.load();
    api.getListings.mockReturnValueOnce(slow);
    store.setFreeAgentFamily('defence');
    api.getListings.mockReturnValueOnce(of(listingsPage([freeAgent('f9')], null)));
    store.setFreeAgentFamily('attack');

    slow.next(listingsPage([freeAgent('late')], null));

    expect(store.freeAgents().map((row) => row.listingId)).toEqual(['f9']);
  });

  it('sends an idempotency key with a bid and re-reads the market', () => {
    api.bid.mockReturnValue(of(listing('l1')));

    store.bid('l1', 2_000_000);

    expect(api.bid).toHaveBeenCalledOnce();
    expect(api.bid.mock.calls[0][0]).toBe('l1');
    expect(api.bid.mock.calls[0][1]).toBe(2_000_000);
    expect(typeof api.bid.mock.calls[0][2]).toBe('string');
    expect(api.getListings).toHaveBeenCalled();
  });

  it('reports a refused bid rather than showing it as accepted', () => {
    api.bid.mockReturnValue(
      throwError(() => new ApiError(400, 'BID_TOO_LOW', 'Bid too low.', null, new Map())),
    );

    store.bid('l1', 1);

    expect(store.error()).toBe('Bid too low.');
  });

  it('flags a bid refused because the club is in the red, and clears the flag on the next try', () => {
    api.bid.mockReturnValueOnce(
      throwError(
        () =>
          new ApiError(409, 'TRANSFER_EMBARGO', 'A club in the red may not bid.', null, new Map()),
      ),
    );
    api.bid.mockReturnValueOnce(of(listing('l1')));

    store.bid('l1', 1_000_000);
    expect(store.embargoed()).toBe(true);
    expect(store.error()).toBe('A club in the red may not bid.');

    store.bid('l1', 1_000_000);
    expect(store.embargoed()).toBe(false);
  });

  it('does not call another refusal an embargo', () => {
    api.bid.mockReturnValue(
      throwError(() => new ApiError(400, 'BID_TOO_LOW', 'Bid too low.', null, new Map())),
    );

    store.bid('l1', 1);

    expect(store.embargoed()).toBe(false);
  });

  it('re-reads the market after a cancellation', () => {
    api.cancel.mockReturnValue(of(listing('l1', 'cancelled')));

    store.cancel('l1');

    expect(api.cancel).toHaveBeenCalledOnce();
    expect(api.getMine).toHaveBeenCalled();
  });
});
