namespace TouchlineManager.Infrastructure.Training;

/// <summary>
/// Switches and timing for the daily training progression (master plan §7.2; `TRN-3`).
/// </summary>
/// <remarks>
/// The progression is gated by configuration rather than by an <c>ops.feature_flags</c> row because the
/// flag table does not exist yet; when it does, this is where its value is read (master plan §6.9, §17.12).
/// Disabled by default, so a deployment that has not opted in never advances a player.
/// </remarks>
public sealed class TrainingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Training";

    /// <summary>
    /// Gets or sets whether the worker enqueues and runs the daily progression. Off unless explicitly
    /// enabled, so the unfinished-or-unwanted behaviour cannot reach a live world by accident.
    /// </summary>
    public bool EnableDailyProgression { get; set; }

    /// <summary>Gets or sets how often the materializer checks that the day's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 300;
}
