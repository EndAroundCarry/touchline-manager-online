using Tactical11Manager.Api.Http;
using Tactical11Manager.Api.Squad;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Auth;
using Tactical11Manager.Contracts.Fans;

namespace Tactical11Manager.Api.Fans;

/// <summary>
/// The fan base's read surface: the manager's Fan Base page and what anyone can see of a club's fans.
/// </summary>
/// <remarks>
/// At the version root like the squad and finance reads. Which club the page shows comes from the caller's tenure, so nothing here
/// trusts a club identity from the request, and the authorization refusals are the squad reads' own because they refuse for the same
/// reasons. The public summary takes a club identity because a rival's size is public game data, like its page.
/// </remarks>
internal static class FanEndpoints
{
    /// <summary>Maps the fan read routes.</summary>
    public static IEndpointRouteBuilder MapFanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("fans")
            .RequireAuthorization();

        group.MapGet("/fans", GetFanBaseAsync)
            .WithName("GetFanBase")
            .WithSummary("Reads the fans of the club the manager holds: count, mood, what they expect next, and the history behind the charts.")
            .Produces<FanBaseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/clubs/{clubId:guid}/fans", GetClubFanSummaryAsync)
            .WithName("GetClubFanSummary")
            .WithSummary("Reads how many fans any club has and how they feel, in a word.")
            .Produces<FanSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetFanBaseAsync(
        HttpContext httpContext,
        string? history,
        GetFanBase query,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, history, cancellationToken);

        if (result.InvalidHistory)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                FanErrorCodes.InvalidHistory,
                "Invalid history.",
                "Ask for the history 'tenure' (since you took the club over) or 'club' (the whole club).");
        }

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.FanBase)
            : SquadEndpoints.Refusal(result.Outcome);
    }

    private static async Task<IResult> GetClubFanSummaryAsync(
        Guid clubId,
        GetClubFanSummary query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(clubId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Summary)
            : SquadEndpoints.Refusal(result.Outcome);
    }
}
