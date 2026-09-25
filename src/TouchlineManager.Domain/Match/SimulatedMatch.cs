namespace TouchlineManager.Domain.Match;

/// <summary>
/// One simulated fixture: the result a snapshot produced (`MAT-9`, master plan §6.6).
/// </summary>
/// <remarks>
/// <para>
/// A match is written once, when its fixture is staged. It carries both hashes — the input it was
/// simulated from and the output it produced — so a disputed scoreline is re-derived rather than argued
/// about, and an engine defect is found by a hash failing rather than by a manager noticing.
/// </para>
/// <para>
/// The type has no mutators because a result is history the moment it exists. Publication is a property
/// of the <em>fixture</em> and its matchday, not of the match: the nine results of a round become public
/// together or not at all (`MAT-7`), so a match that had to remember whether it was published would be a
/// second source of truth for the same fact.
/// </para>
/// <para>
/// <see cref="StatisticsJson"/> is the versioned per-side statistics document. The engine derives every
/// count in it from the event stream, so it reconciles with <see cref="MatchEvent"/> rows by construction
/// (`MAT-5`); it is stored as one immutable engine output rather than as thirty-four columns no query
/// filters on.
/// </para>
/// </remarks>
public sealed class SimulatedMatch
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private SimulatedMatch()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the simulated fixture. Unique, because a fixture is simulated once.</summary>
    public Guid FixtureId { get; private set; }

    /// <summary>Gets the engine version that produced the result.</summary>
    public string EngineVersion { get; private set; } = string.Empty;

    /// <summary>Gets the rules version that produced the result.</summary>
    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Gets the publishable commitment to the seed the match was simulated with (`MAT-10`).</summary>
    public string SeedCommitment { get; private set; } = string.Empty;

    /// <summary>Gets the home side's goals.</summary>
    public int HomeGoals { get; private set; }

    /// <summary>Gets the away side's goals.</summary>
    public int AwayGoals { get; private set; }

    /// <summary>Gets the versioned per-side statistics document (`MAT-5`).</summary>
    public string StatisticsJson { get; private set; } = string.Empty;

    /// <summary>Gets the hash of the input snapshot the result was produced from.</summary>
    public string InputHash { get; private set; } = string.Empty;

    /// <summary>Gets the hash of the result, which re-derivation must reproduce (`MAT-9`).</summary>
    public string OutputHash { get; private set; } = string.Empty;

    /// <summary>Gets the simulation attempt that produced the result.</summary>
    public Guid SimulationAttemptId { get; private set; }

    /// <summary>Gets when the simulation started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when the simulation finished.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Records a simulated fixture.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="fixtureId">The simulated fixture.</param>
    /// <param name="engineVersion">The engine version that produced the result.</param>
    /// <param name="ruleSetVersion">The rules version that produced the result.</param>
    /// <param name="seedCommitment">The commitment to the seed used.</param>
    /// <param name="homeGoals">The home side's goals, zero or more.</param>
    /// <param name="awayGoals">The away side's goals, zero or more.</param>
    /// <param name="statisticsJson">The versioned statistics document.</param>
    /// <param name="inputHash">The hash of the input snapshot.</param>
    /// <param name="outputHash">The hash of the result.</param>
    /// <param name="simulationAttemptId">The attempt that produced the result.</param>
    /// <param name="startedAt">When the simulation started.</param>
    /// <param name="completedAt">When the simulation finished.</param>
    public static SimulatedMatch Record(
        Guid id,
        Guid fixtureId,
        string engineVersion,
        string ruleSetVersion,
        string seedCommitment,
        int homeGoals,
        int awayGoals,
        string statisticsJson,
        string inputHash,
        string outputHash,
        Guid simulationAttemptId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedCommitment);
        ArgumentException.ThrowIfNullOrWhiteSpace(statisticsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputHash);
        ArgumentOutOfRangeException.ThrowIfNegative(homeGoals);
        ArgumentOutOfRangeException.ThrowIfNegative(awayGoals);

        if (fixtureId == Guid.Empty)
        {
            throw new ArgumentException("A match belongs to a fixture.", nameof(fixtureId));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                completedAt,
                "A match cannot finish before it started.");
        }

        return new SimulatedMatch
        {
            Id = id,
            FixtureId = fixtureId,
            EngineVersion = engineVersion,
            RuleSetVersion = ruleSetVersion,
            SeedCommitment = seedCommitment,
            HomeGoals = homeGoals,
            AwayGoals = awayGoals,
            StatisticsJson = statisticsJson,
            InputHash = inputHash,
            OutputHash = outputHash,
            SimulationAttemptId = simulationAttemptId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }
}
