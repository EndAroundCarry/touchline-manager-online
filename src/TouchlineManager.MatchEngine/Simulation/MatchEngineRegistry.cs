using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Registry and resolver for match simulation engines.
/// </summary>
internal static class MatchEngineRegistry
{
    private static readonly Dictionary<string, IMatchSimulationEngine> Engines = new(StringComparer.Ordinal)
    {
        [EngineVersions.EngineLabel] = TickMatchEngine.Instance,
        [EngineVersions.LegacyEngineLabel] = LegacyPossessionEngine.Instance,
    };

    /// <summary>
    /// The default simulation engine when not explicitly resolved from a legacy label.
    /// Defaults to <see cref="TickMatchEngine.Instance"/>.
    /// </summary>
    public static IMatchSimulationEngine DefaultEngine { get; set; } = TickMatchEngine.Instance;

    /// <summary>Registers an engine implementation for a version label.</summary>
    public static void Register(IMatchSimulationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engines[engine.EngineLabel] = engine;
    }

    /// <summary>Resolves the appropriate engine for the given engine version label.</summary>
    public static IMatchSimulationEngine Resolve(string? engineVersion)
    {
        if (engineVersion != null && Engines.TryGetValue(engineVersion, out var engine))
        {
            return engine;
        }

        if (string.Equals(engineVersion, EngineVersions.LegacyEngineLabel, StringComparison.Ordinal))
        {
            return LegacyPossessionEngine.Instance;
        }

        return DefaultEngine;
    }
}
