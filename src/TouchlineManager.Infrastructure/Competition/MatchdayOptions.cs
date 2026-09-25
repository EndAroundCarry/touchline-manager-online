namespace TouchlineManager.Infrastructure.Competition;

/// <summary>
/// Configuration for the matchday worker, bound from the <c>Matchday</c> section (master plan §7.2).
/// </summary>
public sealed class MatchdayOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Matchday";

    /// <summary>
    /// Gets or sets whether the worker materialises lock, resolution, and publication jobs.
    /// </summary>
    /// <remarks>
    /// Enabled by default, because running matchdays is the worker's whole purpose and a deployment that
    /// silently did nothing at kickoff would be a worse default than one that ran the season. An operator
    /// turning it off leaves every deadline in the database untouched: the rows are still the authority, so
    /// switching it back on catches up rather than skipping (ADR-0003).
    /// </remarks>
    public bool EnableMatchdayWorker { get; set; } = true;

    /// <summary>Gets or sets how often the worker looks for deadlines to materialise.</summary>
    public int CheckIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets how far ahead a round's lock and resolution jobs are enqueued.
    /// </summary>
    /// <remarks>
    /// A horizon rather than a per-round flag: the jobs are idempotent on their business key, so a
    /// materialiser that runs every five minutes re-inserts nothing, and one that has been down for a day
    /// re-creates exactly what it missed. The horizon only bounds how far ahead the queue is filled.
    /// </remarks>
    public int MaterializeHorizonDays { get; set; } = 21;
}
