using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>
/// The small pieces the matchday money and a club in administration rest on: counting matchdays across seasons, the relegation clause on a
/// contract, the board's listing, and the dismissal that ends a tenure (`DEBT-2`, `DEBT-4`, `DEBT-5`, `FIN-5`).
/// </summary>
public sealed class AdministrationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 34, 34)]
    [InlineData(2, 1, 35)]
    [InlineData(3, 17, 85)]
    public void A_matchday_ordinal_counts_the_rounds_of_the_seasons_before_it(int season, int round, int expected)
    {
        MatchdayOrdinal.Of(season, round).Should().Be(expected);
    }

    [Fact]
    public void An_administration_that_spans_two_seasons_is_still_counted_in_matchdays()
    {
        var entered = MatchdayOrdinal.Of(1, 22);
        var now = MatchdayOrdinal.Of(2, 22);

        (now - entered).Should().Be(EconomyRuleSet.AdministrationDismissalMatchdays).And.Be(34);

        ClubFinancialState.Next(FinancialStateKind.Administration, entered, now - 1, -1_000, 100).Dismiss
            .Should().BeFalse("a manager who entered administration at matchday 22 has until matchday 22 of the next season");
        ClubFinancialState.Next(FinancialStateKind.Administration, entered, now, -1_000, 100).Dismiss
            .Should().BeTrue("34 matchdays have passed, across the end of the season");
    }

    [Fact]
    public void The_relegation_clause_lowers_a_contracts_wage_and_never_raises_it()
    {
        var contract = Contract(wage: 10_000_000);

        contract.ReduceSeasonWage(EconomyRuleSet.AfterRelegationClause(contract.SeasonWageMinor), Now);

        contract.SeasonWageMinor.Should().Be(6_000_000, "the clause keeps 60%");
        contract.Version.Should().Be(2);

        var raise = () => contract.ReduceSeasonWage(7_000_000, Now);

        raise.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_wage_that_does_not_change_does_not_move_the_version()
    {
        var contract = Contract(wage: EconomyRuleSet.SeasonWageFloorMinor);

        contract.ReduceSeasonWage(EconomyRuleSet.AfterRelegationClause(contract.SeasonWageMinor), Now);

        contract.SeasonWageMinor.Should().Be(EconomyRuleSet.SeasonWageFloorMinor, "a wage at the floor stays there");
        contract.Version.Should().Be(1);
    }

    [Fact]
    public void A_closed_contracts_wage_cannot_change()
    {
        var contract = Contract(wage: 1_000_000);

        contract.Close(PlayerContractCloseReasons.Released, Now);

        var act = () => contract.ReduceSeasonWage(500_000, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_listing_is_the_clubs_own_until_the_board_marks_it()
    {
        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1_000_000,
            500_000,
            2,
            Now,
            Now.AddDays(1),
            idempotencyKey: null,
            Now);

        listing.ListedByBoard.Should().BeFalse();

        listing.MarkListedByBoard();

        listing.ListedByBoard.Should().BeTrue();
        listing.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void A_dismissal_is_a_reason_a_tenure_can_end_for()
    {
        ClubTenureEndReasons.IsKnown(ClubTenureEndReasons.Dismissed).Should().BeTrue();
        ClubTenureEndReasons.Dismissed.Should().Be("dismissed");
    }

    private static PlayerContract Contract(long wage) =>
        PlayerContract.Sign(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startSeasonNumber: 1,
            endSeasonNumber: 3,
            wage,
            SquadStatus.Rotation,
            Now);
}
