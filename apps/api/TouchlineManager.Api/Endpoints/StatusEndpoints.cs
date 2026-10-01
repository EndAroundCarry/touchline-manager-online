using TouchlineManager.Application.Status;
using TouchlineManager.Contracts.Status;

namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// The public service status and document versions (master plan §16 Stage 15, `F-55`, ADR-0050).
/// </summary>
/// <remarks>
/// <para>
/// The product's first anonymous read. It sits at the version root, beside <c>/me</c> and <c>/sync</c>,
/// because it is a resource rather than a module's namespace and it must be reachable while signed out —
/// the register consent and the footer link to the pages it feeds.
/// </para>
/// <para>
/// It carries no personal data, so no authorization policy is attached and the endpoint is explicitly
/// anonymous. It is a small read over a five-second-cached flag and existing tables, so it carries no rate
/// limit; ADR-0050 records that this is a deliberate omission rather than an oversight.
/// </para>
/// </remarks>
internal static class StatusEndpoints
{
    /// <summary>Maps the public status route.</summary>
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("status");

        group
            .MapGet("/status", GetStatusAsync)
            .WithName("GetPublicStatus")
            .WithSummary("Reads the public service status and the published document versions.")
            .WithDescription(
                "Reachable without a session. Reports the server's instant, whether the game is in read-only "
                + "maintenance and the operator's reason, the running season, the next round's kickoff, and "
                + "the terms and privacy versions a consent records. It carries no personal data.")
            .AllowAnonymous()
            .Produces<PublicStatusResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext httpContext,
        GetPublicStatus status,
        CancellationToken cancellationToken)
    {
        var result = await status.ExecuteAsync(cancellationToken);

        // Live state, so it is never cached, the way the sync summary and the funnels are not.
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(result);
    }
}
