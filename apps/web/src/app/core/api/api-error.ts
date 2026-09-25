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
    readonly extensions: ReadonlyMap<string, unknown> = new Map(),
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

  /**
   * Reads one machine-readable Problem Details extension.
   *
   * Some refusals carry a payload the screen acts on rather than only displays: the out-of-capacity answer
   * includes the next tier's generation state and a polling hint (`PYR-10`), and the cooldown refusal
   * includes when it lapses. The extension's own shape is the endpoint's contract, so it is narrowed here
   * rather than in the component.
   */
  extension<T>(key: string): T | null {
    const value = this.extensions.get(key);

    return value === undefined || value === null ? null : (value as T);
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
      readExtensions(problem),
    );
  }
}

/** The members RFC 9457 reserves, which are carried by the typed properties instead. */
const RESERVED_PROBLEM_MEMBERS = new Set([
  'type',
  'title',
  'status',
  'detail',
  'instance',
  'code',
  'errors',
  'correlationId',
  'traceId',
]);

function readExtensions(problem: Record<string, unknown>): ReadonlyMap<string, unknown> {
  const extensions = new Map<string, unknown>();

  for (const [key, value] of Object.entries(problem)) {
    if (!RESERVED_PROBLEM_MEMBERS.has(key) && value !== null && value !== undefined) {
      extensions.set(key, value);
    }
  }

  return extensions;
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
