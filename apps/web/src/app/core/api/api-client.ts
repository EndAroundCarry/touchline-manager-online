import { HttpClient, HttpContext, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { appEnvironment } from '../config/app-environment';
import { ApiHeaders } from './api-headers';

/** Options a caller may pass to shape a command. */
export interface ApiRequestOptions {
  /** Strong entity tag for a conditional write. The server answers 412 when it is stale. */
  readonly etag?: string;

  /** Idempotency key for a command that must not execute twice. */
  readonly idempotencyKey?: string;

  /** Additional per-request context, for example a retry policy. */
  readonly context?: HttpContext;
}

/**
 * The single typed entry point to the API.
 *
 * Feature facades call this instead of `HttpClient` directly, so the base URL, content type, and
 * concurrency headers are applied in exactly one place. Failures arrive as `ApiError` because of
 * {@link problemDetailsInterceptor}.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  /** Issues a GET. */
  get<TResponse>(path: string, options?: ApiRequestOptions): Observable<TResponse> {
    return this.http.get<TResponse>(this.url(path), { headers: this.headers(options) });
  }

  /** Issues a POST. */
  post<TResponse, TBody = unknown>(
    path: string,
    body: TBody,
    options?: ApiRequestOptions,
  ): Observable<TResponse> {
    return this.http.post<TResponse>(this.url(path), body, { headers: this.headers(options) });
  }

  /** Issues a PUT. */
  put<TResponse, TBody = unknown>(
    path: string,
    body: TBody,
    options?: ApiRequestOptions,
  ): Observable<TResponse> {
    return this.http.put<TResponse>(this.url(path), body, { headers: this.headers(options) });
  }

  /** Issues a PATCH. */
  patch<TResponse, TBody = unknown>(
    path: string,
    body: TBody,
    options?: ApiRequestOptions,
  ): Observable<TResponse> {
    return this.http.patch<TResponse>(this.url(path), body, { headers: this.headers(options) });
  }

  /** Issues a DELETE. */
  delete<TResponse>(path: string, options?: ApiRequestOptions): Observable<TResponse> {
    return this.http.delete<TResponse>(this.url(path), { headers: this.headers(options) });
  }

  private url(path: string): string {
    const normalized = path.startsWith('/') ? path : `/${path}`;

    return `${appEnvironment.apiBaseUrl}${normalized}`;
  }

  private headers(options: ApiRequestOptions | undefined): HttpHeaders {
    let headers = new HttpHeaders({ 'Content-Type': 'application/json' });

    if (options?.etag !== undefined) {
      headers = headers.set('If-Match', options.etag);
    }

    if (options?.idempotencyKey !== undefined) {
      headers = headers.set(ApiHeaders.idempotencyKey, options.idempotencyKey);
    }

    return headers;
  }
}
