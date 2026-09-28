using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Decides how far short a club is of covering its week, so the safety step knows what to grant
/// (`FIN-16`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of two numbers, deliberately: the amount a club has available and the amount its week
/// costs. It is the "detect" half of `FIN-16` — the grant that follows is the finance workflow's, and the
/// alert is the operator's — and keeping the decision pure is what lets it be reasoned about and tested
/// without a database, the same shape as the engine's own calculators.
/// </para>
/// <para>
/// Affordability is measured against <em>available</em> funds, not raw cash: money already reserved for a
/// leading bid is committed, and paying wages out of it would leave reserved funds above the cash behind
/// them, which <see cref="Domain.Finance.ClubAccount.Post"/> refuses (`FIN-10`, `FIN-13`).
/// </para>
/// </remarks>
public static class ClubSolvencyGuard
{
    /// <summary>
    /// The amount a club is short of covering an obligation, or zero when it can afford it (`FIN-16`).
    /// </summary>
    /// <param name="availableMinor">What the club can still commit, after existing reservations.</param>
    /// <param name="obligationMinor">The week's obligation, in minor units.</param>
    public static long Shortfall(long availableMinor, long obligationMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(obligationMinor);

        return Math.Max(0, obligationMinor - availableMinor);
    }

    /// <summary>
    /// Whether a club holds fewer than the configured weeks of wages in available funds (`FIN-16`).
    /// </summary>
    /// <remarks>
    /// The threshold the dashboards warn on, distinct from the shortfall the run grants against: a club can
    /// be a payroll risk long before it is unable to pay.
    /// </remarks>
    /// <param name="availableMinor">What the club can still commit, after existing reservations.</param>
    /// <param name="weeklyWageMinor">The club's weekly wage bill, in minor units.</param>
    public static bool IsPayrollAtRisk(long availableMinor, long weeklyWageMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(weeklyWageMinor);

        return weeklyWageMinor > 0 && availableMinor < weeklyWageMinor * WorldRuleSet.PayrollRiskWeeks;
    }
}
