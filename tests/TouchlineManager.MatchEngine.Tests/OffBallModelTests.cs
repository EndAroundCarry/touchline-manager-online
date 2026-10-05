using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The off-ball model (`engine-v10`): where players stand, how open a receiver is, whether he can get to a pass, and
/// who may be given the ball at all. Nothing in the engine calls it yet, so none of these tests moves a match.
/// </summary>
public sealed class OffBallModelTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;
    private static readonly MatchInstructionsV1 Balanced = new();

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 1L)]
    [InlineData(3L, 1L)]
    [InlineData(4L, 2L)]
    [InlineData(99_999_999L, 9_999L)]
    [InlineData(100_000_000L, 10_000L)]
    public void The_integer_square_root_rounds_down(long value, long expected)
    {
        SpatialMath.Sqrt(value).Should().Be(expected);
    }

    [Fact]
    public void The_integer_square_root_brackets_every_value_it_is_given()
    {
        for (var value = 0L; value < 20_000; value += 7)
        {
            var root = SpatialMath.Sqrt(value);

            (root * root).Should().BeLessThanOrEqualTo(value);
            ((root + 1) * (root + 1)).Should().BeGreaterThan(value);
        }
    }

    [Fact]
    public void A_distance_is_symmetric_and_whole()
    {
        var one = new SpatialPoint(100, 100);
        var other = new SpatialPoint(400, 500);

        SpatialMath.Distance(one, other).Should().Be(500);
        SpatialMath.Distance(other, one).Should().Be(500);
        SpatialMath.Distance(one, one).Should().Be(0);
    }

    [Fact]
    public void A_side_is_placed_in_order_and_inside_the_pitch()
    {
        var side = Eleven();
        var placed = OffBallModel.Place(side, isHome: true, hasPossession: true, new SpatialPoint(7_000, 1_000), Balanced, Rules);

        placed.Select(entry => entry.Player).Should().Equal(side);
        placed.Should().OnlyContain(entry =>
            entry.Spot.X >= 0 && entry.Spot.X <= SpatialPitch.PitchLength
            && entry.Spot.Y >= 0 && entry.Spot.Y <= SpatialPitch.PitchWidth);
    }

    [Fact]
    public void The_same_ball_places_a_side_the_same_way_every_time()
    {
        var side = Eleven();
        var ball = new SpatialPoint(6_200, 4_100);

        OffBallModel.Place(side, true, true, ball, Balanced, Rules)
            .Should().Equal(OffBallModel.Place(side, true, true, ball, Balanced, Rules));
    }

    [Theory]
    [InlineData(true, 2_000, 1_000)]
    [InlineData(true, 5_000, 3_500)]
    [InlineData(true, 8_800, 6_000)]
    [InlineData(false, 3_000, 5_500)]
    public void A_side_stands_where_its_mirror_image_stands_at_the_other_end(bool hasPossession, int ballX, int ballY)
    {
        var side = Eleven();
        var mirroredBall = new SpatialPoint(SpatialPitch.PitchLength - ballX, SpatialPitch.PitchWidth - ballY);

        var home = OffBallModel.Place(side, true, hasPossession, new SpatialPoint(ballX, ballY), Balanced, Rules);
        var away = OffBallModel.Place(side, false, hasPossession, mirroredBall, Balanced, Rules);

        for (var index = 0; index < side.Count; index++)
        {
            away[index].Spot.Should().Be(
                new SpatialPoint(
                    SpatialPitch.PitchLength - home[index].Spot.X,
                    SpatialPitch.PitchWidth - home[index].Spot.Y),
                "slot {0} is the same player seen from the other end", index);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_block_moves_up_the_pitch_with_the_ball_whichever_way_it_attacks(bool isHome)
    {
        var side = Eleven();
        var ownEnd = PassagePlanner.FromAttack(1_500, 3_500, isHome);
        var otherEnd = PassagePlanner.FromAttack(8_500, 3_500, isHome);

        var deep = OffBallModel.Place(side, isHome, true, ownEnd, Balanced, Rules);
        var high = OffBallModel.Place(side, isHome, true, otherEnd, Balanced, Rules);

        for (var index = 1; index < side.Count; index++)
        {
            PassagePlanner.AttackingX(high[index].Spot.X, isHome)
                .Should().BeGreaterThan(PassagePlanner.AttackingX(deep[index].Spot.X, isHome));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_attacking_side_in_possession_stands_higher_than_the_same_side_defending(bool isHome)
    {
        var side = Eleven();
        var ball = PassagePlanner.FromAttack(5_000, 3_500, isHome);
        var instructions = new MatchInstructionsV1 { Mentality = MatchMentality.Attacking };

        var attacking = OffBallModel.Place(side, isHome, true, ball, instructions, Rules);
        var defending = OffBallModel.Place(side, isHome, false, ball, instructions, Rules);

        for (var index = 1; index < side.Count; index++)
        {
            PassagePlanner.AttackingX(attacking[index].Spot.X, isHome)
                .Should().BeGreaterThan(PassagePlanner.AttackingX(defending[index].Spot.X, isHome));
        }
    }

    [Fact]
    public void A_receiver_is_less_open_the_closer_a_defender_stands()
    {
        var receiver = Player(MatchPositionFamily.Midfield, positioning: 10);
        var target = new SpatialPoint(6_000, 3_500);
        var previous = int.MinValue;

        foreach (var gap in new[] { 0, 200, 500, 900, 1_300 })
        {
            var defenders = new[] { Defender(new SpatialPoint(6_000 + gap, 3_500)) };
            var openness = OffBallModel.Openness(target, receiver, defenders, Rules);

            openness.Should().BeGreaterThanOrEqualTo(previous);
            openness.Should().BeInRange(0, EngineRulesV2.Certain);
            previous = openness;
        }

        previous.Should().Be(EngineRulesV2.Certain, "a defender further off than the full distance leaves him wholly free");
    }

    [Fact]
    public void A_receiver_with_a_defender_on_him_is_not_open_at_all()
    {
        var receiver = Player(MatchPositionFamily.Attack, positioning: 20);
        var target = new SpatialPoint(7_000, 3_500);

        OffBallModel.Openness(target, receiver, [Defender(target)], Rules).Should().Be(0);
    }

    [Fact]
    public void A_better_reader_of_the_game_closes_a_receiver_down_more()
    {
        var receiver = Player(MatchPositionFamily.Attack, positioning: 10);
        var target = new SpatialPoint(7_000, 3_500);
        var spot = new SpatialPoint(7_600, 3_500);

        var sharp = OffBallModel.Openness(target, receiver, [Defender(spot, marking: 20, positioning: 20)], Rules);
        var poor = OffBallModel.Openness(target, receiver, [Defender(spot, marking: 1, positioning: 1)], Rules);

        sharp.Should().BeLessThan(poor);
    }

    [Fact]
    public void A_receiver_who_finds_space_is_more_open_than_one_who_does_not()
    {
        var target = new SpatialPoint(7_000, 3_500);
        var defenders = new[] { Defender(new SpatialPoint(7_700, 3_500)) };

        var finds = OffBallModel.Openness(target, Player(MatchPositionFamily.Attack, positioning: 20), defenders, Rules);
        var loses = OffBallModel.Openness(target, Player(MatchPositionFamily.Attack, positioning: 1), defenders, Rules);

        finds.Should().BeGreaterThan(loses);
    }

    [Fact]
    public void The_goalkeeper_marks_nobody_and_an_empty_defence_leaves_the_point_free()
    {
        var receiver = Player(MatchPositionFamily.Midfield, positioning: 10);
        var target = new SpatialPoint(9_000, 3_500);
        var keeper = new OffBallPlayer(Player(MatchPositionFamily.Goalkeeper, positioning: 10), target);

        OffBallModel.Openness(target, receiver, [keeper], Rules).Should().Be(EngineRulesV2.Certain);
        OffBallModel.Openness(target, receiver, [], Rules).Should().Be(EngineRulesV2.Certain);
    }

    [Fact]
    public void A_holder_is_under_pressure_only_when_a_defender_is_close()
    {
        var holder = new SpatialPoint(5_000, 3_500);

        OffBallModel.IsUnderPressure(holder, [Defender(new SpatialPoint(5_200, 3_500))], Rules).Should().BeTrue();
        OffBallModel.IsUnderPressure(holder, [Defender(new SpatialPoint(6_500, 3_500))], Rules).Should().BeFalse();
        OffBallModel.IsUnderPressure(holder, [], Rules).Should().BeFalse();
    }

    [Fact]
    public void A_well_placed_player_gets_to_a_pass_a_poorly_placed_one_cannot()
    {
        var target = new SpatialPoint(5_000 + 3_300, 3_500);
        var spot = new SpatialPoint(5_000, 3_500);

        var good = new OffBallPlayer(Player(MatchPositionFamily.Midfield, positioning: 20), spot);
        var poor = new OffBallPlayer(Player(MatchPositionFamily.Midfield, positioning: 1), spot);

        OffBallModel.IsWithinReach(good, target, Rules).Should().BeTrue();
        OffBallModel.IsWithinReach(poor, target, Rules).Should().BeFalse();
        OffBallModel.MaxReach(good.Player, Rules).Should().BeGreaterThan(OffBallModel.MaxReach(poor.Player, Rules));
    }

    [Fact]
    public void The_reach_score_is_full_on_the_spot_and_nothing_at_the_limit()
    {
        var player = Player(MatchPositionFamily.Midfield, positioning: 10);
        var spot = new SpatialPoint(5_000, 3_500);
        var receiver = new OffBallPlayer(player, spot);
        var limit = new SpatialPoint(5_000 + OffBallModel.MaxReach(player, Rules), 3_500);

        OffBallModel.ReachScore(receiver, spot, Rules).Should().Be(EngineRulesV2.Certain);
        OffBallModel.ReachScore(receiver, limit, Rules).Should().Be(0);
        OffBallModel.ReachScore(receiver, new SpatialPoint(9_900, 3_500), Rules).Should().Be(0);
    }

    [Theory]
    [InlineData(true, 4_000, 6_000, 2_000)]
    [InlineData(true, 6_000, 4_500, -1_500)]
    [InlineData(false, 6_000, 4_000, 2_000)]
    [InlineData(false, 4_000, 5_500, -1_500)]
    public void Progress_is_measured_up_the_pitch_for_the_side_that_is_attacking(bool isHome, int holderX, int targetX, int expected)
    {
        OffBallModel.Progress(new SpatialPoint(holderX, 3_500), new SpatialPoint(targetX, 3_500), isHome)
            .Should().Be(expected);
    }

    [Fact]
    public void Only_a_ball_that_goes_forward_earns_a_progress_score()
    {
        var holder = new SpatialPoint(4_000, 3_500);

        OffBallModel.ProgressScore(holder, new SpatialPoint(3_000, 3_500), true, Rules).Should().Be(0);
        OffBallModel.ProgressScore(holder, new SpatialPoint(4_000, 1_000), true, Rules).Should().Be(0);
        OffBallModel.ProgressScore(holder, new SpatialPoint(4_000 + (Rules.OffBallProgressFullGain / 2), 3_500), true, Rules)
            .Should().Be(EngineRulesV2.Certain / 2);
        OffBallModel.ProgressScore(holder, new SpatialPoint(9_000, 3_500), true, Rules).Should().Be(EngineRulesV2.Certain);
    }

    // The depth rule, on the side's own scale: [holder X, receive X, family, under pressure, allowed].
    [Theory]
    [InlineData(6_000, 6_000, MatchPositionFamily.Midfield, false, true)]
    [InlineData(6_000, 7_500, MatchPositionFamily.Attack, false, true)]
    [InlineData(6_000, 5_600, MatchPositionFamily.Midfield, false, true)]
    [InlineData(6_000, 5_200, MatchPositionFamily.Midfield, false, false)]
    [InlineData(6_000, 5_200, MatchPositionFamily.Midfield, true, true)]
    [InlineData(6_000, 5_200, MatchPositionFamily.Attack, true, true)]
    [InlineData(6_000, 3_500, MatchPositionFamily.Midfield, true, true)]
    [InlineData(6_000, 3_400, MatchPositionFamily.Midfield, true, false)]
    [InlineData(6_000, 2_000, MatchPositionFamily.Attack, true, false)]
    [InlineData(6_000, 5_800, MatchPositionFamily.Defence, false, false)]
    [InlineData(6_000, 5_200, MatchPositionFamily.Defence, true, false)]
    [InlineData(3_000, 3_500, MatchPositionFamily.Defence, false, true)]
    [InlineData(3_000, 2_600, MatchPositionFamily.Defence, false, true)]
    [InlineData(3_000, 2_200, MatchPositionFamily.Defence, true, false)]
    [InlineData(4_500, 4_500, MatchPositionFamily.Defence, false, true)]
    [InlineData(4_600, 4_600, MatchPositionFamily.Defence, false, false)]
    [InlineData(3_000, 3_500, MatchPositionFamily.Goalkeeper, true, false)]
    public void The_depth_rule_says_who_may_be_given_the_ball(
        int holderX,
        int receiveX,
        MatchPositionFamily family,
        bool underPressure,
        bool allowed)
    {
        foreach (var isHome in new[] { true, false })
        {
            var holder = PassagePlanner.FromAttack(holderX, 3_500, isHome);
            var receive = PassagePlanner.FromAttack(receiveX, 3_500, isHome);

            OffBallModel.PassesDepthRule(family, holder, receive, isHome, underPressure, Rules)
                .Should().Be(allowed, "the rule reads the side's own scale, whichever end it attacks (home: {0})", isHome);
        }
    }

    [Fact]
    public void The_new_constants_are_validated()
    {
        Rules.Invoking(rules => rules.Validate()).Should().NotThrow();

        (Rules with { BackPassMaxDepth = Rules.BackPassFreeDepth - 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*back-pass*");
        (Rules with { OffBallOpennessFullDistance = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*OffBallOpennessFullDistance*");
        (Rules with { DefenderReceiveMaxHolderX = -1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*DefenderReceiveMaxHolderX*");
    }

    private static OffBallPlayer Defender(SpatialPoint spot, int marking = 10, int positioning = 10) =>
        new(Player(MatchPositionFamily.Defence, positioning, marking), spot);

    private static List<ActiveSlot> Eleven()
    {
        var families = new[]
        {
            MatchPositionFamily.Goalkeeper,
            MatchPositionFamily.Defence,
            MatchPositionFamily.Defence,
            MatchPositionFamily.Defence,
            MatchPositionFamily.Defence,
            MatchPositionFamily.Midfield,
            MatchPositionFamily.Midfield,
            MatchPositionFamily.Midfield,
            MatchPositionFamily.Midfield,
            MatchPositionFamily.Attack,
            MatchPositionFamily.Attack,
        };
        var xs = new[] { 500, 2_000, 2_000, 2_000, 2_000, 4_500, 4_500, 5_000, 5_000, 7_500, 7_500 };
        var ys = new[] { 5_000, 1_500, 3_800, 6_200, 8_500, 2_000, 4_000, 6_000, 8_000, 3_500, 6_500 };

        return [.. families.Select((family, index) => Player(family, 10, 10, xs[index], ys[index], index + 1))];
    }

    private static ActiveSlot Player(
        MatchPositionFamily family,
        int positioning,
        int marking = 10,
        int x = 5_000,
        int y = 5_000,
        int slotNumber = 6)
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();
        values[(int)MatchAttributeName.Positioning] = positioning;
        values[(int)MatchAttributeName.Marking] = marking;

        var participant = new MatchParticipantV1
        {
            ParticipantId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            ClubId = Guid.NewGuid(),
            DisplayName = "Test Player",
            ShirtNumber = slotNumber,
            Position = MatchPosition.Striker,
            Attributes = PlayerAttributesV1.From(values),
            State = PlayerMatchStateV1.Uniform(10_000),
        };

        return new ActiveSlot
        {
            Slot = new MatchSlotV1
            {
                SlotNumber = slotNumber,
                Family = family,
                Role = family switch
                {
                    MatchPositionFamily.Goalkeeper => MatchRole.Goalkeeper,
                    MatchPositionFamily.Defence => MatchRole.CentreBack,
                    MatchPositionFamily.Midfield => MatchRole.CentralMidfielder,
                    _ => MatchRole.Striker,
                },
                X = x,
                Y = y,
                ParticipantId = participant.ParticipantId,
            },
            Participant = participant,
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = new PlayerCondition(10_000, 0, 10_000, 10_000),
        };
    }
}
