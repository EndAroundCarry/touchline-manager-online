using TouchlineManager.Api.Http;
using TouchlineManager.Api.Squad;
using TouchlineManager.Application.Calendar;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Calendar;

namespace TouchlineManager.Api.Calendar;

/// <summary>
/// The club calendar's read surface: what is happening on each day.
/// </summary>
/// <remarks>
/// At the version root like the squad and finance reads. Which club is read comes from the caller's tenure,
/// so nothing here trusts a club identity from the request, and the authorization refusals are the squad
/// reads' own because they refuse for the same reasons. The endpoint knows no kind of event: every event
/// comes from a source registered with the use case.
/// </remarks>
internal static class CalendarEndpoints
{
    /// <summary>Maps the calendar read route.</summary>
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("calendar")
            .RequireAuthorization()
            .MapGet("/calendar", GetCalendarAsync)
            .WithName("GetCalendar")
            .WithSummary("Reads the events in a window of the club the manager holds: matches, finances, injuries and more.")
            .Produces<CalendarResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetCalendarAsync(
        HttpContext httpContext,
        DateTimeOffset? from,
        DateTimeOffset? to,
        GetCalendar query,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, from, to, cancellationToken);

        if (result.InvalidRange)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                CalendarErrorCodes.InvalidRange,
                "Invalid range.",
                "Give both a start and an end, the end after the start and no more than 70 days later.");
        }

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Calendar)
            : SquadEndpoints.Refusal(result.Outcome);
    }
}
