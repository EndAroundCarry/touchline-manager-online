using System.Text.Json;
using FluentAssertions;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// The postings the game builds, so the opening balance carries its reason and its correlation key rather
/// than a bare amount (`FIN-1`, `FIN-17`).
/// </summary>
public sealed class LedgerPostingsTests
{
    [Fact]
    public void The_opening_posting_funds_the_cash_and_correlates_on_the_club()
    {
        var clubId = Guid.CreateVersion7();
        var entryId = Guid.CreateVersion7();

        var posting = LedgerPostings.OpeningBalance(entryId, clubId, 1_000);

        posting.EntryId.Should().Be(entryId);
        posting.Category.Should().Be(LedgerCategory.OpeningBalance);
        posting.CashDeltaMinor.Should().Be(1_000);
        posting.ReservedDeltaMinor.Should().Be(0, "an opening balance reserves nothing");
        posting.SourceType.Should().Be(LedgerSourceType.WorldSeed);
        posting.SourceId.Should().Be(clubId);
        posting.CorrelationId.Should().Be(
            clubId.ToString("D"),
            "a club is funded once, so its own identity is the key a retry collides on (FIN-17)");
    }

    [Fact]
    public void The_description_carries_the_amount_it_funded()
    {
        var posting = LedgerPostings.OpeningBalance(Guid.CreateVersion7(), Guid.CreateVersion7(), 2_500);

        posting.DescriptionTemplate.Should().Be(LedgerPostings.OpeningBalanceTemplate);

        using var document = JsonDocument.Parse(posting.DescriptionParametersJson);

        document.RootElement.GetProperty("amountMinor").GetInt64().Should().Be(2_500);
    }

    [Fact]
    public void A_negative_opening_balance_is_refused()
    {
        var act = () => LedgerPostings.OpeningBalance(Guid.CreateVersion7(), Guid.CreateVersion7(), -1);

        act.Should().Throw<ArgumentOutOfRangeException>("a club is never funded with a debt (FIN-14)");
    }

    [Fact]
    public void The_gate_receipt_credits_the_host_and_keys_on_the_fixture()
    {
        var clubId = Guid.CreateVersion7();
        var fixtureId = Guid.CreateVersion7();

        var posting = LedgerPostings.GateReceipt(Guid.CreateVersion7(), clubId, fixtureId, 4_000);

        posting.Category.Should().Be(LedgerCategory.GateReceipt);
        posting.CashDeltaMinor.Should().Be(4_000, "the gate is a credit (FIN-3)");
        posting.ReservedDeltaMinor.Should().Be(0);
        posting.SourceType.Should().Be(LedgerSourceType.Matchday);
        posting.SourceId.Should().Be(fixtureId);
        posting.CorrelationId.Should().Be(
            $"matchday:{fixtureId:D}",
            "a fixture draws its gate once, so a retried publication collides here (FIN-17)");
        posting.DescriptionTemplate.Should().Be(LedgerPostings.GateReceiptTemplate);
    }

    [Fact]
    public void The_weekly_postings_share_one_correlation_key_per_club_and_week()
    {
        var clubId = Guid.CreateVersion7();
        var weekOf = new DateOnly(2026, 9, 27);

        var sponsorship = LedgerPostings.WeeklySponsorship(Guid.CreateVersion7(), clubId, weekOf, 1, 3_000_000);
        var wages = LedgerPostings.Wages(Guid.CreateVersion7(), clubId, weekOf, 22, 1_200_000);
        var cost = LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, weekOf, 1_000_000);
        var grant = LedgerPostings.EmergencyGrant(Guid.CreateVersion7(), clubId, weekOf, 500_000);

        sponsorship.CorrelationId.Should().Be($"weekly-run:2026-09-27:{clubId:D}");
        wages.CorrelationId.Should().Be(sponsorship.CorrelationId);
        cost.CorrelationId.Should().Be(sponsorship.CorrelationId);
        grant.CorrelationId.Should().Be(sponsorship.CorrelationId);

        // One key, four categories: that is the uniqueness the ledger enforces, so one retried week posts
        // each line once rather than four times (FIN-17).
        new[] { sponsorship, wages, cost, grant }.Select(posting => posting.Category)
            .Should()
            .OnlyHaveUniqueItems();

