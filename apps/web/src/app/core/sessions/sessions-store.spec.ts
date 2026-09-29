import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { SessionsApi } from './sessions-api';
import { SessionsStore } from './sessions-store';
import { ActiveSession, Sessions } from './sessions.models';

function session(overrides: Partial<ActiveSession> = {}): ActiveSession {
  return {
    id: 'session-1',
    issuedAt: '2026-10-06T19:00:00Z',
    expiresAt: '2026-11-05T19:00:00Z',
    lastUsedAt: null,
    isCurrent: false,
    ...overrides,
  };
}

function list(overrides: Partial<Sessions> = {}): Sessions {
  return { sessions: [session()], serverTime: '2026-10-06T19:00:00Z', ...overrides };
}

function createApiStub() {
  return { list: vi.fn(), revoke: vi.fn() };
}

describe('SessionsStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: SessionsStore;

  beforeEach(() => {
    api = createApiStub();
    TestBed.configureTestingModule({ providers: [{ provide: SessionsApi, useValue: api }] });
    store = TestBed.inject(SessionsStore);
  });

  it('reads the sessions', () => {
    api.list.mockReturnValue(of(list({ sessions: [session(), session({ id: 'session-2' })] })));

    store.load();

    expect(store.sessions().length).toBe(2);
    expect(store.loading()).toBe(false);
  });

  it('reports a read failure', () => {
    api.list.mockReturnValue(
      throwError(() => new ApiError(500, 'SERVER_ERROR', 'Boom.', null, new Map())),
    );

    store.load();

    expect(store.error()).toBe('Boom.');
    expect(store.sessions()).toEqual([]);
  });

  it('revokes a session and removes it from the list', () => {
    api.list.mockReturnValue(of(list({ sessions: [session(), session({ id: 'session-2' })] })));
    store.load();
    api.revoke.mockReturnValue(of(undefined));

    store.revoke('session-1');

    expect(api.revoke).toHaveBeenCalledWith('session-1');
    expect(store.sessions().map((entry) => entry.id)).toEqual(['session-2']);
    expect(store.revokingId()).toBeNull();
  });

  it('keeps the row when a revoke fails', () => {
    api.list.mockReturnValue(of(list()));
    store.load();
    api.revoke.mockReturnValue(
      throwError(() => new ApiError(404, 'SESSION_NOT_FOUND', 'No such session.', null, new Map())),
    );

    store.revoke('session-1');

    expect(store.sessions().length).toBe(1);
    expect(store.error()).toBe('No such session.');
  });

  it('does not start a second revoke while one is in flight', () => {
    api.list.mockReturnValue(of(list()));
    store.load();

    const inFlight = new Subject<void>();

    api.revoke.mockReturnValue(inFlight.asObservable());

    store.revoke('session-1');
    store.revoke('session-2');

    expect(api.revoke).toHaveBeenCalledTimes(1);
    expect(store.revokingId()).toBe('session-1');

    inFlight.next();
    inFlight.complete();

    expect(store.revokingId()).toBeNull();
  });

  it('drops the list when the session ends', () => {
    api.list.mockReturnValue(of(list()));
    store.load();

    store.clear();

    expect(store.sessions()).toEqual([]);
    expect(store.error()).toBeNull();
  });
});
