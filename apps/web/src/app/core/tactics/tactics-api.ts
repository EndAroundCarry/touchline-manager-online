import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { SaveTacticalPlanRequest, TacticalPlan, Tactics } from './tactics.models';

/**
 * The tactics module's HTTP surface (master plan §10.4).
 *
 * The plan's `version` is the strong entity tag (`CONC-1`, ADR-0009): a read returns it in the body and
 * every write sends it back in `If-Match`, so a save is refused with `412` rather than overwriting a
 * change made on another device. Nothing here names a club: the server derives it from the tenure.
 */
@Injectable({ providedIn: 'root' })
export class TacticsApi {
  private readonly api = inject(ApiClient);

  /** Reads the club's plans, the squad they are picked from, and the formation presets. */
  get(): Observable<Tactics> {
    return this.api.get<Tactics>('/tactics');
  }

  /** Creates a plan. The first plan a club saves becomes its default (`INS-11`). */
  create(request: SaveTacticalPlanRequest): Observable<TacticalPlan> {
    return this.api.post<TacticalPlan, SaveTacticalPlanRequest>('/tactics', request);
  }

  /** Revises a plan, conditional on the version the client last read. */
  update(planId: string, request: SaveTacticalPlanRequest, etag: string): Observable<TacticalPlan> {
    return this.api.put<TacticalPlan, SaveTacticalPlanRequest>(`/tactics/${planId}`, request, {
      etag,
    });
  }

  /** Makes a plan the club's default, conditional on its version. */
  makeDefault(planId: string, etag: string): Observable<TacticalPlan> {
    return this.api.post<TacticalPlan>(`/tactics/${planId}/make-default`, null, { etag });
  }
}
