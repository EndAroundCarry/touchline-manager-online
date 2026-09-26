import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { Match, MatchPresentation } from './match.models';

/**
 * The match module's HTTP surface (master plan §9.5, §10.5).
 *
 * Two reads, both public game data: a result is the same thing to everybody who can sign in, so neither
 * names the caller. The replay is immutable and the server tags it and caches it accordingly, so a second
 * look at a match a manager has already watched is answered from the browser's cache (§9.5, §11.4).
 */
@Injectable({ providedIn: 'root' })
export class MatchApi {
  private readonly api = inject(ApiClient);

  /** Reads a played match's summary. */
  get(matchId: string): Observable<Match> {
    return this.api.get<Match>(`/matches/${matchId}`);
  }

  /** Reads a played match's commentary timeline and keyframe highlights. */
  presentation(matchId: string): Observable<MatchPresentation> {
    return this.api.get<MatchPresentation>(`/matches/${matchId}/presentation`);
  }
}
