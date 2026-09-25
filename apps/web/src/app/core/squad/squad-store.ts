import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { SquadApi } from './squad-api';
import { ContractList, Player, Squad } from './squad.models';

/**
 * The squad module's view state.
 *
 * A feature-scoped store rather than a global one, following `OnboardingStore`. Unlike the reference data
 * that store caches, everything here is mutable club state: a squad changes when a contract is signed and
 * a player's condition changes after every match, and none of these reads carries a version the client
 * could revalidate against. So this store holds the latest read and never answers from a previous one —
 * every screen load reads again, and the only thing held between screens is what is currently displayed.
 */
@Injectable({ providedIn: 'root' })
export class SquadStore {
  private readonly api = inject(SquadApi);

  private readonly squadSignal = signal<Squad | null>(null);
  private readonly playerSignal = signal<Player | null>(null);
  private readonly contractsSignal = signal<ContractList | null>(null);

  /** The squad last read. */
  readonly squad = this.squadSignal.asReadonly();

  /** The player profile last read. */
  readonly player = this.playerSignal.asReadonly();

  /** The contract list last read. */
  readonly contracts = this.contractsSignal.asReadonly();

  /** Reads a club's squad. */
  loadSquad(clubId: string): Observable<Squad> {
    return this.api.squad(clubId).pipe(tap((squad) => this.squadSignal.set(squad)));
  }

  /** Reads one player's profile. */
  loadPlayer(playerId: string): Observable<Player> {
    return this.api.player(playerId).pipe(tap((player) => this.playerSignal.set(player)));
  }

  /** Reads the contracts of the club the account holds. */
  loadContracts(): Observable<ContractList> {
    return this.api.contracts().pipe(tap((contracts) => this.contractsSignal.set(contracts)));
  }

  /** Forgets everything read. Called when the session ends, so one manager's squad never shows to another. */
  clear(): void {
    this.squadSignal.set(null);
    this.playerSignal.set(null);
    this.contractsSignal.set(null);
  }
}
