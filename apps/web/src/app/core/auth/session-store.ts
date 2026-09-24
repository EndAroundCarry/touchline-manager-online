import { Injectable, computed, inject, signal } from '@angular/core';
import {
  EMPTY,
  Observable,
  catchError,
  defer,
  finalize,
  firstValueFrom,
  map,
  shareReplay,
  tap,
  throwError,
} from 'rxjs';
import { AuthApi } from './auth-api';
import {
  AuthSession,
  LoginPayload,
  ProfileUpdated,
  RequestAccepted,
  UserProfile,
} from './auth.models';

/** What the client currently knows about the session. */
export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous';

/**
 * The single source of truth for the signed-in session.
 *
 * <p>
 * The access token lives here and nowhere else: not in `localStorage`, not in `sessionStorage`, not on
 * a signal the template can read. The interceptor is the only consumer, which is what keeps the token
 * out of reach of any script that manages to run on the page (ADR-0002).
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly authApi = inject(AuthApi);

  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly userSignal = signal<UserProfile | null>(null);
  private readonly statusSignal = signal<SessionStatus>('unknown');
  private readonly profileEtagSignal = signal<string | null>(null);
  private refreshInFlight: Observable<AuthSession> | null = null;

  /** The signed-in account, or null. */
  readonly user = this.userSignal.asReadonly();

  /** Whether the client knows yet, is signed in, or is signed out. */
  readonly status = this.statusSignal.asReadonly();

  /** Whether the client holds a session. */
  readonly isAuthenticated = computed(() => this.statusSignal() === 'authenticated');

  /** Whether the account's email address is confirmed. */
  readonly isVerified = computed(() => this.userSignal()?.emailVerified === true);

  /** The account's display name, or null. */
  readonly displayName = computed(() => this.userSignal()?.displayName ?? null);

  /**
   * The current access token, for the interceptor only.
   *
   * A method rather than a signal property so it cannot be read from a template by accident.
   */
  accessToken(): string | null {
    return this.accessTokenSignal();
  }

  /** Signs in and adopts the returned session. */
  login(payload: LoginPayload): Observable<void> {
    return this.authApi.login(payload).pipe(
      tap((session) => this.adopt(session)),
      map(() => undefined),
    );
  }

  /**
   * Restores a session on application start.
   *
   * Never throws: a failure here means "not signed in", and an unreachable API must not stop the
   * application from rendering its public screens.
   */
  async restore(): Promise<void> {
    try {
      this.adopt(await firstValueFrom(this.refreshOnce()));
    } catch {
      this.forget();
    }
  }

  /**
   * Rotates the session, sharing one in-flight request between concurrent callers.
   *
   * Without the share, a screen that fires three requests on load would rotate the refresh token three
   * times; the second rotation would present an already-consumed token and the server would — correctly
   * — revoke the whole family as a suspected theft.
   */
  refreshOnce(): Observable<AuthSession> {
    if (this.refreshInFlight !== null) {
      return this.refreshInFlight;
    }

    const request$ = this.authApi.refresh().pipe(tap((session) => this.adopt(session)));

    this.refreshInFlight = request$.pipe(
      finalize(() => {
        this.refreshInFlight = null;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.refreshInFlight;
  }

  /**
   * Signs out of this session.
   *
   * The local session is cleared even if the server call fails: a manager who pressed sign out must end
   * up signed out on this device either way, and a token that outlived the attempt is worse than a
   * server-side session that will expire on its own.
   */
  logout(): Observable<void> {
    return this.authApi.logout().pipe(
      catchError(() => EMPTY),
      finalize(() => this.forget()),
    );
  }

  /** Signs out of every session. */
  logoutAll(): Observable<void> {
    return this.authApi.logoutAll().pipe(
      catchError(() => EMPTY),
      finalize(() => this.forget()),
    );
  }

  /** Reads the profile and records the entity tag for the next conditional write. */
  loadProfile(): Observable<UserProfile> {
    return this.authApi.profile().pipe(
      tap((snapshot) => {
        this.userSignal.set(snapshot.profile);
        this.profileEtagSignal.set(snapshot.etag);
      }),
      map((snapshot) => snapshot.profile),
    );
  }

  /**
   * Changes the display name, using the remembered entity tag as the precondition.
   *
   * The version check happens inside `defer` so that calling this before the profile has been loaded
   * produces a failed observable the caller can handle, rather than throwing past their error handling.
   */
  updateDisplayName(displayName: string): Observable<ProfileUpdated> {
    return defer(() => {
      const etag = this.profileEtagSignal();

      if (etag === null) {
        return throwError(
          () => new Error('The profile has not been loaded, so there is no version to send.'),
        );
      }

      return this.authApi.updateDisplayName(displayName, etag).pipe(
        tap((updated) => {
          this.userSignal.set(updated.user);
          this.profileEtagSignal.set(quoteVersion(updated.version));
        }),
      );
    });
  }

  /** Closes the account and clears the local session. */
  deleteAccount(password: string): Observable<RequestAccepted> {
    return this.authApi.deleteAccount(password).pipe(tap(() => this.forget()));
  }

  /** Drops the local session. Called by the interceptor when a refresh fails. */
  forget(): void {
    this.accessTokenSignal.set(null);
    this.userSignal.set(null);
    this.profileEtagSignal.set(null);
    this.statusSignal.set('anonymous');
  }

  private adopt(session: AuthSession): void {
    this.accessTokenSignal.set(session.accessToken);
    this.userSignal.set(session.user);
    this.statusSignal.set('authenticated');
  }
}

/** Formats a version as the strong entity tag the server uses. */
export function quoteVersion(version: number): string {
  return `"${version}"`;
}
