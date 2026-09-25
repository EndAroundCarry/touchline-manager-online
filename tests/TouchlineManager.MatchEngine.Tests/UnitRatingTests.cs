using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The rating model and the tactical bounds that keep attributes dominant (`INS-9`, master plan §8.4).
/// </summary>
public sealed class UnitRatingTests
{
    [Fact]
    public void Every_tactical_modifier_stays_inside_the_bounds()
    {
        // Exhaustive over every combination of the eight instructions, for every unit. This is the check that
        // makes "bound tactical modifiers so attributes remain dominant" a property rather than an intention.
        var rules = EngineRulesV1.Default;

        var mentalities = Enum.GetValues<MatchMentality>();
        var tempos = Enum.GetValues<MatchTempo>();
        var passings = Enum.GetValues<MatchPassingStyle>();
        var widths = Enum.GetValues<MatchWidth>();
        var pressings = Enum.GetValues<MatchPressing>();
        var lines = Enum.GetValues<MatchDefensiveLine>();
        var tacklings = Enum.GetValues<MatchTacklingStyle>();
        var wasting = Enum.GetValues<MatchTimeWasting>();

        var checkedCombinations = 0;

        foreach (var mentality in mentalities)
        {
            foreach (var tempo in tempos)
            {
                foreach (var passing in passings)
                {
                    foreach (var width in widths)
                    {
                        foreach (var pressing in pressings)
                        {
                            foreach (var line in lines)
                            {
                                foreach (var tackling in tacklings)
                                {
                                    foreach (var timeWasting in wasting)
                                    {
                                        var instructions = new MatchInstructionsV1
                                        {
                                            Mentality = mentality,
                                            Tempo = tempo,
                                            Passing = passing,
                                            Width = width,
                                            Pressing = pressing,
                                            DefensiveLine = line,
                                            Tackling = tackling,
                                            TimeWasting = timeWasting,
                                        };

                                        foreach (var unit in Enum.GetValues<MatchUnit>())
                                        {
                                            TacticalModifiers.For(unit, instructions, rules)
                                                .Should().BeInRange(
                                                    rules.MinTacticalModifierBasisPoints,
                                                    rules.MaxTacticalModifierBasisPoints);
                                        }

                                        checkedCombinations++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        checkedCombinations.Should().Be(10_935, "five mentalities, three tempos, and six three-way choices");
    }

    [Fact]
    public void The_bounds_leave_attributes_in_charge()
    {
        // The whole point of the bounds: the best possible instructions must not overturn a substantial
        // difference in ability. Three attribute points is roughly a division's worth of quality.
        var rules = EngineRulesV1.Default;

        var better = RatingsFor(ability: 16, new MatchInstructionsV1());
        var worse = RatingsFor(ability: 9, new MatchInstructionsV1());

        better.BuildUp.Should().BeGreaterThan(worse.BuildUp);
        better.Creation.Should().BeGreaterThan(worse.Creation);
        better.Finishing.Should().BeGreaterThan(worse.Finishing);
        better.DefensiveShape.Should().BeGreaterThan(worse.DefensiveShape);
        better.Goalkeeping.Should().BeGreaterThan(worse.Goalkeeping);

        var worstCase = new MatchInstructionsV1
        {
            Mentality = MatchMentality.Defensive,
            Tempo = MatchTempo.Low,
            Passing = MatchPassingStyle.DirectPassing,
            Width = MatchWidth.Narrow,
            Pressing = MatchPressing.LowBlock,
            DefensiveLine = MatchDefensiveLine.Deep,
            Tackling = MatchTacklingStyle.StayOnFeet,
            TimeWasting = MatchTimeWasting.On,
        };

        var bestCase = new MatchInstructionsV1
        {
            Mentality = MatchMentality.Attacking,
            Tempo = MatchTempo.High,
            Passing = MatchPassingStyle.ShortPassing,
            Width = MatchWidth.Wide,
            Pressing = MatchPressing.HighPress,
            DefensiveLine = MatchDefensiveLine.High,
            Tackling = MatchTacklingStyle.Aggressive,
            TimeWasting = MatchTimeWasting.Off,
        };

        var hampered = UnitRatingCalculator.Calculate(RatingsLineup(16), worstCase, isHome: false, rules);
        var helped = UnitRatingCalculator.Calculate(RatingsLineup(9), bestCase, isHome: true, rules);

        hampered.BuildUp.Should().BeGreaterThan(
            helped.BuildUp,
            "a whole division of quality is worth more than any instruction set");
    }

    [Fact]
    public void Home_advantage_raises_every_rating()
    {
        var rules = EngineRulesV1.Default;
        var instructions = new MatchInstructionsV1();

        var away = UnitRatingCalculator.Calculate(RatingsLineup(13), instructions, isHome: false, rules);
        var home = UnitRatingCalculator.Calculate(RatingsLineup(13), instructions, isHome: true, rules);

        home.BuildUp.Should().BeGreaterThan(away.BuildUp);
        home.DefensivePressure.Should().BeGreaterThan(away.DefensivePressure);
        home.Goalkeeping.Should().BeGreaterThan(away.Goalkeeping);
    }

    [Fact]
    public void A_tired_player_rates_lower_than_a_fresh_one()
    {
        var rules = EngineRulesV1.Default;
        var instructions = new MatchInstructionsV1();

        var fresh = RatingsLineup(13);
        var tired = fresh
            .Select(slot => slot with
            {
                Condition = new PlayerCondition(3_000, 7_000, 2_000, 2_000),
            })
            .ToList();

        var freshRatings = UnitRatingCalculator.Calculate(fresh, instructions, isHome: false, rules);
        var tiredRatings = UnitRatingCalculator.Calculate(tired, instructions, isHome: false, rules);

        tiredRatings.Fitness.Should().BeLessThan(freshRatings.Fitness);
        tiredRatings.Creation.Should().BeLessThan(freshRatings.Creation);
    }

    [Fact]
    public void A_side_with_ten_men_rates_lower_than_one_with_eleven()
    {
        var rules = EngineRulesV1.Default;
        var instructions = new MatchInstructionsV1();

        var eleven = RatingsLineup(13);
        var ten = eleven.Skip(1).ToList();

        var full = UnitRatingCalculator.Calculate(eleven, instructions, isHome: false, rules);
        var depleted = UnitRatingCalculator.Calculate(ten, instructions, isHome: false, rules);

        depleted.Creation.Should().BeLessThan(full.Creation);
        depleted.Goalkeeping.Should().BeLessThan(full.Goalkeeping);
    }

    [Fact]
    public void A_side_with_no_goalkeeper_has_no_goalkeeping_rating()
    {
        // The consequence of a sending-off in goal. It is deliberately not a crash and not a free pass.
        var rules = EngineRulesV1.Default;
        var outfield = RatingsLineup(13).Skip(1).ToList();

        var ratings = UnitRatingCalculator.Calculate(outfield, new MatchInstructionsV1(), isHome: false, rules);

        ratings.Goalkeeping.Should().Be(0);
    }

    [Fact]
    public void An_out_of_position_player_costs_ratings_and_cohesion()
    {
        var rules = EngineRulesV1.Default;
        var instructions = new MatchInstructionsV1();

        var inPosition = RatingsLineup(13);

        var makeshift = inPosition
            .Select(slot => slot.Slot.SlotNumber == 10
                ? slot with { FamiliarityBasisPoints = rules.OutOfPositionPenaltyBasisPoints }
                : slot)
            .ToList();

        var natural = UnitRatingCalculator.Calculate(inPosition, instructions, isHome: false, rules);
        var weakened = UnitRatingCalculator.Calculate(makeshift, instructions, isHome: false, rules);

        weakened.Finishing.Should().BeLessThan(natural.Finishing);
        weakened.Cohesion.Should().BeLessThan(natural.Cohesion);
    }

    [Fact]
    public void A_winger_in_a_wing_back_slot_keeps_full_familiarity()
    {
        // Secondary positions are there to be used: a winger covering wing back is familiar, not out of position.
        var rules = EngineRulesV1.Default;
        var side = TestMatchFactory.Side(7, "Wing Town", 13, new MatchInstructionsV1());

        var winger = side.Squad.First(participant => participant.Position == MatchPosition.LeftWinger);
        var slot = new MatchSlotV1
        {
            SlotNumber = 2,
            Family = MatchPositionFamily.Defence,
            Role = MatchRole.WingBack,
            X = 8_000,
            Y = 2_600,
            ParticipantId = winger.ParticipantId,
        };

        LineupResolver.FamiliarityOf(winger with { SecondaryPositions = [MatchPosition.LeftBack] }, slot, rules)
            .Should().Be(rules.SecondaryPositionPenaltyBasisPoints);
    }

    [Fact]
    public void A_striker_slotted_into_central_midfield_is_out_of_position()
    {
        var rules = EngineRulesV1.Default;
        var side = TestMatchFactory.Side(7, "Wing Town", 13, new MatchInstructionsV1());

        var striker = side.Squad.First(participant => participant.Position == MatchPosition.Striker);
        var slot = side.Slots.First(candidate => candidate.SlotNumber == 7);

        LineupResolver.FamiliarityOf(striker, slot, rules)
            .Should().Be(rules.OutOfPositionPenaltyBasisPoints);
    }

    [Fact]
    public void Ratings_never_exceed_the_configured_ceiling()
    {
        var rules = EngineRulesV1.Default;
        var maximum = PlayerAttributesV1.Uniform(MatchAttributeNames.Max);

        var slots = RatingsLineup(13)
            .Select(slot => slot with { Participant = slot.Participant with { Attributes = maximum } })
            .ToList();

        var ratings = UnitRatingCalculator.Calculate(slots, new MatchInstructionsV1(), isHome: true, rules);

        foreach (var unit in Enum.GetValues<MatchUnit>())
        {
            ratings.Of(unit).Should().BeInRange(0, rules.MaxUnitRating);
        }
    }

    private static IReadOnlyList<ActiveSlot> RatingsLineup(int ability) =>
        [.. LineupResolver
            .Resolve(TestMatchFactory.Side(11, "Rating Town", ability, new MatchInstructionsV1()), MatchSide.Home, EngineRulesV1.Default)
            .Slots
            .Select(ActiveSlot.From)];

    private static MatchUnitRatings RatingsFor(int ability, MatchInstructionsV1 instructions) =>
        UnitRatingCalculator.Calculate(RatingsLineup(ability), instructions, isHome: false, EngineRulesV1.Default);
}
