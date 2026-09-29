namespace TouchlineManager.Domain.Competition;

/// <summary>
/// The checkpoint a season rollover has reached (master plan §7.5, `PR-4`, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// The rollover is a resumable state machine rather than one opaque transaction: each phase commits its
/// own work and records itself here, so a worker that dies between phases is retried by the queue and
/// resumes at the next one instead of restarting a season's worth of movement. The phase is the
/// checkpoint `PR-4` and master plan §7.5 require.
/// </para>
/// <para>
/// The order is the plan's: preflight and freeze, then finalize standings and close the season's
/// entries, then move clubs into the next season and generate its schedule, then seal the closing season
/// and advance the world.
/// </para>
/// </remarks>
public enum SeasonRolloverPhase
{
    /// <summary>Created. Preflight has not yet run.</summary>
    Started = 0,

    /// <summary>Preflight passed and the season was frozen; no new claims or listings are accepted.</summary>
    Frozen = 1,

    /// <summary>Standings are final and the closing season's entries carry their closing figures.</summary>
    Finalized = 2,

    /// <summary>Contracts are resolved: expired players released, unmanaged clubs renewed, retirements applied.</summary>
    Squads = 3,

    /// <summary>The next season exists and every club has been placed into it with a schedule.</summary>
    Moved = 4,

    /// <summary>The closing season is sealed and the next one is active. Terminal.</summary>
    Completed = 5,

    /// <summary>Preflight or generation found something an operator must look at. Terminal until retried.</summary>
    Failed = 6,
}

/// <summary>The questions asked about <see cref="SeasonRolloverPhase"/>.</summary>
public static class SeasonRolloverPhaseRules
{
    /// <summary>Whether the rollover has reached a state it will not leave on its own.</summary>
    public static bool IsTerminal(SeasonRolloverPhase phase) =>
        phase is SeasonRolloverPhase.Completed or SeasonRolloverPhase.Failed;

    /// <summary>Whether every phase before <paramref name="phase"/> has been passed.</summary>
    /// <param name="phase">The phase to test.</param>
    /// <param name="target">The phase to compare against.</param>
    public static bool IsAtLeast(SeasonRolloverPhase phase, SeasonRolloverPhase target) =>
        phase != SeasonRolloverPhase.Failed && phase >= target;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this SeasonRolloverPhase phase) => phase switch
    {
        SeasonRolloverPhase.Started => SeasonRolloverPhases.StartedCode,
        SeasonRolloverPhase.Frozen => SeasonRolloverPhases.FrozenCode,
        SeasonRolloverPhase.Finalized => SeasonRolloverPhases.FinalizedCode,
        SeasonRolloverPhase.Squads => SeasonRolloverPhases.SquadsCode,
        SeasonRolloverPhase.Moved => SeasonRolloverPhases.MovedCode,
        SeasonRolloverPhase.Completed => SeasonRolloverPhases.CompletedCode,
        SeasonRolloverPhase.Failed => SeasonRolloverPhases.FailedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown rollover phase."),
    };

    /// <summary>Parses a stable code back to its phase.</summary>
    public static SeasonRolloverPhase FromCode(string code) => code switch
    {
        SeasonRolloverPhases.StartedCode => SeasonRolloverPhase.Started,
        SeasonRolloverPhases.FrozenCode => SeasonRolloverPhase.Frozen,
        SeasonRolloverPhases.FinalizedCode => SeasonRolloverPhase.Finalized,
        SeasonRolloverPhases.SquadsCode => SeasonRolloverPhase.Squads,
        SeasonRolloverPhases.MovedCode => SeasonRolloverPhase.Moved,
        SeasonRolloverPhases.CompletedCode => SeasonRolloverPhase.Completed,
        SeasonRolloverPhases.FailedCode => SeasonRolloverPhase.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown rollover phase code."),
    };
}

/// <summary>Stable codes for <see cref="SeasonRolloverPhase"/>.</summary>
public static class SeasonRolloverPhases
{
    /// <summary>The code for <see cref="SeasonRolloverPhase.Started"/>.</summary>
    public const string StartedCode = "started";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Frozen"/>.</summary>
    public const string FrozenCode = "frozen";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Finalized"/>.</summary>
    public const string FinalizedCode = "finalized";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Squads"/>.</summary>
    public const string SquadsCode = "squads";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Moved"/>.</summary>
    public const string MovedCode = "moved";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Completed"/>.</summary>
    public const string CompletedCode = "completed";

    /// <summary>The code for <see cref="SeasonRolloverPhase.Failed"/>.</summary>
    public const string FailedCode = "failed";

    /// <summary>The longest code, for sizing the database column.</summary>
    public const int MaxCodeLength = 9;
}
