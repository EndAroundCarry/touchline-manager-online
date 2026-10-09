using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The engine that plays a match with <see cref="TickMatchLoop"/> (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <see cref="TickMatchEngine"/> is the engine the registry resolves for <c>engine-v12</c> and, until Milestone 9 wires the loop in
/// and calibrates it, it still plays the possession engine's matches. This is the loop on its own: the tests and the benchmark
/// tool pass it to <c>MatchSimulator.Simulate</c> to play and film a tick match before it is the default.
/// </remarks>
internal sealed class TickLoopEngine : IMatchSimulationEngine
{
    /// <summary>The singleton instance.</summary>
    public static readonly TickLoopEngine Instance = new();

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
