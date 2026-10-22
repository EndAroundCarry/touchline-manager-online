using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// The rollover's finance settlement: the prize for the final place, the sponsor's bonus, the parachute, the club's standing and each
/// club's season finance summary (`FIN-5`, `OBJ-4`, `STD-2`, master plan §6.8).
/// </summary>
public sealed class SettleSeasonFinancesTests
{
    private static readonly DateTimeOffset SettledAt = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid SeasonId = Guid.CreateVersion7();

    [Fact]
    public async Task Every_club_receives_the_prize_of_its_place_and_a_summary()
    {
        var champion = Guid.CreateVersion7();
        var bottom = Guid.CreateVersion7();

        var accounts = new RecordingAccountRepository(Funded(champion, 10_000_000), Funded(bottom, 2_000_000));

        var ledger = new RecordingLedgerRepository
        {
            BalancesBefore = new Dictionary<Guid, long> { [champion] = 10_000_000, [bottom] = 2_000_000 },
            Totals =
            [
                new ClubCategoryTotal(champion, LedgerCategory.GateReceipt, 3_000_000),
                new ClubCategoryTotal(champion, LedgerCategory.Wages, -1_000_000),
                new ClubCategoryTotal(bottom, LedgerCategory.Wages, -500_000),
            ],
        };

        var summaries = new RecordingSummaryRepository();

        var result = await Create(accounts, ledger, summaries).SettleAsync(
            WorldId,
            ClosingSeason(),
            [new SeasonSettlementClub(champion, 1, 1, 1), new SeasonSettlementClub(bottom, 1, 18, 2)],
            SettledAt,
            CancellationToken.None);

        result.Awarded.Should().Be(2);
        result.Summaries.Should().Be(2);

        var prizes = ledger.Entries.Where(entry => entry.Category == LedgerCategory.PositionAward).ToList();

        prizes.Should().HaveCount(2);

        var championAward = prizes.Single(entry =>
            entry.CorrelationId.EndsWith(champion.ToString("N"), StringComparison.Ordinal));

        championAward.CashDeltaMinor.Should().Be(EconomyRuleSet.PrizeMinorFor(1, 1));

        // The summary's closing cash is opening plus its category lines.
        var championSummary = summaries.Summaries.Single(summary => summary.ClubId == champion);
        championSummary.OpeningCashMinor.Should().Be(10_000_000);
        championSummary.ClosingCashMinor.Should().Be(10_000_000 + 3_000_000 - 1_000_000);

        summaries.Lines.Should().HaveCount(3);
        summaries.Lines.Should().ContainSingle(line =>
            line.ClubSeasonFinanceId == championSummary.Id && line.Category == LedgerCategory.GateReceipt);
    }

