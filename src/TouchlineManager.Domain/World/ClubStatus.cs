namespace TouchlineManager.Domain.World;

/// <summary>
/// The lifecycle state of a club (`WORLD-6`).
/// </summary>
/// <remarks>
/// There is deliberately no "deleted" state. Clubs persist forever; only an audited administrative
/// repair may retire one, and a retired club stays readable so historical seasons still resolve.
/// </remarks>
public enum ClubStatus
{
    /// <summary>Playing, or between seasons and able to be claimed.</summary>
    Active = 0,

    /// <summary>Retired by an audited repair. Never claimable; history preserved.</summary>
    Retired = 1,
}

/// <summary>Storage and transport representation of <see cref="ClubStatus"/>.</summary>
public static class ClubStatuses
{
    /// <summary>The code for <see cref="ClubStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="ClubStatus.Retired"/>.</summary>
    public const string RetiredCode = "retired";

    /// <summary>Converts a status to its stable code.</summary>
    public static string ToCode(this ClubStatus status) => status switch
    {
        ClubStatus.Active => ActiveCode,
        ClubStatus.Retired => RetiredCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown club status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static ClubStatus FromCode(string code) => code switch
    {
        ActiveCode => ClubStatus.Active,
        RetiredCode => ClubStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown club status code."),
    };
}
