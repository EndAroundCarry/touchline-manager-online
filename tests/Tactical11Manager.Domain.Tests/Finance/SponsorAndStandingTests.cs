using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>The sponsor's offers, the season objective and a club's standing (`sponsor-v1`, `OBJ-*`, `STD-*`).</summary>
public sealed class SponsorAndStandingTests
{
    private static readonly Guid World = Guid.Parse("0192f100-0000-7000-8000-000000000001");

    private static readonly Guid Club = Guid.Parse("0192f100-0000-7000-8000-000000000002");

    [Fact]
    public void The_three_offers_are_the_same_for_the_same_world_season_and_club()
    {
        var first = SponsorOffers.For(World, 3, Club, tier: 1, fans: 10_000, standing: 50);
        var second = SponsorOffers.For(World, 3, Club, tier: 1, fans: 10_000, standing: 50);

        second.Should().Equal(first);
        first.Select(offer => offer.Kind).Should().Equal(SponsorOfferKind.Steady, SponsorOfferKind.Balanced, SponsorOfferKind.Performance);
        first.Select(offer => offer.SponsorName).Should().OnlyHaveUniqueItems();
        first.Should().OnlyContain(offer => offer.SponsorName.Length > 3);
    }

    [Fact]
    public void Another_season_or_club_gets_other_offers()
    {
        var names = Enumerable.Range(1, 12).SelectMany(season => SponsorOffers.For(World, season, Club, 1, 10_000, 50).Select(offer => offer.SponsorName)).Distinct();

        names.Should().HaveCountGreaterThan(8);
    }

    [Fact]
    public void The_offers_pay_less_for_certain_as_they_pay_more_for_the_result()
    {
        var offers = SponsorOffers.For(World, 1, Club, 1, FanRuleSetOpening, 50);

        offers[0].FixedSeasonMinor.Should().BeGreaterThan(offers[1].FixedSeasonMinor);
        offers[1].FixedSeasonMinor.Should().BeGreaterThan(offers[2].FixedSeasonMinor);
        offers[0].FixedSeasonMinor.Should().Be(EconomyRuleSet.SponsorBaseSeasonMinorFor(1), "the steady offer at neutral fans and standing is the base");
        offers[1].FixedSeasonMinor.Should().Be(EconomyRuleSet.SponsorBaseSeasonMinorFor(1) * 85 / 100);
        offers.Select(offer => offer.BonusMultiplierBp).Should().Equal(5_000, 10_000, 20_000);
    }

    [Fact]
    public void For_a_club_that_meets_a_realistic_objective_the_three_offers_are_worth_about_the_same()
    {
        foreach (var rank in new[] { 8, 14 })
        {
            var objective = EconomyRuleSet.RealisticObjectiveFor(rank);
            var values = Enum.GetValues<SponsorOfferKind>()
                .Select(kind => EconomyRuleSet.SponsorFixedSeasonMinorFor(kind, 1, FanRuleSetOpening, 50)
                    + EconomyRuleSet.SponsorBonusMinorFor(kind, objective, ObjectiveOutcome.Met, 1, FanRuleSetOpening, 50))
                .ToList();

            ((double)values.Max() / values.Min()).Should().BeLessThan(1.25, $"objective {objective}");
        }
    }

    [Theory]
    [InlineData(1, SeasonObjective.WinLeague)]
    [InlineData(2, SeasonObjective.TopThree)]
    [InlineData(3, SeasonObjective.TopThree)]
    [InlineData(4, SeasonObjective.TopSix)]
    [InlineData(6, SeasonObjective.TopSix)]
    [InlineData(7, SeasonObjective.TopHalf)]
    [InlineData(8, SeasonObjective.TopHalf)]
    [InlineData(9, SeasonObjective.EleventhPlace)]
    [InlineData(11, SeasonObjective.EleventhPlace)]
    [InlineData(12, SeasonObjective.StayUp)]
    [InlineData(16, SeasonObjective.StayUp)]
    public void The_realistic_objective_is_the_strength_rank_rounded_to_a_choice(int rank, SeasonObjective expected)
    {
        EconomyRuleSet.RealisticObjectiveFor(rank).Should().Be(expected);
    }

