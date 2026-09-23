import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { ApiError } from './api-error';
import { ApiHeaders } from './api-headers';
import { CorrelationStore } from './correlation-store';

/**
 * Records the server's correlation ID and converts failures into {@link ApiError}.
 *
 * Centralising this means no feature has to parse Problem Details itself, and the support flow
 * ("quote the reference in the footer") always has a value to quote.
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (request, next) => {
  const correlationStore = inject(CorrelationStore);

  return next(request).pipe(
    tap((event) => {
      if (event instanceof HttpResponse) {
        correlationStore.record(event.headers.get(ApiHeaders.correlationId));
      }
    }),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const apiError = ApiError.fromResponse(error);
        correlationStore.record(apiError.correlationId);

        return throwError(() => apiError);
      }

      return throwError(() => error);
    }),
  );
};
