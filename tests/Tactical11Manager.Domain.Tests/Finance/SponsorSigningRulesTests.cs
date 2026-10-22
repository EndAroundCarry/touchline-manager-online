using FluentAssertions;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>
/// The pure pieces of signing a sponsor: the deadline, a new manager's second thoughts, and the temperament a computer club signs by
/// (`SPN-4`, `SPN-6`, `ai-finance-v1`).
/// </summary>
public sealed class SponsorSigningRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static (ClubSponsorOffer Steady, ClubSponsorOffer Performance, ClubSponsorDeal Deal) Signed()
    {
        var clubId = Guid.CreateVersion7();
        var seasonId = Guid.CreateVersion7();
        var made = SponsorOffers.For(Guid.CreateVersion7(), 1, clubId, 1, 20_000, 50);

        ClubSponsorOffer Record(SponsorOfferKind kind) =>
            ClubSponsorOffer.Record(Guid.CreateVersion7(), clubId, seasonId, made.Single(offer => offer.Kind == kind), 1, 20_000, 50, Now);

        var steady = Record(SponsorOfferKind.Steady);
        var performance = Record(SponsorOfferKind.Performance);
        var quote = SponsorOffers.QuoteFor(steady, SeasonObjective.TopHalf);

        return (steady, performance, ClubSponsorDeal.Sign(Guid.CreateVersion7(), steady, SeasonObjective.TopHalf, quote.MetMinor, quote.ExceededMinor, SponsorSigner.Ai, Now));
    }

    [Fact]
    public void The_deadline_is_the_team_sheet_lock_of_the_fourth_matchday()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 8, 1));
        var dates = SeasonCalendar.MatchdayDates(first, WorldRuleSet.MatchdaysPerSeason);

        var deadline = SponsorDeadline.LockAtFor(first);

        deadline.Should().Be(SeasonCalendar.KickoffAt(dates[3]).AddMinutes(-WorldRuleSet.TeamSheetLockMinutes));
        deadline.Should().BeAfter(SeasonCalendar.KickoffAt(dates[2]), "three matchdays are played before the board signs");
        deadline.Should().BeBefore(SeasonCalendar.KickoffAt(dates[3]));
    }

    [Fact]
    public void A_quote_for_an_offer_a_club_was_made_matches_the_quotes_of_its_basis()
    {
        var (steady, _, _) = Signed();

        var expected = SponsorOffers.Quotes(steady.Kind, steady.Tier, steady.Fans, steady.StandingPoints)
            .Single(quote => quote.Objective == SeasonObjective.TopThree);

        SponsorOffers.QuoteFor(steady, SeasonObjective.TopThree).Should().Be(expected);
    }

    [Fact]
    public void A_deal_can_take_another_offer_and_objective_and_pays_the_new_fixed_part_from_then_on()
    {
        var (_, performance, deal) = Signed();
        var versionBefore = deal.Version;
        var quote = SponsorOffers.QuoteFor(performance, SeasonObjective.WinLeague);

        deal.Replace(performance, SeasonObjective.WinLeague, quote.MetMinor, quote.ExceededMinor, SponsorSigner.Manager, Now.AddDays(1));

        deal.Kind.Should().Be(SponsorOfferKind.Performance);
        deal.OfferId.Should().Be(performance.Id);
        deal.Objective.Should().Be(SeasonObjective.WinLeague);
        deal.TargetPlace.Should().Be(1);
        deal.FixedMatchdayMinor.Should().Be(EconomyRuleSet.PerMatchday(performance.FixedSeasonMinor));
        deal.BonusMetMinor.Should().Be(quote.MetMinor);
        deal.SignedBy.Should().Be(SponsorSigner.Manager);
        deal.SignedAt.Should().Be(Now.AddDays(1), "a deal the present manager made is told from one he inherited");
        deal.Version.Should().Be(versionBefore + 1);
    }

    [Fact]
    public void A_settled_deal_cannot_be_replaced()
    {
        var (_, performance, deal) = Signed();

        deal.Settle(3, Now.AddDays(200));

        var act = () => deal.Replace(performance, SeasonObjective.StayUp, 0, 0, SponsorSigner.Manager, Now.AddDays(201));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_deal_only_takes_an_offer_made_to_its_own_club_and_season()
    {
        var (_, _, deal) = Signed();
        var stranger = SponsorOffers.For(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 1, 20_000, 50)[0];
        var foreign = ClubSponsorOffer.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), stranger, 1, 20_000, 50, Now);

        var act = () => deal.Replace(foreign, SeasonObjective.StayUp, 0, 0, SponsorSigner.Manager, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_clubs_temperament_is_the_one_its_match_plan_is_built_from()
    {
        for (var index = 0; index < 40; index++)
        {
            var clubId = Guid.CreateVersion7();

            AiMatchPlanPolicy.TemperamentOf(clubId)
                .Should().Be(AiMatchPlanPolicy.Decide(clubId, FormationPreset.FourFourTwo, TeamInstructionSet.Neutral).Temperament);
        }
    }

    [Fact]
    public void Temperaments_are_spread_over_all_three_so_the_computer_clubs_sign_all_three_offers()
    {
        var kinds = Enumerable.Range(0, 120)
            .Select(_ => AiFinancePolicy.ChooseOffer(AiMatchPlanPolicy.TemperamentOf(Guid.CreateVersion7())))
            .Distinct();

        kinds.Should().BeEquivalentTo(Enum.GetValues<SponsorOfferKind>());
    }
}
