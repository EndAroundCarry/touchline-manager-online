import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { GameClockAdvance, GameClockStatus, GameClockTarget } from './game-clock.models';

/**
 * The non-production stepped-clock surface (ADR-0049).
 *
 * Both calls exist only where the API mapped them: on a real-time, compressed, or production host the
 * status read answers `404` and the toolbar hides itself, so nothing here runs in the live game.
 */
@Injectable({ providedIn: 'root' })
export class GameClockApi {
  private readonly api = inject(ApiClient);

  /** Reads the current game instant and the next round. */
  status(): Observable<GameClockStatus> {
    return this.api.get<GameClockStatus>('/ops/diagnostics/game-clock');
  }

  /** Asks for one step, to the next day or the next matchday. */
  advance(target: GameClockTarget): Observable<GameClockAdvance> {
    return this.api.post<GameClockAdvance, { target: GameClockTarget }>(
      '/ops/diagnostics/advance-game-clock',
      { target },
      { idempotencyKey: crypto.randomUUID() },
    );
  }
}
