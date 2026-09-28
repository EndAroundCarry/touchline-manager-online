using FluentAssertions;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The pure affordability decision behind the weekly run's grant and the dashboards' payroll warning
/// (`FIN-16`).
/// </summary>
public sealed class ClubSolvencyGuardTests
{
    [Fact]
    public void A_club_that_can_pay_is_not_short()
    {
        ClubSolvencyGuard.Shortfall(availableMinor: 5_000_000, obligationMinor: 3_000_000).Should().Be(0);
        ClubSolvencyGuard.Shortfall(availableMinor: 3_000_000, obligationMinor: 3_000_000).Should()
            .Be(0, "exactly enough is enough");
    }

    [Fact]
    public void The_shortfall_is_what_the_week_costs_beyond_what_the_club_can_commit()
    {
        // Affordability is measured against available funds, not raw cash: reserved money is already promised
        // to a leading bid (FIN-10).
        ClubSolvencyGuard.Shortfall(availableMinor: 1_000_000, obligationMinor: 4_000_000).Should().Be(3_000_000);
    }

    [Fact]
    public void The_risk_horizon_is_the_configured_weeks_of_wages()
    {
        const long wages = 1_000_000;

        ClubSolvencyGuard.IsPayrollAtRisk(
            availableMinor: (wages * WorldRuleSet.PayrollRiskWeeks) - 1,
            weeklyWageMinor: wages).Should().BeTrue();

        ClubSolvencyGuard.IsPayrollAtRisk(
            availableMinor: wages * WorldRuleSet.PayrollRiskWeeks,
            weeklyWageMinor: wages).Should().BeFalse();

        ClubSolvencyGuard.IsPayrollAtRisk(availableMinor: 0, weeklyWageMinor: 0).Should()
            .BeFalse("a club with no wages is not a payroll risk");
    }

    [Fact]
    public void A_negative_input_is_a_programming_error()
    {
        var available = () => ClubSolvencyGuard.Shortfall(availableMinor: -1, obligationMinor: 1_000);
        var obligation = () => ClubSolvencyGuard.Shortfall(availableMinor: 1_000, obligationMinor: -1);

        available.Should().Throw<ArgumentOutOfRangeException>();
        obligation.Should().Throw<ArgumentOutOfRangeException>();
    }
}
