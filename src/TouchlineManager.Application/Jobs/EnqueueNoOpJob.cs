using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Enqueues the no-op diagnostic job used by the Stage 1 walking skeleton.
/// </summary>
/// <remarks>
/// This use case is reachable only from a development-flagged endpoint. It is deleted once real
/// deadline workflows exist, because a public enqueue probe has no place in production.
/// </remarks>
public sealed class EnqueueNoOpJob
{
    private readonly IClock _clock;
    private readonly IJobQueue _jobQueue;

    /// <summary>Initializes the use case.</summary>
    public EnqueueNoOpJob(IClock clock, IJobQueue jobQueue)
    {
        _clock = clock;
        _jobQueue = jobQueue;
    }

    /// <summary>
    /// Enqueues the diagnostic job, due immediately.
    /// </summary>
    /// <param name="businessKeySuffix">
    /// A caller-supplied suffix so the probe can be run repeatedly. Real jobs derive their
    /// business key from domain identity instead.
    /// </param>
    /// <returns>The business key that was enqueued, and whether it was newly inserted.</returns>
    public async Task<(string BusinessKey, bool Enqueued)> ExecuteAsync(
        string businessKeySuffix,
        CancellationToken cancellationToken)
    {
        var businessKey = $"ops.noop:{businessKeySuffix}";

        var enqueued = await _jobQueue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = NoOpJobHandler.TypeName,
                BusinessKey = businessKey,
                DueAt = _clock.UtcNow,
            },
            cancellationToken);

        return (businessKey, enqueued);
    }
}
