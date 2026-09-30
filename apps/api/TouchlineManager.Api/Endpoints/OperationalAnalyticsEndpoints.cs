using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Ops;

namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// The read-only operational funnel surface for operators (master plan §16 Stage 13, `F-54`, ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// This is the product-analytics deliverable: the two funnels — onboarding and retention — as counts. It is
/// deliberately separate from <see cref="OpsEndpoints"/>, whose members are non-production diagnostics
/// probes; this one is a real operator read that Stage 14's console will call.
/// </para>
/// <para>
/// Every figure is derived from rows the game already writes, so the surface adds no personal data, no
/// table, and no client collection. Access is limited to the <c>operator</c> and <c>admin</c> roles
/// (<c>LGL-5</c>, `MAT-11`).
/// </para>
/// </remarks>
internal static class OperationalAnalyticsEndpoints
{
    /// <summary>Maps the operational funnels read onto the ops module group.</summary>
    public static RouteGroupBuilder MapOperationalAnalytics(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapGet("/analytics/funnels", GetOperationalFunnelsAsync)
            .WithName("GetOperationalFunnels")
            .WithSummary("Reads the onboarding and retention funnels as counts (F-54, ADR-0041).")
            .WithDescription(
                "Operator or admin only. Every figure is a count over rows the game already holds; the "
                + "response carries no per-manager row, no identifier beyond the world and season, and no "
                + "hidden value (LGL-5, MAT-11).")
            .RequireAuthorization(AuthorizationPolicies.OperationalAnalyticsRead)
            .Produces<OperationalAnalyticsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetOperationalFunnelsAsync(
        HttpContext httpContext,
        IOperationalAnalyticsQueries queries,
        CancellationToken cancellationToken)
    {
        var snapshot = await queries.GetAsync(cancellationToken);

        if (snapshot is null)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "No world was found.",
                "A world has not been seeded, so there are no funnels to read.");
        }

        // Live operator data, so it is never cached, the way the account export is not (ADR-0036).
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new OperationalAnalyticsResponse(
                snapshot.WorldId,
                snapshot.SeasonNumber,
                snapshot.GeneratedAt,
                new OnboardingFunnelResponse(
                    snapshot.Onboarding.Registered,
                    snapshot.Onboarding.Verified,
                    snapshot.Onboarding.ProfilesCreated,
                    snapshot.Onboarding.ClubsClaimed),
                new RetentionFunnelResponse(
                    snapshot.Retention.ActiveTenures,
                    snapshot.Retention.InactiveTenures,
                    snapshot.Retention.ClosedTenures,
                    snapshot.Retention.ActiveAccounts,
                    snapshot.Retention.AccountsActiveInLastSevenDays)),
            statusCode: StatusCodes.Status200OK);
    }
}
