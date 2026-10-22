using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>
/// How far into the red an account may go, and which postings may take it there (`DEBT-1`, `DEBT-2`).
/// </summary>
public sealed class ClubAccountDebtTests
{
    private const long Limit = 1_000;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static ClubAccount Funded(long cash = 5_000, long overdraftLimit = Limit)
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        account.Post(Posting(LedgerCategory.OpeningBalance, cash, 0), Now);
        account.SetOverdraft(overdraftLimit, Now);

        return account;
    }

    private static LedgerPosting Posting(LedgerCategory category, long cashDelta, long reservedDelta) =>
        new(
            Guid.CreateVersion7(),
            category,
            cashDelta,
            reservedDelta,
            LedgerSourceType.WeeklyRun,
            SourceId: null,
            Guid.CreateVersion7().ToString("N"),
            "test.ledger",
            "{}");

    [Fact]
    public void A_new_account_is_healthy_with_no_overdraft()
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        account.FinancialState.Should().Be(FinancialStateKind.Healthy);
        account.StateSinceMatchday.Should().Be(0);
        account.OverdraftLimitMinor.Should().Be(0);
        account.OverdraftFloorMinor.Should().Be(0, "a club with no sponsor deal behaves as it always did: cash never below zero");
        account.IsInTheRed.Should().BeFalse();
    }

    [Fact]
    public void The_floor_is_twice_the_limit_where_the_emergency_grant_steps_in()
    {
        var account = Funded();

        account.OverdraftLimitMinor.Should().Be(Limit);
        account.OverdraftFloorMinor.Should().Be(Limit * EconomyRuleSet.SafetyNetLimitMultiple, "FIN-16 is the integrity net behind the limit");
    }

    [Theory]
    [InlineData(LedgerCategory.Wages)]
    [InlineData(LedgerCategory.OperatingCost)]
    [InlineData(LedgerCategory.StructureUpkeep)]
    [InlineData(LedgerCategory.GroundUpkeep)]
    public void A_recurring_cost_may_take_the_cash_below_zero(LedgerCategory category)
    {
        var account = Funded(cash: 500);

        var entry = account.Post(Posting(category, -1_200, 0), Now);

        account.CashMinor.Should().Be(-700);
        account.IsInTheRed.Should().BeTrue();
        entry.ResultingCashMinor.Should().Be(-700, "the ledger states what the club held, in the red too");
    }

    [Theory]
    [InlineData(LedgerCategory.TransferPayment)]
    [InlineData(LedgerCategory.StadiumConstruction)]
    [InlineData(LedgerCategory.StructureUpgrade)]
    [InlineData(LedgerCategory.ReleasePayout)]
    [InlineData(LedgerCategory.Compensation)]
    public void A_fee_a_building_or_a_release_still_needs_the_cash(LedgerCategory category)
    {
        var account = Funded(cash: 500);

        var act = () => account.Post(Posting(category, -600, 0), Now);

        act.Should().Throw<InvalidOperationException>("only a recurring cost may be borrowed (DEBT-1)");
        account.CashMinor.Should().Be(500);
    }

    [Fact]
    public void A_recurring_cost_is_refused_below_the_overdraft_floor()
    {
        var account = Funded(cash: 1);

        account.Post(Posting(LedgerCategory.Wages, -2_001, 0), Now);

        var act = () => account.Post(Posting(LedgerCategory.Wages, -1, 0), Now);

        act.Should().Throw<InvalidOperationException>("the floor is the deepest the cash may go (DEBT-1)");
        account.CashMinor.Should().Be(-2_000, "the floor itself is allowed");
    }

    [Fact]
    public void Money_coming_in_while_in_the_red_is_always_allowed()
    {
        var account = Funded(cash: 1);
        account.Post(Posting(LedgerCategory.Wages, -901, 0), Now);

        account.Post(Posting(LedgerCategory.LeagueTv, 400, 0), Now);

        account.CashMinor.Should().Be(-500, "income pays the debt down without leaving it (DEBT-2)");
    }

    [Fact]
    public void Nothing_can_be_reserved_while_the_cash_is_below_zero()
    {
        var account = Funded(cash: 1);
        account.Post(Posting(LedgerCategory.Wages, -101, 0), Now);

        var act = () => account.Post(Posting(LedgerCategory.BidReservation, 0, 1), Now);

        act.Should().Throw<InvalidOperationException>("reserved funds never exceed the cash above zero (FIN-10)");
    }

    [Fact]
    public void A_cost_that_would_leave_a_bid_reserved_beyond_the_cash_is_refused_even_into_the_red()
    {
        var account = Funded(cash: 1_000);
        account.Post(Posting(LedgerCategory.BidReservation, 0, 800), Now);

        var act = () => account.Post(Posting(LedgerCategory.Wages, -1_500, 0), Now);

        act.Should().Throw<InvalidOperationException>("the caller releases the bids first; the account never lets a reservation outlive its cash");
        account.CashMinor.Should().Be(1_000);
        account.ReservedMinor.Should().Be(800);
    }

    [Fact]
    public void The_overdraft_cannot_be_lowered_beneath_what_the_club_already_owes()
    {
        var account = Funded(cash: 1);
        account.Post(Posting(LedgerCategory.Wages, -1_501, 0), Now);

        var act = () => account.SetOverdraft(500, Now);

        act.Should().Throw<InvalidOperationException>("the new floor of 1,000 is above the 1,500 owed");
        account.OverdraftLimitMinor.Should().Be(Limit);
    }

    [Fact]
    public void A_state_is_recorded_with_the_matchday_it_began_on()
    {
        var account = Funded();
        var version = account.Version;

        account.RecordState(new FinancialStep(FinancialStateKind.InTheRed, 12, FinancialTransition.EnteredRed, Dismiss: false, EmergencyGrantMinor: 0), Now);

        account.FinancialState.Should().Be(FinancialStateKind.InTheRed);
        account.StateSinceMatchday.Should().Be(12);
        account.Version.Should().Be(version + 1);
    }

    [Fact]
    public void Only_the_recurring_costs_may_overdraw()
    {
        LedgerCategories.All.Where(category => category.MayOverdraw())
            .Should().BeEquivalentTo(
                [LedgerCategory.Wages, LedgerCategory.OperatingCost, LedgerCategory.StructureUpkeep, LedgerCategory.GroundUpkeep]);
    }

    [Fact]
    public void Every_category_has_a_distinct_code_that_fits_the_column_and_parses_back()
    {
        LedgerCategories.AllCodes.Should().OnlyHaveUniqueItems();
        LedgerCategories.AllCodes.Should().OnlyContain(code => code.Length <= LedgerCategories.MaxCodeLength);

        foreach (var category in LedgerCategories.All)
        {
            LedgerCategories.FromCode(category.ToCode()).Should().Be(category);
        }
    }

    [Theory]
    [InlineData(LedgerCategory.LeagueTv, "league_tv")]
    [InlineData(LedgerCategory.SponsorMatchday, "sponsor_matchday")]
    [InlineData(LedgerCategory.SponsorBonus, "sponsor_bonus")]
    [InlineData(LedgerCategory.Parachute, "parachute")]
    [InlineData(LedgerCategory.GroundUpkeep, "ground_upkeep")]
    [InlineData(LedgerCategory.CleanStart, "clean_start")]
    public void The_new_categories_have_the_codes_the_plan_names(LedgerCategory category, string code)
    {
        category.ToCode().Should().Be(code);
    }

    [Fact]
    public void Every_financial_state_has_a_code_that_parses_back()
    {
        foreach (var state in Enum.GetValues<FinancialStateKind>())
        {
            FinancialStateCodes.FromCode(state.ToCode()).Should().Be(state);
            state.ToCode().Length.Should().BeLessThanOrEqualTo(FinancialStateCodes.MaxCodeLength);
        }
    }
}
