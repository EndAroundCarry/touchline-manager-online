/**
 * Custom HTTP headers that are part of the API contract.
 *
 * Mirrors `TouchlineManager.Contracts.Http.ApiHeaders` on the server. Later stages generate the
 * DTO types from the committed OpenAPI document; these names stay handwritten until then because
 * they are transport concerns rather than payload types.
 */
export const ApiHeaders = {
  /** Carries a correlation ID through one logical operation. */
  correlationId: 'X-Correlation-Id',

  /** Carries the aggregate version for optimistic concurrency. */
  entityTag: 'ETag',

  /** Carries a client-supplied idempotency key for commands that must not execute twice. */
  idempotencyKey: 'Idempotency-Key',
} as const;
