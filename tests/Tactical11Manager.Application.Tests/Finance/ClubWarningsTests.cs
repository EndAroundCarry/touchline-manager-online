using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Tests.Finance;

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

    [Fact]
    public void A_club_in_the_red_is_warned_instead_of_failing_the_payroll_check()
    {
        // Cash below zero leaves nothing available; the payroll check must not throw on it.
        var warnings = ClubWarnings.Calculate(Snapshot(cashMinor: -500_000), Outlook(state: "in_the_red", embargo: true));

        warnings.Should().ContainSingle().Which.Code.Should().Be(FinanceWarningCodes.InTheRed);
    }

    [Fact]
    public void A_club_in_administration_is_told_how_long_it_has()
    {
        var warnings = ClubWarnings.Calculate(
            Snapshot(cashMinor: -9_000_000),
            Outlook(state: "administration", embargo: true, matchdaysLeft: 12));

        var warning = warnings.Should().ContainSingle().Subject;

        warning.Code.Should().Be(FinanceWarningCodes.Administration);
        warning.Message.Should().Contain("12 matchdays");
    }

    [Fact]
    public void A_wage_bill_over_the_budget_is_reported_while_the_club_is_healthy()
    {
        var warnings = ClubWarnings.Calculate(Snapshot(), Outlook(overBudget: true));

        warnings.Should().ContainSingle().Which.Code.Should().Be(FinanceWarningCodes.OverWageBudget);
    }

    [Fact]
    public void A_state_warning_leads_the_list()
    {
        var warnings = ClubWarnings.Calculate(Snapshot(expiringContracts: 3), Outlook(state: "in_the_red", embargo: true));

        warnings.Select(warning => warning.Code).Should().Equal(FinanceWarningCodes.InTheRed, FinanceWarningCodes.ExpiringContracts);
    }

    private static FinanceOutlookResponse Outlook(
        string state = "healthy",
        bool embargo = false,
        int? matchdaysLeft = null,
        bool overBudget = false) =>
        new(state, embargo, matchdaysLeft, 10, 34, 0, 0, 0, 0, 0, overBudget, [], null);

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
