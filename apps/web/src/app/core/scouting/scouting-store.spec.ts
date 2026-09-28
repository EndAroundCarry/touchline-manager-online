import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { ScoutingApi } from './scouting-api';
import { PlayerSearchPage, PlayerSearchResult, Shortlist } from './scouting.models';
import { ScoutingStore } from './scouting-store';

/**
 * The scouting store's guarantees.
 *
 * The search is server-side and grown a page at a time by cursor, and the shortlist is refreshed from the
 * server after every change rather than guessed. A refused read is reported rather than shown as an empty
 * world.
 */

function player(id: string): PlayerSearchResult {
  return {
    playerId: id,
    fullName: `Player ${id}`,
    shortName: id.toUpperCase(),
    nationalityCode: 'GB',
    age: 24,
    preferredFoot: 'Right',
    primaryPosition: 'Striker',
    secondaryPositions: [],
    clubId: 'club-1',
    clubName: 'Somewhere FC',
    tierNumber: 1,
    divisionId: 'division-1',
    divisionName: 'Top Flight',
    isListed: false,
    attributes: {
      technical: {
        finishing: 12,
        passing: 12,
        crossing: 12,
        dribbling: 12,
        firstTouch: 12,
        tackling: 12,
        marking: 12,
        heading: 12,
        technique: 12,
        setPieces: 12,
      },
      mental: {
        decisions: 12,
        vision: 12,
        positioning: 12,
        composure: 12,
        anticipation: 12,
        workRate: 12,
        aggression: 12,
        leadership: 12,
      },
      physical: {
        pace: 12,
        acceleration: 12,
        stamina: 12,
        strength: 12,
        agility: 12,
        jumpingReach: 12,
      },
      goalkeeping: { handling: 1, reflexes: 1, oneOnOnes: 1, aerialAbility: 1 },
    },
  };
}

function page(players: readonly PlayerSearchResult[], nextCursor: string | null): PlayerSearchPage {
  return { players, nextCursor, serverTime: '2026-09-28T00:00:00Z' };
}

function shortlist(playerIds: readonly string[]): Shortlist {
  return {
    entries: playerIds.map((playerId) => ({
      playerId,
      playerName: `Player ${playerId}`,
      shortName: playerId.toUpperCase(),
      primaryPosition: 'Striker',
      age: 24,
      notes: null,
      isListed: false,
    })),
    serverTime: '2026-09-28T00:00:00Z',
  };
}

function createApiStub() {
  return { search: vi.fn(), getShortlist: vi.fn(), add: vi.fn(), remove: vi.fn() };
}

describe('ScoutingStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: ScoutingStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: ScoutingApi, useValue: api }] });

    store = TestBed.inject(ScoutingStore);
  });

  it('reads the first page and exposes a further page when the server offers one', () => {
    api.search.mockReturnValue(of(page([player('p1')], 'cursor-1')));

    store.search();

    expect(store.results()).toHaveLength(1);
    expect(store.hasMore()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('appends the next page and clears the cursor at the end', () => {
    api.search.mockReturnValueOnce(of(page([player('p1')], 'cursor-1')));
    api.search.mockReturnValueOnce(of(page([player('p2')], null)));

    store.search();
    store.loadMore();

    expect(store.results().map((result) => result.playerId)).toEqual(['p1', 'p2']);
    expect(store.hasMore()).toBe(false);
  });

  it('reports a refused first search rather than showing an empty world', () => {
    api.search.mockReturnValue(
      throwError(() => new ApiError(403, 'NO_CLUB', 'No club.', null, new Map())),
    );

    store.search();

    expect(store.error()).toBe('No club.');
    expect(store.loading()).toBe(false);
  });

  it('refreshes the shortlist from the server after adding a player', () => {
    api.add.mockReturnValue(of(shortlist(['p1'])));

    store.add('p1', 'watching');

    expect(api.add).toHaveBeenCalledWith('p1', 'watching');
    expect(store.isShortlisted('p1')).toBe(true);
  });

  it('refreshes the shortlist from the server after removing a player', () => {
    api.getShortlist.mockReturnValue(of(shortlist(['p1'])));
    api.remove.mockReturnValue(of(shortlist([])));

    store.loadShortlist();
    store.remove('p1');

    expect(store.isShortlisted('p1')).toBe(false);
    expect(store.shortlist()).toHaveLength(0);
  });
});
