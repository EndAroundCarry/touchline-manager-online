using FluentValidation;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Competition;
using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Api.Squad;

/// <summary>
/// The fixture team-sheet HTTP surface (master plan §10.4, §11.1; F-19, `SQ-4`).
/// </summary>
/// <remarks>
/// <para>
/// Addressed by fixture, because a prepared side belongs to one fixture rather than to the club: the URL is
/// <c>/fixtures/{fixtureId}/team-sheet</c> and the club it answers for comes from the caller's tenure. The
/// sheet's <em>version</em> is the strong entity tag: a read returns it in the body, and a save that
/// replaces an existing selection must send it back in <c>If-Match</c>, so a side prepared on one device
/// cannot be silently overwritten on another (`CONC-1`, ADR-0009). The first save carries no tag, because
/// there is nothing to be conditional against yet.
/// </para>
/// <para>
/// Handlers do three things: read the account, call one use case, and translate the outcome into a status
/// and a stable code.
/// </para>
/// </remarks>
internal static class TeamSheetEndpoints
{
    /// <summary>Maps the fixture team-sheet routes.</summary>
    public static IEndpointRouteBuilder MapTeamSheetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("squad")
            .RequireAuthorization();

        group.MapGet("/fixtures/{fixtureId:guid}/team-sheet", GetTeamSheetAsync)
            .WithName("GetFixtureTeamSheet")
            .WithSummary("Reads the caller's club's side for a fixture, prepared or not.")
            .Produces<FixtureTeamSheetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/fixtures/{fixtureId:guid}/team-sheet", SaveTeamSheetAsync)
            .WithName("SaveFixtureTeamSheet")
            .WithSummary("Prepares or replaces the club's side for a fixture.")
            .WithDescription(
                "Replaces the whole selection. Requires If-Match with the sheet's current version once one "
                + "exists. A selection that breaks a team-sheet rule answers 400 "
                + "TEAM_SHEET_VALIDATION_FAILED with the issues; a locked fixture answers 409.")
            .Produces<FixtureTeamSheetResponse>(StatusCodes.Status200OK)
            .Produces<FixtureTeamSheetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetTeamSheetAsync(
        HttpContext httpContext,
        Guid fixtureId,
        GetFixtureTeamSheet query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, fixtureId, cancellationToken);

        return result.Outcome == CompetitionReadOutcome.Found
            ? Results.Ok(result.TeamSheet)
            : result.Outcome == CompetitionReadOutcome.ClubNotManaged
                ? CompetitionRefusals.NotYours()
                : CompetitionRefusals.Read(
                    result.Outcome,
                    CompetitionErrorCodes.FixtureNotYours,
                    "Your club does not play in that fixture.");
    }

    private static async Task<IResult> SaveTeamSheetAsync(
        HttpContext httpContext,
        Guid fixtureId,
        SaveFixtureTeamSheetRequest request,
        IValidator<SaveFixtureTeamSheetRequest> validator,
        SaveFixtureTeamSheet useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body, so a save that cannot be conditional is answered as such
        // rather than validating a selection that will not be applied.
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, fixtureId, expectedVersion, request, cancellationToken);

        return result.Outcome switch
        {
            SaveFixtureTeamSheetOutcome.Created => Saved(httpContext, result, created: true),
            SaveFixtureTeamSheetOutcome.Updated => Saved(httpContext, result, created: false),
            _ => CompetitionRefusals.Write(result),
        };
    }

    private static IResult Saved(
        HttpContext httpContext,
        SaveFixtureTeamSheetResult result,
        bool created)
    {
        var sheet = result.TeamSheet!;

        if (sheet.SheetVersion is { } version)
        {
            httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(version);
        }

        return created
            ? Results.Created(
                $"{ModuleEndpointGroups.VersionPrefix}/fixtures/{sheet.FixtureId}/team-sheet",
                sheet)
            : Results.Ok(sheet);
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
