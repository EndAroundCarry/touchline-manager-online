import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { SyncStore } from '../sync/sync-store';
import { MessagesApi } from './messages-api';
import { Conversation, ConversationList, DirectMessage } from './messages.models';

/**
 * The direct-message view state (`COM-10`…`COM-16`).
 *
 * Two screens share it: the conversation list and one open thread. Like `InboxStore` nothing is optimistic:
 * a send, a mark and a block are asked of the server first, and the screen is rewritten from what it answers,
 * because a message that appeared and then vanished (a block, a rate limit) is worse than one that appears a
 * moment later.
 *
 * A thread is held newest first, as the server pages it, so "load earlier" appends. {@link threadMessages}
 * turns it round for reading.
 */
@Injectable({ providedIn: 'root' })
export class ConversationsStore {
  private readonly api = inject(MessagesApi);

  // The tab and navigation badge live on the shell's poll, so a mark or a send asks it to read again.
  private readonly sync = inject(SyncStore);

  private readonly listSignal = signal<ConversationList | null>(null);
  private readonly listLoadingSignal = signal(false);
  private readonly listLoadingMoreSignal = signal(false);
  private readonly listErrorSignal = signal<string | null>(null);

  private readonly threadSignal = signal<Conversation | null>(null);
  private readonly threadLoadingSignal = signal(false);
  private readonly threadLoadingMoreSignal = signal(false);
  private readonly threadErrorSignal = signal<string | null>(null);

  private readonly sendingSignal = signal(false);
  private readonly sendErrorSignal = signal<string | null>(null);
  private readonly blockingSignal = signal(false);
  private readonly blockErrorSignal = signal<string | null>(null);

  /** The conversations last read, most recently active first. */
  readonly conversations = computed(() => this.listSignal()?.conversations ?? []);

  /** How many direct messages are unread, for the tab. */
  readonly unreadCount = computed(() => this.listSignal()?.unreadCount ?? 0);

  /** Whether a further page of conversations exists. */
  readonly hasMoreConversations = computed(() => (this.listSignal()?.nextCursor ?? null) !== null);

  readonly listLoading = this.listLoadingSignal.asReadonly();
  readonly listLoadingMore = this.listLoadingMoreSignal.asReadonly();
  readonly listError = this.listErrorSignal.asReadonly();

  /** The open thread: subject, the other manager and what a reply may do. */
  readonly thread = this.threadSignal.asReadonly();

  /** The open thread's messages, oldest first, for reading. */
  readonly threadMessages = computed<readonly DirectMessage[]>(() =>
    (this.threadSignal()?.messages ?? []).slice().reverse(),
  );

  /** Whether older messages exist than the ones held. */
  readonly hasEarlier = computed(() => (this.threadSignal()?.nextCursor ?? null) !== null);

  readonly threadLoading = this.threadLoadingSignal.asReadonly();
  readonly threadLoadingMore = this.threadLoadingMoreSignal.asReadonly();
  readonly threadError = this.threadErrorSignal.asReadonly();

  /** Whether a send is mid-flight. */
  readonly sending = this.sendingSignal.asReadonly();

  /** Why the last send was refused, in words. */
  readonly sendError = this.sendErrorSignal.asReadonly();

  /** Whether a block or unblock is mid-flight. */
  readonly blocking = this.blockingSignal.asReadonly();

  /** Why the last block or unblock failed. */
  readonly blockError = this.blockErrorSignal.asReadonly();

  /** Reads the first page of conversations. */
  loadList(): void {
    this.listLoadingSignal.set(true);
    this.listErrorSignal.set(null);

    this.api.list(null).subscribe({
      next: (page) => {
        this.listSignal.set(page);
        this.listLoadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.listLoadingSignal.set(false);
        this.listErrorSignal.set(describe(error, 'Your conversations could not be loaded.'));
      },
    });
  }

  /** Reads the next page of conversations and appends it. */
  loadMoreConversations(): void {
    const current = this.listSignal();
    const cursor = current?.nextCursor ?? null;

    if (current === null || cursor === null || this.listLoadingMoreSignal()) {
      return;
    }

    this.listLoadingMoreSignal.set(true);
    this.listErrorSignal.set(null);

    this.api.list(cursor).subscribe({
      next: (page) => {
        this.listSignal.set({
          ...page,
          conversations: [...current.conversations, ...page.conversations],
        });
        this.listLoadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.listLoadingMoreSignal.set(false);
        this.listErrorSignal.set(describe(error, 'Older conversations could not be loaded.'));
      },
    });
  }

  /** Opens a thread: reads its newest page, then marks what is addressed to the reader read (`COM-16`). */
  openThread(id: string): void {
    this.threadSignal.set(null);
    this.threadLoadingSignal.set(true);
    this.threadErrorSignal.set(null);
    this.sendErrorSignal.set(null);
    this.blockErrorSignal.set(null);

    this.api.conversation(id, null).subscribe({
      next: (thread) => {
        this.threadSignal.set(thread);
        this.threadLoadingSignal.set(false);
        this.markThreadRead(thread);
      },
      error: (error: unknown) => {
        this.threadLoadingSignal.set(false);
        this.threadErrorSignal.set(describe(error, 'That conversation could not be loaded.'));
      },
    });
  }

