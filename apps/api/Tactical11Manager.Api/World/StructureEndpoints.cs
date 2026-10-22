using Tactical11Manager.Api.Auth;
using Tactical11Manager.Api.Http;
using Tactical11Manager.Api.Squad;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Auth;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;

namespace Tactical11Manager.Api.World;

/// <summary>
/// The snack bar and merchandise shop HTTP surface (`STRC-1`…`STRC-9`).
/// </summary>
/// <remarks>
/// <para>
/// Like the stadium, these sit at the version root: the buildings are a page of the club the caller manages,
/// addressed as <c>/structures</c>. Which club that is comes from the caller's tenure, so nothing here trusts
/// a club identity from the request.
/// </para>
/// <para>
/// Each building has its own <em>version</em>, the strong entity tag of an upgrade order for that building. A
/// read returns both in the body, since one header cannot carry two. An upgrade must send the building's
/// version back in <c>If-Match</c>, so a double-click, a retry, or a second device cannot pay for the same
/// decision twice (`CONC-1`, ADR-0009).
/// </para>
/// </remarks>
internal static class StructureEndpoints
{
    /// <summary>Maps the structure routes.</summary>
    public static IEndpointRouteBuilder MapStructureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("world")
            .RequireAuthorization();

        group.MapGet("/structures", GetStructuresAsync)
            .WithName("GetStructures")
            .WithSummary("Reads the snack bar and the merchandise shop of the club the manager holds: levels, takings, upkeep and upgrade costs.")
            .Produces<StructuresResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/structures/{kind}/upgrade", UpgradeAsync)
            .WithName("UpgradeStructure")
            .WithSummary("Orders the next level of a building, pays for it, and starts the work. Requires If-Match.")
            .Produces<StructuresResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetStructuresAsync(
        HttpContext httpContext,
        GetStructures query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return result.Outcome == Application.Squad.SquadReadOutcome.Found
            ? Results.Ok(result.Structures)
            : SquadEndpoints.Refusal(result.Outcome);
    }

    private static async Task<IResult> UpgradeAsync(
        HttpContext httpContext,
        string kind,
        UpgradeStructure useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var result = await useCase.ExecuteAsync(userId, kind, expectedVersion, cancellationToken);

        if (result.Outcome != UpgradeStructureOutcome.Started)
        {
            return Refusal(result.Outcome);
        }

        var structures = result.Structures!;
        var upgraded = kind == structures.SnackBar.Kind ? structures.SnackBar : structures.Merchandise;

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(upgraded.Version);

        return Results.Ok(structures);
    }

    /// <summary>Turns an upgrade refusal into a status and a stable code.</summary>
    private static IResult Refusal(UpgradeStructureOutcome outcome) => outcome switch
    {
        UpgradeStructureOutcome.PreconditionRequired => ProblemResults.Code(
            StatusCodes.Status428PreconditionRequired,
            ApiErrorCodes.PreconditionRequired,
            "A version is required.",
            "Send the building's current entity tag in If-Match so a concurrent change is not paid for twice."),

        UpgradeStructureOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "That changed.",
            "The building changed since you read it. Reload it and order again."),

        UpgradeStructureOutcome.UnknownKind => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            StructureErrorCodes.UnknownKind,
            "No such building.",
            "Choose snack_bar or merchandise."),

        UpgradeStructureOutcome.MaxLevel => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            StructureErrorCodes.MaxLevel,
            "Already at the top level.",
            "The building cannot be upgraded further (STRC-1)."),

        UpgradeStructureOutcome.Building => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            StructureErrorCodes.Building,
            "Already being upgraded.",
            "One upgrade runs at a time. Wait for the work to finish (STRC-3)."),

        UpgradeStructureOutcome.Embargo => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            StructureErrorCodes.BuildEmbargo,
            "Building embargo.",
            "A club in the red or in administration may not build until its cash is back above zero (DEBT-3)."),

        UpgradeStructureOutcome.InsufficientFunds => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            StructureErrorCodes.InsufficientFunds,
            "Insufficient funds.",
            "The club does not hold that much cash after its existing reservations (FIN-10)."),

        UpgradeStructureOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is nothing to upgrade."),

        UpgradeStructureOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        UpgradeStructureOutcome.WorldNotSeeded => ProblemResults.Code(
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
