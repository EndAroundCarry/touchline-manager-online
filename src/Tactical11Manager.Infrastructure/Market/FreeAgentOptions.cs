namespace Tactical11Manager.Infrastructure.Market;

/// <summary>
/// Switches and timing for the weekly free-agent pool (`FA-5`, master plan §7.2).
/// </summary>
/// <remarks>
/// Enabled by default, like the AI market: the pool is part of the game's market rather than an optional
/// simulation, and the work is idempotent. A week's batch row is unique, so a repeat pass creates nothing.
/// </remarks>
public sealed class FreeAgentOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "FreeAgents";

    /// <summary>Gets or sets whether the worker opens the weekly free-agent pool.</summary>
    public bool EnableBatches { get; set; } = true;

    /// <summary>Gets or sets how often the worker checks that the week's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 600;
}
