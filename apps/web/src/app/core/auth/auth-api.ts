import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { ApiHeaders } from '../api/api-headers';
import { authEndpointContext } from './auth-context';
import {
  AuthSession,
  LoginPayload,
  ProfileSnapshot,
  ProfileUpdated,
  RegisterPayload,
  RegistrationAccepted,
  RequestAccepted,
  ResetPasswordPayload,
  UserProfile,
} from './auth.models';

/**
 * The auth module's HTTP surface.
 *
 * Stateless commands live here (register, verify, reset) and are called straight from their screens;
 * anything that changes the session lives on {@link SessionStore}. Splitting them that way keeps the
 * store about one thing — what the client currently is — rather than a grab-bag of endpoints.
 */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly api = inject(ApiClient);

  /** Creates an account and asks the server to send a verification email. */
  register(payload: RegisterPayload): Observable<RegistrationAccepted> {
    return this.api.post<RegistrationAccepted, RegisterPayload>('/auth/register', payload, {
      context: authEndpointContext(),
    });
  }

  /** Confirms an email address with a single-use token. */
  verifyEmail(userId: string, token: string): Observable<RequestAccepted> {
    return this.api.post<RequestAccepted, { userId: string; token: string }>(
      '/auth/verify-email',
      { userId, token },
      { context: authEndpointContext() },
    );
  }

  /** Requests a fresh verification link. */
  resendVerification(email: string): Observable<RequestAccepted> {
    return this.api.post<RequestAccepted, { email: string }>(
      '/auth/resend-verification',
      { email },
      { context: authEndpointContext() },
    );
  }

  /** Signs in and starts a session. */
  login(payload: LoginPayload): Observable<AuthSession> {
    return this.api.post<AuthSession, LoginPayload>('/auth/login', payload, {
      context: authEndpointContext(),
    });
  }

  /** Rotates the refresh session. The cookie is sent because this endpoint is credentialed. */
  refresh(): Observable<AuthSession> {
    return this.api.post<AuthSession, Record<string, never>>(
      '/auth/refresh',
      {},
      { withCredentials: true, context: authEndpointContext() },
    );
  }

  /** Revokes the presented refresh session. */
  logout(): Observable<void> {
    return this.api.post<void, Record<string, never>>(
      '/auth/logout',
      {},
      { withCredentials: true, context: authEndpointContext() },
    );
  }

  /**
   * Revokes every session and invalidates every issued access token.
   *
   * Deliberately *not* an auth-endpoint request: it needs the bearer token, and it must go through
   * the normal 401 handling.
   */
  logoutAll(): Observable<void> {
    return this.api.post<void, Record<string, never>>('/auth/logout-all', {});
  }

  /** Starts a password reset. */
  forgotPassword(email: string): Observable<RequestAccepted> {
    return this.api.post<RequestAccepted, { email: string }>(
      '/auth/forgot-password',
      { email },
      { context: authEndpointContext() },
    );
  }

  /** Completes a password reset and signs every session out. */
  resetPassword(payload: ResetPasswordPayload): Observable<RequestAccepted> {
    return this.api.post<RequestAccepted, ResetPasswordPayload>('/auth/reset-password', payload, {
      context: authEndpointContext(),
    });
  }

  /** Reads the profile together with the entity tag for the next conditional write. */
  profile(): Observable<ProfileSnapshot> {
    return this.api.getWithResponse<UserProfile>('/me').pipe(
      map((response) => {
        if (response.body === null) {
          throw new Error('The profile response was empty.');
        }

        return { profile: response.body, etag: response.headers.get(ApiHeaders.entityTag) };
      }),
    );
  }

  /** Changes the display name under an optimistic concurrency check. */
  updateDisplayName(displayName: string, etag: string): Observable<ProfileUpdated> {
    return this.api.patch<ProfileUpdated, { displayName: string }>(
      '/me',
      { displayName },
      { etag },
    );
  }

  /** Closes the account, confirming with the current password. */
  deleteAccount(password: string): Observable<RequestAccepted> {
    return this.api.deleteWithBody<RequestAccepted, { password: string }>('/me', { password });
  }
}
