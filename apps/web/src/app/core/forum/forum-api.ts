import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  CreatedForumThread,
  ForumOverview,
  ForumPost,
  ForumThreadDetail,
  ForumThreadList,
} from './forum.models';

/**
 * The community forum's HTTP surface.
 *
 * The author of a thread or a reply is always the authenticated manager; the page never names one. A country
 * subforum has one address for every country, because the server reads the manager's country from the club they
 * run.
 */
@Injectable({ providedIn: 'root' })
export class ForumApi {
  private readonly api = inject(ApiClient);

  /** Reads the sections and subforums the manager may open, with their counts. */
  overview(): Observable<ForumOverview> {
    return this.api.get<ForumOverview>('/forum/overview');
  }

  /** Reads the threads of one subforum. */
  threads(subforumId: string): Observable<ForumThreadList> {
    return this.api.get<ForumThreadList>(
      `/forum/subforums/${encodeURIComponent(subforumId)}/threads`,
    );
  }

  /** Reads one thread with its posts. */
  thread(threadId: string): Observable<ForumThreadDetail> {
    return this.api.get<ForumThreadDetail>(`/forum/threads/${encodeURIComponent(threadId)}`);
  }

  /** Starts a thread in a subforum. */
  createThread(subforumId: string, title: string, body: string): Observable<CreatedForumThread> {
    return this.api.post<CreatedForumThread>(
      `/forum/subforums/${encodeURIComponent(subforumId)}/threads`,
      { title, body },
    );
  }

  /** Replies in a thread. */
  reply(threadId: string, body: string): Observable<ForumPost> {
    return this.api.post<ForumPost>(`/forum/threads/${encodeURIComponent(threadId)}/posts`, {
      body,
    });
  }
}
