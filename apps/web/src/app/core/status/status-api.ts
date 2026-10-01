import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { PublicStatus } from './status.models';

/**
 * The public service status read (master plan §16 Stage 15, `F-55`, ADR-0050).
 *
 * One anonymous read serves the status page and the versioned legal pages. It is not the shell's poll: it is
 * only read by the screens that show it, so it never runs on a background tab that is not looking at it.
 */
@Injectable({ providedIn: 'root' })
export class StatusApi {
  private readonly api = inject(ApiClient);

  /** Reads the public service status and the published document versions. */
  summary(): Observable<PublicStatus> {
    return this.api.get<PublicStatus>('/status');
  }
}
