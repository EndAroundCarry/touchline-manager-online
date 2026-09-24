namespace TouchlineManager.Contracts.Http;

/// <summary>
/// Stable machine-readable codes for failures that are not specific to one module.
/// </summary>
/// <remarks>
/// RFC 9457 Problem Details carries the HTTP status; this code carries the reason. Clients branch on
/// the code, never on the human-readable text (master plan §10).
/// </remarks>
public static class ApiErrorCodes
{
    /// <summary>The request body or query failed field-level validation. Details are in <c>errors</c>.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>No usable credentials were presented, or the session is no longer valid.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>The caller is authenticated but not permitted to perform the action.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>The resource does not exist, or is not visible to this caller.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The request must be conditional and no <c>If-Match</c> header was supplied.</summary>
    public const string PreconditionRequired = "PRECONDITION_REQUIRED";

    /// <summary>The supplied <c>If-Match</c> version is stale. Reload and reapply.</summary>
    public const string PreconditionFailed = "PRECONDITION_FAILED";

    /// <summary>Too many requests from this client. Retry after the advertised delay.</summary>
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
}
