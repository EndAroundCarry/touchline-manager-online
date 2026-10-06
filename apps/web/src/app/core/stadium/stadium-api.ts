import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { entityTag } from '../notifications/notification-preferences-api';
import { BuildSeatsRequest, Stadium } from './stadium.models';

/**
 * The stadium's HTTP surface.
 *
 * Nothing here names a club: the server derives it from the authenticated tenure. A build order always
 * carries the stadium's version in `If-Match`, so a double click, a retry, or a second device cannot pay for
 * the same decision twice (`CONC-1`).
 */
@Injectable({ providedIn: 'root' })
export class StadiumApi {
  private readonly api = inject(ApiClient);

  /** Reads the club's stadium: its places, prices, build costs and expected crowd. */
  get(): Observable<Stadium> {
    return this.api.get<Stadium>('/stadium');
  }

  /** Adds places of one kind, against the version the manager last read. */
  build(request: BuildSeatsRequest, version: number): Observable<Stadium> {
    return this.api.post<Stadium, BuildSeatsRequest>('/stadium/seats', request, {
      etag: entityTag(version),
    });
  }
}
