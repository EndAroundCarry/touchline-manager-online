import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { NewsApi } from './news-api';
import { NewsStore } from './news-store';
import { NewsItem, NewsPage } from './news.models';

function item(overrides: Partial<NewsItem> = {}): NewsItem {
  return {
    id: 'news-1',
    category: 'result',
    countryId: null,
    divisionId: null,
    title: 'A result',
    body: 'A body.',
    publishedAt: '2026-10-06T19:00:00Z',
    ...overrides,
  };
}

function page(overrides: Partial<NewsPage> = {}): NewsPage {
  return {
    items: [item()],
    nextCursor: null,
    serverTime: '2026-10-06T19:00:00Z',
    ...overrides,
  };
}

function createApiStub() {
  return { list: vi.fn() };
}

describe('NewsStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: NewsStore;

  beforeEach(() => {
    api = createApiStub();
    TestBed.configureTestingModule({ providers: [{ provide: NewsApi, useValue: api }] });
    store = TestBed.inject(NewsStore);
  });

  it('reads the first page and exposes the cursor', () => {
    api.list.mockReturnValue(of(page({ nextCursor: 'cursor-1' })));

    store.load();

    expect(api.list).toHaveBeenCalledWith(null, null, null);
    expect(store.items()).toHaveLength(1);
    expect(store.hasMore()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('scopes the read to a division or a country', () => {
    api.list.mockReturnValue(of(page()));

    store.load('division-1', 'country-1');

    expect(api.list).toHaveBeenCalledWith(null, 'division-1', 'country-1');
  });

  it('appends the next page rather than replacing the list', () => {
    api.list.mockReturnValueOnce(of(page({ nextCursor: 'cursor-1' })));
    store.load();
    api.list.mockReturnValueOnce(of(page({ items: [item({ id: 'news-2' })], nextCursor: null })));

    store.loadMore();

    expect(api.list).toHaveBeenLastCalledWith('cursor-1', null, null);
    expect(store.items()).toHaveLength(2);
    expect(store.hasMore()).toBe(false);
  });

  it('does not walk the cursor when there is no next page', () => {
    api.list.mockReturnValue(of(page({ nextCursor: null })));
    store.load();
    api.list.mockClear();

    store.loadMore();

    expect(api.list).not.toHaveBeenCalled();
  });

  it('reports why a read failed and shows nothing', () => {
    api.list.mockReturnValue(
      throwError(() => new ApiError(404, 'WORLD_NOT_SEEDED', 'No world.', null, new Map())),
    );

    store.load();

    expect(store.error()).toBe('No world.');
    expect(store.items()).toHaveLength(0);
  });

  it('leaves nothing behind when the session ends', () => {
    api.list.mockReturnValue(of(page({ nextCursor: 'cursor-1' })));
    store.load();

    store.clear();

    expect(store.items()).toHaveLength(0);
    expect(store.hasMore()).toBe(false);
    expect(store.error()).toBeNull();
  });
});
