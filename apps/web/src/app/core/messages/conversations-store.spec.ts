import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ApiError } from '../api/api-error';
import { SyncStore } from '../sync/sync-store';
import { ConversationsStore } from './conversations-store';
import { MessagesApi } from './messages-api';
import { conversation, conversationList, directMessage, summary } from './messages-test-data';

/**
 * The direct-message store's guarantees (`COM-10`…`COM-16`).
 *
 * Opening a thread marks what was addressed to the reader read, a send or a block is rewritten from the
 * server's answer rather than assumed, and a thread is held newest first but read oldest first.
 */

function createApiStub() {
  return {
    list: vi.fn(),
    conversation: vi.fn(),
    start: vi.fn(),
    reply: vi.fn(),
    markRead: vi.fn(),
    block: vi.fn(),
    unblock: vi.fn(),
  };
}

function refusal(status: number, detail: string): ApiError {
  return new ApiError(status, 'MANAGER_BLOCKED', detail, null, new Map());
}

describe('ConversationsStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let sync: { refresh: ReturnType<typeof vi.fn> };
  let store: ConversationsStore;

  beforeEach(() => {
    api = createApiStub();
    sync = { refresh: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: MessagesApi, useValue: api },
        { provide: SyncStore, useValue: sync },
      ],
    });

    store = TestBed.inject(ConversationsStore);
  });

  it('reads the first page of conversations with its unread count and cursor', () => {
    api.list.mockReturnValue(of(conversationList({ unreadCount: 4, nextCursor: 'cursor-1' })));

    store.loadList();

    expect(api.list).toHaveBeenCalledWith(null);
    expect(store.conversations()).toHaveLength(1);
    expect(store.unreadCount()).toBe(4);
    expect(store.hasMoreConversations()).toBe(true);
    expect(store.listLoading()).toBe(false);
  });

  it('appends the next page of conversations rather than replacing the list', () => {
    api.list.mockReturnValueOnce(of(conversationList({ nextCursor: 'cursor-1' })));
    store.loadList();

    api.list.mockReturnValueOnce(
      of(
        conversationList({ conversations: [summary({ id: 'conversation-2' })], nextCursor: null }),
      ),
    );
    store.loadMoreConversations();

    expect(api.list).toHaveBeenLastCalledWith('cursor-1');
    expect(store.conversations().map((c) => c.id)).toEqual(['conversation-1', 'conversation-2']);
    expect(store.hasMoreConversations()).toBe(false);
  });

  it('reports why the list could not be read', () => {
    api.list.mockReturnValue(throwError(() => refusal(500, 'The list is unavailable.')));

    store.loadList();

    expect(store.listError()).toBe('The list is unavailable.');
    expect(store.listLoading()).toBe(false);
  });

  it('opens a thread and marks what is addressed to the reader read (COM-16)', () => {
    api.conversation.mockReturnValue(of(conversation()));
    api.markRead.mockReturnValue(of(undefined));

    store.openThread('conversation-1');

    expect(api.conversation).toHaveBeenCalledWith('conversation-1', null);
    expect(api.markRead).toHaveBeenCalledWith('conversation-1');
    expect(store.threadMessages()[0].readAt).toBe('2026-10-20T12:00:00Z');
    expect(sync.refresh).toHaveBeenCalled();
  });

  it('does not ask to mark a thread read when nothing in it is unread for the reader', () => {
    api.conversation.mockReturnValue(
      of(
        conversation({
          messages: [
            directMessage({ id: 'dm-2', fromMe: true }),
            directMessage({ id: 'dm-1', readAt: '2026-10-20T11:00:00Z' }),
          ],
        }),
      ),
    );

    store.openThread('conversation-1');

    expect(api.markRead).not.toHaveBeenCalled();
  });

  it('keeps the messages unread when the mark fails, and still shows the thread', () => {
    api.conversation.mockReturnValue(of(conversation()));
    api.markRead.mockReturnValue(throwError(() => refusal(500, 'No.')));

    store.openThread('conversation-1');

    expect(store.thread()).not.toBeNull();
    expect(store.threadMessages()[0].readAt).toBeNull();
    expect(store.threadError()).toBeNull();
  });

  it('reads a thread oldest first although the server pages it newest first', () => {
    api.conversation.mockReturnValue(
      of(
        conversation({
          messages: [
            directMessage({ id: 'newest', fromMe: true }),
            directMessage({ id: 'oldest', fromMe: true }),
          ],
        }),
      ),
    );

    store.openThread('conversation-1');

    expect(store.threadMessages().map((m) => m.id)).toEqual(['oldest', 'newest']);
  });

  it('appends earlier messages behind the ones held and follows the cursor', () => {
    api.conversation.mockReturnValueOnce(
      of(
        conversation({
          messages: [directMessage({ id: 'm3', fromMe: true })],
          nextCursor: 'older',
        }),
      ),
    );
    store.openThread('conversation-1');

    api.conversation.mockReturnValueOnce(
      of(conversation({ messages: [directMessage({ id: 'm2', fromMe: true })], nextCursor: null })),
    );
    store.loadEarlier();

    expect(api.conversation).toHaveBeenLastCalledWith('conversation-1', 'older');
    expect(store.threadMessages().map((m) => m.id)).toEqual(['m2', 'm3']);
    expect(store.hasEarlier()).toBe(false);
  });

  it('reports a failed open of a thread', () => {
    api.conversation.mockReturnValue(
      throwError(
        () => new ApiError(404, 'CONVERSATION_NOT_FOUND', 'No such conversation.', null, new Map()),
      ),
    );

    store.openThread('missing');

    expect(store.thread()).toBeNull();
    expect(store.threadError()).toBe('No such conversation.');
  });

  it('starts a conversation, asks the shell to read again and reports where it went', () => {
    api.start.mockReturnValue(
      of({ conversationId: 'conversation-9', messageId: 'dm-9', sentAt: '2026-10-20T12:00:00Z' }),
    );
    const sent = vi.fn();

    store.start('club-b', 'A friendly', 'Hello', sent);

    expect(api.start).toHaveBeenCalledWith('club-b', 'A friendly', 'Hello');
    expect(sent).toHaveBeenCalledWith('conversation-9');
    expect(sync.refresh).toHaveBeenCalled();
    expect(store.sending()).toBe(false);
  });

  it('says plainly when a manager is not accepting messages (COM-15)', () => {
    api.start.mockReturnValue(
      throwError(() => refusal(409, 'That manager is not accepting your messages.')),
    );
    const sent = vi.fn();

    store.start('club-b', 'A friendly', 'Hello', sent);

    expect(sent).not.toHaveBeenCalled();
    expect(store.sendError()).toBe('That manager is not accepting your messages.');
    expect(store.sending()).toBe(false);
  });

  it('turns the rate limit into words (COM-14)', () => {
    api.start.mockReturnValue(throwError(() => refusal(429, '')));

    store.start('club-b', 'A friendly', 'Hello', vi.fn());

    expect(store.sendError()).toContain('too quickly');
  });

  it('replies, then reads the thread again so the message appears from the server', () => {
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [] })));
    store.openThread('conversation-1');

    api.reply.mockReturnValue(
      of({ conversationId: 'conversation-1', messageId: 'dm-2', sentAt: '2026-10-20T12:05:00Z' }),
    );
    api.conversation.mockReturnValueOnce(
      of(conversation({ messages: [directMessage({ id: 'dm-2', fromMe: true })] })),
    );
    const sent = vi.fn();

    store.reply('Sure', sent);

    expect(api.reply).toHaveBeenCalledWith('conversation-1', 'Sure');
    expect(sent).toHaveBeenCalled();
    expect(store.threadMessages().map((m) => m.id)).toEqual(['dm-2']);
  });

  it('reads the thread again after a refused reply, so the reason for the refusal shows', () => {
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [] })));
    store.openThread('conversation-1');

    api.reply.mockReturnValue(
      throwError(() => refusal(409, 'That manager is not accepting your messages.')),
    );
    api.conversation.mockReturnValueOnce(
      of(
        conversation({
          messages: [],
          canReply: false,
          replyBlockedReason: 'That manager is not accepting your messages.',
        }),
      ),
    );
    const sent = vi.fn();

    store.reply('Sure', sent);

    expect(sent).not.toHaveBeenCalled();
    expect(store.sendError()).toBe('That manager is not accepting your messages.');
    expect(store.thread()?.canReply).toBe(false);
  });

  it('blocks the other manager in the thread, then reads the thread again', () => {
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [] })));
    store.openThread('conversation-1');

    api.block.mockReturnValue(of(undefined));
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [], blockedByMe: true })));

    store.block();

    expect(api.block).toHaveBeenCalledWith('manager-b');
    expect(store.thread()?.blockedByMe).toBe(true);
    expect(store.blocking()).toBe(false);
  });

  it('lifts a block on the other manager', () => {
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [], blockedByMe: true })));
    store.openThread('conversation-1');

    api.unblock.mockReturnValue(of(undefined));
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [] })));

    store.unblock();

    expect(api.unblock).toHaveBeenCalledWith('manager-b');
    expect(store.thread()?.blockedByMe).toBe(false);
  });

  it('reports a block that failed and leaves the thread as it was', () => {
    api.conversation.mockReturnValueOnce(of(conversation({ messages: [] })));
    store.openThread('conversation-1');

    api.block.mockReturnValue(throwError(() => refusal(500, 'The block failed.')));

    store.block();

    expect(store.blockError()).toBe('The block failed.');
    expect(store.thread()?.blockedByMe).toBe(false);
  });

  it('forgets everything when the session ends', () => {
    api.list.mockReturnValue(of(conversationList()));
    api.conversation.mockReturnValue(of(conversation({ messages: [] })));
    store.loadList();
    store.openThread('conversation-1');

    store.clear();

    expect(store.conversations()).toEqual([]);
    expect(store.thread()).toBeNull();
    expect(store.unreadCount()).toBe(0);
  });
});
