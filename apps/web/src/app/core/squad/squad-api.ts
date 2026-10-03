import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  ContractList,
  ContractRenewal,
  Player,
  PlayerMatches,
  RenewalQuote,
  Squad,
} from './squad.models';

/**
 * The squad module's HTTP surface (master plan §10.3).
 *
 * Reads only, so there is no idempotency key and no conditional request anywhere here: nothing in this
 * milestone changes state, and a cached answer would be the only way a stale one could appear. The
 * manager's own club is resolved by the server from their tenure, so none of these calls names a
 * manager.
 */
@Injectable({ providedIn: 'root' })
export class SquadApi {
  private readonly api = inject(ApiClient);

  /** Reads a club's squad. Refused when the caller does not hold the club. */
  squad(clubId: string): Observable<Squad> {
    return this.api.get<Squad>(`/clubs/${clubId}/squad`);
  }

  /** Reads one player's profile. */
  player(playerId: string): Observable<Player> {
    return this.api.get<Player>(`/players/${playerId}`);
  }

  /** Reads one player's match-by-match statistics across their seasons. */
  playerMatches(playerId: string): Observable<PlayerMatches> {
    return this.api.get<PlayerMatches>(`/players/${playerId}/matches`);
  }

  /** Reads the contracts of the club the caller holds. */
  contracts(): Observable<ContractList> {
    return this.api.get<ContractList>('/contracts');
  }

  /** Asks the server for the deterministic renewal quote for a contract and a term (`CON-3`). */
  renewalQuote(contractId: string, seasons: number): Observable<RenewalQuote> {
    return this.api.post<RenewalQuote, { seasons: number }>(
      `/contracts/${contractId}/renewal-quote`,
      { seasons },
    );
  }

  /**
   * Accepts a renewal (`CON-4`).
   *
   * The quote's contract version is sent as `If-Match`, so a renewal agreed against a stale read is refused
   * with `412` rather than signing over a change made elsewhere.
   */
  renew(contractId: string, seasons: number, version: number): Observable<ContractRenewal> {
    return this.api.post<ContractRenewal, { seasons: number }>(
      `/contracts/${contractId}/renew`,
      { seasons },
      { etag: `"${version}"` },
    );
  }
}
