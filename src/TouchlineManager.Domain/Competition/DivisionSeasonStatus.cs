namespace TouchlineManager.Domain.Competition;

/// <summary>The lifecycle state of one tier's instance in one season.</summary>
public enum DivisionSeasonStatus
{
    /// <summary>Entries and fixtures exist; no matchday has been played.</summary>
    Scheduled = 0,

    /// <summary>Play is under way.</summary>
    Active = 1,

    /// <summary>Standings are final and frozen for rollover (`PR-4`).</summary>
    Completed = 2,
}

/// <summary>The questions asked about <see cref="DivisionSeasonStatus"/>.</summary>
public static class DivisionSeasonStatusRules
{
    /// <summary>Whether standings may still change from published results.</summary>
    public static bool AcceptsResults(DivisionSeasonStatus status) =>
        status is DivisionSeasonStatus.Scheduled or DivisionSeasonStatus.Active;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this DivisionSeasonStatus status) => status switch
    {
        DivisionSeasonStatus.Scheduled => DivisionSeasonStatuses.ScheduledCode,
        DivisionSeasonStatus.Active => DivisionSeasonStatuses.ActiveCode,
        DivisionSeasonStatus.Completed => DivisionSeasonStatuses.CompletedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown division-season status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static DivisionSeasonStatus FromCode(string code) => code switch
    {
        DivisionSeasonStatuses.ScheduledCode => DivisionSeasonStatus.Scheduled,
        DivisionSeasonStatuses.ActiveCode => DivisionSeasonStatus.Active,
        DivisionSeasonStatuses.CompletedCode => DivisionSeasonStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown division-season status code."),
    };
}

/// <summary>Stable codes for <see cref="DivisionSeasonStatus"/>.</summary>
public static class DivisionSeasonStatuses
{
    /// <summary>The code for <see cref="DivisionSeasonStatus.Scheduled"/>.</summary>
    public const string ScheduledCode = "scheduled";

    /// <summary>The code for <see cref="DivisionSeasonStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="DivisionSeasonStatus.Completed"/>.</summary>
    public const string CompletedCode = "completed";
}
