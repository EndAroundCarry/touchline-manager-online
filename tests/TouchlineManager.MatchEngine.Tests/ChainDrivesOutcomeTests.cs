using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The receiver chain drives the outcome, and a cross is headed (`engine-v10`, M4): how well the ball was played up the
/// pitch nudges the chances, the player it ended with is the one who shoots, and a cross ends in a header the defence
/// can win.
/// </summary>
public sealed class ChainDrivesOutcomeTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    private static readonly SpatialPointSet Points = new();

    // ---- What the chain says of itself ----------------------------------------------------------------

    [Fact]
    public void A_chain_reads_its_most_marked_receiver_over_the_approach_and_its_last_over_every_leg()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();
        var four = Guid.NewGuid();

        var chain = new ReceiverChain(
            [
                new ReceiverLeg(one, two, Points.A, Openness: 6_000, Score: 7_000),
                new ReceiverLeg(two, three, Points.B, Openness: 3_000, Score: 5_000),
                new ReceiverLeg(three, four, Points.C, Openness: 1_000, Score: 3_000),
            ],
            one,
            four);

        var whole = chain.Quality(Rules);
        var approach = chain.Quality(Rules, approachLegs: 2);

        whole.Weakest.Should().Be(1_000);
        approach.Weakest.Should().Be(3_000, "the leg into the final third is played after the attack has progressed");
        approach.Final.Should().Be(1_000, "the player the chain ends with is the one who has the ball for the chance");
        approach.Choice.Should().Be(5_000, "the mean of the scores of the receivers chosen");
    }

    [Fact]
    public void A_chain_nobody_played_a_pass_in_says_nothing_either_way()
    {
        var one = Guid.NewGuid();
        var solo = new ReceiverChain([new ReceiverLeg(one, null, Points.A)], one, one);

        var quality = solo.Quality(Rules);

        quality.ProgressNudge(Rules).Should().Be(0);
        quality.CreationNudge(Rules).Should().Be(0);
    }

    [Fact]
    public void A_crowded_pass_makes_a_loss_likelier_and_a_free_man_less_so_and_the_nudge_is_bounded()
    {
        var neutral = new ChainQuality(
            Rules.ChainWeakestOpennessReference,
            Rules.ChainFinalOpennessReference,
            Rules.ChainChoiceReference);

        neutral.ProgressNudge(Rules).Should().Be(0);
        neutral.CreationNudge(Rules).Should().Be(0);

        var crowded = new ChainQuality(0, 0, 0);
        var free = new ChainQuality(EngineRulesV2.Certain, EngineRulesV2.Certain, EngineRulesV2.Certain);

        crowded.ProgressNudge(Rules).Should().BeNegative().And.BeGreaterThanOrEqualTo(-Rules.ChainProgressSwingBasisPoints);
        free.ProgressNudge(Rules).Should().BePositive().And.BeLessThanOrEqualTo(Rules.ChainProgressSwingBasisPoints);

        var creationBound = Rules.ChainCreationOpennessSwingBasisPoints + Rules.ChainCreationChoiceSwingBasisPoints;

        crowded.CreationNudge(Rules).Should().BeNegative().And.BeGreaterThanOrEqualTo(-creationBound);
        free.CreationNudge(Rules).Should().BePositive().And.BeLessThanOrEqualTo(creationBound);
    }

    [Fact]
    public void The_nudges_are_small_beside_what_the_sides_ratings_decide()
    {
        // A chain can nudge a chance, never decide it: the whole swing is a fraction of the one the ratings have.
        Rules.ChainProgressSwingBasisPoints.Should().BeLessThan(Rules.ProgressControlSwingBasisPoints / 2);
        (Rules.ChainCreationOpennessSwingBasisPoints + Rules.ChainCreationChoiceSwingBasisPoints)
            .Should().BeLessThan(Rules.CreationSwingBasisPoints / 2);
    }

    [Fact]
    public void A_player_given_the_ball_into_the_final_third_is_credited_with_the_pass_that_follows_and_the_one_who_gave_it_to_him_with_his_goal()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();

        var passing = new PossessionPassing(MatchSide.Home);

        passing.Chained(new ReceiverChain([new ReceiverLeg(one, two, Points.A)], one, two));

        passing.ChainHolder.Should().Be(two);
        passing.CreatorFor(three).Should().Be(two);

        passing.Entered(two, three);

        passing.ChainHolder.Should().Be(three, "he has the ball from here");
        passing.ChainFeeder.Should().Be(two);
        passing.PasserOf(1).Should().Be(one, "the approach's pass is still its passer's");
        passing.PasserOf(2).Should().Be(three, "the pass that creates the chance is the man who has the ball's");
        passing.CreatorFor(one).Should().Be(three);
        passing.CreatorFor(three).Should().Be(two, "he cannot assist his own goal, so it is the man who played the ball in to him");
    }

    // ---- What a played match records ------------------------------------------------------------------

    [Fact]
    public void The_ball_played_into_the_final_third_goes_to_the_player_who_fights_the_duel_with_it()
    {
        var duels = 0;

        for (var seed = 1UL; seed <= 12; seed++)
        {
            foreach (var passage in PassageTestHelpers.Play(seed).Passages.Where(DuelFought))
            {
                var touches = passage.Touches;
                var tackle = touches.ToList().FindIndex(touch => touch.Action == PassageAction.Tackle);

                // A holder who found nobody to play it to carries it in himself, and the duel is fought as it always was.
                if (tackle < 2 || touches[tackle - 1].Action != PassageAction.Carry || touches[tackle - 2].Action != PassageAction.Receive)
                {
                    continue;
                }

                touches[tackle - 1].ParticipantId.Should().Be(touches[tackle - 2].ParticipantId, "the man who was given the ball takes the man on");
                duels++;
            }
        }

        duels.Should().BeGreaterThan(300);
    }

    [Fact]
    public void A_defender_never_fights_the_duel_at_the_edge_of_the_box()
    {
        for (var seed = 1UL; seed <= 12; seed++)
        {
            var match = PassageTestHelpers.Play(seed);
            var defenders = match.Input.Home.Slots.Concat(match.Input.Away.Slots)
                .Where(slot => slot.Family == MatchPositionFamily.Defence)
                .Select(slot => slot.ParticipantId)
                .ToHashSet();

            foreach (var passage in match.Passages.Where(DuelFought))
            {
                var touches = passage.Touches.ToList();
                var tackle = touches.FindIndex(touch => touch.Action == PassageAction.Tackle);

                if (tackle < 2 || touches[tackle - 1].Action != PassageAction.Carry || touches[tackle - 2].Action != PassageAction.Receive)
                {
                    continue;
                }

                defenders.Should().NotContain(touches[tackle - 1].ParticipantId);
            }
        }
    }

    [Fact]
    public void The_player_the_move_ended_with_takes_the_shot_more_than_his_share()
    {
        var shots = 0;
        var byTheMan = 0;

        for (var seed = 1UL; seed <= 40; seed++)
        {
            foreach (var passage in PassageTestHelpers.Play(seed).Passages.Where(passage => passage.Outcome == PassageOutcome.OpenPlayShot))
            {
                var touches = passage.Touches.ToList();
                var shot = touches.FindIndex(touch => touch.Action == PassageAction.Shot);
                var received = touches.Take(Math.Max(0, shot)).LastOrDefault(touch => touch.Action == PassageAction.Receive);

                if (shot < 0 || received.ParticipantId == Guid.Empty)
                {
                    continue;
                }

                shots++;
                byTheMan += touches[shot].ParticipantId == received.ParticipantId ? 1 : 0;
            }
        }

        shots.Should().BeGreaterThan(400);

        // Ten players could take it, so a tenth would be the share without the ball; the bonus makes him well ahead.
        ((double)byTheMan / shots).Should().BeGreaterThan(0.17).And.BeLessThan(0.60);
    }

    [Fact]
    public void An_open_play_cross_ends_in_a_header_from_the_box_by_somebody_other_than_the_man_who_crossed_it()
    {
        var headed = 0;
        var cleared = 0;

        for (var seed = 1UL; seed <= 24; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            foreach (var passage in match.Passages.Where(passage => passage.Outcome is PassageOutcome.OpenPlayShot or PassageOutcome.CreationFailed))
            {
                var touches = passage.Touches.ToList();
                var header = touches.FindIndex(touch => touch.Action == PassageAction.Header);

                if (header < 0)
                {
                    continue;
                }

                var delivery = passage.Waypoints.Last(waypoint => waypoint.FractionBasisPoints <= touches[header].FractionBasisPoints);

                delivery.Kind.Should().Be(PassageWaypointKind.Cross, "the header meets a cross");
                delivery.Z.Should().Be(Rules.HeaderAltitude);
                PassageTestHelpers.AttackingX(delivery.X, passage.Side).Should().BeInRange(Rules.BoxXMinBasisPoints, Rules.BoxXMaxBasisPoints);
                PassageTestHelpers.AttackingY(delivery.Y, passage.Side).Should().BeInRange(Rules.BoxYMinBasisPoints, Rules.BoxYMaxBasisPoints);

                var crosser = touches.Take(header).Last(touch => touch.Action == PassageAction.Cross);

                touches[header].ParticipantId.Should().NotBe(crosser.ParticipantId, "a man does not head his own cross");

                var attackers = match.Input.SideOf(passage.Side).Squad.Select(participant => participant.ParticipantId).ToHashSet();

                attackers.Should().Contain(crosser.ParticipantId);

                if (passage.Outcome == PassageOutcome.OpenPlayShot)
                {
                    attackers.Should().Contain(touches[header].ParticipantId, "when the header is the shot, an attacker won it");
                    headed++;
                }
                else
                {
                    attackers.Should().NotContain(touches[header].ParticipantId, "when the cross is cleared, a defender won it");
                    cleared++;
                }
            }
        }

        headed.Should().BeGreaterThan(60, "a quarter of the chances from open play are crossed");
        cleared.Should().BeGreaterThan(5, "the defence wins some of the headers");
        ((double)headed / (headed + cleared)).Should().BeInRange(0.65, 0.90, "the attacker is favoured in the air");
    }

    [Fact]
    public void A_header_from_open_play_that_scores_is_set_up_by_the_man_who_crossed_it()
    {
        var goals = 0;

        for (var seed = 1UL; seed <= 80; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            foreach (var passage in match.Passages.Where(passage => passage.Outcome == PassageOutcome.OpenPlayShot))
            {
                var touches = passage.Touches.ToList();
                var header = touches.FindIndex(touch => touch.Action == PassageAction.Header);

                if (header < 0 || !PassageTestHelpers.EventsOf(match, passage).Any(pair => pair.Event.IsGoal))
                {
                    continue;
                }

                var crosser = touches.Take(header).Last(touch => touch.Action == PassageAction.Cross).ParticipantId;
                var scorer = PassageTestHelpers.EventsOf(match, passage).Single(pair => pair.Event.IsGoal).Event.ParticipantId;
                var line = match.Result.PlayerLines.Single(candidate => candidate.ParticipantId == crosser);

                scorer.Should().NotBe(crosser);
                line.Assists.Should().BeGreaterThan(0, "the man who crossed the ball a header scored from has an assist");
                goals++;
            }
        }

        goals.Should().BeGreaterThan(20);
    }

    [Fact]
    public void A_cross_is_still_about_a_quarter_of_the_chances_and_the_goals_stay_where_they_were()
    {
        var crossed = 0;
        var chances = 0;
        var goals = 0;
        const int matches = 150;

        for (var seed = 1UL; seed <= matches; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            goals += match.Result.HomeGoals + match.Result.AwayGoals;

            foreach (var passage in match.Passages.Where(passage => passage.Outcome == PassageOutcome.OpenPlayShot))
            {
                chances++;
                crossed += passage.Touches.Any(touch => touch.Action == PassageAction.Header) ? 1 : 0;
            }
        }

        ((double)crossed / chances).Should().BeInRange(0.15, 0.35);
        ((double)goals / matches).Should().BeInRange(2.5, 3.3, "the calibration the engine has held since engine-v1");
    }

    // ---- What the people on the pitch are worth -------------------------------------------------------

    [Fact]
    public void A_side_that_sees_and_chooses_well_scores_more_than_one_that_does_not()
    {
        const int matches = 250;

        var clever = Goals(18, matches);
        var muddled = Goals(4, matches);

        clever.Should().BeGreaterThan(muddled * 1.15, "Vision and Decisions pick the receiver and nudge the chances");
    }

    // ---- The rules that shape it ----------------------------------------------------------------------

    [Fact]
    public void The_rules_for_a_chain_that_drives_the_outcome_are_validated()
    {
        Rules.Invoking(rules => rules.Validate()).Should().NotThrow();

        (Rules with { ChainWeakestOpennessReference = -1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ChainWeakestOpennessReference*");
        (Rules with { ChainFinalOpennessReference = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ChainFinalOpennessReference*");
        (Rules with { ChainCreationChoiceSwingBasisPoints = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ChainCreationChoiceSwingBasisPoints*");
        (Rules with { ShooterChainBonusBasisPoints = EngineRulesV2.Certain - 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ShooterChainBonusBasisPoints*");
        (Rules with { CrossCreationMultiplierBasisPoints = EngineRulesV2.Certain - 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*CrossCreationMultiplierBasisPoints*");
        (Rules with { ReachWeightFloorBasisPoints = EngineRulesV2.Certain + 1 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*ReachWeightFloorBasisPoints*");
        (Rules with { CrossHeaderDeliveryBaseline = 0 })
            .Invoking(rules => rules.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*cross header*");
    }

    /// <summary>Whether the possession went on to the duel at the edge of the box, and the defender won or lost it without a foul.</summary>
    private static bool DuelFought(MatchPassageV1 passage) =>
        passage.Outcome is PassageOutcome.OpenPlayShot or PassageOutcome.CreationFailed or PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded;

    private static double Goals(int mind, int matches)
    {
        var total = 0;

        for (var seed = 1UL; seed <= (ulong)matches; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var values = Enumerable.Repeat(13, MatchAttributeNames.Count).ToArray();

            values[(int)MatchAttributeName.Vision] = mind;
            values[(int)MatchAttributeName.Decisions] = mind;

            var attributes = PlayerAttributesV1.From(values);
            var squad = input.Home.Squad.Select(participant => participant with { Attributes = attributes }).ToList();

            total += MatchSimulator.Simulate(input with { Home = input.Home with { Squad = squad } }).HomeGoals;
        }

        return (double)total / matches;
    }

    /// <summary>A few distinct points for the legs of a hand-built chain.</summary>
    private sealed class SpatialPointSet
    {
        public SpatialPoint A { get; } = new(2_000, 3_500);

        public SpatialPoint B { get; } = new(4_000, 3_500);

        public SpatialPoint C { get; } = new(6_000, 3_500);
    }
}
