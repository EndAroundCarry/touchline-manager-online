using System.Globalization;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// The risks a club is running, as the dashboard and the finance screen show them (`FIN-16`, `SQ-2`,
/// master plan §11.1).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the summary the finance read already holds, so the warning needs no second query and
/// cannot disagree with the numbers beside it. The three risks are the ones the plan names for a dashboard:
/// contracts about to expire, a payroll the club cannot comfortably carry, and a squad below the minimum it
/// must field.
/// </para>
/// <para>
/// The messages are English and stable-coded, the same token-plus-text contract the inbox uses, so a client
/// can render them today and localise them later from the code rather than the sentence.
/// </para>
/// </remarks>
public static class ClubWarnings
{
    /// <summary>Calculates the club's warnings, in a stable order.</summary>
    /// <param name="snapshot">The finance summary the club is described by.</param>
    public static IReadOnlyList<FinanceWarningResponse> Calculate(FinanceSummarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var warnings = new List<FinanceWarningResponse>(3);

        if (snapshot.ExpiringContracts > 0)
        {
            var noun = snapshot.ExpiringContracts == 1 ? "contract" : "contracts";
            var verb = snapshot.ExpiringContracts == 1 ? "ends" : "end";

            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.ExpiringContracts,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{snapshot.ExpiringContracts} {noun} {verb} at the close of this season (CON-6).")));
        }

        if (ClubSolvencyGuard.IsPayrollAtRisk(
            snapshot.CashMinor - snapshot.ReservedMinor,
            snapshot.WeeklyWageMinor))
        {
            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.PayrollRisk,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The club holds fewer than {WorldRuleSet.PayrollRiskWeeks} weeks of wages in available funds (FIN-16).")));
        }

        if (snapshot.ContractedPlayers < WorldRuleSet.SquadMinimumRegistered
            || snapshot.Goalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.MinimumSquad,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The squad holds {snapshot.ContractedPlayers} players and {snapshot.Goalkeepers} goalkeepers; a legal side needs at least {WorldRuleSet.SquadMinimumRegistered} and {WorldRuleSet.MinimumGoalkeepers} (SQ-2).")));
        }

        return warnings;
    }
}
