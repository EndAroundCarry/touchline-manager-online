namespace Tactical11Manager.Application.Abstractions.Ops;

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

    /// <summary>A club's money moved between healthy, in the red and administration (`DEBT-2`).</summary>
    public const string StateChanged = "finance.state.changed";

    /// <summary>The board of a club in administration listed a player (`DEBT-4`).</summary>
    public const string BoardListedPlayer = "finance.administration.board_listed";

    /// <summary>The board of a club in administration released a player nobody bought (`DEBT-4`).</summary>
    public const string BoardReleasedPlayer = "finance.administration.board_released";

    /// <summary>A club's manager was dismissed after the club stayed in administration (`DEBT-5`).</summary>
    public const string ManagerDismissed = "finance.administration.manager_dismissed";

    /// <summary>A leading bid was withdrawn so the club could pay its wages (`FIN-10`).</summary>
    public const string BidWithdrawn = "finance.bid.withdrawn";

    /// <summary>An operator posted a compensating entry that corrected an earlier one (`FIN-12`).</summary>
    public const string CompensatingEntry = "finance.compensating_entry.posted";
}
