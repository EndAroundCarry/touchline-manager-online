using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// What a clean start changes in the club's own aggregates: the ground, the buildings and the account go back to the tier's opening state
/// (`TKO-3`).
/// </summary>
public sealed class CleanStartDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("keep", TakeoverStart.Keep)]
    [InlineData("clean_start", TakeoverStart.CleanStart)]
    public void A_start_round_trips_through_its_code(string code, TakeoverStart start)
    {
        TakeoverStarts.FromCode(code).Should().Be(start);
        start.ToCode().Should().Be(code);
        TakeoverStarts.TryFromCode(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(start);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Keep")]
    [InlineData("fresh")]
    public void A_code_that_is_not_a_start_is_not_parsed(string? code)
    {
        TakeoverStarts.TryFromCode(code, out _).Should().BeFalse();

        var act = () => TakeoverStarts.FromCode(code!);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_start_has_a_code_and_the_two_are_offered_keep_first() =>
        TakeoverStarts.All.Should().Equal(TakeoverStart.Keep, TakeoverStart.CleanStart);

    [Fact]
    public void A_grown_ground_goes_back_to_the_places_every_club_opens_with()
    {
        var stadium = ClubStadium.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        stadium.AddSeats(StadiumStand.Standing, 20_000, Now);
        stadium.AddSeats(StadiumStand.Vip, 3_000, Now);
        stadium.Level.Should().BeGreaterThan(1);

        var versionBefore = stadium.Version;

        stadium.ResetToOpening(Now.AddDays(1));

        stadium.Seats.Should().Be(EconomyRuleSet.OpeningSeats);
        stadium.Capacity.Should().Be(5_000);
        stadium.Level.Should().Be(1);
        stadium.Version.Should().Be(versionBefore + 1);
        stadium.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Theory]
    [InlineData(StructureKind.SnackBar)]
    [InlineData(StructureKind.Merchandise)]
    public void A_building_goes_back_to_level_one_and_drops_the_upgrade_under_way(StructureKind kind)
    {
        var building = ClubStructure.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), kind, Now);

        building.StartUpgrade(Now.AddDays(2), Now);
        building.CompleteUpgrade(Now.AddDays(2));
        building.StartUpgrade(Now.AddDays(5), Now.AddDays(3));
        building.Level.Should().Be(2);
        building.IsBuilding.Should().BeTrue();

        building.ResetToOpening(Now.AddDays(4));

        building.Level.Should().Be(StructureRuleSet.OpeningLevel);
        building.IsBuilding.Should().BeFalse();
        building.UpgradeTargetLevel.Should().BeNull();
        building.UpgradeDueAt.Should().BeNull();
        building.CompleteUpgrade(Now.AddDays(5)).Should().BeFalse("the job that would have finished the work finds nothing to finish");
    }

    [Fact]
    public void An_account_in_administration_is_healthy_again_once_its_cash_is_set_and_its_bids_are_released()
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000), Now);
        account.SetOverdraft(1_000, Now);
        account.Post(Posting(LedgerCategory.Wages, -2_800), Now);
        account.RecordState(ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 10, account.CashMinor, account.OverdraftLimitMinor), Now);

        account.CashMinor.Should().Be(-1_800);
        account.FinancialState.Should().Be(FinancialStateKind.Administration);

        account.Post(Posting(LedgerCategory.CleanStart, 1_800 + 4_000), Now);
        account.RestoreHealth(Now);

        account.CashMinor.Should().Be(4_000);
        account.FinancialState.Should().Be(FinancialStateKind.Healthy);
        account.StateSinceMatchday.Should().Be(0);
    }

    [Fact]
    public void An_account_still_in_debt_cannot_be_restored_to_health()
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000), Now);
        account.SetOverdraft(1_000, Now);
        account.Post(Posting(LedgerCategory.Wages, -1_500), Now);

        var act = () => account.RestoreHealth(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_account_with_a_bid_still_reserved_cannot_be_restored_to_health()
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000), Now);
        account.Post(
            new LedgerPosting(
                Guid.CreateVersion7(),
                LedgerCategory.BidReservation,
                0,
                400,
                LedgerSourceType.Transfer,
                SourceId: null,
                Guid.CreateVersion7().ToString("N"),
                "test.ledger",
                "{}"),
            Now);

        var act = () => account.RestoreHealth(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    private static LedgerPosting Posting(LedgerCategory category, long cashDelta) =>
        new(
            Guid.CreateVersion7(),
            category,
            cashDelta,
            0,
            LedgerSourceType.WeeklyRun,
            SourceId: null,
            Guid.CreateVersion7().ToString("N"),
            "test.ledger",
            "{}");
}
