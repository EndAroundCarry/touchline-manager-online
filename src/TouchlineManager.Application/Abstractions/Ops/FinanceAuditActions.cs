namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// Recorded audit actions for the finance module.
/// </summary>
/// <remarks>
/// Money that moved without a decision a manager made — a grant, a compensating repair — has to be
/// answerable when a balance is questioned (`FIN-12`, `FIN-16`, master plan §12.3), so every such write
/// records who or what caused it and why.
/// </remarks>
public static class FinanceAuditActions
{
    /// <summary>The safety step granted a club emergency funds to cover its wage run (`FIN-16`).</summary>
    public const string EmergencyGrant = "finance.emergency_grant.granted";

    /// <summary>An operator posted a compensating entry that corrected an earlier one (`FIN-12`).</summary>
    public const string CompensatingEntry = "finance.compensating_entry.posted";
}
