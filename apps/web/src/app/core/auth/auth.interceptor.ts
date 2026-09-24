import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { IS_AUTH_ENDPOINT, SKIP_AUTH_RETRY } from './auth-context';
import { SessionStore } from './session-store';

/**
 * Attaches the access token and recovers once from an expired one.
 *
 * A 401 on an ordinary request means the access token aged out, so the interceptor rotates the session
 * and replays the request exactly once. The replay is flagged so a second 401 cannot loop, and a
 * refresh failure ends the session rather than retrying forever. Auth endpoints are excluded entirely:
 * a rejected sign-in is an answer, not an expiry.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const store = inject(SessionStore);

  const isAuthEndpoint = request.context.get(IS_AUTH_ENDPOINT);
  const token = store.accessToken();

  const authorized =
    isAuthEndpoint || token === null
      ? request
      : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  return next(authorized).pipe(
    catchError((error: unknown) => {
      const isExpiredSession = error instanceof ApiError && error.status === 401;

      if (!isExpiredSession || isAuthEndpoint || request.context.get(SKIP_AUTH_RETRY)) {
        return throwError(() => error);
      }

      return store.refreshOnce().pipe(
        switchMap(() => {
          const refreshedToken = store.accessToken();

          const retried =
            refreshedToken === null
              ? request
              : request.clone({
                  setHeaders: { Authorization: `Bearer ${refreshedToken}` },
                  context: request.context.set(SKIP_AUTH_RETRY, true),
                });

          return next(retried);
        }),
        catchError(() => {
          store.forget();

          return throwError(() => error);
        }),
      );
    }),
  );
};
