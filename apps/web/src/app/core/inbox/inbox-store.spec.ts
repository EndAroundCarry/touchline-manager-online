import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { SyncStore } from '../sync/sync-store';
import { InboxApi } from './inbox-api';
import { InboxStore } from './inbox-store';
import { InboxMessage, InboxPage } from './inbox.models';

/**
 * The inbox store's guarantees (`F-41`, `§10.7`, `§11.2`).
 *
 * The cursor walk, the unread count, and the two marks are the whole surface: a read replaces the list, a
 * "load older" appends rather than replacing, and a mark rewrites only the messages it touched.
 */

function message(overrides: Partial<InboxMessage> = {}): InboxMessage {
  return {
    id: 'm1',
    category: 'result',
    templateKey: 'inbox.result.recorded',
    title: 'Round 1: won 2–1',
    body: 'At home to Vale Athletic. You are 3rd in the table after round 1.',
    relatedEntityId: 'match-1',
    isRead: false,
    createdAt: '2026-10-06T19:00:00Z',
    readAt: null,
    ...overrides,
  };
}

function page(overrides: Partial<InboxPage> = {}): InboxPage {
  return {
    messages: [message()],
    unreadCount: 1,
    nextCursor: null,
    serverTime: '2026-10-06T19:00:00Z',
    ...overrides,
  };
}

function createApiStub() {
  return { list: vi.fn(), markRead: vi.fn(), markAllRead: vi.fn() };
}

describe('InboxStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let sync: { refresh: ReturnType<typeof vi.fn> };
  let store: InboxStore;

  beforeEach(() => {
    api = createApiStub();
    sync = { refresh: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: InboxApi, useValue: api },
        { provide: SyncStore, useValue: sync },
      ],
    });

    store = TestBed.inject(InboxStore);
  });

  it('reads the first page and exposes the unread count and cursor', () => {
    api.list.mockReturnValue(of(page({ unreadCount: 4, nextCursor: 'cursor-1' })));

    store.load();

    expect(api.list).toHaveBeenCalledWith(null, false);
    expect(store.messages()).toHaveLength(1);
    expect(store.unreadCount()).toBe(4);
    expect(store.hasMore()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('reports why a read failed', () => {
    api.list.mockReturnValue(
      throwError(() => new ApiError(403, 'NO_CLUB', 'Denied.', null, new Map())),
    );

    store.load();

    expect(store.error()).toBe('Denied.');
    expect(store.messages()).toHaveLength(0);
  });

  it('appends the next page rather than replacing the list', () => {
    api.list
      .mockReturnValueOnce(of(page({ nextCursor: 'cursor-1' })))
      .mockReturnValueOnce(
        of(page({ messages: [message({ id: 'm2' })], nextCursor: null, unreadCount: 1 })),
      );

    store.load();
    store.loadMore();

    expect(api.list).toHaveBeenLastCalledWith('cursor-1', false);
    expect(store.messages().map((item) => item.id)).toEqual(['m1', 'm2']);
    expect(store.hasMore()).toBe(false);
  });

  it('re-reads from the top when the unread filter is switched on', () => {
    api.list.mockReturnValue(of(page()));

    store.load();
    store.setUnreadOnly(true);

    expect(api.list).toHaveBeenLastCalledWith(null, true);
    expect(store.unreadOnly()).toBe(true);
  });

  it('marks one message read and drops the unread count by one', () => {
    api.list.mockReturnValue(of(page({ unreadCount: 3 })));
    api.markRead.mockReturnValue(of(undefined));

    store.load();
    store.markRead('m1');

    expect(api.markRead).toHaveBeenCalledWith('m1');
    expect(store.messages()[0].isRead).toBe(true);
    expect(store.unreadCount()).toBe(2);
    expect(sync.refresh).toHaveBeenCalledOnce();
  });

  it('marks every message read and empties the count', () => {
    api.list.mockReturnValue(
      of(page({ messages: [message(), message({ id: 'm2' })], unreadCount: 2 })),
    );
    api.markAllRead.mockReturnValue(of(undefined));

    store.load();
    store.markAllRead();

    expect(store.messages().every((item) => item.isRead)).toBe(true);
    expect(store.unreadCount()).toBe(0);
  });

  it('does not change the list when the server refuses the mark', () => {
    api.list.mockReturnValue(of(page({ unreadCount: 1 })));
    api.markRead.mockReturnValue(
      throwError(() => new ApiError(404, 'MESSAGE_NOT_FOUND', 'Gone.', null, new Map())),
    );

    store.load();
    store.markRead('m1');

    expect(store.actionError()).toBe('Gone.');
    expect(store.messages()[0].isRead).toBe(false);
    expect(store.unreadCount()).toBe(1);
  });

  it('leaves nothing behind when the session ends', () => {
    api.list.mockReturnValue(of(page({ unreadCount: 2 })));

    store.load();
    store.clear();

    expect(store.messages()).toHaveLength(0);
    expect(store.unreadCount()).toBe(0);
    expect(store.unreadOnly()).toBe(false);
  });
});
