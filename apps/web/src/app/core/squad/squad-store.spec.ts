import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { SquadApi } from './squad-api';
import { SquadStore } from './squad-store';
import { ContractList, Player, Squad } from './squad.models';

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

function createSquadApiStub() {
  return {
    squad: vi.fn(),
    player: vi.fn(),
    contracts: vi.fn(),
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

  it('propagates a refusal rather than storing an empty squad', async () => {
    api.squad.mockReturnValue(throwError(() => new Error('CLUB_NOT_MANAGED')));

    await expect(firstValueFrom(store.loadSquad('club-2'))).rejects.toThrow('CLUB_NOT_MANAGED');
    expect(store.squad()).toBeNull();
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
