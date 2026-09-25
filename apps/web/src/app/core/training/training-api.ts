import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  PlayerTrainingFocus,
  SaveTrainingRequest,
  SetPlayerTrainingFocusRequest,
  Training,
} from './training.models';

/**
 * The training module's HTTP surface (master plan §10.4).
 *
 * The plan's `version` is the strong entity tag (`CONC-1`, ADR-0009): a read returns it in the body and a
 * revise sends it back in `If-Match`, so a save is refused with `412` rather than overwriting a change made
 * on another device. Creating the club's first plan carries no tag, because there is nothing to be
 * conditional against. Nothing here names a club: the server derives it from the tenure.
 */
@Injectable({ providedIn: 'root' })
export class TrainingApi {
  private readonly api = inject(ApiClient);

  /** Reads the club's training plan, the squad it applies to, and the option lists. */
  get(): Observable<Training> {
    return this.api.get<Training>('/training');
  }

  /**
   * Sets or revises the club's plan.
   *
   * `etag` is required once a plan exists and omitted when it does not, which is exactly the server's own
   * rule: a first plan is a create, and a change to a set plan must be conditional.
   */
  save(request: SaveTrainingRequest, etag: string | undefined): Observable<Training> {
    return this.api.put<Training, SaveTrainingRequest>('/training', request, options(etag));
  }

  /** Sets or clears one player's individual focus, conditional on the focus's version when one is set. */
  setFocus(
    playerId: string,
    focusFamily: string | null,
    etag: string | undefined,
  ): Observable<PlayerTrainingFocus> {
    const body: SetPlayerTrainingFocusRequest = { focusFamily };

    return this.api.put<PlayerTrainingFocus, SetPlayerTrainingFocusRequest>(
      `/players/${playerId}/training-focus`,
      body,
      options(etag),
    );
  }
}

/** Builds the request options, omitting `If-Match` when there is no version to send. */
function options(etag: string | undefined): { readonly etag: string } | undefined {
  return etag === undefined ? undefined : { etag };
}
