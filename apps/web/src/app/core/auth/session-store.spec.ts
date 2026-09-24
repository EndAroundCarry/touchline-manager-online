import { TestBed } from '@angular/core/testing';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthSession, UserProfile } from './auth.models';
import { SessionStore, quoteVersion } from './session-store';

/**
 * The session store is the single source of truth for "is this client signed in", so these tests pin
 * the guarantees the guards and the interceptor rely on: the sharing of one rotation between
 * concurrent callers (ADR-0002), that a failed restore is never fatal, and that signing out always
 * clears local state.
 */

const profile: UserProfile = {
  id: 'user-1',
  email: 'manager@example.com',
  displayName: 'Manager One',
  emailVerified: true,
  status: 'active',
  roles: ['player'],
  lastLoginAt: null,
  createdAt: '2026-09-01T00:00:00Z',
};

function session(accessToken = 'access-token-1'): AuthSession {
  return {
    accessToken,
    accessTokenExpiresAt: '2026-09-01T00:15:00Z',
    serverTime: '2026-09-01T00:00:00Z',
    user: profile,
  };
}

/** A hand-written double, so no test in this layer needs a database or a socket. */
function createAuthApiStub() {
  return {
    register: vi.fn(),
    verifyEmail: vi.fn(),
    resendVerification: vi.fn(),
    login: vi.fn(),
    refresh: vi.fn(),
    logout: vi.fn(),
    logoutAll: vi.fn(),
    forgotPassword: vi.fn(),
    resetPassword: vi.fn(),
    profile: vi.fn(),
    updateDisplayName: vi.fn(),
    deleteAccount: vi.fn(),
  };
}

