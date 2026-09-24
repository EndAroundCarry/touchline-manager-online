namespace TouchlineManager.Api.Middleware;

/// <summary>
/// Adds the response security headers the API is responsible for (ADR-0002, master plan §12.2).
/// </summary>
/// <remarks>
/// <para>
/// The edge re-asserts these for the web client, but the API sets them too: a JSON response fetched
/// directly must not be sniffable, framable, or able to load resources of its own.
/// </para>
/// <para>
/// The content security policy is the strictest possible for a JSON API —
/// <c>default-src 'none'</c> and no framing ancestors — because this origin never serves documents
/// that need to load anything.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Adds the headers, then continues.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

        return _next(context);
    }
}
