namespace TouchlineManager.Domain.Competition;

/// <summary>The lifecycle state of a season (`CAL-6`, master plan §6.4).</summary>
public enum SeasonStatus
{
    /// <summary>Created but not started. Fixtures exist; nothing is played.</summary>
    Scheduled = 0,

    /// <summary>Running. Matchdays lock, simulate, and publish.</summary>
    Active = 1,

    /// <summary>All matchdays published; the rollover state machine holds the season.</summary>
    Rollover = 2,

    /// <summary>Finished and immutable. History, never rewritten (`PR-6`).</summary>
    Completed = 3,
}

/// <summary>The questions asked about <see cref="SeasonStatus"/>.</summary>
public static class SeasonStatusRules
{
    /// <summary>
    /// Whether the season accepts new claims into its divisions.
    /// </summary>
    /// <remarks>
    /// Claims are refused during rollover. A manager who joined mid-rollover would be assigned to a
    /// division whose membership is being rewritten, which is exactly the partial assignment
    /// `PYR-10` forbids.
    /// </remarks>
    public static bool AcceptsClaims(SeasonStatus status) => status == SeasonStatus.Active;

    /// <summary>Whether the season's results may still change.</summary>
    public static bool IsMutable(SeasonStatus status) =>
        status is SeasonStatus.Scheduled or SeasonStatus.Active or SeasonStatus.Rollover;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this SeasonStatus status) => status switch
    {
        SeasonStatus.Scheduled => SeasonStatuses.ScheduledCode,
        SeasonStatus.Active => SeasonStatuses.ActiveCode,
        SeasonStatus.Rollover => SeasonStatuses.RolloverCode,
        SeasonStatus.Completed => SeasonStatuses.CompletedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown season status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static SeasonStatus FromCode(string code) => code switch
    {
        SeasonStatuses.ScheduledCode => SeasonStatus.Scheduled,
        SeasonStatuses.ActiveCode => SeasonStatus.Active,
        SeasonStatuses.RolloverCode => SeasonStatus.Rollover,
        SeasonStatuses.CompletedCode => SeasonStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown season status code."),
    };
}

/// <summary>Stable codes for <see cref="SeasonStatus"/>.</summary>
public static class SeasonStatuses
{
    /// <summary>The code for <see cref="SeasonStatus.Scheduled"/>.</summary>
    public const string ScheduledCode = "scheduled";

    /// <summary>The code for <see cref="SeasonStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="SeasonStatus.Rollover"/>.</summary>
    public const string RolloverCode = "rollover";

    /// <summary>The code for <see cref="SeasonStatus.Completed"/>.</summary>
    public const string CompletedCode = "completed";
}
