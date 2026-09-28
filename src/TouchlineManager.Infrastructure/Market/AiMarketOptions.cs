namespace TouchlineManager.Infrastructure.Market;

/// <summary>
/// Switches and timing for the AI transfer market evaluation (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// Enabled by default, like the AI club evaluation: the AI's market behaviour is part of the game rather
/// than an optional simulation, and the work is idempotent — a club's daily decisions carry deterministic
/// keys, so a repeat pass creates nothing. A deployment that has not opted in would leave every AI club
/// neither buying nor selling, which is playable but is not the stage.
/// </remarks>
public sealed class AiMarketOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AiMarket";

    /// <summary>Gets or sets whether the worker evaluates the AI-controlled clubs on the market.</summary>
    public bool EnableEvaluation { get; set; } = true;

    /// <summary>Gets or sets how often the worker checks that the day's job exists, in seconds.</summary>
    public int CheckIntervalSeconds { get; set; } = 300;
}
