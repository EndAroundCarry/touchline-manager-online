import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { problemDetailsInterceptor } from '../api/problem-details.interceptor';
import { authEndpointContext } from './auth-context';
import { authInterceptor } from './auth.interceptor';
import { AuthSession } from './auth.models';
import { SessionStore } from './session-store';

/**
 * The interceptor owns two guarantees: an ordinary request carries the current access token, and an
 * expired token is recovered from exactly once. Both are asserted here because a mistake in either
 * direction is silent — a missing header looks like a permissions bug, and a retry loop looks like a
 * hung screen.
 */

const problem = {
  code: 'UNAUTHORIZED',
  title: 'Unauthorized',
  status: 401,
  detail: 'Sign in again.',
};

const session: AuthSession = {
  accessToken: 'access-token-2',
  accessTokenExpiresAt: '2026-09-01T00:15:00Z',
  serverTime: '2026-09-01T00:00:00Z',
  user: {
    id: 'user-1',
    email: 'manager@example.com',
    displayName: 'Manager One',
    emailVerified: true,
    status: 'active',
    roles: ['player'],
    lastLoginAt: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
};

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let token: ReturnType<typeof signal<string | null>>;
  let store: {
    accessToken: () => string | null;
    refreshOnce: ReturnType<typeof vi.fn>;
    forget: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    token = signal<string | null>('access-token-1');

    store = {
      accessToken: () => token(),
      refreshOnce: vi.fn(),
      forget: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        // The same order as app.config.ts: the auth interceptor must see the `ApiError` the
        // problem-details interceptor produces, or it could not tell an expiry from any other failure.
        provideHttpClient(withInterceptors([authInterceptor, problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: store },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => http.verify());

  it('attaches the current access token to an ordinary request', () => {
    client.get('/api/v1/me').subscribe();

    const request = http.expectOne('/api/v1/me');

    expect(request.request.headers.get('Authorization')).toBe('Bearer access-token-1');

    request.flush({});
  });

  it('does not attach a token to an auth endpoint', () => {
    client.post('/api/v1/auth/login', {}, { context: authEndpointContext() }).subscribe();

    const request = http.expectOne('/api/v1/auth/login');

    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush({});
  });

  it('reports a rejected sign-in as an answer rather than an expiry', () => {
    let failure: unknown;

    client
      .post('/api/v1/auth/login', {}, { context: authEndpointContext() })
      .subscribe({ error: (error: unknown) => (failure = error) });

    http
      .expectOne('/api/v1/auth/login')
      .flush(problem, { status: 401, statusText: 'Unauthorized' });

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).code).toBe('UNAUTHORIZED');
    // A 401 from a sign-in form means "wrong credentials"; refreshing here would be nonsense.
    expect(store.refreshOnce).not.toHaveBeenCalled();
  });

  it('rotates the session and replays an expired request exactly once', () => {
    store.refreshOnce.mockImplementation(() => {
      token.set('access-token-2');

      return of(session);
    });

    const received: unknown[] = [];
    client.get('/api/v1/me').subscribe((value) => received.push(value));

    http.expectOne('/api/v1/me').flush(problem, { status: 401, statusText: 'Unauthorized' });

    const replayed = http.expectOne('/api/v1/me');

    expect(replayed.request.headers.get('Authorization')).toBe('Bearer access-token-2');

    replayed.flush({ displayName: 'Manager One' });

    expect(received).toHaveLength(1);
    expect(store.refreshOnce).toHaveBeenCalledTimes(1);
  });

  it('surfaces the failure instead of looping when the replay is refused too', () => {
    store.refreshOnce.mockImplementation(() => {
      token.set('access-token-2');

      return of(session);
    });

    let failure: unknown;
    client.get('/api/v1/me').subscribe({ error: (error: unknown) => (failure = error) });

    http.expectOne('/api/v1/me').flush(problem, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/v1/me').flush(problem, { status: 401, statusText: 'Unauthorized' });

    expect(failure).toBeInstanceOf(ApiError);
    expect(store.refreshOnce).toHaveBeenCalledTimes(1);
  });

  it('ends the session when the rotation itself fails', () => {
    store.refreshOnce.mockReturnValue(
      throwError(() => new ApiError(401, 'UNAUTHORIZED', 'Sign in again.', null, new Map())),
    );

    let failure: unknown;
    client.get('/api/v1/me').subscribe({ error: (error: unknown) => (failure = error) });

    http.expectOne('/api/v1/me').flush(problem, { status: 401, statusText: 'Unauthorized' });

    expect(store.forget).toHaveBeenCalled();
    expect(failure).toBeInstanceOf(ApiError);
  });

  it('sends the request unauthenticated when no session is held', () => {
    token.set(null);

    client.get('/api/v1/me').subscribe();

    const request = http.expectOne('/api/v1/me');

    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush({});
  });
});
