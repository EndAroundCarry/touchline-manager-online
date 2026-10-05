using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// What the player on the ball does with it (`engine-v10`): pass, dribble on or shoot from distance, by how much each
/// is worth to him and how well his Decisions let him tell.
/// </summary>
public sealed class SoloPlayTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    // A build-up from the middle third to the edge of the box, whose last leg starts where a shot from distance is open.
    private static readonly SpatialPoint[] ToTheBox =
    [
        new(6_000, 3_500),
        new(7_300, 3_000),
        new(8_200, 3_500),
        new(8_400, 3_500),
    ];

    [Fact]
    public void A_holder_with_better_Decisions_takes_the_option_worth_most_more_often()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var lowest = SoloPlay.BestChoiceChance(WithAttribute(Midfielder(state), MatchAttributeName.Decisions, 1), Rules);
        var highest = SoloPlay.BestChoiceChance(WithAttribute(Midfielder(state), MatchAttributeName.Decisions, 20), Rules);

        highest.Should().BeGreaterThan(lowest);
        lowest.Should().BeInRange(Rules.SoloBestChoiceLowestBasisPoints, Rules.SoloBestChoiceHighestBasisPoints);
        highest.Should().BeInRange(Rules.SoloBestChoiceLowestBasisPoints, Rules.SoloBestChoiceHighestBasisPoints);

        // Over every draw there is, he takes the best exactly as often as his chance says.
        var utilities = new SoloUtilities(7_000, 4_000, 3_000);

        BestShare(utilities, lowest).Should().BeApproximately(lowest / 10_000.0, 0.0005);
        BestShare(utilities, highest).Should().BeApproximately(highest / 10_000.0, 0.0005);
        BestShare(utilities, highest).Should().BeGreaterThan(BestShare(utilities, lowest));
    }

    [Fact]
    public void The_best_option_is_the_one_worth_most_and_a_tie_goes_to_the_pass_then_the_dribble()
    {
        SoloPlay.Decide(new SoloUtilities(7_000, 4_000, 3_000), EngineRulesV2.Certain, 0, 0).Should().Be(LegAction.Pass);
        SoloPlay.Decide(new SoloUtilities(3_000, 4_000, 2_000), EngineRulesV2.Certain, 0, 0).Should().Be(LegAction.Dribble);
        SoloPlay.Decide(new SoloUtilities(3_000, 4_000, 6_000), EngineRulesV2.Certain, 0, 0).Should().Be(LegAction.Shoot);

        SoloPlay.Decide(new SoloUtilities(5_000, 5_000, 5_000), EngineRulesV2.Certain, 0, 0).Should().Be(LegAction.Pass);
        SoloPlay.Decide(new SoloUtilities(4_000, 5_000, 5_000), EngineRulesV2.Certain, 0, 0).Should().Be(LegAction.Dribble);
    }

    [Fact]
    public void An_option_that_is_not_open_is_never_taken()
    {
        var onlyTheDribble = new SoloUtilities(null, 100, null);

        for (var draw = 0; draw < EngineRulesV2.Certain; draw += 7)
        {
            SoloPlay.Decide(onlyTheDribble, 5_000, draw, EngineRulesV2.Certain - 1 - draw).Should().Be(LegAction.Dribble);
            SoloPlay.Decide(new SoloUtilities(4_000, 2_000, null), 0, draw, draw).Should().NotBe(LegAction.Shoot);
        }
    }

    [Fact]
    public void A_holder_who_misjudges_takes_another_option_in_proportion_to_what_it_is_worth()
    {
        // The pass is worth most; when he does not take it he takes the dribble three times in four.
        var utilities = new SoloUtilities(8_000, 6_000, 2_000);
        var dribbles = 0;
        var shots = 0;

        for (var draw = 0; draw < EngineRulesV2.Certain; draw++)
        {
            switch (SoloPlay.Decide(utilities, 0, EngineRulesV2.Certain - 1, draw))
            {
                case LegAction.Dribble:
                    dribbles++;
                    break;

                case LegAction.Shoot:
                    shots++;
                    break;

                case LegAction.Pass:
                    Assert.Fail("a holder who misjudges does not take the best");
                    break;
            }
        }

        (dribbles + shots).Should().Be(EngineRulesV2.Certain);
        ((double)dribbles / EngineRulesV2.Certain).Should().BeApproximately(6_001.0 / (6_001 + 2_001), 0.001);
    }

    [Fact]
    public void Dribbling_and_the_space_ahead_make_a_dribble_worth_more()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var from = new SpatialPoint(6_000, 3_500);
        var free = OffBallModel.Place(state.Away.Active, isHome: false, hasPossession: false, new SpatialPoint(2_000, 3_500), state.Away.Instructions, Rules);

        var skilled = WithAttribute(Midfielder(state), MatchAttributeName.Dribbling, 19);
        var clumsy = WithAttribute(Midfielder(state), MatchAttributeName.Dribbling, 3);

        SoloPlay.DribbleUtility(skilled, from, free, isHome: true, Rules)
            .Should().BeGreaterThan(SoloPlay.DribbleUtility(clumsy, from, free, isHome: true, Rules), "the better dribbler is likelier to carry it on");

        // A defender standing where he would run into leaves him less room.
        var ahead = new SpatialPoint(from.X + Rules.SoloDribbleStep, from.Y);
        var marker = new OffBallPlayer(state.Away.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Defence), ahead);

        SoloPlay.DribbleUtility(skilled, from, [marker], isHome: true, Rules)
            .Should().BeLessThan(SoloPlay.DribbleUtility(skilled, from, [], isHome: true, Rules), "a man in the way is a worse dribble");

        SoloPlay.DribbleUtility(skilled, from, [], isHome: true, Rules).Should().BeInRange(0, EngineRulesV2.Certain);
    }

    [Fact]
    public void A_shot_from_distance_is_open_only_between_the_line_and_the_box_for_either_side()
    {
        SoloPlay.CanShoot(new SpatialPoint(Rules.LongShotMinX - 1, 3_500), isHome: true, Rules).Should().BeFalse("too far out");
        SoloPlay.CanShoot(new SpatialPoint(Rules.LongShotMinX, 3_500), isHome: true, Rules).Should().BeTrue();
        SoloPlay.CanShoot(new SpatialPoint(Rules.LongShotMaxX - 1, 3_500), isHome: true, Rules).Should().BeTrue();
        SoloPlay.CanShoot(new SpatialPoint(Rules.LongShotMaxX, 3_500), isHome: true, Rules).Should().BeFalse("in the box the shot is the chance the attack creates");

        // The away side attacks towards the low end of the pitch.
        SoloPlay.CanShoot(new SpatialPoint(SpatialPitch.PitchLength - Rules.LongShotMinX, 3_500), isHome: false, Rules).Should().BeTrue();
        SoloPlay.CanShoot(new SpatialPoint(SpatialPitch.PitchLength - Rules.LongShotMaxX, 3_500), isHome: false, Rules).Should().BeFalse();
        SoloPlay.CanShoot(new SpatialPoint(Rules.LongShotMinX, 3_500), isHome: false, Rules).Should().BeFalse("that is his own half");
    }

    [Fact]
    public void A_shot_from_distance_is_worth_more_to_a_better_finisher_who_is_nearer_the_goal()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var far = new SpatialPoint(Rules.LongShotMinX, 3_500);
        var near = new SpatialPoint(Rules.LongShotMaxX - 100, 3_500);

        var sharp = WithAttribute(WithAttribute(Midfielder(state), MatchAttributeName.Finishing, 19), MatchAttributeName.Composure, 19);
        var wild = WithAttribute(WithAttribute(Midfielder(state), MatchAttributeName.Finishing, 3), MatchAttributeName.Composure, 3);

        SoloPlay.ShootUtility(sharp, far, isHome: true, Rules).Should().BeGreaterThan(SoloPlay.ShootUtility(wild, far, isHome: true, Rules));
        SoloPlay.ShootUtility(sharp, near, isHome: true, Rules).Should().BeGreaterThan(SoloPlay.ShootUtility(sharp, far, isHome: true, Rules));
        SoloPlay.ShootUtility(sharp, near, isHome: true, Rules).Should().BeInRange(0, EngineRulesV2.Certain);
    }

    [Fact]
    public void A_holder_with_nobody_to_play_to_and_a_shot_on_shoots_when_he_is_sure_of_himself_and_not_when_he_is_not()
    {
        var shot = 0;
        var wild = 0;

        for (var ordinal = 1; ordinal <= 200; ordinal++)
        {
            shot += Alone(ordinal, finishing: 20, decisions: 20).EndsInShot ? 1 : 0;
            wild += Alone(ordinal, finishing: 1, decisions: 20).EndsInShot ? 1 : 0;
        }

        shot.Should().BeGreaterThan(150, "with nobody to give it to, a sure finisher at the edge of the box has a go");
        wild.Should().BeLessThan(shot, "a poor finisher would rather carry it on");
    }

    [Fact]
    public void Only_the_last_leg_can_be_a_shot_and_only_when_the_caller_allows_it()
    {
        for (var ordinal = 1; ordinal <= 200; ordinal++)
        {
            var chain = Alone(ordinal, finishing: 20, decisions: 20);

            chain.Legs.Count(leg => leg.Shoots).Should().BeLessThanOrEqualTo(1);

            if (chain.EndsInShot)
            {
                var last = chain.Legs[^1];

                last.Shoots.Should().BeTrue();
                last.IsPass.Should().BeFalse("a shot is not a pass");
                last.To.Should().Be(chain.Legs.Count > 1 ? chain.Legs[^2].To : ToTheBox[0], "the ball is where the holder is when he shoots");
                chain.Legs.Take(chain.Legs.Count - 1).Should().OnlyContain(leg => !leg.Shoots);
            }

            var barred = Alone(ordinal, finishing: 20, decisions: 20, canShoot: false);

            barred.EndsInShot.Should().BeFalse("only the ball played into the final third can be shot instead");
        }
    }

    [Fact]
    public void A_holder_with_better_Decisions_keeps_the_ball_less_often_when_somebody_is_there_to_pass_to()
    {
        SoloShare(decisions: 1).Should().BeGreaterThan(SoloShare(decisions: 20), "he is likelier to pass it up when he cannot tell");
    }

    [Fact]
    public void A_goal_from_a_shot_taken_from_distance_has_nobody_to_thank_for_it()
    {
        var state = StateOf(TestMatchFactory.Even(), 1);
        var scorer = Midfielder(state).Participant.ParticipantId;

        state.BeginPassage(MatchSide.Home, 0, PassageRestartKind.None);
        state.Passing.LongShot = true;

        AssistPlanner.Credit(state, MatchSide.Home, scorer, 1);

        state.Home.Assists.Should().BeEmpty("the holder took it on himself");

        // Without the shot being his own, the goal is credited to somebody: it is the flag that holds it back.
        state.BeginPassage(MatchSide.Home, 0, PassageRestartKind.None);

        AssistPlanner.Credit(state, MatchSide.Home, scorer, 1);

        state.Home.Assists.Values.Sum().Should().Be(1);
    }

    [Fact]
    public void Shots_from_distance_are_taken_by_the_man_who_had_the_ball_and_are_a_small_share_of_the_shots()
    {
        var longShots = 0;
        var shots = 0;

        for (var seed = 1UL; seed <= 60; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, EngineRulesV2.Default, null, recorder);

            shots += result.Events.Count(matchEvent => matchEvent.Type is EngineEventType.Goal or EngineEventType.ShotSaved
                or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork);

            foreach (var passage in recorder.Passages.Where(passage => passage.Outcome == PassageOutcome.OpenPlayShot
                && !passage.Touches.Any(touch => touch.Action is PassageAction.Tackle or PassageAction.Header)))
            {
                longShots++;

                var shot = passage.Touches.Last(touch => touch.Action == PassageAction.Shot);
                var before = passage.Touches.TakeWhile(touch => touch != shot).Last(touch => touch.Action is PassageAction.Receive or PassageAction.Carry);

                before.ParticipantId.Should().Be(shot.ParticipantId, "the man who shoots from distance is the one who has the ball");

                var shotEvent = result.Events.Single(matchEvent => passage.EventSequences.Contains(matchEvent.Sequence)
                    && matchEvent.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked
                        or EngineEventType.ShotOffTarget or EngineEventType.Woodwork);

                shotEvent.ParticipantId.Should().Be(shot.ParticipantId);
            }
        }

        longShots.Should().BeGreaterThan(20, "a match has a few shots from distance");
        ((double)longShots / shots).Should().BeInRange(0.01, 0.12, "a shot from distance is a small share of the shots");
    }

    [Fact]
    public void The_rules_for_solo_play_are_validated()
    {
        Rules.Invoking(rules => rules.Validate()).Should().NotThrow();

        (Rules with { SoloBestChoiceLowestBasisPoints = Rules.SoloBestChoiceHighestBasisPoints + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*accuracies*");
        (Rules with { SoloBestChoiceHighestBasisPoints = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*SoloBestChoiceHighestBasisPoints*");
        (Rules with { LongShotGoalMultiplierBasisPoints = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*long shot's goal multiplier*");
        (Rules with { SoloDribbleSkillWeight = 0, SoloDribbleSpaceWeight = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*solo score weights*");
        (Rules with { SoloDribbleStep = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*SoloDribbleStep*");
        (Rules with { LongShotMaxX = Rules.LongShotMinX })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*long shot band*");
    }

    /// <summary>Gets the share of every draw there is on which the holder takes the best of three options.</summary>
    private static double BestShare(SoloUtilities utilities, int bestChance)
    {
        var best = 0;

        for (var draw = 0; draw < EngineRulesV2.Certain; draw++)
        {
            best += SoloPlay.Decide(utilities, bestChance, draw, 0) == LegAction.Pass ? 1 : 0;
        }

        return (double)best / EngineRulesV2.Certain;
    }

    /// <summary>Gets the share of legs a holder of a given Decisions keeps the ball on, with the rest of his side there to pass to.</summary>
    private static double SoloShare(int decisions)
    {
        var legs = 0;
        var solo = 0;

        for (var ordinal = 1; ordinal <= 1_500; ordinal++)
        {
            var state = StateOf(TestMatchFactory.Even(), ordinal);
            var holder = Midfielder(state);
            var edited = WithAttribute(holder, MatchAttributeName.Decisions, decisions);

            state.Home.Active[state.Home.Active.IndexOf(holder)] = edited;

            var chain = ReceiverChooser.Choose(state, MatchSide.Home, ToTheBox, edited.Participant.ParticipantId, lost: false)!;

            legs += chain.Legs.Count;
            solo += chain.Legs.Count(leg => !leg.IsPass);
        }

        return (double)solo / legs;
    }

    /// <summary>Plays a chain to the edge of the box with the holder alone on the pitch, so there is nobody to pass to.</summary>
    private static ReceiverChain Alone(int ordinal, int finishing, int decisions, bool canShoot = true)
    {
        var state = StateOf(TestMatchFactory.Even(), ordinal);
        var holder = Midfielder(state);
        var edited = WithAttribute(
            WithAttribute(WithAttribute(holder, MatchAttributeName.Finishing, finishing), MatchAttributeName.Composure, finishing),
            MatchAttributeName.Decisions,
            decisions);

        state.Home.Active.RemoveAll(slot => slot.Slot.Family != MatchPositionFamily.Goalkeeper);
        state.Home.Active.Add(edited);

        return ReceiverChooser.Choose(
            state,
            MatchSide.Home,
            ToTheBox,
            edited.Participant.ParticipantId,
            lost: false,
            unpulledFrom: ToTheBox.Length - 2,
            canShoot: canShoot)!;
    }

    private static ActiveSlot Midfielder(MatchState state) =>
        state.Home.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Midfield);

    private static ActiveSlot WithAttribute(ActiveSlot slot, MatchAttributeName attribute, int value)
    {
        var values = slot.Participant.Attributes.Values.ToArray();

        values[(int)attribute] = value;

        return slot with { Participant = slot.Participant with { Attributes = PlayerAttributesV1.From(values) } };
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
