import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { Sessions } from './sessions.models';

/**
 * The session-management surface (`F-07`).
 *
 * Both calls are credentialed so the HttpOnly refresh cookie travels with them: that is how the server
 * knows which listed session is the current one. They are *not* marked as auth endpoints, because they
 * authenticate with the bearer token and must go through the normal 401 handling like any other
 * authenticated read.
 */
@Injectable({ providedIn: 'root' })
export class SessionsApi {
  private readonly api = inject(ApiClient);

  /** Lists the account's active sessions, marking the current one. */
  list(): Observable<Sessions> {
    return this.api.get<Sessions>('/auth/sessions', { withCredentials: true });
  }

  /** Revokes one of the account's sessions. */
  revoke(sessionId: string): Observable<void> {
    return this.api.delete<void>(`/auth/sessions/${sessionId}`, { withCredentials: true });
  }
}
