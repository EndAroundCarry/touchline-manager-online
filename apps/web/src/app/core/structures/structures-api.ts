import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { entityTag } from '../notifications/notification-preferences-api';
import { StructureKind, Structures } from './structures.models';

/**
 * The structures' HTTP surface.
 *
 * Nothing here names a club: the server derives it from the authenticated tenure. An upgrade order always
 * carries that building's version in `If-Match`, so a double click, a retry, or a second device cannot pay for
 * the same decision twice (`CONC-1`).
 */
@Injectable({ providedIn: 'root' })
export class StructuresApi {
  private readonly api = inject(ApiClient);

  /** Reads both buildings, with their books. */
  get(): Observable<Structures> {
    return this.api.get<Structures>('/structures');
  }

  /** Orders the next level of one building, against the version the manager last read. */
  upgrade(kind: StructureKind, version: number): Observable<Structures> {
    return this.api.post<Structures, Record<string, never>>(
      `/structures/${kind}/upgrade`,
      {},
      { etag: entityTag(version) },
    );
  }
}