describe('SessionStore', () => {
  let api: ReturnType<typeof createAuthApiStub>;
  let store: SessionStore;

  beforeEach(() => {
    api = createAuthApiStub();

    TestBed.configureTestingModule({
      providers: [{ provide: AuthApi, useValue: api }],
    });

    store = TestBed.inject(SessionStore);
  });

  it('starts out not knowing whether there is a session', () => {
    expect(store.status()).toBe('unknown');
    expect(store.isAuthenticated()).toBe(false);
    expect(store.accessToken()).toBeNull();
  });

  it('adopts the session returned by a successful sign-in', async () => {
    api.login.mockReturnValue(of(session()));

    await firstValueFrom(store.login({ email: profile.email, password: 'correct-horse-battery' }));

    expect(store.isAuthenticated()).toBe(true);
    expect(store.user()).toEqual(profile);
    expect(store.displayName()).toBe('Manager One');
    expect(store.isVerified()).toBe(true);
    expect(store.accessToken()).toBe('access-token-1');
  });

  it('rotates the refresh session only once when several callers ask at the same time', () => {
    // A screen firing three requests on load must not consume the refresh token three times: the
    // second rotation would present a spent token and the server would revoke the family (ADR-0002).
    const pending = new Subject<AuthSession>();
    api.refresh.mockReturnValue(pending);

    const first = store.refreshOnce();
    const second = store.refreshOnce();

    expect(first).toBe(second);

    const seen: AuthSession[] = [];
    first.subscribe((value) => seen.push(value));
    second.subscribe((value) => seen.push(value));

    expect(api.refresh).toHaveBeenCalledTimes(1);

    pending.next(session());
    pending.complete();

    expect(seen).toHaveLength(2);
    expect(store.isAuthenticated()).toBe(true);
  });

  it('stops sharing a rotation once it has settled', () => {
    api.refresh.mockReturnValue(of(session()));

    firstValueFrom(store.refreshOnce());
    firstValueFrom(store.refreshOnce());

    expect(api.refresh).toHaveBeenCalledTimes(2);
  });

  it('restores an existing session on start', async () => {
    api.refresh.mockReturnValue(of(session()));

    await expect(store.restore()).resolves.toBeUndefined();

    expect(store.isAuthenticated()).toBe(true);
    expect(store.accessToken()).toBe('access-token-1');
  });

  it('leaves the client signed out instead of throwing when a restore fails', async () => {
    // An unreachable API must not stop the public screens from rendering.
    api.refresh.mockReturnValue(throwError(() => new Error('offline')));

    await expect(store.restore()).resolves.toBeUndefined();

    expect(store.status()).toBe('anonymous');
    expect(store.isAuthenticated()).toBe(false);
  });

  it('clears the local session even when signing out fails', async () => {
    api.login.mockReturnValue(of(session()));
    await firstValueFrom(store.login({ email: profile.email, password: 'correct-horse-battery' }));

    api.logout.mockReturnValue(throwError(() => new Error('offline')));

    await firstValueFrom(store.logout(), { defaultValue: undefined });

    expect(store.isAuthenticated()).toBe(false);
    expect(store.accessToken()).toBeNull();
    expect(store.user()).toBeNull();
  });

  it('clears the local session when signing out everywhere', async () => {
    api.login.mockReturnValue(of(session()));
    await firstValueFrom(store.login({ email: profile.email, password: 'correct-horse-battery' }));

    api.logoutAll.mockReturnValue(of(undefined));

    await firstValueFrom(store.logoutAll());

    expect(store.isAuthenticated()).toBe(false);
  });

  it('carries the entity tag from a profile read into the next conditional write', async () => {
    api.profile.mockReturnValue(of({ profile, etag: '"3"' }));

    await firstValueFrom(store.loadProfile());

    const renamed = { ...profile, displayName: 'Manager Two' };
    api.updateDisplayName.mockReturnValue(of({ user: renamed, version: 4 }));

    const updated = await firstValueFrom(store.updateDisplayName('Manager Two'));

    expect(api.updateDisplayName).toHaveBeenCalledWith('Manager Two', '"3"');
    expect(updated.version).toBe(4);
    expect(store.displayName()).toBe('Manager Two');
  });

  it('uses the version returned by a write as the precondition for the following one', async () => {
    api.profile.mockReturnValue(of({ profile, etag: '"3"' }));
    await firstValueFrom(store.loadProfile());

    api.updateDisplayName.mockReturnValue(of({ user: profile, version: 4 }));
    await firstValueFrom(store.updateDisplayName('Manager Two'));

    api.updateDisplayName.mockReturnValue(of({ user: profile, version: 5 }));
    await firstValueFrom(store.updateDisplayName('Manager Three'));

    expect(api.updateDisplayName).toHaveBeenLastCalledWith('Manager Three', quoteVersion(4));
  });

  it('refuses to write a profile change before the profile has been read', async () => {
    // There is no version to send, so the write must fail loudly rather than be sent unconditionally.
    api.updateDisplayName.mockReturnValue(of({ user: profile, version: 2 }));

    await expect(firstValueFrom(store.updateDisplayName('Manager Two'))).rejects.toThrow();

    expect(api.updateDisplayName).not.toHaveBeenCalled();
  });

  it('closes the account and drops the local session', async () => {
    api.login.mockReturnValue(of(session()));
    await firstValueFrom(store.login({ email: profile.email, password: 'correct-horse-battery' }));

    api.deleteAccount.mockReturnValue(
      of({ message: 'The account is closing.', serverTime: '2026-09-01T00:01:00Z' }),
    );

    await firstValueFrom(store.deleteAccount('correct-horse-battery'));

    expect(store.isAuthenticated()).toBe(false);
    expect(store.user()).toBeNull();
  });

  it('drops every trace of the session when forgotten', async () => {
    api.login.mockReturnValue(of(session()));
    await firstValueFrom(store.login({ email: profile.email, password: 'correct-horse-battery' }));

    store.forget();

    expect(store.status()).toBe('anonymous');
    expect(store.user()).toBeNull();
    expect(store.accessToken()).toBeNull();
    expect(store.displayName()).toBeNull();
  });
});

describe('quoteVersion', () => {
  it('formats a version as the strong entity tag the server uses', () => {
    expect(quoteVersion(7)).toBe('"7"');
  });
});
