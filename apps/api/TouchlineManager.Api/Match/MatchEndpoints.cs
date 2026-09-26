using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Match;
using TouchlineManager.Contracts.Match;

namespace TouchlineManager.Api.Match;

/// <summary>
/// The match module's read surface: a played match's summary and its replay (master plan §9.5, §10.5).
/// </summary>
/// <remarks>
/// <para>
/// Like the competition reads, these sit at the version root rather than under <c>/api/v1/match</c>, because
/// §10.5 addresses a match as <c>/matches/{matchId}</c> and its replay as
/// <c>/matches/{matchId}/presentation</c>. Those paths are the contract.
/// </para>
/// <para>
/// Both are public game data — a result is the thing a manager shares and a table is built from — so neither
/// is gated on holding a club. Only a published match is readable, which the read itself enforces, so a
/// staged score cannot leak (`MAT-7`).
/// </para>
/// <para>
/// The replay is immutable once published, so it answers a conditional request with <c>304 Not Modified</c>
/// and carries a long-lived, immutable cache directive. That is what lets the service worker keep recently
/// viewed presentations and what keeps a manager off the network for a match they already watched (§9.5,
/// §11.4).
/// </para>
/// </remarks>
internal static class MatchEndpoints
{
    /// <summary>Maps the match read routes.</summary>
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("match")
            .RequireAuthorization();

        group.MapGet("/matches/{matchId:guid}", GetMatchAsync)
            .WithName("GetMatch")
            .WithSummary("Reads a played match's summary, score, and statistics.")
            .Produces<MatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/matches/{matchId:guid}/presentation", GetMatchPresentationAsync)
            .WithName("GetMatchPresentation")
            .WithSummary("Reads a played match's commentary timeline and keyframe highlights.")
            .Produces<MatchPresentationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetMatchAsync(
        Guid matchId,
        GetMatch query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(matchId, cancellationToken);

        return result.Outcome == MatchReadOutcome.Found
            ? Results.Ok(result.Match)
            : NotFound();
    }

    private static async Task<IResult> GetMatchPresentationAsync(
        HttpContext httpContext,
        Guid matchId,
        GetMatchPresentation query,
        CancellationToken cancellationToken)
    {
        var result = await query.ExecuteAsync(matchId, cancellationToken);

        if (result.Outcome != MatchReadOutcome.Found
            || result.Presentation is null
            || result.EntityTag is null)
        {
            return NotFound();
        }

        var entityTag = EntityTagHeader.ForToken(result.EntityTag);

        httpContext.Response.Headers.ETag = entityTag;

        if (EntityTagHeader.MatchesIfNoneMatch(
            httpContext.Request.Headers.IfNoneMatch.ToString(),
            entityTag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // Immutable: the events and the engine are frozen, so the same bytes are always described by the
        // same tag. `private`, because the request is authenticated even though the payload is public.
        httpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return Results.Ok(result.Presentation);
    }

    private static IResult NotFound() => ProblemResults.Code(
        StatusCodes.Status404NotFound,
        MatchErrorCodes.MatchNotFound,
        "No such match.",
        "That match does not exist in this world, or its result has not been published yet.");
}
