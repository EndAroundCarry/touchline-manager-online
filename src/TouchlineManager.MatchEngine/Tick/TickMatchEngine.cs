using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The discrete tick-based match engine (10 Hz).
/// </summary>
/// <remarks>
/// Operates at 10 ticks per match second (100 ms per tick).
/// Autonomous player agents, ball physics, tactical steering, and physical duels are simulated in discrete time.
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

        // Milestone 0 scaffolding: The tick-based components (kinematics, agents, AI, state machine)
        // are phased in across Milestones 1-8. During initial wrap, delegates to legacy execution.
        LegacyPossessionEngine.Instance.Run(state);
    }
}
