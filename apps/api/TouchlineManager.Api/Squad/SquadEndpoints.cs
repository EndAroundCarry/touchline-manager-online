using TouchlineManager.Api.Http;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.Squad;

/// <summary>
/// The squad module's read surface (master plan §10.3).
/// </summary>
/// <remarks>
/// <para>
/// Like the world endpoints, these sit at the version root rather than under <c>/api/v1/squad</c>, because
/// §10.3 addresses a squad as <c>/clubs/{clubId}/squad</c>, a player as <c>/players/{playerId}</c>, and a
/// club's contracts as <c>/contracts</c>. Those paths are the contract; moving them to suit an internal
/// grouping would break every client that already reads one.
/// </para>
/// <para>
/// Handlers do three things: read the account, call one use case, and translate the outcome into a status
/// and a stable code. Which club the caller may read comes from their tenure, so nothing here trusts a
/// club identity from the request beyond using it to look one up.
/// </para>
/// </remarks>
internal static class SquadEndpoints
{
    /// <summary>Maps the squad read routes.</summary>
    public static IEndpointRouteBuilder MapSquadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("squad")
            .RequireAuthorization();

        group.MapGet("/clubs/{clubId:guid}/squad", GetSquadAsync)
            .WithName("GetSquad")
            .WithSummary("Reads the squad of the club the manager holds.")
            .Produces<SquadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/players/{playerId:guid}", GetPlayerAsync)
            .WithName("GetPlayer")
            .WithSummary("Reads one player's profile, including the displayed attribute grid.")
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/contracts", ListContractsAsync)
            .WithName("ListContracts")
            .WithSummary("Lists the contracts of the club the manager holds.")
            .Produces<ContractsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetSquadAsync(
        HttpContext httpContext,
        Guid clubId,
        GetSquad query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, clubId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Squad)
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> GetPlayerAsync(
        HttpContext httpContext,
        Guid playerId,
        GetPlayer query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, playerId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Player)
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> ListContractsAsync(
        HttpContext httpContext,
        ListContracts query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Contracts)
            : Refusal(result.Outcome);
    }

    /// <summary>
    /// Turns a refusal into a status and a stable code.
    /// </summary>
    /// <remarks>
    /// The three authorization refusals are deliberately distinct. A client that receives
    /// <c>CLUB_NOT_MANAGED</c> knows its view is stale and can refresh; one that receives <c>NO_CLUB</c>
    /// knows the manager has nothing to look at and can send them to onboarding. Collapsing them into one
    /// forbidden response would leave the client guessing. Shared with the tactics reads, which refuse for
    /// the same reasons.
    /// </remarks>
    internal static IResult Refusal(SquadReadOutcome outcome) => outcome switch
    {
        SquadReadOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no squad to read."),

        SquadReadOutcome.ClubNotManaged => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.ClubNotManaged,
            "Not your club.",
            "You do not manage that club."),

        SquadReadOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SquadReadOutcome.PlayerNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            SquadErrorCodes.PlayerNotFound,
            "No such player.",
            "That player does not exist in this world."),

        SquadReadOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.ClubNotFound,
            "No such club.",
            "That club does not exist in this world."),
    };

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
