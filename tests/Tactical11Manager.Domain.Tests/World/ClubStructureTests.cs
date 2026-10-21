using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// A club's snack bar and merchandise shop: what they open with and how an upgrade runs (`STRC-1`,
/// `STRC-3`), and the takings history rows (`STRC-4`, `STRC-6`, `STRC-8`).
/// </summary>
public sealed class ClubStructureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static ClubStructure Opened(StructureKind kind = StructureKind.SnackBar) =>
        ClubStructure.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), kind, Now);

    [Theory]
    [InlineData(StructureKind.SnackBar)]
    [InlineData(StructureKind.Merchandise)]
    public void Every_building_opens_at_level_one_with_no_work_under_way(StructureKind kind)
    {
        var structure = Opened(kind);

        structure.Kind.Should().Be(kind);
        structure.Level.Should().Be(1);
        structure.IsBuilding.Should().BeFalse();
        structure.UpgradeTargetLevel.Should().BeNull();
        structure.UpgradeDueAt.Should().BeNull();
        structure.IsAtMaxLevel.Should().BeFalse();
        structure.Version.Should().Be(1);
        structure.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Starting_an_upgrade_names_the_next_level_and_leaves_the_old_one_trading()
    {
        var structure = Opened();
        var dueAt = Now.AddDays(1);

        var target = structure.StartUpgrade(dueAt, Now);

        target.Should().Be(2);
        structure.Level.Should().Be(1, "the building keeps trading at its old level until the work finishes (STRC-3)");
        structure.IsBuilding.Should().BeTrue();
        structure.UpgradeTargetLevel.Should().Be(2);
        structure.UpgradeDueAt.Should().Be(dueAt);
        structure.Version.Should().Be(2);
    }

    [Fact]
    public void Completing_the_upgrade_raises_the_level_and_clears_the_work()
    {
        var structure = Opened();
        structure.StartUpgrade(Now.AddDays(1), Now);

        var changed = structure.CompleteUpgrade(Now.AddDays(1));

        changed.Should().BeTrue();
        structure.Level.Should().Be(2);
        structure.IsBuilding.Should().BeFalse();
        structure.UpgradeDueAt.Should().BeNull();
        structure.UpdatedAt.Should().Be(Now.AddDays(1));
        structure.Version.Should().Be(3);
    }

    [Fact]
    public void Completing_twice_does_nothing_the_second_time_so_a_retried_job_is_harmless()
    {
        var structure = Opened();
        structure.StartUpgrade(Now.AddDays(1), Now);
        structure.CompleteUpgrade(Now.AddDays(1));
        var version = structure.Version;

        var changed = structure.CompleteUpgrade(Now.AddDays(2));

        changed.Should().BeFalse();
        structure.Level.Should().Be(2);
        structure.Version.Should().Be(version, "nothing changed, so nothing is versioned");
    }

    [Fact]
    public void Completing_a_building_that_was_never_upgraded_does_nothing()
    {
        var structure = Opened();

        structure.CompleteUpgrade(Now).Should().BeFalse();
        structure.Level.Should().Be(1);
        structure.Version.Should().Be(1);
    }

    [Fact]
    public void Only_one_upgrade_runs_at_a_time()
    {
        var structure = Opened();
        structure.StartUpgrade(Now.AddDays(1), Now);

        var act = () => structure.StartUpgrade(Now.AddDays(2), Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*STRC-3*");
        structure.UpgradeTargetLevel.Should().Be(2, "the running work is untouched");
        structure.UpgradeDueAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void A_building_climbs_one_level_at_a_time_up_to_ten_and_no_further()
    {
        var structure = Opened();
        var now = Now;

        for (var expected = 2; expected <= StructureRuleSet.MaxLevel; expected++)
        {
            structure.StartUpgrade(now.AddDays(StructureRuleSet.BuildDaysFor(expected)), now);
            now = now.AddDays(StructureRuleSet.BuildDaysFor(expected));
            structure.CompleteUpgrade(now);

            structure.Level.Should().Be(expected);
        }

        structure.IsAtMaxLevel.Should().BeTrue();

        var act = () => structure.StartUpgrade(now.AddDays(1), now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*STRC-1*");
        structure.Level.Should().Be(10);
    }

    [Fact]
    public void An_upgrade_that_finishes_before_it_starts_is_a_programming_error()
    {
        var structure = Opened();

        var act = () => structure.StartUpgrade(Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
        structure.IsBuilding.Should().BeFalse();
    }

    [Theory]
    [InlineData(StructureKind.SnackBar, "snack_bar")]
    [InlineData(StructureKind.Merchandise, "merchandise")]
    public void A_kind_round_trips_through_its_stable_code(StructureKind kind, string code)
    {
        kind.ToCode().Should().Be(code);
        StructureKinds.FromCode(code).Should().Be(kind);
        StructureKinds.TryFromCode(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SnackBar")]
    [InlineData("stadium")]
    public void An_unknown_kind_code_is_refused(string? code)
    {
        StructureKinds.TryFromCode(code, out _).Should().BeFalse();

        var act = () => StructureKinds.FromCode(code!);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_match_sale_row_keeps_the_crowd_the_base_the_bonus_and_the_reason()
    {
        var fixtureId = Guid.CreateVersion7();
        var entryId = Guid.CreateVersion7();

        var row = ClubStructureTakings.MatchSale(
            Guid.CreateVersion7(), Guid.CreateVersion7(), fixtureId, new DateOnly(2026, 10, 19), level: 1, spectators: 2_890,
            baseMinor: 289_000, bonusBp: 3_000, bonusReason: "underdog_win", amountMinor: 375_700, entryId, Now);

        row.Kind.Should().Be(StructureKind.SnackBar);
        row.RowKind.Should().Be(StructureTakingsKind.MatchSale);
        row.FixtureId.Should().Be(fixtureId);
        row.Customers.Should().Be(2_890);
        row.BaseMinor.Should().Be(289_000);
        row.BonusBp.Should().Be(3_000);
        row.BonusReason.Should().Be("underdog_win");
        row.AmountMinor.Should().Be(375_700);
        row.LedgerEntryId.Should().Be(entryId);
        row.WeekOf.Should().Be(new DateOnly(2026, 10, 19));
    }

    [Fact]
    public void A_week_sale_row_belongs_to_the_shop_and_no_fixture()
    {
        var row = ClubStructureTakings.WeekSale(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 10, 19), level: 2, fans: 10_000,
            baseMinor: 400_000, bonusBp: 500, bonusReason: "calm", amountMinor: 420_000, Guid.CreateVersion7(), Now);

        row.Kind.Should().Be(StructureKind.Merchandise);
        row.RowKind.Should().Be(StructureTakingsKind.WeekSale);
        row.FixtureId.Should().BeNull();
        row.AmountMinor.Should().BePositive();
    }

    [Fact]
    public void An_upkeep_row_is_a_cost_with_no_customers_and_no_bonus()
    {
        var entryId = Guid.CreateVersion7();

        var snack = ClubStructureTakings.Upkeep(
            Guid.CreateVersion7(), Guid.CreateVersion7(), StructureKind.SnackBar, new DateOnly(2026, 10, 19), level: 3, upkeepMinor: 60_000, entryId, Now);

        snack.RowKind.Should().Be(StructureTakingsKind.Upkeep);
        snack.AmountMinor.Should().Be(-60_000);
        snack.BaseMinor.Should().Be(60_000);
        snack.Customers.Should().Be(0);
        snack.BonusBp.Should().Be(0);
        snack.BonusReason.Should().Be(ClubStructureTakings.NoBonusReason);
        snack.LedgerEntryId.Should().Be(entryId, "both buildings' upkeep rows point at the one combined entry");
    }

    [Theory]
    [InlineData(StructureTakingsKind.MatchSale, "match_sale")]
    [InlineData(StructureTakingsKind.WeekSale, "week_sale")]
    [InlineData(StructureTakingsKind.Upkeep, "upkeep")]
    public void A_row_kind_round_trips_through_its_stable_code(StructureTakingsKind kind, string code)
    {
        kind.ToCode().Should().Be(code);
        StructureTakingsKinds.FromCode(code).Should().Be(kind);
    }

    [Fact]
    public void A_row_with_a_negative_base_or_customers_is_a_programming_error()
    {
        var negativeBase = () => ClubStructureTakings.WeekSale(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 10, 19), 1, 10, -1, 0, "calm", 0, Guid.CreateVersion7(), Now);
        var negativeCustomers = () => ClubStructureTakings.WeekSale(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 10, 19), 1, -1, 0, 0, "calm", 0, Guid.CreateVersion7(), Now);

        negativeBase.Should().Throw<ArgumentOutOfRangeException>();
        negativeCustomers.Should().Throw<ArgumentOutOfRangeException>();
    }
}
