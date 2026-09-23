namespace TouchlineManager.Contracts.Http;

/// <summary>
/// Custom HTTP header names that form part of the API contract.
/// </summary>
/// <remarks>
/// These live in Contracts because clients depend on them: the Angular PWA echoes the correlation
/// ID and quotes it in support requests, and the future native client does the same. Changing a name
/// here is a contract change.
/// </remarks>
public static class ApiHeaders
{
    /// <summary>
    /// Carries a correlation ID through one logical operation. Accepted on a request when it is
    /// safe, always returned on the response.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// Carries the aggregate version for optimistic concurrency, and is required in
    /// <c>If-Match</c> on conflicting updates (ADR-0009).
    /// </summary>
    public const string EntityTag = "ETag";

    /// <summary>
    /// Carries a client-supplied idempotency key for commands that must not execute twice
    /// (ADR-0003, CONC-3).
    /// </summary>
    public const string IdempotencyKey = "Idempotency-Key";
}
