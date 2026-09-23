using TouchlineManager.Application.Jobs;

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
