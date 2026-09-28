using TouchlineManager.Api.Http;
using TouchlineManager.Api.Squad;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Finance;

namespace TouchlineManager.Api.Finance;

/// <summary>
/// The finance module's read surface (master plan §10.7).
/// </summary>
/// <remarks>
/// Like the squad endpoints, these sit at the version root rather than under <c>/api/v1/finance</c>, because
/// §10.7 addresses them as <c>/finances/summary</c> and <c>/finances/ledger</c>. Which club the caller may
/// read comes from their tenure, so nothing here trusts a club identity from the request. The authorization
/// refusals are the squad reads' own, because they refuse for the same reasons.
/// </remarks>
internal static class FinanceEndpoints
{
    /// <summary>Maps the finance read routes.</summary>
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("finance")
            .RequireAuthorization();

        group.MapGet("/finances/summary", GetSummaryAsync)
            .WithName("GetFinanceSummary")
            .WithSummary("Reads the club the manager holds: its money and its season's totals by category.")
            .Produces<FinanceSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/finances/ledger", GetLedgerAsync)
            .WithName("GetFinanceLedger")
            .WithSummary("Reads one page of the club's ledger, newest first.")
            .Produces<FinanceLedgerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext httpContext,
        GetFinanceSummary query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Summary)
            : SquadEndpoints.Refusal(result.Outcome);
    }

    private static async Task<IResult> GetLedgerAsync(
        HttpContext httpContext,
        string? cursor,
        GetFinanceLedger query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // A cursor that does not decode is a client defect rather than a missing resource, so it is refused
        // before the use case runs and before any club is resolved.
        if (!FinanceLedgerCursor.TryDecode(cursor, out var beforeSequence))
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                FinanceErrorCodes.InvalidCursor,
                "Invalid cursor.",
                "The ledger cursor could not be read.");
        }

        var result = await query.ExecuteAsync(userId, beforeSequence, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Ledger)
            : SquadEndpoints.Refusal(result.Outcome);
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
