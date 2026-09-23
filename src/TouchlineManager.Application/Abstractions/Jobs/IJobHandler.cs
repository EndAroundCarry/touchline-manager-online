namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>
/// Handles one job type. Handlers call application use cases, so they apply the same
/// authorization and invariant checks as the API.
/// </summary>
public interface IJobHandler
{
    /// <summary>Gets the job type this handler serves. Must be unique across the application.</summary>
    string JobType { get; }

    /// <summary>Handles the job. Must be idempotent for the job's business key.</summary>
    Task HandleAsync(LeasedJob job, CancellationToken cancellationToken);
}
