using FluentAssertions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Finance;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The risks the dashboard warns about (`FIN-16`, `SQ-2`, master plan §11.1).
/// </summary>
public sealed class ClubWarningsTests
{
    [Fact]
    public void A_healthy_club_has_nothing_to_warn_about()
    {
        var warnings = ClubWarnings.Calculate(Snapshot());

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Contracts_ending_this_season_are_reported()
    {
        var warnings = ClubWarnings.Calculate(Snapshot(expiringContracts: 4));

        warnings.Should().ContainSingle()
            .Which.Code.Should().Be(FinanceWarningCodes.ExpiringContracts);
    }

    [Fact]
    public void A_payroll_the_club_cannot_carry_is_reported()
    {
        // Less than the risk horizon of wages in available funds.
        var warnings = ClubWarnings.Calculate(Snapshot(
            cashMinor: (1_000_000 * WorldRuleSet.PayrollRiskWeeks) - 1,
            weeklyWageMinor: 1_000_000));

        warnings.Should().ContainSingle()
            .Which.Code.Should().Be(FinanceWarningCodes.PayrollRisk);
    }

    [Fact]
    public void A_squad_below_the_minimum_is_reported()
    {
        var tooFew = ClubWarnings.Calculate(Snapshot(contractedPlayers: WorldRuleSet.SquadMinimumRegistered - 1));
        var tooFewKeepers = ClubWarnings.Calculate(Snapshot(goalkeepers: WorldRuleSet.MinimumGoalkeepers - 1));

        tooFew.Should().ContainSingle().Which.Code.Should().Be(FinanceWarningCodes.MinimumSquad);
        tooFewKeepers.Should().ContainSingle().Which.Code.Should().Be(FinanceWarningCodes.MinimumSquad);
    }

    private static FinanceSummarySnapshot Snapshot(
        long cashMinor = 50_000_000,
        long reservedMinor = 0,
        long weeklyWageMinor = 1_000_000,
        int contractedPlayers = WorldRuleSet.GeneratorSquadTarget,
        int goalkeepers = WorldRuleSet.GeneratedGoalkeepers,
        int expiringContracts = 0) =>
        new(
            cashMinor,
            reservedMinor,
            weeklyWageMinor,
            contractedPlayers,
            TierNumber: 1,
            SeasonLabel: "2026/27",
            goalkeepers,
            expiringContracts,
            Totals: []);
}
