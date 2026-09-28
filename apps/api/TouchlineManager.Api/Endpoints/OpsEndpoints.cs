using TouchlineManager.Api.Http;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Market;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Market;

namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// Stage 1 diagnostics for the ops module.
/// </summary>
/// <remarks>
/// This is the walking skeleton: it proves that a request can enqueue a durable job, that the row
/// lands in <c>ops.jobs</c>, and that a separate worker process claims, executes, and completes it.
/// Real deadline endpoints replace it from Stage 2 onward.
/// </remarks>
internal static class OpsEndpoints
{
    /// <summary>Maps the diagnostic job probe onto the ops module group.</summary>
    public static RouteGroupBuilder MapJobProbe(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/noop-job", EnqueueNoOpJobAsync)
            .WithName("EnqueueNoOpJobProbe")
            .WithSummary("Enqueues a no-op durable job to verify the API, database, and worker pipeline.")
            .WithDescription(
                "Development and staging diagnostics only. Supply the same key twice to observe "
                + "enqueue idempotency: the second call reports enqueued=false and creates no second row.")
            .Produces<NoOpJobProbeResponse>(StatusCodes.Status202Accepted);

        return group;
    }

    /// <summary>Maps the Stage 7 matchday trigger onto the ops module group.</summary>
    /// <remarks>
    /// It exists so the end-to-end journey can watch a real round play without waiting for its calendar
    /// deadline. It enqueues the round's real lock and resolution jobs; the worker still does all of the
    /// work (ADR-0016). Like the job probe it is mapped only when the diagnostics flag is on.
    /// </remarks>
    public static RouteGroupBuilder MapMatchdayTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/play-matchday", PlayMatchdayAsync)
            .WithName("PlayMatchdayProbe")
            .WithSummary("Enqueues a round's real lock and resolution jobs, due now, so a matchday can be watched.")
            .WithDescription(
                "Development and staging diagnostics only. The worker locks, simulates, and publishes the "
                + "round exactly as it would on the calendar; this only does what the worker-only scheduler "
                + "normally does, and a repeated call is a no-op.")
            .Produces<MatchdayTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Maps the market's non-production auction trigger onto the ops module group.</summary>
    /// <remarks>
    /// The market counterpart of the matchday trigger: it enqueues a listing's real resolution job due now, so
    /// a journey can watch an auction settle without waiting for its window. The worker still resolves the
    /// listing exactly as it would on the calendar (ADR-0016). Mapped only when the diagnostics flag is on.
    /// </remarks>
    public static RouteGroupBuilder MapAuctionTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/play-auction", PlayAuctionAsync)
            .WithName("PlayAuctionProbe")
            .WithSummary("Enqueues a listing's resolution job, due now, so an auction can be watched.")
            .WithDescription(
                "Development and staging diagnostics only. The worker resolves the listing exactly as it "
                + "would at its window; this only does what the worker-only scheduler normally does, and a "
                + "repeated call is a no-op.")
            .Produces<AuctionTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> PlayAuctionAsync(
        PlayAuctionRequest request,
        TriggerAuctions trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request.ListingId, cancellationToken);

        if (result.Outcome == MarketOutcome.NotFound)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                MarketErrorCodes.ListingNotFound,
                "No listing was found.",
                "The requested listing does not exist.");
        }

        return Results.Json(
            new AuctionTriggerResponse(result.ListingId, result.ResolveKey, result.Enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PlayMatchdayAsync(
        PlayMatchdayRequest request,
        TriggerMatchday trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request.MatchdayId, cancellationToken);

        if (result.Outcome == TriggerMatchdayOutcome.MatchdayNotFound)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.MatchdayNotFound,
                "No matchday was found.",
                "The requested matchday does not exist.");
        }

        return Results.Json(
            new MatchdayTriggerResponse(
                result.MatchdayId,
                result.LockKey,
                result.ResolveKey,
                result.LockEnqueued,
                result.ResolveEnqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> EnqueueNoOpJobAsync(
        EnqueueNoOpJob useCase,
        CancellationToken cancellationToken,
        string? key = null)
    {
        // A caller-supplied key makes the probe repeatable and demonstrates idempotency. Real jobs
        // derive their business key from domain identity, never from a random value.
        var suffix = string.IsNullOrWhiteSpace(key) ? Guid.CreateVersion7().ToString() : key;

        var (businessKey, enqueued) = await useCase.ExecuteAsync(suffix, cancellationToken);

        return Results.Json(
            new NoOpJobProbeResponse(businessKey, enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }
}

/// <summary>Response of the diagnostic job probe.</summary>
/// <param name="BusinessKey">The business key the job was enqueued under.</param>
/// <param name="Enqueued">
/// <see langword="true"/> when a new row was inserted; <see langword="false"/> when a job with the
/// same business key already existed and the enqueue was a no-op.
/// </param>
internal sealed record NoOpJobProbeResponse(string BusinessKey, bool Enqueued);

/// <summary>The body of the matchday trigger.</summary>
/// <param name="MatchdayId">The round to play.</param>
internal sealed record PlayMatchdayRequest(Guid MatchdayId);

/// <summary>Response of the matchday trigger.</summary>
/// <param name="MatchdayId">The round that was triggered.</param>
/// <param name="LockKey">The business key of the enqueued lock job.</param>
/// <param name="ResolveKey">The business key of the enqueued resolution job.</param>
/// <param name="LockEnqueued"><see langword="true"/> when the lock row was newly inserted.</param>
/// <param name="ResolveEnqueued"><see langword="true"/> when the resolution row was newly inserted.</param>
internal sealed record MatchdayTriggerResponse(
    Guid MatchdayId,
    string LockKey,
    string ResolveKey,
    bool LockEnqueued,
    bool ResolveEnqueued);

/// <summary>The body of the auction trigger.</summary>
/// <param name="ListingId">The listing to resolve.</param>
internal sealed record PlayAuctionRequest(Guid ListingId);

/// <summary>Response of the auction trigger.</summary>
/// <param name="ListingId">The listing that was triggered.</param>
/// <param name="ResolveKey">The business key of the enqueued resolution job.</param>
/// <param name="Enqueued"><see langword="true"/> when the resolution row was newly inserted.</param>
internal sealed record AuctionTriggerResponse(Guid ListingId, string ResolveKey, bool Enqueued);
