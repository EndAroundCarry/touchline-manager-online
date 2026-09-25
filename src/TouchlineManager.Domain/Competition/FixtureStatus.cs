namespace TouchlineManager.Domain.Competition;

/// <summary>
/// Where one fixture sits between its creation and its published result (master plan §6.4, §7.4).
/// </summary>
/// <remarks>
/// The states exist so that a matchday can be published all-or-nothing (`MAT-7`): a simulated result sits
/// in <see cref="Staged"/> while the rest of its division's fixtures are still being simulated, and only
/// when every one of them is staged does publication move them all to <see cref="Published"/> in one
/// transaction. A score therefore exists only in the last two states, which is what the database's own
/// check constraint refuses to let anything else carry.
/// </remarks>
public enum FixtureStatus
{
    /// <summary>Created and waiting for its team-sheet lock.</summary>
    Scheduled = 0,

    /// <summary>The team sheets locked and the immutable input snapshot was taken (`MAT-1`).</summary>
    Locked = 1,

    /// <summary>A simulation attempt is under way.</summary>
    Simulating = 2,

    /// <summary>The result exists but the division's matchday has not published yet (`MAT-7`).</summary>
    Staged = 3,

    /// <summary>The result is public and its projections are applied.</summary>
    Published = 4,

    /// <summary>Voided by an operator with a reason; it holds no result (`MAT-10`).</summary>
    Void = 5,
}

/// <summary>The questions asked about <see cref="FixtureStatus"/>.</summary>
public static class FixtureStatusRules
{
    /// <summary>
    /// Whether a fixture in this state carries a score. Score and status must agree, and this is the one
    /// definition both the domain and the database check constraint read.
    /// </summary>
    public static bool CarriesScore(FixtureStatus status) =>
        status is FixtureStatus.Staged or FixtureStatus.Published;

    /// <summary>Whether the fixture has been locked, so its snapshot may not change (`MAT-1`, `CAL-3`).</summary>
    public static bool IsLocked(FixtureStatus status) => status != FixtureStatus.Scheduled;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this FixtureStatus status) => status switch
    {
        FixtureStatus.Scheduled => FixtureStatuses.ScheduledCode,
        FixtureStatus.Locked => FixtureStatuses.LockedCode,
        FixtureStatus.Simulating => FixtureStatuses.SimulatingCode,
        FixtureStatus.Staged => FixtureStatuses.StagedCode,
        FixtureStatus.Published => FixtureStatuses.PublishedCode,
        FixtureStatus.Void => FixtureStatuses.VoidCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown fixture status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static FixtureStatus FromCode(string code) => code switch
    {
        FixtureStatuses.ScheduledCode => FixtureStatus.Scheduled,
        FixtureStatuses.LockedCode => FixtureStatus.Locked,
        FixtureStatuses.SimulatingCode => FixtureStatus.Simulating,
        FixtureStatuses.StagedCode => FixtureStatus.Staged,
        FixtureStatuses.PublishedCode => FixtureStatus.Published,
        FixtureStatuses.VoidCode => FixtureStatus.Void,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown fixture status code."),
    };
}

/// <summary>Stable codes for <see cref="FixtureStatus"/>.</summary>
public static class FixtureStatuses
{
    /// <summary>The code for <see cref="FixtureStatus.Scheduled"/>.</summary>
    public const string ScheduledCode = "scheduled";

    /// <summary>The code for <see cref="FixtureStatus.Locked"/>.</summary>
    public const string LockedCode = "locked";

    /// <summary>The code for <see cref="FixtureStatus.Simulating"/>.</summary>
    public const string SimulatingCode = "simulating";

    /// <summary>The code for <see cref="FixtureStatus.Staged"/>.</summary>
    public const string StagedCode = "staged";

    /// <summary>The code for <see cref="FixtureStatus.Published"/>.</summary>
    public const string PublishedCode = "published";

    /// <summary>The code for <see cref="FixtureStatus.Void"/>.</summary>
    public const string VoidCode = "void";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 16;
}

/// <summary>The publication state of one division's matchday taken as a unit (`CAL-10`, `MAT-7`).</summary>
public enum MatchdayPublicationStatus
{
    /// <summary>Fixtures have not all staged; nothing about this matchday is public.</summary>
    Pending = 0,

    /// <summary>Every fixture staged and validated; waiting for the single publication transaction.</summary>
    Staged = 1,

    /// <summary>The whole matchday published at once; it cannot be un-published.</summary>
    Published = 2,
}

/// <summary>The questions asked about <see cref="MatchdayPublicationStatus"/>.</summary>
public static class MatchdayPublicationStatusRules
{
    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this MatchdayPublicationStatus status) => status switch
    {
        MatchdayPublicationStatus.Pending => MatchdayPublicationStatuses.PendingCode,
        MatchdayPublicationStatus.Staged => MatchdayPublicationStatuses.StagedCode,
        MatchdayPublicationStatus.Published => MatchdayPublicationStatuses.PublishedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown matchday publication status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static MatchdayPublicationStatus FromCode(string code) => code switch
    {
        MatchdayPublicationStatuses.PendingCode => MatchdayPublicationStatus.Pending,
        MatchdayPublicationStatuses.StagedCode => MatchdayPublicationStatus.Staged,
        MatchdayPublicationStatuses.PublishedCode => MatchdayPublicationStatus.Published,
        _ => throw new ArgumentOutOfRangeException(
            nameof(code),
            code,
            "Unknown matchday publication status code."),
    };
}

/// <summary>Stable codes for <see cref="MatchdayPublicationStatus"/>.</summary>
public static class MatchdayPublicationStatuses
{
    /// <summary>The code for <see cref="MatchdayPublicationStatus.Pending"/>.</summary>
    public const string PendingCode = "pending";

    /// <summary>The code for <see cref="MatchdayPublicationStatus.Staged"/>.</summary>
    public const string StagedCode = "staged";

    /// <summary>The code for <see cref="MatchdayPublicationStatus.Published"/>.</summary>
    public const string PublishedCode = "published";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 16;
}
