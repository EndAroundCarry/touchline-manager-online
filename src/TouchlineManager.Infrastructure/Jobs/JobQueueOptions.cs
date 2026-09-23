namespace TouchlineManager.Infrastructure.Jobs;

/// <summary>
/// Configuration for the durable job queue worker, bound from the <c>Worker</c> section.
/// </summary>
public sealed class JobQueueOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Worker";

    /// <summary>Gets or sets how long the worker waits between claim attempts when idle.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Gets or sets the maximum number of jobs claimed per batch.</summary>
    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>
    /// Gets or sets the lease duration. A crashed worker's jobs become claimable again once the
    /// lease expires, so this is the worst-case delay before recovery.
    /// </summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Gets or sets the maximum wait before the next poll when the queue is empty.</summary>
    public int MaxIdlePollIntervalSeconds { get; set; } = 30;
}
