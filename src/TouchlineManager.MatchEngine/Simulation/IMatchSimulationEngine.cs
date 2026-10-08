namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Strategy interface for match simulation engines.
/// </summary>
internal interface IMatchSimulationEngine
{
    /// <summary>The engine version label (e.g. "engine-v12", "engine-v11").</summary>
    string EngineLabel { get; }

    /// <summary>The rule set version label (e.g. "engine-rules-v11", "engine-rules-v10").</summary>
    string RuleSetLabel { get; }

    /// <summary>
    /// Simulates the match to completion, mutating the provided match state.
    /// </summary>
    /// <param name="state">The runtime state of the match.</param>
    void Run(MatchState state);
}
