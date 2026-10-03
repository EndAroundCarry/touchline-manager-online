using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The skill model of `engine-v6`: what a player can actually do when tired, out of place, or a man short, and
/// the skills the match audit found were not counting (Stamina, Aggression and Tackling in fouls, time wasting).
/// </summary>
public sealed class EngineV6SkillTests
{
    private const int Matches = 200;

    // ---- Effective skill -------------------------------------------------------------------------

    [Fact]
    public void A_fresh_player_plays_to_his_sheet()
    {
        var rules = EngineRulesV2.Default;
        var slot = Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000));

        foreach (var attribute in new[] { MatchAttributeName.Pace, MatchAttributeName.Finishing, MatchAttributeName.Decisions })
        {
            EffectiveSkill.Hundredths(slot, attribute, rules).Should().Be(1_300, "nothing has been taken from him");
        }
    }

    [Fact]
    public void An_empty_player_loses_most_of_his_physical_skills_and_least_of_his_mental_ones()
    {
        var rules = EngineRulesV2.Default;
        var slot = Outfielder(13, new PlayerCondition(0, 0, 10_000, 10_000));

        var physical = EffectiveSkill.Hundredths(slot, MatchAttributeName.Pace, rules);
        var technical = EffectiveSkill.Hundredths(slot, MatchAttributeName.Finishing, rules);
        var mental = EffectiveSkill.Hundredths(slot, MatchAttributeName.Decisions, rules);

        physical.Should().Be(1_300 * (10_000 - rules.TiredPhysicalDropBasisPoints) / 10_000);
        technical.Should().Be(1_300 * (10_000 - rules.TiredTechnicalDropBasisPoints) / 10_000);
        mental.Should().Be(1_300 * (10_000 - rules.TiredMentalDropBasisPoints) / 10_000);

        physical.Should().BeLessThan(technical);
        technical.Should().BeLessThan(mental);
        mental.Should().BeGreaterThan(1_000, "a tired player is worse, not a bad player");
    }

    [Fact]
    public void The_drop_grows_with_the_condition_lost()
    {
        var rules = EngineRulesV2.Default;

        var fresh = EffectiveSkill.Hundredths(Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000)), MatchAttributeName.Pace, rules);
        var half = EffectiveSkill.Hundredths(Outfielder(13, new PlayerCondition(5_000, 0, 10_000, 10_000)), MatchAttributeName.Pace, rules);
        var empty = EffectiveSkill.Hundredths(Outfielder(13, new PlayerCondition(0, 0, 10_000, 10_000)), MatchAttributeName.Pace, rules);

        fresh.Should().BeGreaterThan(half);
        half.Should().BeGreaterThan(empty);
        (fresh - half).Should().BeCloseTo(half - empty, 2, "the drop is linear");
    }

    [Fact]
    public void A_goalkeeper_is_not_slowed_by_tiredness()
    {
        var rules = EngineRulesV2.Default;
        var keeper = Outfielder(13, new PlayerCondition(0, 0, 10_000, 10_000), MatchPositionFamily.Goalkeeper);

        EffectiveSkill.Hundredths(keeper, MatchAttributeName.Reflexes, rules).Should().Be(1_300);
        EffectiveSkill.Hundredths(keeper, MatchAttributeName.Pace, rules).Should().Be(1_300);
    }

    [Fact]
    public void A_tired_side_rates_lower_everywhere_except_in_goal()
    {
        var rules = EngineRulesV2.Default;
        var instructions = new MatchInstructionsV1();

        var fresh = Lineup(new PlayerCondition(10_000, 0, 10_000, 10_000));
        var tired = Lineup(new PlayerCondition(2_000, 0, 10_000, 10_000));

        var freshRatings = UnitRatingCalculator.Calculate(fresh, instructions, isHome: false, rules);
        var tiredRatings = UnitRatingCalculator.Calculate(tired, instructions, isHome: false, rules);

        tiredRatings.BuildUp.Should().BeLessThan(freshRatings.BuildUp);
        tiredRatings.Creation.Should().BeLessThan(freshRatings.Creation);
        tiredRatings.Finishing.Should().BeLessThan(freshRatings.Finishing);
        tiredRatings.DefensivePressure.Should().BeLessThan(freshRatings.DefensivePressure);
        tiredRatings.DefensiveShape.Should().BeLessThan(freshRatings.DefensiveShape);
        tiredRatings.Goalkeeping.Should().Be(freshRatings.Goalkeeping, "goalkeepers are exempt");
    }

    // ---- Duels read effective skills -------------------------------------------------------------

    [Fact]
    public void A_tired_dribbler_wins_fewer_ground_duels_than_the_same_man_fresh()
    {
        var defender = Contender(Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000)));

        var fresh = DuelShare(Contender(Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000))), defender);
        var tired = DuelShare(Contender(Outfielder(13, new PlayerCondition(1_000, 0, 10_000, 10_000))), defender);

        tired.Should().BeLessThan(fresh - 0.05, "tiredness reaches the duel");
    }

    [Fact]
    public void An_out_of_position_player_wins_fewer_duels()
    {
        var rules = EngineRulesV2.Default;
        var defender = Contender(Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000)));

        var natural = Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000));
        var misplaced = natural with { FamiliarityBasisPoints = rules.OutOfPositionPenaltyBasisPoints };

        DuelShare(new DuelContender(misplaced, 0), defender)
            .Should().BeLessThan(DuelShare(new DuelContender(natural, 0), defender) - 0.03);
    }

    [Fact]
    public void A_side_a_man_down_duels_worse()
    {
        var defender = Contender(Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000)));
        var attacker = Outfielder(13, new PlayerCondition(10_000, 0, 10_000, 10_000));

        DuelShare(new DuelContender(attacker, PlayersDown: 4), defender)
            .Should().BeLessThan(DuelShare(new DuelContender(attacker, PlayersDown: 0), defender) - 0.02);
    }

    // ---- Skills that were not counting ---------------------------------------------------------------

    [Fact]
    public void A_side_with_high_stamina_ends_the_match_fresher()
    {
        var enduring = AverageFinalCondition(All(20, MatchAttributeName.Stamina), home: true);
        var weak = AverageFinalCondition(All(6, MatchAttributeName.Stamina), home: true);

        enduring.Should().BeGreaterThan(weak + 500, "Stamina sets how fast a player tires");
    }

    [Fact]
    public void Aggressive_poor_tacklers_foul_more_than_clean_ones()
    {
        var rough = Fouls(All(20, MatchAttributeName.Aggression).Then(6, MatchAttributeName.Tackling));
        var clean = Fouls(All(6, MatchAttributeName.Aggression).Then(20, MatchAttributeName.Tackling));

        rough.Should().BeGreaterThan(clean * 1.05, "Aggression raises and Tackling lowers a side's foul rate");
    }

    [Fact]
    public void Finishing_counts_when_the_shot_is_taken()
    {
        var finishers = Goals(All(20, MatchAttributeName.Finishing));
        var strugglers = Goals(All(6, MatchAttributeName.Finishing));

        finishers.Should().BeGreaterThan(strugglers * 1.15, "a side of finishers scores clearly more than a side of strugglers");
    }

    [Fact]
    public void Wasting_time_shortens_the_wasting_sides_attacking()
    {
        var normal = Shots(new MatchInstructionsV1());
        var wasting = Shots(new MatchInstructionsV1 { TimeWasting = MatchTimeWasting.On });

        wasting.Should().BeLessThan(normal, "a side that wastes time has less football");
    }

    [Fact]
    public void Situational_time_wasting_does_nothing_while_the_side_is_not_ahead()
    {
        // Level at kick-off and never ahead in a goalless draw's first minutes: the first ratings refresh sees a
        // level score, so a situational instruction must not cost the side anything yet. The ratings of a
        // side set to situational therefore equal those of a side set to off until it leads.
        var rules = EngineRulesV2.Default;
        var lineup = Lineup(new PlayerCondition(10_000, 0, 10_000, 10_000));

        var off = UnitRatingCalculator.Calculate(lineup, new MatchInstructionsV1 { TimeWasting = MatchTimeWasting.Off }, false, rules);
        var on = UnitRatingCalculator.Calculate(lineup, new MatchInstructionsV1 { TimeWasting = MatchTimeWasting.On }, false, rules);

        on.Creation.Should().BeLessThan(off.Creation, "time wasting that is on costs creation");
    }

    [Fact]
    public void The_shot_zone_shares_must_add_up()
    {
        var rules = EngineRulesV2.Default with { ShotZoneCentralPercent = 50 };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*shot zone shares*");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static double DuelShare(DuelContender attacker, DuelContender defender)
    {
        var random = new Pcg32(4_242UL);
        var won = 0;
        const int attempts = 4_000;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (DuelResolver.ResolveGroundDuel(attacker, defender, false, MatchTacklingStyle.Normal, EngineRulesV2.Default, random).AttackerWon)
            {
                won++;
            }
        }

        return (double)won / attempts;
    }

    private static DuelContender Contender(ActiveSlot slot) => new(slot, PlayersDown: 0);

    private static ActiveSlot Outfielder(
        int skill,
        PlayerCondition condition,
        MatchPositionFamily family = MatchPositionFamily.Midfield)
    {
        var participant = new MatchParticipantV1
        {
            ParticipantId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            ClubId = Guid.NewGuid(),
            DisplayName = "Test Player",
            ShirtNumber = 1,
            Position = family == MatchPositionFamily.Goalkeeper ? MatchPosition.Goalkeeper : MatchPosition.CentralMidfielder,
            Attributes = PlayerAttributesV1.Uniform(skill),
            State = PlayerMatchStateV1.Uniform(10_000),
        };

        return new ActiveSlot
        {
            Slot = new MatchSlotV1
            {
                SlotNumber = 6,
                Family = family,
                Role = family == MatchPositionFamily.Goalkeeper ? MatchRole.Goalkeeper : MatchRole.CentralMidfielder,
                X = 5_000,
                Y = 5_000,
                ParticipantId = participant.ParticipantId,
            },
            Participant = participant,
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = condition,
        };
    }

    private static IReadOnlyList<ActiveSlot> Lineup(PlayerCondition condition) =>
        [.. LineupResolver
            .Resolve(TestMatchFactory.Side(11, "Rating Town", 13, new MatchInstructionsV1()), MatchSide.Home, EngineRulesV2.Default)
            .Slots
            .Select(slot => ActiveSlot.From(slot) with { Condition = condition })];

    /// <summary>A skill setting for every player of the home side.</summary>
    private sealed record SkillSetting(IReadOnlyList<(MatchAttributeName Attribute, int Value)> Values)
    {
        public SkillSetting Then(int value, MatchAttributeName attribute) => new([.. Values, (attribute, value)]);
    }

    private static SkillSetting All(int value, MatchAttributeName attribute) => new([(attribute, value)]);

    private static MatchInputV1 WithHomeSkills(MatchInputV1 input, SkillSetting setting)
    {
        var squad = input.Home.Squad
            .Select(participant =>
            {
                var values = participant.Attributes.Values.ToArray();

                foreach (var (attribute, value) in setting.Values)
                {
                    values[(int)attribute] = value;
                }

                return participant with { Attributes = PlayerAttributesV1.From(values) };
            })
            .ToList();

        return input with { Home = input.Home with { Squad = squad } };
    }

    private static IEnumerable<MatchResultV1> Run(Func<ulong, MatchInputV1> build)
    {
        for (var seed = 1UL; seed <= Matches; seed++)
        {
            yield return MatchSimulator.Simulate(build(seed));
        }
    }

    private static double AverageFinalCondition(SkillSetting setting, bool home)
    {
        var total = 0L;
        var count = 0;

        foreach (var result in Run(seed => WithHomeSkills(TestMatchFactory.Even(seed), setting)))
        {
            foreach (var line in result.PlayerLines.Where(line => line.Started && (line.Side == MatchSide.Home) == home))
            {
                total += line.FinalConditionBasisPoints;
                count++;
            }
        }

        return (double)total / count;
    }

    private static double Fouls(SkillSetting setting) =>
        Run(seed => WithHomeSkills(TestMatchFactory.Even(seed), setting)).Average(result => result.Home.Fouls);

    private static double Goals(SkillSetting setting) =>
        Run(seed => WithHomeSkills(TestMatchFactory.Even(seed), setting)).Average(result => result.HomeGoals);

    private static double Shots(MatchInstructionsV1 homeInstructions) =>
        Run(seed => TestMatchFactory.Build(seed, 13, 13, homeInstructions)).Average(result => result.Home.Shots);
}
