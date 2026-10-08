namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Encapsulates the legacy macro possession-based simulation engine (<see cref="PossessionSimulator"/>).
/// </summary>
/// <remarks>
/// Preserved intact for regression testing, backward compatibility, and historical match replay verification.
/// </remarks>
internal sealed class LegacyPossessionEngine : IMatchSimulationEngine
{
    /// <summary>The singleton instance of the legacy possession engine.</summary>
    public static readonly LegacyPossessionEngine Instance = new();

    /// <inheritdoc/>
    public string EngineLabel => EngineVersions.LegacyEngineLabel;

    /// <inheritdoc/>
    public string RuleSetLabel => EngineVersions.LegacyRuleSetLabel;

    /// <inheritdoc/>
    public void Run(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        PossessionSimulator.Run(state);
    }
}
