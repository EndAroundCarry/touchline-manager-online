using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Who the player on the ball plays it to (`engine-v10`): a person at each end of every leg of the approach, chosen
/// from what the holder sees and how well he weighs it, and kept out of the play stream.
/// </summary>
public sealed class ReceiverChooserTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    // A build-up from the back, up the middle, to a point on the edge of the box: three legs.
    private static readonly SpatialPoint[] BuildUp =
    [
        new(3_000, 3_500),
        new(4_500, 2_600),
        new(6_000, 4_000),
        new(7_500, 3_500),
    ];

    [Fact]
    public void A_chain_has_a_leg_for_each_step_and_the_last_lands_where_it_was_planned()
    {
        var state = StateOf(TestMatchFactory.Even(), ordinal: 7);
        var holder = Midfielder(state);

        var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, holder.Participant.ParticipantId, lost: false)!;

        chain.Legs.Should().HaveCount(BuildUp.Length - 1);
        chain.Carrier.Should().Be(holder.Participant.ParticipantId);
        chain.Legs[0].Passer.Should().Be(holder.Participant.ParticipantId);
        chain.Legs[^1].To.Should().Be(BuildUp[^1], "the last touch is never pulled, so the pressure point stays where the plan put it");
    }

    [Fact]
    public void Each_pass_is_played_by_the_man_who_was_given_the_ball_and_to_a_teammate_who_is_not_in_goal()
    {
        for (var ordinal = 1; ordinal <= 200; ordinal++)
        {
            var state = StateOf(TestMatchFactory.Even(), ordinal);
            var holder = Midfielder(state);
            var outfield = state.Home.Outfield.Select(slot => slot.Participant.ParticipantId).ToHashSet();

            var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, holder.Participant.ParticipantId, lost: false)!;
            var has = holder.Participant.ParticipantId;

            foreach (var leg in chain.Legs)
            {
                leg.Passer.Should().Be(has, "the player on the ball is the one who plays it");

                if (leg.Receiver is Guid receiver)
                {
                    receiver.Should().NotBe(leg.Passer);
                    outfield.Should().Contain(receiver);
                    has = receiver;
                }
            }

            chain.Holder.Should().Be(has);
        }
    }

    [Fact]
    public void The_same_possession_chooses_the_same_receivers_and_never_touches_the_play_stream()
    {
        var input = TestMatchFactory.Even();
        var first = StateOf(input, ordinal: 31);
        var second = StateOf(input, ordinal: 31);
        var holder = Midfielder(first).Participant.ParticipantId;

        var one = ReceiverChooser.Choose(first, MatchSide.Home, BuildUp, holder, lost: false)!;
        var two = ReceiverChooser.Choose(second, MatchSide.Home, BuildUp, holder, lost: false)!;

        one.Legs.Should().Equal(two.Legs);

        // Choosing consumed nothing from the play stream: its next draws are those of a fresh generator.
        var fresh = new Pcg32(input.Seed);

        for (var draw = 0; draw < 5; draw++)
        {
            first.Random.NextUInt32().Should().Be(fresh.NextUInt32());
        }
    }

    [Fact]
    public void A_different_possession_chooses_differently()
    {
        var holder = Midfielder(StateOf(TestMatchFactory.Even(), 1)).Participant.ParticipantId;
        var chains = Enumerable.Range(1, 40)
            .Select(ordinal => ReceiverChooser.Choose(StateOf(TestMatchFactory.Even(), ordinal), MatchSide.Home, BuildUp, holder, lost: false)!)
            .Select(chain => string.Join(',', chain.Legs.Select(leg => leg.Receiver)))
            .ToHashSet();

        chains.Count.Should().BeGreaterThan(5);
    }

    [Theory]
    [InlineData(MatchPassFocus.Wings)]
    [InlineData(MatchPassFocus.Centre)]
    [InlineData(MatchPassFocus.CentreAndLeft)]
    public void A_pulled_touch_stays_on_the_pitch_and_in_the_lane_it_was_planned_in(MatchPassFocus focus)
    {
        foreach (var isHome in new[] { true, false })
        {
            var input = TestMatchFactory.WithInstructions(
                new MatchInstructionsV1 { PassFocus = focus },
                new MatchInstructionsV1 { PassFocus = focus });
            var side = isHome ? MatchSide.Home : MatchSide.Away;

            for (var ordinal = 1; ordinal <= 150; ordinal++)
            {
                var state = StateOf(input, ordinal);
                var runtime = state.SideOf(side);
                var points = BuildUp.Select(point => PassagePlanner.FromAttack(point.X, LaneOf(point.Y, ordinal), isHome)).ToArray();
                var holder = runtime.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Midfield).Participant.ParticipantId;

                var chain = ReceiverChooser.Choose(state, side, points, holder, lost: false)!;

                for (var index = 0; index < chain.Legs.Count; index++)
                {
                    var landed = chain.Legs[index].To;
                    var planned = points[index + 1];

                    landed.X.Should().BeInRange(0, SpatialPitch.PitchLength);
                    landed.Y.Should().BeInRange(0, SpatialPitch.PitchWidth);
                    LaneIndex(landed, isHome).Should().Be(LaneIndex(planned, isHome), "a pull never takes the ball out of its lane");
                }
            }
        }
    }

    [Fact]
    public void A_better_Vision_sees_more_of_the_pitch_and_a_further_teammate_is_seen_less()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var lowest = WithAttribute(Midfielder(state), MatchAttributeName.Vision, 1);
        var highest = WithAttribute(Midfielder(state), MatchAttributeName.Vision, 20);

        ReceiverChooser.SeeChance(highest, 1_000, Rules).Should().BeGreaterThan(ReceiverChooser.SeeChance(lowest, 1_000, Rules));
        ReceiverChooser.SeeChance(lowest, 0, Rules).Should().BeInRange(Rules.ReceiverSeeLowestBasisPoints, Rules.ReceiverSeeHighestBasisPoints);
        ReceiverChooser.SeeChance(highest, 0, Rules).Should().BeInRange(Rules.ReceiverSeeLowestBasisPoints, Rules.ReceiverSeeHighestBasisPoints);
        ReceiverChooser.SeeChance(highest, 5_000, Rules).Should().BeLessThan(ReceiverChooser.SeeChance(highest, 500, Rules));
        ReceiverChooser.SeeChance(highest, 100_000, Rules)
            .Should().Be(ReceiverChooser.SeeChance(highest, Rules.ReceiverSeeFullDistance, Rules), "the loss with distance stops at its full value");
    }

    [Fact]
    public void A_better_Decisions_favours_the_best_placed_teammate_more_sharply()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var lowest = ReceiverChooser.ChoiceGain(WithAttribute(Midfielder(state), MatchAttributeName.Decisions, 1), Rules);
        var highest = ReceiverChooser.ChoiceGain(WithAttribute(Midfielder(state), MatchAttributeName.Decisions, 20), Rules);

        highest.Should().BeGreaterThan(lowest);
        lowest.Should().BeInRange(Rules.ReceiverChoiceGainLowest, Rules.ReceiverChoiceGainHighest);
        highest.Should().BeInRange(Rules.ReceiverChoiceGainLowest, Rules.ReceiverChoiceGainHighest);

        // The odds of the best against the worst: the sharper the holder, the longer they are, and a worse score is
        // never impossible.
        var oddsAtLowest = (double)ReceiverChooser.ChoiceWeight(EngineRulesV2.Certain, lowest) / ReceiverChooser.ChoiceWeight(0, lowest);
        var oddsAtHighest = (double)ReceiverChooser.ChoiceWeight(EngineRulesV2.Certain, highest) / ReceiverChooser.ChoiceWeight(0, highest);

        oddsAtHighest.Should().BeGreaterThan(oddsAtLowest * 2);
        ReceiverChooser.ChoiceWeight(0, highest).Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_holder_who_weighs_it_better_gives_the_ball_to_a_more_open_man()
    {
        var poor = MeanOpenness(vision: 20, decisions: 1);
        var good = MeanOpenness(vision: 20, decisions: 20);

        good.Should().BeGreaterThan(poor + 100, "a sharper choice among the same teammates is a more open receiver");
    }

    [Fact]
    public void A_holder_who_sees_more_gives_the_ball_to_a_more_open_man()
    {
        var blind = MeanOpenness(vision: 1, decisions: 20);
        var sighted = MeanOpenness(vision: 20, decisions: 20);

        sighted.Should().BeGreaterThan(blind + 50, "the more he sees, the better the best of what he sees");
    }

    [Fact]
    public void A_holder_who_sees_less_keeps_the_ball_more_often()
    {
        SoloShare(vision: 1).Should().BeGreaterThan(SoloShare(vision: 20), "he did not see them");
    }

    [Fact]
    public void A_defender_is_never_given_the_ball_beyond_the_halfway_line()
    {
        for (var ordinal = 1; ordinal <= 300; ordinal++)
        {
            var state = StateOf(TestMatchFactory.Even(), ordinal);
            var families = state.Home.Outfield.ToDictionary(slot => slot.Participant.ParticipantId, slot => slot.Slot.Family);
            var holder = Midfielder(state).Participant.ParticipantId;

            var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, holder, lost: false)!;

            foreach (var leg in chain.Legs.Where(leg => leg.Receiver is not null && PassagePlanner.AttackingX(leg.To.X, true) > Rules.DefenderReceiveMaxPointX))
            {
                families[leg.Receiver!.Value].Should().NotBe(MatchPositionFamily.Defence);
            }
        }
    }

    [Fact]
    public void With_nobody_to_play_it_to_the_holder_keeps_the_ball_to_the_end()
    {
        var state = StateOf(TestMatchFactory.Even(), 3);
        var holder = Midfielder(state);

        state.Home.Active.RemoveAll(slot => slot.Slot.Family != MatchPositionFamily.Goalkeeper && slot != holder);

        var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, holder.Participant.ParticipantId, lost: false)!;

        chain.Legs.Should().OnlyContain(leg => !leg.IsPass && leg.Passer == holder.Participant.ParticipantId);
        chain.Passers.Should().BeEmpty();
        chain.Holder.Should().Be(holder.Participant.ParticipantId);
    }

    [Fact]
    public void A_goalkeeper_cannot_start_a_chain_and_neither_can_a_man_who_is_off()
    {
        var state = StateOf(TestMatchFactory.Even(), 3);
        var keeper = state.Home.Goalkeeper!;

        ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, keeper.Participant.ParticipantId, lost: false).Should().BeNull();
        ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, Guid.NewGuid(), lost: false).Should().BeNull();
    }

    [Fact]
    public void A_path_with_no_leg_has_a_chain_with_no_leg()
    {
        var state = StateOf(TestMatchFactory.Even(), 3);
        var holder = Midfielder(state).Participant.ParticipantId;

        var chain = ReceiverChooser.Choose(state, MatchSide.Home, [BuildUp[0]], holder, lost: true)!;

        chain.Legs.Should().BeEmpty();
        chain.Holder.Should().Be(holder);
    }

    [Fact]
    public void A_pass_that_is_lost_is_likelier_a_poor_passers_and_one_that_found_his_man_a_good_ones()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var poor = WithAttribute(Midfielder(state), MatchAttributeName.Passing, 4);
        var good = WithAttribute(Midfielder(state), MatchAttributeName.Passing, 18);

        ReceiverChooser.HandsWeight(1_000, good, lostNext: false, Rules).Should().BeGreaterThan(ReceiverChooser.HandsWeight(1_000, poor, lostNext: false, Rules));
        ReceiverChooser.HandsWeight(1_000, poor, lostNext: true, Rules).Should().BeGreaterThan(ReceiverChooser.HandsWeight(1_000, good, lostNext: true, Rules));
        ReceiverChooser.HandsWeight(1, good, lostNext: false, Rules).Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_goal_is_set_up_by_the_man_who_has_the_ball_at_the_end_of_the_chain_or_the_one_who_gave_it_to_him()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();

        var passing = new PossessionPassing(MatchSide.Home);

        passing.CreatorFor(three).Should().BeNull("with no chain there is nobody to credit but the draw");
        passing.PasserOf(1).Should().BeNull();

        passing.Chained(new ReceiverChain(
            [new ReceiverLeg(one, two, BuildUp[1]), new ReceiverLeg(two, three, BuildUp[2])],
            one,
            three));

        passing.PasserOf(1).Should().Be(one);
        passing.PasserOf(2).Should().Be(two);
        passing.PasserOf(3).Should().Be(three, "every pass after the approach is the man on the ball's");
        passing.CreatorFor(one).Should().Be(three);
        passing.CreatorFor(three).Should().Be(two, "he cannot assist his own goal, so it is the man who passed to him");

        var solo = new PossessionPassing(MatchSide.Home);

        solo.Chained(new ReceiverChain([new ReceiverLeg(one, null, BuildUp[1])], one, one));
        solo.CreatorFor(one).Should().BeNull("a man who took it all the way has nobody to share it with");
    }

    [Fact]
    public void The_receiver_stream_is_not_the_geometry_stream_or_the_pass_stream()
    {
        var state = StateOf(TestMatchFactory.Even(), 9);

        var receiver = ReceiverChooser.CreateStream(state).NextUInt32();
        var geometry = PassagePlanner.CreateStream(state).NextUInt32();

        receiver.Should().NotBe(geometry);
    }

    [Fact]
    public void The_rules_for_choosing_a_receiver_are_validated()
    {
        Rules.Invoking(rules => rules.Validate()).Should().NotThrow();

        (Rules with { ReceiverSeeFullDistance = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ReceiverSeeFullDistance*");
        (Rules with { ReceiverSeeLowestBasisPoints = Rules.ReceiverSeeHighestBasisPoints + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*sight*");
        (Rules with { ReceiverPullBasisPoints = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ReceiverPullBasisPoints*");
        (Rules with { ReceiverOpennessWeight = 0, ReceiverProgressWeight = 0, ReceiverReachWeight = 0, ReceiverLaneWeight = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*score weights*");
        (Rules with { ReceiverChoiceGainHighest = Rules.ReceiverChoiceGainLowest - 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*gains*");
    }

    /// <summary>Gets the share of legs a holder of a given Vision keeps the ball on, over many possessions.</summary>
    private static double SoloShare(int vision)
    {
        var legs = 0;
        var solo = 0;

        for (var ordinal = 1; ordinal <= 1_000; ordinal++)
        {
            var state = StateOf(TestMatchFactory.Even(), ordinal);
            var holder = WithAttribute(Midfielder(state), MatchAttributeName.Vision, vision);

            state.Home.Active[state.Home.Active.IndexOf(Midfielder(state))] = holder;

            var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, holder.Participant.ParticipantId, lost: false)!;

            legs += chain.Legs.Count;
            solo += chain.Legs.Count(leg => !leg.IsPass);
        }

        return (double)solo / legs;
    }

    /// <summary>Gets how open the man the holder gave the first ball to is, on average over many possessions.</summary>
    private static double MeanOpenness(int vision, int decisions)
    {
        var total = 0L;
        var count = 0;

        for (var ordinal = 1; ordinal <= 2_000; ordinal++)
        {
            var state = StateOf(TestMatchFactory.Even(), ordinal);
            var holder = Midfielder(state);
            var index = state.Home.Active.IndexOf(holder);
            var edited = WithAttribute(WithAttribute(holder, MatchAttributeName.Vision, vision), MatchAttributeName.Decisions, decisions);

            state.Home.Active[index] = edited;

            var chain = ReceiverChooser.Choose(state, MatchSide.Home, BuildUp, edited.Participant.ParticipantId, lost: false)!;

            if (chain.Legs[0].Receiver is not Guid receiverId)
            {
                continue;
            }

            var receiver = state.Home.Active.First(slot => slot.Participant.ParticipantId == receiverId);
            var defenders = OffBallModel.Place(
                state.Away.Active, isHome: false, hasPossession: false, BuildUp[1], state.Away.Instructions, Rules);

            total += OffBallModel.Openness(chain.Legs[0].To, receiver, defenders, Rules);
            count++;
        }

        count.Should().BeGreaterThan(1_500, "most possessions have somebody to play to");

        return (double)total / count;
    }

    private static ActiveSlot Midfielder(MatchState state) =>
        state.Home.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Midfield);

    private static ActiveSlot WithAttribute(ActiveSlot slot, MatchAttributeName attribute, int value)
    {
        var values = slot.Participant.Attributes.Values.ToArray();

        values[(int)attribute] = value;

        return slot with { Participant = slot.Participant with { Attributes = PlayerAttributesV1.From(values) } };
    }

    /// <summary>Moves a lateral position of the build-up into a different lane for each possession, so all three are tried.</summary>
    private static int LaneOf(int y, int ordinal) =>
        Math.Clamp(y + (((ordinal % 3) - 1) * 2_500), 100, SpatialPitch.PitchWidth - 100);

    private static int LaneIndex(SpatialPoint point, bool isHome)
    {
        var y = PassagePlanner.AttackingY(point.Y, isHome);

        return y < Rules.PassLeftLaneMaxYBasisPoints ? 0 : y >= Rules.PassRightLaneMinYBasisPoints ? 2 : 1;
    }

    private static MatchState StateOf(MatchInputV1 input, int ordinal)
    {
        var home = BuildSide(input.Home, MatchSide.Home);
        var away = BuildSide(input.Away, MatchSide.Away);

        return new MatchState(input, Rules, new Pcg32(input.Seed), home, away) { PossessionOrdinal = ordinal };
    }

    private static SideRuntime BuildSide(MatchSideV1 side, MatchSide which)
    {
        var lineup = LineupResolver.Resolve(side, which, Rules);
        var runtime = new SideRuntime { Which = which, Lineup = lineup, Bench = [.. lineup.Bench] };

        foreach (var slot in lineup.Slots)
        {
            runtime.Active.Add(ActiveSlot.From(slot));
        }

        runtime.RecalculateRatings(Rules);

        return runtime;
    }
}
