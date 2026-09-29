using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.Competition;

/// <summary>
/// Configuration for the season-rollover worker (`PR-4`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// Bound and validated like the other schedulers' options, so a misconfigured interval fails at startup
/// rather than the first time the materialiser ticks. <see cref="EnableRollover"/> lets an operator pause an
/// automatic rollover — for example while a rollover defect is investigated — without stopping the season's
/// play.
/// </remarks>
public sealed class SeasonRolloverOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Rollover";

    /// <summary>Gets or sets whether the worker materialises a rollover job. On by default.</summary>
    public bool EnableRollover { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser looks for a season due to roll over, in seconds.</summary>
    [Range(30, 86_400)]
    public int CheckIntervalSeconds { get; set; } = 300;
}