    [Theory]
    [InlineData(SeasonObjective.WinLeague, 1, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.WinLeague, 2, ObjectiveOutcome.Missed)]
    [InlineData(SeasonObjective.TopThree, 3, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopThree, 4, ObjectiveOutcome.Missed)]
    [InlineData(SeasonObjective.TopSix, 6, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopSix, 4, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopSix, 3, ObjectiveOutcome.Exceeded)]
    [InlineData(SeasonObjective.TopHalf, 8, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopHalf, 6, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.TopHalf, 5, ObjectiveOutcome.Exceeded)]
    [InlineData(SeasonObjective.TopHalf, 9, ObjectiveOutcome.Missed)]
    [InlineData(SeasonObjective.EleventhPlace, 11, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.EleventhPlace, 8, ObjectiveOutcome.Exceeded)]
    [InlineData(SeasonObjective.EleventhPlace, 12, ObjectiveOutcome.Missed)]
    [InlineData(SeasonObjective.StayUp, 13, ObjectiveOutcome.Met)]
    [InlineData(SeasonObjective.StayUp, 10, ObjectiveOutcome.Exceeded)]
    [InlineData(SeasonObjective.StayUp, 14, ObjectiveOutcome.Missed)]
    public void An_objective_is_met_at_its_place_exceeded_by_three_places_and_otherwise_missed(SeasonObjective objective, int place, ObjectiveOutcome expected)
    {
        EconomyRuleSet.OutcomeOf(objective, place).Should().Be(expected);
    }

