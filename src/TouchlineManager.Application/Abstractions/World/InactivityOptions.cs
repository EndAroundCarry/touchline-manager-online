using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Configuration for the inactivity ladder (`OCC-1`–`OCC-3`, ADR-0027).
/// </summary>
/// <remarks>
/// Bound and validated like the other schedulers' options, so a misconfigured interval fails at startup rather
/// than the first time the materialiser ticks. <see cref="EnableEvaluation"/> lets an operator pause the ladder
/// without stopping the season.
/// </remarks>
public sealed class InactivityOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Inactivity";

    /// <summary>Gets or sets whether the worker materialises the ladder's daily job. On by default.</summary>
    public bool EnableEvaluation { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser looks for today's row, in seconds.</summary>
    [Range(30, 86_400)]
    public int CheckIntervalSeconds { get; set; } = 300;
}
