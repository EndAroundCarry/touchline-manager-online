namespace TouchlineManager.Infrastructure.World;

/// <summary>
/// Switches and timing for the AI club evaluation (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// Enabled by default, unlike the daily progression: the AI's tactics are part of the game rather than an
/// optional simulation, and the work is idempotent — it fills a club's missing plans and never overwrites
/// one. A deployment that has not opted in would leave every AI club fielding the neutral fallback, which
/// is playable but is not the stage (`INS-12`).
/// </remarks>
public sealed class AiClubOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AiClubs";

    /// <summary>Gets or sets whether the worker evaluates the AI-controlled clubs.</summary>
    public bool EnableEvaluation { get; set; } = true;

    /// <summary>Gets or sets how often the worker checks that the day's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 300;
}