    [Fact]
    public void A_harder_objective_pays_more_and_exceeding_it_pays_half_as_much_again()
    {
        long Bonus(SeasonObjective objective, ObjectiveOutcome outcome) =>
            EconomyRuleSet.SponsorBonusMinorFor(SponsorOfferKind.Balanced, objective, outcome, 1, FanRuleSetOpening, 50);

        Bonus(SeasonObjective.WinLeague, ObjectiveOutcome.Met).Should().BeGreaterThan(Bonus(SeasonObjective.TopThree, ObjectiveOutcome.Met));
        Bonus(SeasonObjective.TopThree, ObjectiveOutcome.Met).Should().BeGreaterThan(Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Met));
        Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Met).Should().Be(EconomyRuleSet.SponsorBonusBaseMinorTier1, "a top-half finish at neutral fans and standing pays the base");
        Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Met).Should().BeGreaterThan(Bonus(SeasonObjective.StayUp, ObjectiveOutcome.Met));
        Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Exceeded).Should().Be(Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Met) * 3 / 2);
        Bonus(SeasonObjective.TopHalf, ObjectiveOutcome.Missed).Should().Be(0);
    }

    [Fact]
    public void The_places_the_objectives_ask_for_follow_the_size_of_the_table()
    {
        Enum.GetValues<SeasonObjective>().Select(EconomyRuleSet.TargetPlaceFor).Should().Equal(1, 3, 6, 8, 11, 13);
        EconomyRuleSet.TargetPlaceFor(SeasonObjective.TopHalf).Should().Be(EconomyRuleSet.ClubsPerDivision / 2);
        EconomyRuleSet.TargetPlaceFor(SeasonObjective.StayUp).Should().Be(EconomyRuleSet.ClubsPerDivision - WorldRuleSet.RelegatedPerTier, "staying up is the last place that is not relegated");
        EconomyRuleSet.OutcomeOf(SeasonObjective.StayUp, EconomyRuleSet.ClubsPerDivision - WorldRuleSet.RelegatedPerTier + 1).Should().Be(ObjectiveOutcome.Missed, "the first relegated place misses staying up");
    }

    [Fact]
    public void Targeting_the_top_three_and_making_it_pays_more_than_targeting_eleventh_and_making_it()
    {
        var (_, top3) = ObjectiveBonus.Settle(SponsorOfferKind.Balanced, SeasonObjective.TopThree, 3, 1, FanRuleSetOpening, 50);
        var (_, eleventh) = ObjectiveBonus.Settle(SponsorOfferKind.Balanced, SeasonObjective.EleventhPlace, 9, 1, FanRuleSetOpening, 50);

        top3.Should().BeGreaterThan(eleventh);
    }

    [Fact]
    public void The_fans_lift_the_sponsor_by_the_square_root_of_their_growth_and_never_above_four_times()
    {
        EconomyRuleSet.SponsorFansFactorBp(FanRuleSetOpening, 1).Should().Be(10_000);
        EconomyRuleSet.SponsorFansFactorBp(FanRuleSetOpening * 4, 1).Should().Be(20_000);
        EconomyRuleSet.SponsorFansFactorBp(FanRuleSetOpening * 100, 1).Should().Be(40_000);
        EconomyRuleSet.SponsorFansFactorBp(0, 1).Should().Be(0);
    }

    [Fact]
    public void Standing_lifts_the_sponsor_above_the_tiers_opening_standing_and_lowers_it_below()
    {
        EconomyRuleSet.SponsorStandingFactorBp(50, 1).Should().Be(10_000);
        EconomyRuleSet.SponsorStandingFactorBp(40, 2).Should().Be(10_000, "tier 2 opens at 40");
        EconomyRuleSet.SponsorStandingFactorBp(90, 1).Should().BeGreaterThan(10_000);
        EconomyRuleSet.SponsorStandingFactorBp(20, 1).Should().BeLessThan(10_000);
        EconomyRuleSet.SponsorStandingFactorBp(1, 1).Should().BeGreaterThanOrEqualTo(EconomyRuleSet.StandingFactorFloorBp);
        EconomyRuleSet.SponsorStandingFactorBp(100, 4).Should().BeLessThanOrEqualTo(EconomyRuleSet.StandingFactorCeilingBp);
    }

    [Fact]
    public void The_quotes_for_the_objective_picker_run_from_the_title_to_staying_up()
    {
        var quotes = SponsorOffers.Quotes(SponsorOfferKind.Balanced, 1, FanRuleSetOpening, 50);

        quotes.Select(quote => quote.TargetPlace).Should().Equal(1, 3, 6, 8, 11, 13);
        quotes.Select(quote => quote.MetMinor).Should().BeInDescendingOrder();
        quotes.Should().OnlyContain(quote => quote.ExceededMinor == quote.MetMinor * 3 / 2);
    }

    [Fact]
    public void Standing_starts_at_fifty_in_the_first_tier_and_ten_less_in_each_tier_below()
    {
        EconomyRuleSet.OpeningStandingFor(1).Should().Be(50);
        EconomyRuleSet.OpeningStandingFor(2).Should().Be(40);
        EconomyRuleSet.OpeningStandingFor(5).Should().Be(10);
        EconomyRuleSet.OpeningStandingFor(9).Should().Be(1, "never below the scale");
        ClubStanding.PointsOf(ClubStanding.OpeningCentis(3)).Should().Be(30);
    }

    [Theory]
    [InlineData(1, false, 6)]
    [InlineData(2, false, 4)]
    [InlineData(3, false, 4)]
    [InlineData(4, false, 2)]
    [InlineData(6, false, 2)]
    [InlineData(7, false, 0)]
    [InlineData(10, false, 0)]
    [InlineData(11, false, -2)]
    [InlineData(13, false, -2)]
    [InlineData(14, true, -5)]
    [InlineData(16, true, -5)]
    public void A_final_place_moves_the_standing_by_the_table(int place, bool relegated, int points)
    {
        EconomyRuleSet.StandingFinishDelta(place, relegated).Should().Be(points);
    }

    [Theory]
    [InlineData(ObjectiveOutcome.Exceeded, 2)]
    [InlineData(ObjectiveOutcome.Met, 1)]
    [InlineData(ObjectiveOutcome.Missed, -2)]
    public void An_objective_moves_the_standing_by_the_table(ObjectiveOutcome outcome, int points)
    {
        EconomyRuleSet.StandingObjectiveDelta(outcome).Should().Be(points);
    }

    [Fact]
    public void A_club_that_wins_the_title_every_season_reaches_about_ninety_five_after_about_ten()
    {
        var standing = ClubStanding.OpeningCentis(1);

        for (var season = 1; season <= 10; season++)
        {
            standing = ClubStanding.Step(standing, 1, 1, ObjectiveOutcome.Met, 1);
        }

        ClubStanding.PointsOf(standing).Should().BeInRange(90, 99);
    }

    [Fact]
    public void A_club_that_is_regularly_in_the_top_three_reaches_the_eighties_and_a_mid_table_club_stays_where_it_was()
    {
        var topThree = ClubStanding.OpeningCentis(1);
        var middle = ClubStanding.OpeningCentis(1);

        for (var season = 1; season <= 10; season++)
        {
            topThree = ClubStanding.Step(topThree, 1, 3, ObjectiveOutcome.Met, 1);
            middle = ClubStanding.Step(middle, 1, 9, null, 1);
        }

        ClubStanding.PointsOf(topThree).Should().BeInRange(78, 90);
        ClubStanding.PointsOf(middle).Should().Be(50);
    }

    [Fact]
    public void A_relegated_club_is_pulled_toward_the_lower_tiers_base_and_a_promoted_club_toward_the_higher()
    {
        var down = ClubStanding.Step(5_000, 1, 15, ObjectiveOutcome.Missed, 2);
        var up = ClubStanding.Step(4_000, 2, 1, ObjectiveOutcome.Met, 1);

        ClubStanding.PointsOf(down).Should().BeLessThan(50);
        ClubStanding.PointsOf(up).Should().BeGreaterThan(40);
    }

    [Fact]
    public void Standing_stays_on_its_scale()
    {
        var top = 10_000;
        var bottom = 100;

        ClubStanding.Step(top, 1, 1, ObjectiveOutcome.Exceeded, 1).Should().BeLessThanOrEqualTo(10_000);
        ClubStanding.Step(bottom, 4, 16, ObjectiveOutcome.Missed, 5).Should().BeGreaterThanOrEqualTo(100);
    }

    private static int FanRuleSetOpening => FanRuleSet.StartFansFor(1);
}
