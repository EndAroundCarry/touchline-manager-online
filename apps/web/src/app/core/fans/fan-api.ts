import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { FanBase, FanHistoryScope } from './fan.models';

/**
 * The fan base's HTTP surface.
 *
 * Nothing here names a club: the server derives it from the authenticated tenure.
 */
@Injectable({ providedIn: 'root' })
export class FanApi {
  private readonly api = inject(ApiClient);

  /** Reads the manager's club's fans, with the history since the takeover or for the whole club. */
  get(history: FanHistoryScope): Observable<FanBase> {
    return this.api.get<FanBase>(`/fans?history=${encodeURIComponent(history)}`);
  }
}
