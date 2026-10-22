using Tactical11Manager.Api.Http;
using Tactical11Manager.Api.Squad;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Auth;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;

namespace Tactical11Manager.Api.Finance;

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

        group.MapGet("/finances/sponsor", GetSponsorAsync)
            .WithName("GetSponsor")
            .WithSummary("Reads the club's sponsor offers, the deal it signed, the realistic objective and the deadline.")
            .Produces<SponsorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/finances/sponsor", SignSponsorAsync)
            .WithName("SignSponsor")
            .WithSummary("Signs the season's sponsor deal: an offer and an objective. Requires If-Match, the deal's version (0 before a deal).")
            .Produces<SponsorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetSponsorAsync(
        HttpContext httpContext,
        GetSponsorOffers query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        if (result.Outcome != SquadReadOutcome.Found)
        {
            return SquadEndpoints.Refusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Sponsor!.Version);

        return Results.Ok(result.Sponsor);
    }

    private static async Task<IResult> SignSponsorAsync(
        HttpContext httpContext,
        SignSponsorRequest request,
        SignSponsorDeal useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var result = await useCase.ExecuteAsync(userId, request, expectedVersion, cancellationToken);

        if (result.Outcome != SignSponsorDealOutcome.Agreed)
        {
            return Refusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Sponsor!.Version);

        return Results.Ok(result.Sponsor);
    }

    /// <summary>Turns a refused signing into a status and a stable code.</summary>
    private static IResult Refusal(SignSponsorDealOutcome outcome) => outcome switch
    {
        SignSponsorDealOutcome.PreconditionRequired => ProblemResults.Code(
            StatusCodes.Status428PreconditionRequired,
            ApiErrorCodes.PreconditionRequired,
            "A version is required.",
            "Send the deal's current entity tag in If-Match (0 when the club has not signed) so a retry does not sign twice."),

        SignSponsorDealOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "That changed.",
            "The deal changed since you read it. Reload the offers and choose again."),

        SignSponsorDealOutcome.UnknownOffer => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            SponsorErrorCodes.UnknownOffer,
            "No such offer.",
            "Choose steady, balanced or performance."),

        SignSponsorDealOutcome.UnknownObjective => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            SponsorErrorCodes.UnknownObjective,
            "No such objective.",
            "Choose win_league, top_three, top_six, top_half, eleventh or stay_up."),

        SignSponsorDealOutcome.AlreadySigned => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            SponsorErrorCodes.AlreadySigned,
            "Already signed.",
            "The club has signed its sponsor for the season. A new manager may change the deal in the first matchdays after taking the club over (SPN-6)."),

        SignSponsorDealOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no sponsor to sign."),

        SignSponsorDealOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SignSponsorDealOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            SponsorErrorCodes.NoOffers,
            "No sponsor offers.",
            "The club has been made no offers for this season."),
    };

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
