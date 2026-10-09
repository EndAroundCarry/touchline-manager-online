using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The discrete tick-based match engine (10 Hz, `tick-engine-v1`, Milestone 9).
/// </summary>
/// <remarks>
/// <para>
/// The engine plays the whole match as a physical simulation: twenty-two autonomous player agents and a
/// physical ball at a discrete time step of 100 ms, steered by the tactical geometry, the zonal defending,
/// the off-ball support, the ball-carrier brain, the goalkeeping and shot-stopping, and the restart state
/// machine. Every player and the ball has a real position at every tick, so the replay needs no
/// reconstruction: the continuous trace is what the film is sliced from.
/// </para>
/// <para>
/// The engine is the active engine for `engine-v12` snapshots. The legacy possession engine remains
/// registered for `engine-v11` snapshots, which still reproduce byte for byte (ADR-0004).
/// </para>
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

        TickMatchLoop.Run(state);
    }
}