        sponsorship.CashDeltaMinor.Should().Be(3_000_000, "sponsorship is a credit (FIN-4)");
        wages.CashDeltaMinor.Should().Be(-1_200_000, "wages are a debit (FIN-7)");
        cost.CashDeltaMinor.Should().Be(-1_000_000, "the operating cost is a debit (FIN-9)");
        grant.CashDeltaMinor.Should().Be(500_000, "the grant is a credit (FIN-16)");
        grant.SourceType.Should().Be(LedgerSourceType.SafetyJob);
    }

    [Fact]
    public void The_position_award_names_the_season_the_tier_and_the_rank()
    {
        var seasonId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();

        var posting = LedgerPostings.PositionAward(Guid.CreateVersion7(), clubId, seasonId, tier: 1, rank: 3, 10_000_000);

        posting.Category.Should().Be(LedgerCategory.PositionAward);
        posting.SourceType.Should().Be(LedgerSourceType.SeasonRollover);
        posting.SourceId.Should().Be(seasonId);
        posting.CorrelationId.Should().Be($"rollover:{seasonId:N}:{clubId:N}");

        using var document = JsonDocument.Parse(posting.DescriptionParametersJson);

        document.RootElement.GetProperty("rank").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("tier").GetInt32().Should().Be(1);
    }

    [Fact]
    public void The_wage_posting_records_how_many_players_it_covered()
    {
        var posting = LedgerPostings.Wages(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateOnly(2026, 9, 27),
            playerCount: 22,
            amountMinor: 1_200_000);

        using var document = JsonDocument.Parse(posting.DescriptionParametersJson);

        document.RootElement.GetProperty("playerCount").GetInt32().Should().Be(22);
        document.RootElement.GetProperty("amountMinor").GetInt64().Should().Be(1_200_000);
    }

    [Fact]
    public void A_compensating_entry_must_move_something_and_carries_the_operator_key()
    {
        var movesNothing = () => LedgerPostings.Compensation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            cashDeltaMinor: 0,
            "repair-1");

        movesNothing.Should().Throw<ArgumentOutOfRangeException>("an entry that moves nothing is refused (FIN-11)");

        var posting = LedgerPostings.Compensation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            cashDeltaMinor: -250,
            "repair-1");

        posting.Category.Should().Be(LedgerCategory.Compensation);
        posting.SourceType.Should().Be(LedgerSourceType.AdminRepair);
        posting.CorrelationId.Should().Be("repair-1", "the operator's key makes the repair idempotent (FIN-12)");
        posting.CashDeltaMinor.Should().Be(-250);
        posting.ReversesEntryId.Should().BeNull("the operator named no entry to correct");
    }

    [Fact]
    public void A_compensating_entry_can_name_the_entry_it_corrects()
    {
        var corrected = Guid.CreateVersion7();

        var posting = LedgerPostings.Compensation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            cashDeltaMinor: 1_000,
            "repair-2",
            corrected);

        posting.ReversesEntryId.Should().Be(corrected, "the correction points at the line it corrects (FIN-12)");
        posting.Category.Should().Be(LedgerCategory.Compensation);
    }

    [Fact]
    public void A_non_positive_income_posting_is_refused()
    {
        var act = () => LedgerPostings.WeeklySponsorship(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateOnly(2026, 9, 27),
            tier: 1,
            amountMinor: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Stadium_works_debit_the_cash_and_key_on_the_ground_at_the_version_they_produced()
    {
        var clubId = Guid.CreateVersion7();
        var stadiumId = Guid.CreateVersion7();

        var posting = LedgerPostings.StadiumConstruction(
            Guid.CreateVersion7(), clubId, stadiumId, stadiumVersion: 4, StadiumStand.CoveredSeating, 250, 25_000_000);

        posting.Category.Should().Be(LedgerCategory.StadiumConstruction);
        posting.SourceType.Should().Be(LedgerSourceType.Stadium);
        posting.SourceId.Should().Be(stadiumId);
        posting.CashDeltaMinor.Should().Be(-25_000_000, "building is a spend");
        posting.ReservedDeltaMinor.Should().Be(0);
        posting.CorrelationId.Should().Be(
            $"stadium:{stadiumId:D}:v4",
            "a retried order collides with its first entry, a genuine second order does not (FIN-17)");
    }

    [Theory]
    [InlineData(StadiumStand.Standing, 1, "Stadium works: 1 standing place")]
    [InlineData(StadiumStand.Standing, 1_500, "Stadium works: 1,500 standing places")]
    [InlineData(StadiumStand.Seating, 40, "Stadium works: 40 seats")]
    [InlineData(StadiumStand.CoveredSeating, 1, "Stadium works: 1 covered seat")]
    [InlineData(StadiumStand.Vip, 12, "Stadium works: 12 VIP seats")]
    public void A_stadium_works_entry_reads_as_the_places_it_bought(StadiumStand stand, int count, string expected)
    {
        var posting = LedgerPostings.StadiumConstruction(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, stand, count, 1_000);

        LedgerEntryText.Render(posting.DescriptionTemplate, posting.DescriptionParametersJson)
            .Should().Be(expected);
    }

    [Fact]
    public void An_order_that_costs_nothing_or_adds_nothing_is_refused()
    {
        var free = () => LedgerPostings.StadiumConstruction(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, StadiumStand.Standing, 5, 0);
        var empty = () => LedgerPostings.StadiumConstruction(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, StadiumStand.Standing, 0, 100);

        free.Should().Throw<ArgumentOutOfRangeException>();
        empty.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_release_payout_debits_the_cash_and_keys_on_the_contract_it_closed()
    {
        var contractId = Guid.CreateVersion7();

        var posting = LedgerPostings.ReleasePayout(
            Guid.CreateVersion7(), Guid.CreateVersion7(), contractId, remainingWeeks: 30, amountMinor: 450_000);

        posting.Category.Should().Be(LedgerCategory.ReleasePayout);
        posting.SourceType.Should().Be(LedgerSourceType.Contract);
        posting.SourceId.Should().Be(contractId);
        posting.CashDeltaMinor.Should().Be(-450_000, "a release is a spend");
        posting.ReservedDeltaMinor.Should().Be(0);
        posting.CorrelationId.Should().Be(
            $"release:{contractId:D}",
            "a contract closes once, so a retried release collides with its first entry (FIN-17)");
        LedgerEntryText.Render(posting.DescriptionTemplate, posting.DescriptionParametersJson)
            .Should().Be("Player release payout");
    }

    [Fact]
    public void A_release_that_costs_nothing_is_refused_by_the_posting()
    {
        var free = () => LedgerPostings.ReleasePayout(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 10, 0);

        free.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_release_category_and_source_round_trip_through_their_stable_codes()
    {
        LedgerCategory.ReleasePayout.ToCode().Should().Be("release_payout");
        LedgerCategories.FromCode("release_payout").Should().Be(LedgerCategory.ReleasePayout);
        LedgerSourceType.Contract.ToCode().Should().Be("contract");
        LedgerSourceTypes.FromCode("contract").Should().Be(LedgerSourceType.Contract);

        LedgerSourceTypes.All.Select(source => source.ToCode().Length).Max()
            .Should().BeLessThanOrEqualTo(LedgerSourceTypes.MaxCodeLength, "the column holds every code");
    }

    [Fact]
    public void Snack_bar_takings_credit_the_host_and_share_the_gates_key_under_their_own_category()
    {
        var clubId = Guid.CreateVersion7();
        var fixtureId = Guid.CreateVersion7();

        var gate = LedgerPostings.GateReceipt(Guid.CreateVersion7(), clubId, fixtureId, 4_000);
        var snacks = LedgerPostings.SnackBarTakings(
            Guid.CreateVersion7(), clubId, fixtureId, level: 3, spectators: 2_890, bonusBp: 3_000, amountMinor: 5_000);

        snacks.Category.Should().Be(LedgerCategory.SnackBar);
        snacks.SourceType.Should().Be(LedgerSourceType.Matchday);
        snacks.SourceId.Should().Be(fixtureId);
        snacks.CashDeltaMinor.Should().Be(5_000);
        snacks.ReservedDeltaMinor.Should().Be(0);
        snacks.CorrelationId.Should().Be(gate.CorrelationId, "one publication, one key, so a retry collides with both (FIN-17)");
        LedgerEntryText.Render(snacks.DescriptionTemplate, snacks.DescriptionParametersJson)
            .Should().Be("Snack bar takings");

        using var document = JsonDocument.Parse(snacks.DescriptionParametersJson);

        document.RootElement.GetProperty("spectators").GetInt64().Should().Be(2_890);
        document.RootElement.GetProperty("bonusBp").GetInt64().Should().Be(3_000);
        document.RootElement.GetProperty("level").GetInt64().Should().Be(3);
    }

    [Fact]
    public void Merchandise_takings_and_structure_upkeep_share_the_weeks_key_under_their_own_categories()
    {
        var clubId = Guid.CreateVersion7();
        var weekOf = new DateOnly(2026, 10, 18);

        var sponsorship = LedgerPostings.WeeklySponsorship(Guid.CreateVersion7(), clubId, weekOf, 1, 30_000);
        var merchandise = LedgerPostings.MerchandiseTakings(
            Guid.CreateVersion7(), clubId, weekOf, level: 2, fans: 10_000, bonusBp: 500, amountMinor: 4_200);
        var upkeep = LedgerPostings.StructureUpkeep(
            Guid.CreateVersion7(), clubId, weekOf, snackBarLevel: 1, merchandiseLevel: 2, amountMinor: 750);

        merchandise.Category.Should().Be(LedgerCategory.Merchandise);
        merchandise.SourceType.Should().Be(LedgerSourceType.WeeklyRun);
        merchandise.CashDeltaMinor.Should().Be(4_200);
        upkeep.Category.Should().Be(LedgerCategory.StructureUpkeep);
        upkeep.SourceType.Should().Be(LedgerSourceType.WeeklyRun);
        upkeep.CashDeltaMinor.Should().Be(-750, "upkeep is a cost");

        merchandise.CorrelationId.Should().Be(sponsorship.CorrelationId);
        upkeep.CorrelationId.Should().Be(sponsorship.CorrelationId, "a retried week skips the club by this key (FIN-17)");
        LedgerEntryText.Render(merchandise.DescriptionTemplate, merchandise.DescriptionParametersJson)
            .Should().Be("Merchandise takings");
        LedgerEntryText.Render(upkeep.DescriptionTemplate, upkeep.DescriptionParametersJson)
            .Should().Be("Weekly structure upkeep");
    }

    [Theory]
    [InlineData(StructureKind.SnackBar, 4, "Snack bar upgraded to level 4")]
    [InlineData(StructureKind.Merchandise, 10, "Merchandise shop upgraded to level 10")]
    public void A_structure_upgrade_debits_the_cash_and_keys_on_the_building_at_the_version_it_produced(
        StructureKind kind,
        int targetLevel,
        string expectedText)
    {
        var clubId = Guid.CreateVersion7();
        var structureId = Guid.CreateVersion7();

        var posting = LedgerPostings.StructureUpgrade(
            Guid.CreateVersion7(), clubId, structureId, structureVersion: 5, kind, targetLevel, 70_000);

        posting.Category.Should().Be(LedgerCategory.StructureUpgrade);
        posting.SourceType.Should().Be(LedgerSourceType.Structure);
        posting.SourceId.Should().Be(structureId);
        posting.CashDeltaMinor.Should().Be(-70_000, "an upgrade is a spend");
        posting.ReservedDeltaMinor.Should().Be(0);
        posting.CorrelationId.Should().Be(
            $"structure:{structureId:D}:v5",
            "a retried order collides with its first entry, the next upgrade does not (FIN-17)");
        LedgerEntryText.Render(posting.DescriptionTemplate, posting.DescriptionParametersJson)
            .Should().Be(expectedText);
    }

    [Fact]
    public void A_structure_posting_that_moves_nothing_is_refused()
    {
        var clubId = Guid.CreateVersion7();
        var weekOf = new DateOnly(2026, 10, 18);

        var postings = new Action[]
        {
            () => LedgerPostings.SnackBarTakings(Guid.CreateVersion7(), clubId, Guid.CreateVersion7(), 1, 100, 0, 0),
            () => LedgerPostings.MerchandiseTakings(Guid.CreateVersion7(), clubId, weekOf, 1, 100, 0, 0),
            () => LedgerPostings.StructureUpkeep(Guid.CreateVersion7(), clubId, weekOf, 1, 1, 0),
            () => LedgerPostings.StructureUpgrade(
                Guid.CreateVersion7(), clubId, Guid.CreateVersion7(), 2, StructureKind.SnackBar, 2, 0),
        };

        foreach (var post in postings)
        {
            post.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void The_structure_categories_and_source_round_trip_through_their_stable_codes()
    {
        (LedgerCategory Category, string Code)[] categories =
        [
            (LedgerCategory.SnackBar, "snack_bar"),
            (LedgerCategory.Merchandise, "merchandise"),
            (LedgerCategory.StructureUpkeep, "structure_upkeep"),
            (LedgerCategory.StructureUpgrade, "structure_upgrade"),
        ];

        foreach (var (category, code) in categories)
        {
            category.ToCode().Should().Be(code);
            LedgerCategories.FromCode(code).Should().Be(category);
        }

        LedgerSourceType.Structure.ToCode().Should().Be("structure");
        LedgerSourceTypes.FromCode("structure").Should().Be(LedgerSourceType.Structure);
        LedgerCategories.All.Select(category => category.ToCode().Length).Max()
            .Should().BeLessThanOrEqualTo(LedgerCategories.MaxCodeLength);
        LedgerSourceTypes.All.Select(source => source.ToCode().Length).Max()
            .Should().BeLessThanOrEqualTo(LedgerSourceTypes.MaxCodeLength);
    }

    [Fact]
    public void The_new_category_and_source_round_trip_through_their_stable_codes()
    {
        LedgerCategory.StadiumConstruction.ToCode().Should().Be("stadium_construction");
        LedgerCategories.FromCode("stadium_construction").Should().Be(LedgerCategory.StadiumConstruction);
        LedgerSourceType.Stadium.ToCode().Should().Be("stadium");
        LedgerSourceTypes.FromCode("stadium").Should().Be(LedgerSourceType.Stadium);

        LedgerCategories.All.Select(category => category.ToCode().Length).Max()
            .Should().BeLessThanOrEqualTo(LedgerCategories.MaxCodeLength, "the column holds every code");
    }
}
