namespace TouchlineManager.Infrastructure.Finance;

/// <summary>
/// Switches and timing for the weekly finance settlement (master plan §7.2; `CON-2`, `FIN-4`, `FIN-7`,
/// `FIN-9`).
/// </summary>
/// <remarks>
/// Gated by configuration rather than by an <c>ops.feature_flags</c> row because the flag table does not
/// exist yet; when it does, this is where its value is read (master plan §6.9, §17.12). Disabled by default,
/// so a deployment that has not opted in never charges a wage.
/// </remarks>
public sealed class FinanceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Finance";

    /// <summary>
    /// Gets or sets whether the worker enqueues and runs the weekly settlement. Off unless explicitly
    /// enabled, so a half-configured economy cannot reach a live world by accident.
    /// </summary>
    public bool EnableWeeklyRun { get; set; }

    /// <summary>Gets or sets how often the materialiser checks that the week's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 3600;
}
