import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { InboxPage } from './inbox.models';

/**
 * The comms module's HTTP surface (master plan §10.7, F-41).
 *
 * Nothing here names a manager or a club: the recipient is the authenticated account, resolved by the
 * server. The page walk is a keyset cursor, so a message arriving between two pages cannot shift the
 * window the way an offset would.
 */
@Injectable({ providedIn: 'root' })
export class InboxApi {
  private readonly api = inject(ApiClient);

  /** Reads one page of the inbox, optionally only the unread messages, continuing from a cursor. */
  list(cursor: string | null, unreadOnly: boolean): Observable<InboxPage> {
    const parameters = new URLSearchParams();

    if (cursor !== null) {
      parameters.set('cursor', cursor);
    }

    if (unreadOnly) {
      parameters.set('unread', 'true');
    }

    const query = parameters.toString();

    return this.api.get<InboxPage>(query.length === 0 ? '/inbox' : `/inbox?${query}`);
  }

  /** Marks one message read. */
  markRead(messageId: string): Observable<void> {
    return this.api.post<void, Record<string, never>>(`/inbox/${messageId}/read`, {});
  }

  /** Marks every unread message read. */
  markAllRead(): Observable<void> {
    return this.api.post<void, Record<string, never>>('/inbox/read-all', {});
  }
}
