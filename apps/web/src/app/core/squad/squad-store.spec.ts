import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { SquadApi } from './squad-api';
import { SquadStore } from './squad-store';
import { ContractList, Player, PlayerMatches, Squad } from './squad.models';

/**
 * The squad store's guarantees.
 *
 * The one that matters here is the absence of caching: everything this store holds is mutable club state
 * with no version to revalidate against, so a second read must reach the server rather than answering
 * from the first. The rest is that signing out leaves nothing of the previous manager behind.
 */

const squad = { clubId: 'club-1', clubName: 'Ashvale United', players: [] } as unknown as Squad;
const otherSquad = { clubId: 'club-2', clubName: 'Vale Rovers', players: [] } as unknown as Squad;
const player = { id: 'player-1', fullName: 'Alaric Alderwick' } as unknown as Player;
const contracts = { clubId: 'club-1', contracts: [] } as unknown as ContractList;

const quote = {
  contractId: 'contract-1',
  playerId: 'player-1',
  seasons: 3,
  startSeasonNumber: 1,
  endSeasonNumber: 3,
  weeklyWageMinor: 1_200_000,
  contractVersion: 4,
  serverTime: '2026-09-28T00:00:00Z',
};

const renewedPlayer = {
  id: 'player-1',
  fullName: 'Alaric Alderwick',
  contract: { id: 'contract-2' },
} as unknown as Player;

function createSquadApiStub() {
  return {
    squad: vi.fn(),
    player: vi.fn(),
    playerMatches: vi.fn(),
    contracts: vi.fn(),
    renewalQuote: vi.fn(),
    renew: vi.fn(),
  };
}

describe('SquadStore', () => {
  let api: ReturnType<typeof createSquadApiStub>;
  let store: SquadStore;

  beforeEach(() => {
    api = createSquadApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: SquadApi, useValue: api }] });

    store = TestBed.inject(SquadStore);
  });

  it('publishes the squad it read', async () => {
    api.squad.mockReturnValue(of(squad));

    const loaded = await firstValueFrom(store.loadSquad('club-1'));

    expect(loaded).toBe(squad);
    expect(store.squad()).toBe(squad);
    expect(api.squad).toHaveBeenCalledWith('club-1');
  });

  it('reads again rather than answering a second call from the first read', async () => {
    api.squad.mockReturnValueOnce(of(squad)).mockReturnValueOnce(of(otherSquad));

    await firstValueFrom(store.loadSquad('club-1'));
    await firstValueFrom(store.loadSquad('club-2'));

    expect(api.squad).toHaveBeenCalledTimes(2);
    expect(store.squad()).toBe(otherSquad);
  });

  it('publishes the player and the contracts it read', async () => {
    api.player.mockReturnValue(of(player));
    api.contracts.mockReturnValue(of(contracts));

    await firstValueFrom(store.loadPlayer('player-1'));
    await firstValueFrom(store.loadContracts());

    expect(store.player()).toBe(player);
    expect(store.contracts()).toBe(contracts);
  });

  it('publishes the player match history it read, and forgets it on clear', async () => {
    const matches = { playerId: 'player-1', matches: [] } as unknown as PlayerMatches;

    api.playerMatches.mockReturnValue(of(matches));

    await firstValueFrom(store.loadPlayerMatches('player-1'));

    expect(api.playerMatches).toHaveBeenCalledWith('player-1');
    expect(store.playerMatches()).toBe(matches);

    store.clear();

    expect(store.playerMatches()).toBeNull();
  });

  it('propagates a refusal rather than storing an empty squad', async () => {
    api.squad.mockReturnValue(throwError(() => new Error('CLUB_NOT_MANAGED')));

    await expect(firstValueFrom(store.loadSquad('club-2'))).rejects.toThrow('CLUB_NOT_MANAGED');
    expect(store.squad()).toBeNull();
  });

  it('publishes the renewal quote it read, and forgets it on demand', async () => {
    api.renewalQuote.mockReturnValue(of(quote));

    await firstValueFrom(store.quoteRenewal('contract-1', 3));

    expect(api.renewalQuote).toHaveBeenCalledWith('contract-1', 3);
    expect(store.renewalQuote()).toBe(quote);

    store.clearRenewalQuote();

    expect(store.renewalQuote()).toBeNull();
  });

  it('re-signs under the quoted version and re-reads the profile the server now holds', async () => {
    api.renewalQuote.mockReturnValue(of(quote));
    api.renew.mockReturnValue(of({ ...quote, contractId: 'contract-2', version: 1 }));
    api.player.mockReturnValue(of(renewedPlayer));

    await firstValueFrom(store.quoteRenewal('contract-1', 3));
    await firstValueFrom(store.renewContract('contract-1', 'player-1', 3, quote.contractVersion));

    // The quote's version is the If-Match the command carries, so a stale quote is refused (CONC-1).
    expect(api.renew).toHaveBeenCalledWith('contract-1', 3, quote.contractVersion);
    expect(store.player()).toBe(renewedPlayer);
    expect(store.renewalQuote()).toBeNull();
  });

  it('forgets everything on clear, so one manager never sees the previous squad', async () => {
    api.squad.mockReturnValue(of(squad));
    api.player.mockReturnValue(of(player));
    api.contracts.mockReturnValue(of(contracts));

    await firstValueFrom(store.loadSquad('club-1'));
    await firstValueFrom(store.loadPlayer('player-1'));
    await firstValueFrom(store.loadContracts());

    store.clear();

    expect(store.squad()).toBeNull();
    expect(store.player()).toBeNull();
    expect(store.contracts()).toBeNull();
  });
});
