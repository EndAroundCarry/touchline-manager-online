import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { Conversation, ConversationList, SentDirectMessage } from './messages.models';

/**
 * The direct-message HTTP surface (`COM-10`…`COM-16`).
 *
 * A manager is named only by the identity the server handed out in a conversation; the sender is always the
 * authenticated account. Paging is a keyset cursor, like the inbox's.
 */
@Injectable({ providedIn: 'root' })
export class MessagesApi {
  private readonly api = inject(ApiClient);

  /** Reads one page of the conversation list. */
  list(cursor: string | null): Observable<ConversationList> {
    return this.api.get<ConversationList>(`/conversations${query(cursor)}`);
  }

  /** Reads one page of a thread, newest messages first. */
  conversation(id: string, cursor: string | null): Observable<Conversation> {
    return this.api.get<Conversation>(`/conversations/${id}${query(cursor)}`);
  }

  /** Starts a conversation with the manager who runs a club. */
  start(clubId: string, subject: string, body: string): Observable<SentDirectMessage> {
    return this.api.post<SentDirectMessage>('/conversations', { clubId, subject, body });
  }

  /** Replies in a conversation. */
  reply(id: string, body: string): Observable<SentDirectMessage> {
    return this.api.post<SentDirectMessage>(`/conversations/${id}/messages`, { body });
  }

  /** Marks every message addressed to the reader in a conversation read (`COM-16`). */
  markRead(id: string): Observable<void> {
    return this.api.post<void, Record<string, never>>(`/conversations/${id}/read`, {});
  }

  /** Blocks a manager (`COM-15`). */
  block(managerId: string): Observable<void> {
    return this.api.put<void, Record<string, never>>(`/blocked-managers/${managerId}`, {});
  }

  /** Lifts a block. */
  unblock(managerId: string): Observable<void> {
    return this.api.delete<void>(`/blocked-managers/${managerId}`);
  }
}

function query(cursor: string | null): string {
  return cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`;
}