    [Fact]
    public async Task The_prize_is_paid_at_the_tier_the_club_finished_in()
    {
        var promoted = Guid.CreateVersion7();
        var ledger = new RecordingLedgerRepository();

        await Create(new RecordingAccountRepository(Funded(promoted, 1)), ledger, new RecordingSummaryRepository())
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(promoted, 2, 1, 1)],
                SettledAt,
                CancellationToken.None);

        ledger.Entries.Should().ContainSingle(entry => entry.Category == LedgerCategory.PositionAward)
            .Which.CashDeltaMinor.Should().Be(EconomyRuleSet.PrizeMinorFor(2, 1), "the tier it played in, not the tier it goes up to");
        ledger.Entries.Should().NotContain(entry => entry.Category == LedgerCategory.Parachute);
    }

    [Fact]
    public async Task A_relegated_club_receives_its_prize_and_then_the_parachute()
    {
        var relegated = Guid.CreateVersion7();
        var ledger = new RecordingLedgerRepository();

        var result = await Create(new RecordingAccountRepository(Funded(relegated, 1)), ledger, new RecordingSummaryRepository())
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(relegated, 1, 17, 2)],
                SettledAt,
                CancellationToken.None);

        result.Parachutes.Should().Be(1);

        ledger.Entries.Select(entry => entry.Category).Should().Equal(
            [LedgerCategory.PositionAward, LedgerCategory.Parachute],
            "the prize of the place first, then the one-off help for the lower division");

        ledger.Entries.Single(entry => entry.Category == LedgerCategory.Parachute).CashDeltaMinor
            .Should().Be(EconomyRuleSet.ParachuteMinorFor(1));
    }

    [Theory]
    [InlineData(SeasonObjective.TopSix, 4, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopSix, 1, ObjectiveOutcome.Exceeded)]
    [InlineData(SeasonObjective.TopSix, 12, ObjectiveOutcome.Missed)]
    public async Task The_sponsor_pays_its_bonus_only_when_the_objective_is_met_or_exceeded(
        SeasonObjective objective,
        int finalPlace,
        ObjectiveOutcome expected)
    {
        var clubId = Guid.CreateVersion7();
        var deal = Deal(clubId, objective);
        var ledger = new RecordingLedgerRepository();
        var sponsors = new RecordingSponsorRepository(deal);

        var result = await Create(
                new RecordingAccountRepository(Funded(clubId, 1)),
                ledger,
                new RecordingSummaryRepository(),
                sponsors)
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(clubId, 1, finalPlace, 1)],
                SettledAt,
                CancellationToken.None);

        deal.Outcome.Should().Be(expected);
        deal.FinalPlace.Should().Be(finalPlace);

        var bonus = ledger.Entries.SingleOrDefault(entry => entry.Category == LedgerCategory.SponsorBonus);

        if (expected == ObjectiveOutcome.Missed)
        {
            bonus.Should().BeNull("a missed objective pays nothing");
            result.Bonuses.Should().Be(0);
        }
        else
        {
            bonus.Should().NotBeNull();
            bonus!.CashDeltaMinor.Should().Be(expected == ObjectiveOutcome.Exceeded ? deal.BonusExceededMinor : deal.BonusMetMinor);
            result.Bonuses.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(4, FinanceInboxTemplates.ObjectiveMet)]
    [InlineData(1, FinanceInboxTemplates.ObjectiveExceeded)]
    [InlineData(12, FinanceInboxTemplates.ObjectiveMissed)]
    public async Task The_manager_is_told_how_the_season_ended_against_the_objective(int finalPlace, string template)
    {
        var clubId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var inbox = new RecordingInboxRepository();

        inbox.Targets.Add(new ClubInboxTarget(clubId, managerId, "Test FC"));

        await Create(
                new RecordingAccountRepository(Funded(clubId, 1)),
                new RecordingLedgerRepository(),
                new RecordingSummaryRepository(),
                new RecordingSponsorRepository(Deal(clubId, SeasonObjective.TopSix)),
                inbox)
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(clubId, 1, finalPlace, 1)],
                SettledAt,
                CancellationToken.None);

        var message = inbox.Messages.Should().ContainSingle().Which;

        message.TemplateKey.Should().Be(template);
        message.RecipientManagerId.Should().Be(managerId);
    }

    [Fact]
    public async Task A_computer_club_is_told_nothing_about_its_objective()
    {
        var clubId = Guid.CreateVersion7();
        var inbox = new RecordingInboxRepository();

        await Create(
                new RecordingAccountRepository(Funded(clubId, 1)),
                new RecordingLedgerRepository(),
                new RecordingSummaryRepository(),
                new RecordingSponsorRepository(Deal(clubId, SeasonObjective.TopSix)),
                inbox)
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(clubId, 1, 3, 1)],
                SettledAt,
                CancellationToken.None);

        inbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task A_clubs_standing_moves_by_its_finish_and_the_step_is_recorded()
    {
        var clubId = Guid.CreateVersion7();
        var club = NewClub(clubId, tier: 1);
        var before = club.StandingCentis;
        var sponsors = new RecordingSponsorRepository();

        var result = await Create(
                new RecordingAccountRepository(Funded(clubId, 1)),
                new RecordingLedgerRepository(),
                new RecordingSummaryRepository(),
                sponsors,
                inbox: null,
                club)
            .SettleAsync(
                WorldId,
                ClosingSeason(),
                [new SeasonSettlementClub(clubId, 1, 1, 1)],
                SettledAt,
                CancellationToken.None);

        result.Steps.Should().Be(1);

        var step = sponsors.Steps.Should().ContainSingle().Which;

        step.ClubId.Should().Be(clubId);
        step.BeforeCentis.Should().Be(before);
        step.AfterCentis.Should().Be(club.StandingCentis).And.BeGreaterThan(before, "a champion gains standing");
        step.FinalPlace.Should().Be(1);
        step.Outcome.Should().BeNull("the club had no objective this season");
    }

    [Fact]
    public async Task A_club_with_no_ledger_history_gets_a_zero_summary()
    {
        var clubId = Guid.CreateVersion7();
        var summaries = new RecordingSummaryRepository();

        var result = await Create(
                new RecordingAccountRepository(Funded(clubId, 1)),
                new RecordingLedgerRepository(),
                summaries)
            .SettleAsync(WorldId, ClosingSeason(), [new SeasonSettlementClub(clubId, 1, 9, 1)], SettledAt, CancellationToken.None);

        result.Awarded.Should().Be(1);
        result.Summaries.Should().Be(1);

        var summary = summaries.Summaries.Single();
        summary.OpeningCashMinor.Should().Be(0);
        summary.ClosingCashMinor.Should().Be(0);
        summaries.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_plan_settles_nothing()
    {
        var summaries = new RecordingSummaryRepository();

        var result = await Create(new RecordingAccountRepository(), new RecordingLedgerRepository(), summaries)
            .SettleAsync(WorldId, ClosingSeason(), [], SettledAt, CancellationToken.None);

        result.Should().Be(new SeasonSettlementResult(0, 0, 0, 0, 0));
        summaries.Summaries.Should().BeEmpty();
    }

    private static SettleSeasonFinances Create(
        RecordingAccountRepository accounts,
        RecordingLedgerRepository ledger,
        RecordingSummaryRepository summaries,
        RecordingSponsorRepository? sponsors = null,
        RecordingInboxRepository? inbox = null,
        params Club[] clubs) =>
        new(
            accounts,
            ledger,
            summaries,
            sponsors ?? new RecordingSponsorRepository(),
            new StubClubRepository(clubs),
            inbox ?? new RecordingInboxRepository(),
            NullLogger<SettleSeasonFinances>.Instance);

    private static Club NewClub(Guid clubId, int tier) =>
        Club.Generate(
            clubId,
            WorldId,
            Guid.CreateVersion7(),
            new ClubIdentity("Northgate United", "NOR", "Northgate", "North", "badge-seed"),
            tier,
            foundingGameYear: 1950,
            SettledAt);

    private static ClubSponsorDeal Deal(Guid clubId, SeasonObjective objective)
    {
        var made = SponsorOffers.For(WorldId, 1, clubId, 1, 20_000, 50)
            .Single(offer => offer.Kind == SponsorOfferKind.Balanced);

        var offer = ClubSponsorOffer.Record(Guid.CreateVersion7(), clubId, SeasonId, made, 1, 20_000, 50, SettledAt);
        var quote = SponsorOffers.Quotes(offer.Kind, offer.Tier, offer.Fans, offer.StandingPoints)
            .Single(candidate => candidate.Objective == objective);

        return ClubSponsorDeal.Sign(Guid.CreateVersion7(), offer, objective, quote.MetMinor, quote.ExceededMinor, SponsorSigner.Manager, SettledAt);
    }

    private static Season ClosingSeason() =>
        Season.Create(
            SeasonId,
            WorldId,
            sequenceNumber: 1,
            gameYear: 2026,
            WorldRuleSet.Version,
            firstMatchday: new DateOnly(2026, 9, 1),
            SettledAt);

    private sealed class RecordingSummaryRepository : IClubSeasonFinanceRepository
    {
        public List<ClubSeasonFinance> Summaries { get; } = [];

        public List<ClubSeasonFinanceLine> Lines { get; } = [];

        public void Add(ClubSeasonFinance summary) => Summaries.Add(summary);

        public void AddLine(ClubSeasonFinanceLine line) => Lines.Add(line);
    }
}
