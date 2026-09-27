import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { SyncStore } from '../sync/sync-store';
import { InboxApi } from './inbox-api';
import { InboxPage } from './inbox.models';

/**
 * The inbox module's view state (master plan §11.1, F-41).
 *
 * A feature-scoped store following `TrainingStore`. It holds the page last read and grows it by cursor, so
 * the screen never asks for an offset the server does not offer and a message arriving mid-read cannot shift
 * the window.
 *
 * A read or "mark all" is not optimistic: the server is asked first, and the list is rewritten from the
 * answer. Marking is a small, reversible write, but a badge that promised a message was read and then
 * unread is worse than one that updates a moment later (`§11.2`'s rule that money-and-deadline commands are
 * never optimistic applies here by the same argument).
 */
@Injectable({ providedIn: 'root' })
export class InboxStore {
  private readonly api = inject(InboxApi);

  // The navigation badge lives on the shell's poll, so a mark asks it to read again rather than leaving the
  // badge a poll behind the screen the manager is looking at.
  private readonly sync = inject(SyncStore);

  private readonly pageSignal = signal<InboxPage | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly loadingMoreSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly actionErrorSignal = signal<string | null>(null);
  private readonly markingIdSignal = signal<string | null>(null);
  private readonly markingAllSignal = signal(false);
  private readonly unreadOnlySignal = signal(false);

  /** The messages last read, newest first. */
  readonly messages = computed(() => this.pageSignal()?.messages ?? []);

  /** How many of the manager's messages are unread, for the badge. */
  readonly unreadCount = computed(() => this.pageSignal()?.unreadCount ?? 0);

  /** Whether a further page exists, so the screen offers to load one. */
  readonly hasMore = computed(() => (this.pageSignal()?.nextCursor ?? null) !== null);

  /** Whether the first page is being read. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Whether a further page is being read. */
  readonly loadingMore = this.loadingMoreSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Why the last mark failed, announced beside the list. */
  readonly actionError = this.actionErrorSignal.asReadonly();

  /** The message whose mark is mid-flight, so only that row's control is disabled. */
  readonly markingId = this.markingIdSignal.asReadonly();

  /** Whether "mark all" is mid-flight. */
  readonly markingAll = this.markingAllSignal.asReadonly();

  /** Whether the list is filtered to unread messages only. */
  readonly unreadOnly = this.unreadOnlySignal.asReadonly();

  /** Reads the first page, replacing whatever was shown. */
  load(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.actionErrorSignal.set(null);

    this.api.list(null, this.unreadOnlySignal()).subscribe({
      next: (page) => {
        this.pageSignal.set(page);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your inbox could not be loaded.',
        );
      },
    });
  }

  /** Reads the next page and appends it. */
  loadMore(): void {
    const cursor = this.pageSignal()?.nextCursor ?? null;

    if (cursor === null || this.loadingMoreSignal()) {
      return;
    }

    this.loadingMoreSignal.set(true);
    this.errorSignal.set(null);

    this.api.list(cursor, this.unreadOnlySignal()).subscribe({
      next: (page) => {
        this.append(page);
        this.loadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingMoreSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Older messages could not be loaded.',
        );
      },
    });
  }

  /** Switches the list between all messages and unread only, re-reading from the top. */
  setUnreadOnly(value: boolean): void {
    if (this.unreadOnlySignal() === value) {
      return;
    }

    this.unreadOnlySignal.set(value);
    this.pageSignal.set(null);

    this.load();
  }

  /** Marks one message read. */
  markRead(messageId: string): void {
    if (this.markingIdSignal() !== null || this.markingAllSignal()) {
      return;
    }

    this.markingIdSignal.set(messageId);
    this.actionErrorSignal.set(null);

    this.api.markRead(messageId).subscribe({
      next: () => {
        this.markingIdSignal.set(null);
        this.markLocally(messageId);
        this.sync.refresh();
      },
      error: (error: unknown) => {
        this.markingIdSignal.set(null);
        this.actionErrorSignal.set(
          error instanceof ApiError ? error.detail : 'That message could not be marked read.',
        );
      },
    });
  }

  /** Marks every unread message read. */
  markAllRead(): void {
    if (this.markingAllSignal() || this.markingIdSignal() !== null) {
      return;
    }

    this.markingAllSignal.set(true);
    this.actionErrorSignal.set(null);

    this.api.markAllRead().subscribe({
      next: () => {
        this.markingAllSignal.set(false);
        this.markAllLocally();
        this.sync.refresh();
      },
      error: (error: unknown) => {
        this.markingAllSignal.set(false);
        this.actionErrorSignal.set(
          error instanceof ApiError ? error.detail : 'Your messages could not all be marked read.',
        );
      },
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.pageSignal.set(null);
    this.loadingSignal.set(false);
    this.loadingMoreSignal.set(false);
    this.markingIdSignal.set(null);
    this.markingAllSignal.set(false);
    this.unreadOnlySignal.set(false);
    this.errorSignal.set(null);
    this.actionErrorSignal.set(null);
  }

  private append(page: InboxPage): void {
    const current = this.pageSignal();

    if (current === null) {
      this.pageSignal.set(page);

      return;
    }

    this.pageSignal.set({
      ...page,
      messages: [...current.messages, ...page.messages],
    });
  }

  private markLocally(messageId: string): void {
    const page = this.pageSignal();

    if (page === null) {
      return;
    }

    this.pageSignal.set({
      ...page,
      messages: page.messages.map((message) =>
        message.id === messageId ? { ...message, isRead: true, readAt: page.serverTime } : message,
      ),
      unreadCount: Math.max(0, page.unreadCount - 1),
    });
  }

  private markAllLocally(): void {
    const page = this.pageSignal();

    if (page === null) {
      return;
    }

    this.pageSignal.set({
      ...page,
      messages: page.messages.map((message) => ({
        ...message,
        isRead: true,
        readAt: page.serverTime,
      })),
      unreadCount: 0,
    });
  }
}
