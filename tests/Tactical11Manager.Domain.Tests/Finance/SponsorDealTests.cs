using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>
/// The sponsor deal a club signs and the season's standing step, as the rows that keep them (`SPN-1`, `SPN-2`, `OBJ-1`, `STD-3`).
/// </summary>
public sealed class SponsorDealTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static ClubSponsorOffer Offer(SponsorOfferKind kind = SponsorOfferKind.Balanced, int tier = 1, int fans = 20_000, int standing = 50)
    {
        var made = SponsorOffers.For(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), tier, fans, standing)
            .Single(offer => offer.Kind == kind);

        return ClubSponsorOffer.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), made, tier, fans, standing, Now);
    }

    private static ClubSponsorDeal Deal(ClubSponsorOffer offer, SeasonObjective objective = SeasonObjective.TopHalf)
    {
        var quote = SponsorOffers.Quotes(offer.Kind, offer.Tier, offer.Fans, offer.StandingPoints).Single(candidate => candidate.Objective == objective);

        return ClubSponsorDeal.Sign(Guid.CreateVersion7(), offer, objective, quote.MetMinor, quote.ExceededMinor, SponsorSigner.Manager, Now);
    }

    [Fact]
    public void An_offer_keeps_the_figures_and_the_basis_it_was_made_from()
    {
        var offer = Offer(SponsorOfferKind.Performance, tier: 2, fans: 8_000, standing: 40);

        offer.Kind.Should().Be(SponsorOfferKind.Performance);
        offer.FixedSeasonMinor.Should().Be(EconomyRuleSet.SponsorFixedSeasonMinorFor(SponsorOfferKind.Performance, 2, 8_000, 40));
        offer.BonusMultiplierBp.Should().Be(EconomyRuleSet.SponsorBonusMultiplierBp(SponsorOfferKind.Performance));
        offer.Tier.Should().Be(2);
        offer.Fans.Should().Be(8_000);
        offer.StandingPoints.Should().Be(40);
        offer.SponsorName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_deal_pays_the_offers_fixed_part_in_equal_matchday_parts()
    {
        var offer = Offer();

        var deal = Deal(offer);

        deal.OfferId.Should().Be(offer.Id);
        deal.ClubId.Should().Be(offer.ClubId);
        deal.SeasonId.Should().Be(offer.SeasonId);
        deal.FixedMatchdayMinor.Should().Be(offer.FixedSeasonMinor / EconomyRuleSet.MatchdaysPerSeason);
        deal.TargetPlace.Should().Be(8, "the top half asks for 8th of 16");
        deal.IsSettled.Should().BeFalse();
        deal.Version.Should().Be(1);
    }

    [Fact]
    public void A_deal_refuses_a_bonus_for_exceeding_that_is_smaller_than_the_bonus_for_meeting()
    {
        var offer = Offer();

        var act = () => ClubSponsorDeal.Sign(Guid.CreateVersion7(), offer, SeasonObjective.TopHalf, 100, 99, SponsorSigner.Board, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(8, ObjectiveOutcome.Met)]
    [InlineData(6, ObjectiveOutcome.Met)]
    [InlineData(5, ObjectiveOutcome.Exceeded)]
    [InlineData(1, ObjectiveOutcome.Exceeded)]
    [InlineData(9, ObjectiveOutcome.Missed)]
    public void A_season_settles_against_the_objective_once(int place, ObjectiveOutcome outcome)
    {
        var deal = Deal(Offer());

        var bonus = deal.Settle(place, Now.AddDays(300));

        deal.Outcome.Should().Be(outcome);
        deal.FinalPlace.Should().Be(place);
        deal.IsSettled.Should().BeTrue();
        deal.BonusPaidMinor.Should().Be(bonus);
        bonus.Should().Be(outcome switch
        {
            ObjectiveOutcome.Exceeded => deal.BonusExceededMinor,
            ObjectiveOutcome.Met => deal.BonusMetMinor,
            _ => 0,
        });

        var again = () => deal.Settle(place, Now.AddDays(301));

        again.Should().Throw<InvalidOperationException>("a season is settled once (OBJ-4)");
    }

    [Fact]
    public void A_new_manager_may_choose_another_objective_until_the_season_is_settled()
    {
        var offer = Offer();
        var deal = Deal(offer, SeasonObjective.TopHalf);
        var harder = SponsorOffers.Quotes(offer.Kind, offer.Tier, offer.Fans, offer.StandingPoints).Single(quote => quote.Objective == SeasonObjective.TopThree);

        deal.ChangeObjective(SeasonObjective.TopThree, harder.MetMinor, harder.ExceededMinor, SponsorSigner.Manager, Now.AddDays(1));

        deal.Objective.Should().Be(SeasonObjective.TopThree);
        deal.TargetPlace.Should().Be(3);
        deal.BonusMetMinor.Should().Be(harder.MetMinor);
        deal.Version.Should().Be(2);

        deal.Settle(2, Now.AddDays(300));

        var late = () => deal.ChangeObjective(SeasonObjective.StayUp, 1, 2, SponsorSigner.Manager, Now.AddDays(301));

        late.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Every_sponsor_code_parses_back_and_fits_its_column()
    {
        foreach (var kind in Enum.GetValues<SponsorOfferKind>())
        {
            SponsorCodes.OfferFromCode(kind.ToCode()).Should().Be(kind);
        }

        foreach (var objective in Enum.GetValues<SeasonObjective>())
        {
            SponsorCodes.ObjectiveFromCode(objective.ToCode()).Should().Be(objective);
        }

        foreach (var outcome in Enum.GetValues<ObjectiveOutcome>())
        {
            SponsorCodes.OutcomeFromCode(outcome.ToCode()).Should().Be(outcome);
        }

        foreach (var signer in Enum.GetValues<SponsorSigner>())
        {
            SponsorCodes.SignerFromCode(signer.ToCode()).Should().Be(signer);
        }

        SponsorCodes.OfferCodes.Concat(SponsorCodes.ObjectiveCodes).Concat(SponsorCodes.OutcomeCodes).Concat(SponsorCodes.SignerCodes)
            .Should().OnlyContain(code => code.Length <= SponsorCodes.MaxCodeLength);
    }

    [Fact]
    public void A_standing_step_keeps_where_the_club_was_where_it_went_and_why()
    {
        var before = ClubStanding.OpeningCentis(1);
        var after = ClubStanding.Step(before, finishedTier: 1, place: 1, ObjectiveOutcome.Exceeded, nextTier: 1);

        var step = ClubStandingEvent.Record(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, 1, ObjectiveOutcome.Exceeded, before, after, Now);

        step.BeforeCentis.Should().Be(before);
        step.AfterCentis.Should().Be(after).And.BeGreaterThan(before, "a champion who exceeded the objective gains standing");
        step.Outcome.Should().Be(ObjectiveOutcome.Exceeded);
        step.FinalPlace.Should().Be(1);
    }

    [Fact]
    public void A_club_standing_opens_at_its_tiers_value_and_moves_in_hundredths()
    {
        var club = Club.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("Northgate United", "NOR", "Northgate", "North", "badge-seed"),
            tier: 2,
            foundingGameYear: 1950,
            Now);

        club.StandingCentis.Should().Be(EconomyRuleSet.OpeningStandingFor(2) * ClubStanding.CentisPerPoint);
        club.Reputation.Should().Be(EconomyRuleSet.OpeningStandingFor(2));

        club.RecordStanding((EconomyRuleSet.OpeningStandingFor(2) * 100) + 149, Now.AddDays(300));

        club.StandingCentis.Should().Be((EconomyRuleSet.OpeningStandingFor(2) * 100) + 149);
        club.Reputation.Should().Be(EconomyRuleSet.OpeningStandingFor(2) + 1, "a manager reads whole points");

        club.RecordStanding(int.MaxValue, Now.AddDays(301));
        club.Reputation.Should().Be(EconomyRuleSet.StandingMax);
        club.StandingCentis.Should().Be(EconomyRuleSet.StandingMax * ClubStanding.CentisPerPoint, "the scale is capped");
    }
}
