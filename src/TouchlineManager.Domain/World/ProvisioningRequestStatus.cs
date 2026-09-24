namespace TouchlineManager.Domain.World;

/// <summary>
/// Where a division-provisioning request is in its lifecycle (`PYR-2`, ADR-0005).
/// </summary>
public enum ProvisioningRequestStatus
{
    /// <summary>Recorded but not yet picked up by a worker.</summary>
    Requested = 0,

    /// <summary>A worker is generating the tier.</summary>
    Running = 1,

    /// <summary>Generation, validation, and backfill all completed. The tier is claimable.</summary>
    Completed = 2,

    /// <summary>Generation failed and the tier is not claimable. Retrying is an operator decision.</summary>
    Failed = 3,
}

/// <summary>The questions asked about <see cref="ProvisioningRequestStatus"/>.</summary>
public static class ProvisioningRequestStatusRules
{
    /// <summary>Whether the request has reached a state it will not leave on its own.</summary>
    public static bool IsTerminal(ProvisioningRequestStatus status) =>
        status is ProvisioningRequestStatus.Completed or ProvisioningRequestStatus.Failed;

    /// <summary>Whether a manager may be told the requested tier is coming.</summary>
    public static bool IsPending(ProvisioningRequestStatus status) =>
        status is ProvisioningRequestStatus.Requested or ProvisioningRequestStatus.Running;

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this ProvisioningRequestStatus status) => status switch
    {
        ProvisioningRequestStatus.Requested => ProvisioningRequestStatuses.RequestedCode,
        ProvisioningRequestStatus.Running => ProvisioningRequestStatuses.RunningCode,
        ProvisioningRequestStatus.Completed => ProvisioningRequestStatuses.CompletedCode,
        ProvisioningRequestStatus.Failed => ProvisioningRequestStatuses.FailedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown provisioning status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static ProvisioningRequestStatus FromCode(string code) => code switch
    {
        ProvisioningRequestStatuses.RequestedCode => ProvisioningRequestStatus.Requested,
        ProvisioningRequestStatuses.RunningCode => ProvisioningRequestStatus.Running,
        ProvisioningRequestStatuses.CompletedCode => ProvisioningRequestStatus.Completed,
        ProvisioningRequestStatuses.FailedCode => ProvisioningRequestStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown provisioning status code."),
    };
}

/// <summary>Stable codes for <see cref="ProvisioningRequestStatus"/>.</summary>
public static class ProvisioningRequestStatuses
{
    /// <summary>The code for <see cref="ProvisioningRequestStatus.Requested"/>.</summary>
    public const string RequestedCode = "requested";

    /// <summary>The code for <see cref="ProvisioningRequestStatus.Running"/>.</summary>
    public const string RunningCode = "running";

    /// <summary>The code for <see cref="ProvisioningRequestStatus.Completed"/>.</summary>
    public const string CompletedCode = "completed";

    /// <summary>The code for <see cref="ProvisioningRequestStatus.Failed"/>.</summary>
    public const string FailedCode = "failed";
}
