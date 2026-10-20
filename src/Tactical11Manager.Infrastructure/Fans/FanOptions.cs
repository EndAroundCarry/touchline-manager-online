namespace Tactical11Manager.Infrastructure.Fans;

/// <summary>
/// Switches and timing for the weekly fan review (`FAN-8`, master plan §7.2).
/// </summary>
/// <remarks>
/// Enabled by default, like the free-agent pool: the review is part of how the game rates a club rather than an optional simulation, and the
/// work is idempotent. A club is reviewed once for a week, so a repeat pass changes nothing.
/// </remarks>
public sealed class FanOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Fans";

    /// <summary>Gets or sets whether the worker enqueues and runs the weekly fan review.</summary>
    public bool EnableWeeklyReview { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser checks that the week's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 3600;
}
