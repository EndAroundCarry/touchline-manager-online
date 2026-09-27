import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { SyncSummary } from '../inbox/inbox.models';

/**
 * The synchronization poll's HTTP surface (master plan §10.7, §11.2, ADR-0007).
 *
 * One lightweight read the shell repeats while the tab is visible, so the unread badge stays fresh without
 * re-reading every feature. It deliberately does not duplicate a feature payload.
 */
@Injectable({ providedIn: 'root' })
export class SyncApi {
  private readonly api = inject(ApiClient);

  /** Reads the unread inbox count and the server's current instant. */
  summary(): Observable<SyncSummary> {
    return this.api.get<SyncSummary>('/sync');
  }
}
