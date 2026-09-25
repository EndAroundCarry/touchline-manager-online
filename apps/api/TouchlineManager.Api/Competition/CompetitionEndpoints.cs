using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Api.Competition;

/// <summary>
/// The competition module's fixture surface (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Like the squad and world reads, these sit at the version root rather than under
/// <c>/api/v1/competition</c>, because §10.5 addresses a division's calendar as
/// <c>/divisions/{divisionId}/fixtures</c>, the manager's own list as <c>/fixtures/mine</c>, and a single
/// fixture as <c>/fixtures/{fixtureId}</c>. Those paths are the contract.
/// </para>
/// <para>
/// Handlers do three things: read the account, call one use case, and translate the outcome into a status
/// and a stable code. Fixtures are public game data, so the reads are not gated on holding a club; what the
/// caller's tenure adds is which side of the fixture is theirs.
/// </para>
/// </remarks>
internal static class CompetitionEndpoints
{
    /// <summary>Maps the fixture read routes.</summary>
    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("competition")
            .RequireAuthorization();

        group.MapGet("/divisions/{divisionId:guid}/fixtures", ListDivisionFixturesAsync)
            .WithName("ListDivisionFixtures")
            .WithSummary("Reads a division's whole fixture calendar for the season in progress.")
            .Produces<DivisionFixturesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/fixtures/mine", GetMyFixturesAsync)
            .WithName("GetMyFixtures")
            .WithSummary("Reads the fixtures of the club the manager holds, with the next one named.")
            .Produces<MyFixturesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/fixtures/{fixtureId:guid}", GetFixtureAsync)
            .WithName("GetFixture")
            .WithSummary("Reads one fixture in full, naming the caller's side when they hold a club.")
            .Produces<FixtureDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListDivisionFixturesAsync(
        Guid divisionId,
        ListDivisionFixtures query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(divisionId, cancellationToken);

        return result.Outcome == CompetitionReadOutcome.Found
            ? Results.Ok(result.Fixtures)
            : CompetitionRefusals.Read(
                result.Outcome,
                SquadErrorCodes.ClubNotManaged,
                "You do not manage that club.");
    }

    private static async Task<IResult> GetMyFixturesAsync(
        HttpContext httpContext,
        GetMyFixtures query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return result.Outcome == CompetitionReadOutcome.Found
            ? Results.Ok(result.Fixtures)
            : CompetitionRefusals.Read(
                result.Outcome,
                SquadErrorCodes.ClubNotManaged,
                "You do not manage that club.");
    }

    private static async Task<IResult> GetFixtureAsync(
        HttpContext httpContext,
        Guid fixtureId,
        GetFixture query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, fixtureId, cancellationToken);

        return result.Outcome == CompetitionReadOutcome.Found
            ? Results.Ok(result.Fixture)
            : CompetitionRefusals.Read(
                result.Outcome,
                CompetitionErrorCodes.FixtureNotFound,
                "That fixture does not exist in this world.");
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
