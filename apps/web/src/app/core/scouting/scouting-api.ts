import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { PlayerSearchPage, ScoutingFilters, Shortlist } from './scouting.models';

/**
 * The scouting module's HTTP surface (master plan §10.6; `SCT-1`, `SCT-3`).
 *
 * Nothing here names a manager: the shortlist belongs to the authenticated profile, which the server derives
 * from the session. The search is server-side and cursor-paged, so the client never walks an unbounded list.
 */
@Injectable({ providedIn: 'root' })
export class ScoutingApi {
  private readonly api = inject(ApiClient);

  /** Searches the global player database. */
  search(filters: ScoutingFilters, cursor: string | null): Observable<PlayerSearchPage> {
    const query = new URLSearchParams();

    if (filters.name) {
      query.set('name', filters.name);
    }

    if (filters.position) {
      query.set('family', filters.position);
    }

    if (filters.ageMin !== null) {
      query.set('ageMin', String(filters.ageMin));
    }

    if (filters.ageMax !== null) {
      query.set('ageMax', String(filters.ageMax));
    }

    if (filters.abilityMin !== null) {
      query.set('abilityMin', String(filters.abilityMin));
    }

    query.set('sort', filters.sort);
    query.set('pageSize', '25');

    if (cursor !== null) {
      query.set('cursor', cursor);
    }

    return this.api.get<PlayerSearchPage>(`/scouting/players?${query.toString()}`);
  }

  /** Reads the manager's private shortlist. */
  getShortlist(): Observable<Shortlist> {
    return this.api.get<Shortlist>('/shortlist');
  }

  /** Adds a player to the shortlist, or updates the note already kept. */
  add(playerId: string, notes: string | null): Observable<Shortlist> {
    return this.api.post<Shortlist, { notes: string | null }>(`/shortlist/${playerId}`, { notes });
  }

  /** Removes a player from the shortlist. */
  remove(playerId: string): Observable<Shortlist> {
    return this.api.delete<Shortlist>(`/shortlist/${playerId}`);
  }
}
