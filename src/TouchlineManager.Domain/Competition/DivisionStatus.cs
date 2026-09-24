namespace TouchlineManager.Domain.Competition;

/// <summary>The lifecycle state of a division (master plan §6.4, `PYR-8`).</summary>
public enum DivisionStatus
{
    /// <summary>Being generated. Not claimable: it has no valid squads or table yet (`PYR-8`).</summary>
    Provisioning = 0,

    /// <summary>Complete and playable. Claimable, and part of the promotion/relegation chain.</summary>
    Active = 1,

    /// <summary>Retired by an audited repair. History preserved, never claimable.</summary>
    Retired = 2,
}

/// <summary>The questions asked about <see cref="DivisionStatus"/>.</summary>
public static class DivisionStatusRules
{
    /// <summary>Whether a manager may take over a club in a division in this state (`PYR-8`).</summary>
    public static bool IsClaimable(DivisionStatus status) => status == DivisionStatus.Active;

    /// <summary>Whether the division takes part in promotion and relegation (`PYR-13`).</summary>
    public static bool ParticipatesInMovement(DivisionStatus status) => status == DivisionStatus.Active;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this DivisionStatus status) => status switch
    {
        DivisionStatus.Provisioning => DivisionStatuses.ProvisioningCode,
        DivisionStatus.Active => DivisionStatuses.ActiveCode,
        DivisionStatus.Retired => DivisionStatuses.RetiredCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown division status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static DivisionStatus FromCode(string code) => code switch
    {
        DivisionStatuses.ProvisioningCode => DivisionStatus.Provisioning,
        DivisionStatuses.ActiveCode => DivisionStatus.Active,
        DivisionStatuses.RetiredCode => DivisionStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown division status code."),
    };
}

/// <summary>Stable codes for <see cref="DivisionStatus"/>.</summary>
public static class DivisionStatuses
{
    /// <summary>The code for <see cref="DivisionStatus.Provisioning"/>.</summary>
    public const string ProvisioningCode = "provisioning";

    /// <summary>The code for <see cref="DivisionStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="DivisionStatus.Retired"/>.</summary>
    public const string RetiredCode = "retired";
}
