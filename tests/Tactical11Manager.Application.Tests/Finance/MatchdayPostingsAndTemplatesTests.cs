using FluentAssertions;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// The postings a matchday and a season's end make, and the sentences the club's money produces (`FIN-4`, `FIN-7`, `SPN-3`, `OBJ-4`,
/// `DEBT-2`…`DEBT-5`).
/// </summary>
public sealed class MatchdayPostingsAndTemplatesTests
{
    [Fact]
    public void The_three_postings_of_a_matchday_share_one_key_that_fits_the_ledger()
    {
        var clubId = Guid.CreateVersion7();
        var matchdayId = Guid.CreateVersion7();

        var television = LedgerPostings.LeagueTv(Guid.CreateVersion7(), clubId, matchdayId, 2, 1_000);
        var sponsor = LedgerPostings.SponsorMatchday(Guid.CreateVersion7(), clubId, matchdayId, 2_000);
        var wages = LedgerPostings.MatchdayWages(Guid.CreateVersion7(), clubId, matchdayId, 22, 3_000);
        var grant = LedgerPostings.MatchdayEmergencyGrant(Guid.CreateVersion7(), clubId, matchdayId, 4_000);

        television.CashDeltaMinor.Should().Be(1_000);
        sponsor.CashDeltaMinor.Should().Be(2_000);
        wages.CashDeltaMinor.Should().Be(-3_000, "wages are a cost");
        grant.CashDeltaMinor.Should().Be(4_000);

        new[] { television, sponsor, wages, grant }.Select(posting => posting.CorrelationId).Distinct()
            .Should().ContainSingle("a club's matchday is one operation, told apart by category (FIN-17)")
            .Which.Length.Should().BeLessThanOrEqualTo(LedgerEntry.MaxCorrelationIdLength);

        new[] { television, sponsor, wages, grant }.Select(posting => posting.Category).Should().OnlyHaveUniqueItems();
        wages.Category.Should().Be(LedgerCategory.Wages);
        wages.SourceType.Should().Be(LedgerSourceType.Matchday);
    }

    [Fact]
    public void A_seasons_end_postings_share_the_rollover_key()
    {
        var clubId = Guid.CreateVersion7();
        var seasonId = Guid.CreateVersion7();

        var prize = LedgerPostings.PositionAward(Guid.CreateVersion7(), clubId, seasonId, 1, 3, 4_000);
        var bonus = LedgerPostings.SponsorBonus(Guid.CreateVersion7(), clubId, seasonId, 3, 2, 1_000);
        var parachute = LedgerPostings.Parachute(Guid.CreateVersion7(), clubId, seasonId, 1, 500);

        new[] { prize, bonus, parachute }.Select(posting => posting.CorrelationId).Distinct().Should().ContainSingle();
        bonus.Category.Should().Be(LedgerCategory.SponsorBonus);
        parachute.Category.Should().Be(LedgerCategory.Parachute);
        parachute.SourceType.Should().Be(LedgerSourceType.SeasonRollover);
    }

    [Fact]
    public void The_ground_upkeep_is_a_weekly_cost_and_the_release_fee_is_the_agents_to_pay()
    {
        var clubId = Guid.CreateVersion7();
        var upkeep = LedgerPostings.GroundUpkeep(Guid.CreateVersion7(), clubId, new DateOnly(2026, 10, 18), 12_000, 700);
        var fee = LedgerPostings.ReleaseFee(Guid.CreateVersion7(), clubId, Guid.CreateVersion7(), 900);

        upkeep.CashDeltaMinor.Should().Be(-700);
        upkeep.Category.Should().Be(LedgerCategory.GroundUpkeep);
        fee.CashDeltaMinor.Should().Be(900, "the agent pays the club");
        fee.Category.Should().Be(LedgerCategory.TransferProceeds);
    }

    [Theory]
    [InlineData("league_tv")]
    [InlineData("sponsor_matchday")]
    [InlineData("matchday_wages")]
    [InlineData("sponsor_bonus")]
    [InlineData("parachute")]
    [InlineData("ground_upkeep")]
    [InlineData("release_fee")]
    public void Every_new_ledger_template_renders_without_the_amount(string name)
    {
        var template = $"finance.{name}";
        var text = LedgerEntryText.Render(template, "{\"amountMinor\":1000,\"playerCount\":22,\"target\":3,\"rank\":2,\"tier\":1}");

        text.Should().NotBeNullOrWhiteSpace().And.NotContain("1000", "the amount is the entry's own column");
    }

    [Fact]
    public void The_money_messages_render_in_plain_words()
    {
        Render(FinanceInboxTemplates.InTheRedMessage(-5_000, 40_000)).Title.Should().Be("The club is in the red");
        Render(FinanceInboxTemplates.BackInTheBlackMessage(100)).Title.Should().Be("The club is out of the red");
        Render(FinanceInboxTemplates.AdministrationEnteredMessage(-50_000, 40_000, 34)).Body.Should().Contain("34 matchdays");
        Render(FinanceInboxTemplates.AdministrationLeftMessage(-10_000)).Title.Should().Be("The club is out of administration");
        Render(FinanceInboxTemplates.BoardListedMessage(["A. Striker", "B. Winger"], 12)).Body
            .Should().Contain("A. Striker and B. Winger").And.Contain("12 matchdays");
        Render(FinanceInboxTemplates.BoardReleasedMessage("C. Keeper", 900, Guid.CreateVersion7())).Title
            .Should().Be("C. Keeper was released by the board");
        Render(FinanceInboxTemplates.BidWithdrawnMessage("D. Back", 5_000, Guid.CreateVersion7())).Body
            .Should().Contain("D. Back").And.Contain("wages");
        Render(FinanceInboxTemplates.ManagerDismissedMessage("Northgate United", 34)).Title
            .Should().Be("You were dismissed by Northgate United");
    }

    [Fact]
    public void The_money_messages_sit_on_the_squad_shelf()
    {
        FinanceInboxTemplates.InTheRedMessage(-1, 1).Category.Should().Be(InboxCategory.Squad);
    }

    [Fact]
    public void A_dismissal_email_names_the_club_and_the_wait()
    {
        var email = OccupancyEmails.Dismissed("a@b.example", "Northgate United", 34);

        email.To.Should().Be("a@b.example");
        email.Subject.Should().Contain("Northgate United");
        email.TextBody.Should().Contain("34 matchdays").And.Contain("short wait");
    }

    private static InboxText Render(InboxDraft draft) => InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);
}
