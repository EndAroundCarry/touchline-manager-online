using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>
/// Configuration for the deadline-reminder materialiser (`COM-3`, ADR-0029).
/// </summary>
/// <remarks>
/// Bound and validated like the other schedulers' options, so a misconfigured interval fails at startup rather
/// than the first time the materialiser ticks. <see cref="EnableReminders"/> lets an operator pause reminders
/// without stopping the season.
/// </remarks>
public sealed class ReminderOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Reminders";

    /// <summary>Gets or sets whether the worker materialises reminder jobs. On by default.</summary>
    public bool EnableReminders { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser looks for rounds about to lock, in seconds.</summary>
    [Range(30, 86_400)]
    public int CheckIntervalSeconds { get; set; } = 300;
}
