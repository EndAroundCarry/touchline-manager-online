namespace TouchlineManager.Api.Configuration;

/// <summary>
/// Exact CORS allowlist (ADR-0002). Wildcard origins are never permitted.
/// </summary>
public sealed class CorsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Cors";

    /// <summary>Gets or sets the exact origins permitted to send credentialed requests.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Switches for walking-skeleton and troubleshooting surfaces.
/// </summary>
/// <remarks>
/// Anything gated here is inaccessible in production by default, so an unfinished route cannot
/// leak into the live game (master plan §17.12).
/// </remarks>
public sealed class DiagnosticsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// Gets or sets whether the Stage 1 no-op job probe is reachable. It exists to prove the
    /// API → database → worker pipeline and is removed once real deadline jobs exist.
    /// </summary>
    public bool EnableJobProbe { get; set; }
}

/// <summary>
/// Deployment-shape settings the API needs to behave correctly behind a proxy.
/// </summary>
public sealed class HostingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Hosting";

    /// <summary>
    /// Gets or sets the number of proxies in front of the API, so that client IP and scheme are
    /// read from forwarded headers correctly. Zero disables forwarded-header processing.
    /// </summary>
    public int KnownProxies { get; set; }
}

/// <summary>
/// Throttling for the endpoints an attacker would hammer (ADR-0002, master plan §12.1).
/// </summary>
/// <remarks>
/// The limits are configurable so that load and functional tests can raise them instead of
/// discovering a throttle as a flaky failure — a test asserting that a manager can sign in should not
/// be measuring the rate limiter.
/// </remarks>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Gets or sets the requests allowed per client address per window on auth endpoints.</summary>
    public int AuthPermitLimit { get; set; } = 20;

    /// <summary>Gets or sets the length of that window in seconds.</summary>
    public int AuthWindowSeconds { get; set; } = 60;
}