  /** Reads the next page of older messages and appends it. */
  loadEarlier(): void {
    const current = this.threadSignal();
    const cursor = current?.nextCursor ?? null;

    if (current === null || cursor === null || this.threadLoadingMoreSignal()) {
      return;
    }

    this.threadLoadingMoreSignal.set(true);
    this.threadErrorSignal.set(null);

    this.api.conversation(current.id, cursor).subscribe({
      next: (page) => {
        this.threadSignal.set({
          ...current,
          messages: [...current.messages, ...page.messages],
          nextCursor: page.nextCursor,
        });
        this.threadLoadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.threadLoadingMoreSignal.set(false);
        this.threadErrorSignal.set(describe(error, 'Earlier messages could not be loaded.'));
      },
    });
  }

  /** Starts a conversation, then reports where it went. */
  start(
    clubId: string,
    subject: string,
    body: string,
    onSent: (conversationId: string) => void,
  ): void {
    if (this.sendingSignal()) {
      return;
    }

    this.sendingSignal.set(true);
    this.sendErrorSignal.set(null);

    this.api.start(clubId, subject, body).subscribe({
      next: (sent) => {
        this.sendingSignal.set(false);
        this.sync.refresh();
        onSent(sent.conversationId);
      },
      error: (error: unknown) => {
        this.sendingSignal.set(false);
        this.sendErrorSignal.set(describe(error, 'Your message could not be sent.'));
      },
    });
  }

  /** Replies in the open thread, then reads it again so the message appears from the server's answer. */
  reply(body: string, onSent: () => void): void {
    const thread = this.threadSignal();

    if (thread === null || this.sendingSignal()) {
      return;
    }

    this.sendingSignal.set(true);
    this.sendErrorSignal.set(null);

    this.api.reply(thread.id, body).subscribe({
      next: () => {
        this.sendingSignal.set(false);
        this.sync.refresh();
        this.refreshThread(thread.id);
        onSent();
      },
      error: (error: unknown) => {
        this.sendingSignal.set(false);
        this.sendErrorSignal.set(describe(error, 'Your message could not be sent.'));

        // A refusal such as a block changes what the thread allows, so the reason is read again.
        this.refreshThread(thread.id);
      },
    });
  }

  /** Blocks the other manager in the open thread (`COM-15`). */
  block(): void {
    this.setBlocked(true);
  }

  /** Lifts the block on the other manager in the open thread. */
  unblock(): void {
    this.setBlocked(false);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.listSignal.set(null);
    this.listLoadingSignal.set(false);
    this.listLoadingMoreSignal.set(false);
    this.listErrorSignal.set(null);
    this.threadSignal.set(null);
    this.threadLoadingSignal.set(false);
    this.threadLoadingMoreSignal.set(false);
    this.threadErrorSignal.set(null);
    this.sendingSignal.set(false);
    this.sendErrorSignal.set(null);
    this.blockingSignal.set(false);
    this.blockErrorSignal.set(null);
  }

  private setBlocked(blocked: boolean): void {
    const thread = this.threadSignal();

    if (thread === null || this.blockingSignal()) {
      return;
    }

    this.blockingSignal.set(true);
    this.blockErrorSignal.set(null);

    const request = blocked
      ? this.api.block(thread.otherManager.managerId)
      : this.api.unblock(thread.otherManager.managerId);

    request.subscribe({
      next: () => {
        this.blockingSignal.set(false);
        this.refreshThread(thread.id);
      },
      error: (error: unknown) => {
        this.blockingSignal.set(false);
        this.blockErrorSignal.set(
          describe(
            error,
            blocked ? 'That manager could not be blocked.' : 'That block could not be lifted.',
          ),
        );
      },
    });
  }

  /** Reads the newest page again and replaces the thread, keeping the reader's place if it fails. */
  private refreshThread(id: string): void {
    this.api.conversation(id, null).subscribe({
      next: (thread) => {
        if (this.threadSignal()?.id === id) {
          this.threadSignal.set(thread);
        }
      },
      error: () => {
        // The thread already on screen stays; the next open reads it again.
      },
    });
  }

  private markThreadRead(thread: Conversation): void {
    if (!thread.messages.some((message) => !message.fromMe && message.readAt === null)) {
      return;
    }

    this.api.markRead(thread.id).subscribe({
      next: () => {
        const current = this.threadSignal();

        if (current?.id === thread.id) {
          this.threadSignal.set({
            ...current,
            messages: current.messages.map((message) =>
              message.fromMe || message.readAt !== null
                ? message
                : { ...message, readAt: current.serverTime },
            ),
          });
        }

        this.sync.refresh();
      },
      error: () => {
        // The messages stay unread and the next open tries again; reading them is not blocked.
      },
    });
  }
}

function describe(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback;
  }

  if (error.status === 429) {
    return 'You are sending messages too quickly. Try again in a little while.';
  }

  return error.detail.length > 0 ? error.detail : fallback;
}
