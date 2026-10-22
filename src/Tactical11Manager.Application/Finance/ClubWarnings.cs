using System.Globalization;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// The risks a club is running, as the dashboard and the finance screen show them (`FIN-16`, `SQ-2`,
/// master plan §11.1).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the summary the finance read already holds, so the warning needs no second query and
/// cannot disagree with the numbers beside it. The risks are the ones the plan names for a dashboard:
/// contracts about to expire, a payroll the club cannot comfortably carry, a squad below the minimum it
/// must field, and, once the economy rules are in play, a club in the red, in administration or over its
/// wage budget (`DEBT-3`, `DEBT-4`, `BUD-2`).
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
    /// <param name="outlook">Where the club's money is heading, or null when it is not known.</param>
    public static IReadOnlyList<FinanceWarningResponse> Calculate(FinanceSummarySnapshot snapshot, FinanceOutlookResponse? outlook = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var warnings = new List<FinanceWarningResponse>(5);

        // The money warnings come first: they stop the club buying and building, which is more pressing than a contract running out.
        if (outlook is { State: "administration" })
        {
            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.Administration,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The club is in administration: the board is selling the highest earners, and the manager is dismissed in {outlook.AdministrationMatchdaysLeft} matchdays if it stays there (DEBT-5).")));
        }
        else if (outlook is { State: "in_the_red" })
        {
            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.InTheRed,
                "The club's cash is below zero, so it cannot bid for players or build until it is back above zero (DEBT-3)."));
        }
        else if (outlook is { OverBudget: true })
        {
            warnings.Add(new FinanceWarningResponse(
                FinanceWarningCodes.OverWageBudget,
                "The wage bill is over the wage budget, the share of the season's income a club can comfortably pay (BUD-2)."));
        }

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

        // A club in the red has no available funds at all, and the red warning already says so.
        if (outlook is not { Embargo: true }
            && ClubSolvencyGuard.IsPayrollAtRisk(
                Math.Max(0, snapshot.CashMinor - snapshot.ReservedMinor),
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
