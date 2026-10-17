import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { Match, MatchInstructionNames, MatchPresentation } from './match.models';

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

  /**
   * Reads the names of the match instructions the caller's own club played with in a match.
   *
   * Unlike the other two reads this one answers for the caller, so the server does not let it be cached.
   */
  instructionNames(matchId: string): Observable<MatchInstructionNames> {
    return this.api.get<MatchInstructionNames>(`/matches/${matchId}/my-instructions`);
  }
}
