using FluentValidation;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Api.Squad;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.World;

/// <summary>
/// The stadium HTTP surface (`STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// <para>
/// Like the squad and finance endpoints, these sit at the version root: the stadium is a page of the club the
/// caller manages, addressed as <c>/stadium</c>. Which club that is comes from the caller's tenure, so nothing
/// here trusts a club identity from the request.
/// </para>
/// <para>
/// The ground's <em>version</em> is the strong entity tag. A read returns it, and a build order must send it
/// back in <c>If-Match</c>, so a double-click, a retry, or a second device cannot pay for the same decision
/// twice (`CONC-1`, ADR-0009).
/// </para>
/// </remarks>
internal static class StadiumEndpoints
{
    /// <summary>Maps the stadium routes.</summary>
    public static IEndpointRouteBuilder MapStadiumEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("world")
            .RequireAuthorization();

        group.MapGet("/stadium", GetStadiumAsync)
            .WithName("GetStadium")
            .WithSummary("Reads the stadium of the club the manager holds: its places, prices and build costs.")
            .Produces<StadiumResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/stadium/seats", BuildSeatsAsync)
            .WithName("BuildStadiumSeats")
            .WithSummary("Adds places of one kind to the stadium and charges the club. Requires If-Match.")
            .Produces<StadiumResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetStadiumAsync(
        HttpContext httpContext,
        GetStadium query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        if (result.Outcome != Application.Squad.SquadReadOutcome.Found)
        {
            return SquadEndpoints.Refusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Stadium!.Version);

        return Results.Ok(result.Stadium);
    }

    private static async Task<IResult> BuildSeatsAsync(
        HttpContext httpContext,
        BuildSeatsRequest request,
        IValidator<BuildSeatsRequest> validator,
        BuildStadiumSeats useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body: an order that cannot be conditional is refused for that, which
        // is more useful than validating an order that will not be applied.
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, expectedVersion, request, cancellationToken);

        if (result.Outcome == BuildStadiumSeatsOutcome.Built)
        {
            httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Stadium!.Version);

            return Results.Ok(result.Stadium);
        }

        return Refusal(result.Outcome);
    }

    /// <summary>Turns a build refusal into a status and a stable code.</summary>
    private static IResult Refusal(BuildStadiumSeatsOutcome outcome) => outcome switch
    {
        BuildStadiumSeatsOutcome.PreconditionRequired => ProblemResults.Code(
            StatusCodes.Status428PreconditionRequired,
            ApiErrorCodes.PreconditionRequired,
            "A version is required.",
            "Send the current entity tag in If-Match so a concurrent change is not paid for twice."),

        BuildStadiumSeatsOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "That changed.",
            "The stadium changed since you read it. Reload it and order again."),

        BuildStadiumSeatsOutcome.InvalidOrder => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            "Invalid order.",
            "Choose a kind of place and a number of places to add."),

        BuildStadiumSeatsOutcome.InsufficientFunds => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            StadiumErrorCodes.InsufficientFunds,
            "Insufficient funds.",
            "The club does not hold that much cash after its existing reservations (FIN-10)."),

        BuildStadiumSeatsOutcome.StadiumFull => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            StadiumErrorCodes.StadiumFull,
            "The stadium is full.",
            "The ground cannot hold that many more places (STAD-1)."),

        BuildStadiumSeatsOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no stadium to build."),

        BuildStadiumSeatsOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        BuildStadiumSeatsOutcome.WorldNotSeeded => ProblemResults.Code(
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
