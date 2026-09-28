namespace TouchlineManager.Contracts.Finance;

/// <summary>
/// Stable machine-readable error codes for the finance module (master plan §10).
/// </summary>
/// <remarks>
/// A client branches on the code rather than the wording. The authorization refusals the finance reads share
/// with the squad reads are the world and squad codes; the one code unique to this surface is a cursor that
/// does not decode, which is a client defect rather than a missing resource.
/// </remarks>
public static class FinanceErrorCodes
{
    /// <summary>The ledger cursor did not decode.</summary>
    public const string InvalidCursor = "INVALID_CURSOR";
}

/// <summary>
/// Stable codes for the risks the finance summary warns about (master plan §11.1; `FIN-16`, `SQ-2`).
/// </summary>
/// <remarks>
/// A client branches on the code rather than the wording, so the warning can be localised without the client
/// parsing a sentence.
/// </remarks>
public static class FinanceWarningCodes
{
    /// <summary>One or more contracts end at the end of this season.</summary>
    public const string ExpiringContracts = "EXPIRING_CONTRACTS";

    /// <summary>The club holds fewer weeks of wages than the risk horizon (`FIN-16`).</summary>
    public const string PayrollRisk = "PAYROLL_RISK";

    /// <summary>The club is below the minimum registered squad or short of goalkeepers (`SQ-2`).</summary>
    public const string MinimumSquad = "MINIMUM_SQUAD";
}
