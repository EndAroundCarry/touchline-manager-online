using FluentValidation;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
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

        group.MapPost("/contracts/{contractId:guid}/renewal-quote", RequestRenewalQuoteAsync)
            .WithName("RequestRenewalQuote")
            .WithSummary("Returns the deterministic renewal quote for a player and a term (CON-3).")
            .Produces<RenewalQuoteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/contracts/{contractId:guid}/renew", RenewContractAsync)
            .WithName("RenewContract")
            .WithSummary("Accepts a renewal. Requires If-Match with the contract's current version (CON-4).")
            .Produces<ContractRenewalResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

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

    private static async Task<IResult> RequestRenewalQuoteAsync(
        HttpContext httpContext,
        Guid contractId,
        RenewalQuoteRequest request,
        IValidator<RenewalQuoteRequest> validator,
        RequestRenewalQuote useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, contractId, request.Seasons, cancellationToken);

        return result.Outcome == ContractRenewalOutcome.Quoted
            ? Results.Ok(result.Quote)
            : RenewalRefusal(result.Outcome);
    }

    private static async Task<IResult> RenewContractAsync(
        HttpContext httpContext,
        Guid contractId,
        RenewContractRequest request,
        IValidator<RenewContractRequest> validator,
        RenewContract useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body: a renewal without If-Match cannot be conditional, and saying so
        // is more useful than validating a request that will not be applied (CONC-1).
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        if (expectedVersion is null)
        {
            return RenewalPreconditionRequired();
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(
            userId,
            contractId,
            request.Seasons,
            expectedVersion,
            cancellationToken);

        if (result.Outcome != ContractRenewalOutcome.Renewed)
        {
            return RenewalRefusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Contract!.Version);

        return Results.Ok(result.Contract);
    }

    /// <summary>Turns a renewal refusal into a status and a stable code.</summary>
    private static IResult RenewalRefusal(ContractRenewalOutcome outcome) => outcome switch
    {
        ContractRenewalOutcome.ContractNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            SquadErrorCodes.ContractNotFound,
            "No such contract.",
            "That contract does not exist for your club."),

        ContractRenewalOutcome.InvalidTerm => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            SquadErrorCodes.InvalidContractTerm,
            "That contract length is not allowed.",
            "Choose a contract of one to three game seasons (CON-1)."),

        ContractRenewalOutcome.PreconditionRequired => RenewalPreconditionRequired(),

        ContractRenewalOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "The contract changed.",
            "Reload the contract and reapply the renewal."),

        ContractRenewalOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no contract to renew."),

        ContractRenewalOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        ContractRenewalOutcome.WorldNotSeeded => ProblemResults.Code(
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

    private static IResult RenewalPreconditionRequired() => ProblemResults.Code(
        StatusCodes.Status428PreconditionRequired,
        ApiErrorCodes.PreconditionRequired,
        "A version is required.",
        "Send the contract's current entity tag in If-Match so a concurrent change is not overwritten.");

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
