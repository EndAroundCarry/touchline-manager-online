using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The discrete tick-based match engine (10 Hz).
/// </summary>
/// <remarks>
/// Operates at 10 ticks per match second (100 ms per tick): 22 autonomous players and one ball are stepped through
/// <see cref="TickMatchLoop"/> for the whole match, and the possession engine is kept only as the fallback a snapshot frozen
/// against <c>engine-v11</c> resolves to (`tick-engine-v1`, Milestone 9).
/// </remarks>
internal sealed class TickMatchEngine : IMatchSimulationEngine
{
    /// <summary>The singleton instance of the tick match engine.</summary>
    public static readonly TickMatchEngine Instance = new();

    /// <inheritdoc/>
    public string EngineLabel => EngineVersions.EngineLabel;

    /// <inheritdoc/>
    public string RuleSetLabel => EngineVersions.RuleSetLabel;

    /// <inheritdoc/>
    public void Run(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        TickMatchLoop.Play(state);
    }
}
