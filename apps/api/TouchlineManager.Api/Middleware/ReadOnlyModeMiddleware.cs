using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.Middleware;

/// <summary>
/// Refuses manager commands while the game is in read-only mode (master plan §13, `F-51`).
/// </summary>
/// <remarks>
/// <para>
/// The switch is a world-scoped feature flag an operator sets through the admin surface. While it is on,
/// every mutating manager request is answered <c>503</c> with a stated reason, and reads keep working: that
/// is what "blocks manager writes while keeping published content available" means. The worker is
/// untouched, so deadlines still advance and published content stays live.
/// </para>
/// <para>
/// It runs after authorization, so an unauthorized caller still gets its <c>401</c>/<c>403</c> first. Safe
/// methods always pass. A mutation passes when it is operator surface — the admin and operational-analytics
/// policies — so an operator can still lift the flag, and when it is a non-game path: sign-in under
/// <c>/api/v1/auth</c> (which must keep working so a manager can sign back in to read) and anything outside
/// <c>/api/v1</c> such as the health probes. Everything else is refused.
/// </para>
/// </remarks>
internal sealed class ReadOnlyModeMiddleware
{
    private static readonly HashSet<string> OperatorPolicies = new(StringComparer.Ordinal)
    {
        AuthorizationPolicies.AdminRead,
        AuthorizationPolicies.AdminMutate,
        AuthorizationPolicies.OperationalAnalyticsRead,
    };

    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    public ReadOnlyModeMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Refuses a command when read-only mode is on; otherwise continues.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsSafeMethod(context.Request.Method) || IsExempt(context))
        {
            await _next(context);
            return;
        }

        var readOnly = await context.RequestServices
            .GetRequiredService<IReadOnlyMode>()
            .GetStateAsync(context.RequestAborted);

        if (!readOnly.Enabled)
        {
            await _next(context);
            return;
        }

        await ProblemResults
            .Code(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.ReadOnlyMode,
                "Read-only mode.",
                readOnly.Message
                    ?? "The game is read-only for a moment. You can still read; changes will work again shortly.")
            .ExecuteAsync(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method)
        || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method)
        || HttpMethods.IsTrace(method);

    private static bool IsExempt(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is not null
            && endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Any(data => data.Policy is { } policy && OperatorPolicies.Contains(policy)))
        {
            return true;
        }

        var path = context.Request.Path;

        return !path.StartsWithSegments("/api/v1") || path.StartsWithSegments("/api/v1/auth");
    }
}
