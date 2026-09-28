using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>
/// Configuration for the outbox dispatcher (`MOD-4`, ADR-0028).
/// </summary>
/// <remarks>
/// Bound and validated like the other schedulers' options, so a misconfigured interval fails at startup
/// rather than the first time the materialiser ticks. <see cref="EnableDispatch"/> lets an operator pause
/// outbound mail without stopping the game.
/// </remarks>
public sealed class OutboxOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Outbox";

    /// <summary>Gets or sets whether the worker materialises dispatch jobs. On by default.</summary>
    public bool EnableDispatch { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser places a dispatch row, in seconds.</summary>
    [Range(30, 86_400)]
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>Gets or sets the most messages one dispatch pass drains.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;
}
