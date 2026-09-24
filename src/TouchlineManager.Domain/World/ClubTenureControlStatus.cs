namespace TouchlineManager.Domain.World;

/// <summary>
/// How much control a manager currently has over a club (game rules §4).
/// </summary>
/// <remarks>
/// The three states exist because the inactivity ladder needs a middle ground: at
/// <see cref="Inactive"/> the AI keeps the club in a legal state but the manager can resume simply by
/// logging in (`OCC-2`). Collapsing that into a single "not managed" state would make resumption
/// indistinguishable from a fresh takeover.
/// </remarks>
public enum ClubTenureControlStatus
{
    /// <summary>The manager is in control and is expected to act.</summary>
    Active = 0,

    /// <summary>Inactivity threshold passed; AI assists, and logging in restores control (`OCC-2`).</summary>
    Inactive = 1,

    /// <summary>The tenure has ended. The club is fully AI-controlled.</summary>
    Closed = 2,
}

/// <summary>The questions the rest of the product asks about <see cref="ClubTenureControlStatus"/>.</summary>
public static class ClubTenureControlStatusRules
{
    /// <summary>
    /// Whether a tenure in this state still counts as human occupancy for pyramid capacity.
    /// </summary>
    /// <remarks>
    /// An inactive tenure counts until it closes (`OCC-8`). Counting only active tenures would make a
    /// tier look emptier than it is and provision a new one while the old one still has a returning
    /// manager coming back to it.
    /// </remarks>
    public static bool OccupiesCapacity(ClubTenureControlStatus status) =>
        status is ClubTenureControlStatus.Active or ClubTenureControlStatus.Inactive;

    /// <summary>Whether the tenure is open, so the club is not available to another manager.</summary>
    public static bool IsOpen(ClubTenureControlStatus status) => OccupiesCapacity(status);

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this ClubTenureControlStatus status) => status switch
    {
        ClubTenureControlStatus.Active => ClubTenureControlStatuses.ActiveCode,
        ClubTenureControlStatus.Inactive => ClubTenureControlStatuses.InactiveCode,
        ClubTenureControlStatus.Closed => ClubTenureControlStatuses.ClosedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown tenure status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static ClubTenureControlStatus FromCode(string code) => code switch
    {
        ClubTenureControlStatuses.ActiveCode => ClubTenureControlStatus.Active,
        ClubTenureControlStatuses.InactiveCode => ClubTenureControlStatus.Inactive,
        ClubTenureControlStatuses.ClosedCode => ClubTenureControlStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown tenure status code."),
    };
}

/// <summary>Stable codes for <see cref="ClubTenureControlStatus"/>.</summary>
public static class ClubTenureControlStatuses
{
    /// <summary>The code for <see cref="ClubTenureControlStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="ClubTenureControlStatus.Inactive"/>.</summary>
    public const string InactiveCode = "inactive";

    /// <summary>The code for <see cref="ClubTenureControlStatus.Closed"/>.</summary>
    public const string ClosedCode = "closed";
}

/// <summary>
/// Why a tenure ended.
/// </summary>
/// <remarks>
/// Recorded rather than inferred from timestamps, because the resignation cooldown (`OCC-4`) applies
/// to exactly one of these and the support answer to "why did I lose my club?" depends on which.
/// </remarks>
public static class ClubTenureEndReasons
{
    /// <summary>The manager resigned voluntarily.</summary>
    public const string Resigned = "resigned";

    /// <summary>The tenure closed after the inactivity threshold (`OCC-3`).</summary>
    public const string InactivityClosed = "inactivity_closed";

    /// <summary>An administrator ended the tenure, for example on suspension (`OCC-6`).</summary>
    public const string AdministratorClosed = "administrator_closed";

    /// <summary>Whether a reason is one the tenure aggregate recognises.</summary>
    public static bool IsKnown(string reason) =>
        reason is Resigned or InactivityClosed or AdministratorClosed;
}
