using System.Diagnostics;
using System.Text.RegularExpressions;
using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.Middleware;

/// <summary>
/// Establishes a correlation ID for every request and echoes it back.
/// </summary>
/// <remarks>
/// The ID threads one logical operation through API logs, job records, database audit rows, and
/// email dispatch, which is what makes an incident reconstructable after the fact
/// (data-classification §4).
/// </remarks>
internal sealed partial class CorrelationIdMiddleware
{
    /// <summary>The request and response header carrying the correlation ID.</summary>
    public const string HeaderName = ApiHeaders.CorrelationId;

    /// <summary>The <see cref="HttpContext.Items"/> key holding the resolved correlation ID.</summary>
    public const string ItemKey = "correlationId";

    /// <summary>Maximum accepted length of a client-supplied correlation ID.</summary>
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>Initializes the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Resolves the correlation ID, exposes it to logs and error responses, then continues.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            var candidate = supplied.ToString();

            // A client-supplied value is untrusted input that lands in logs, so it is accepted
            // only when it cannot be used to forge or split log entries.
            if (candidate.Length is > 0 and <= MaxLength && SafeCorrelationId().IsMatch(candidate))
            {
                return candidate;
            }
        }

        return Guid.CreateVersion7().ToString();
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+$")]
    private static partial Regex SafeCorrelationId();
}
