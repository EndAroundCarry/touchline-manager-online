import { HttpErrorResponse } from '@angular/common/http';

/**
 * A failure returned by the API, shaped for display.
 *
 * The server answers with RFC 9457 Problem Details plus a stable machine-readable `code` and a
 * correlation ID. The UI branches on `code`, never on the human-readable text, so wording can change
 * without breaking behaviour (master plan §10).
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly detail: string,
    readonly correlationId: string | null,
    readonly fieldErrors: ReadonlyMap<string, string[]>,
  ) {
    super(detail);
    this.name = 'ApiError';
  }

  /** True when the resource changed underneath the client and the manager must reapply. */
  get isPreconditionFailed(): boolean {
    return this.status === 412;
  }

  /** True when the request never reached the server, so retrying is safe. */
  get isOffline(): boolean {
    return this.status === 0;
  }

  static fromResponse(response: HttpErrorResponse): ApiError {
    const correlationId =
      response.headers?.get('X-Correlation-Id') ?? readExtension(response, 'correlationId');

    if (typeof response.error !== 'object' || response.error === null) {
      return new ApiError(
        response.status,
        'UNEXPECTED_RESPONSE',
        'The server returned an unexpected response.',
        correlationId,
        new Map(),
      );
    }

    const problem = response.error as Record<string, unknown>;

    return new ApiError(
      response.status,
      typeof problem['code'] === 'string' ? problem['code'] : `HTTP_${response.status}`,
      typeof problem['detail'] === 'string'
        ? problem['detail']
        : typeof problem['title'] === 'string'
          ? problem['title']
          : 'The request could not be completed.',
      correlationId,
      readFieldErrors(problem),
    );
  }
}

function readExtension(response: HttpErrorResponse, key: string): string | null {
  if (typeof response.error !== 'object' || response.error === null) {
    return null;
  }

  const value = (response.error as Record<string, unknown>)[key];

  return typeof value === 'string' ? value : null;
}

function readFieldErrors(problem: Record<string, unknown>): ReadonlyMap<string, string[]> {
  const errors = problem['errors'];

  if (typeof errors !== 'object' || errors === null) {
    return new Map();
  }

  const map = new Map<string, string[]>();

  for (const [field, messages] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(messages)) {
      map.set(
        field,
        messages.filter((message): message is string => typeof message === 'string'),
      );
    }
  }

  return map;
}
