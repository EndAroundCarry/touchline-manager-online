using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// How a corner is shown (`replay-v6`): what puts the ball behind, how the twenty-two are set for it, and who is where
/// when it is delivered.
/// </summary>
public sealed class FilmCornerTests
{
    private const int Seeds = 24;

    [Fact]
    public void A_corner_is_won_by_somebody_on_the_ball_and_not_by_a_pass_lofted_over_the_line()
    {
        int corners = 0, headed = 0, blocked = 0, tipped = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed)));

            foreach (var possession in script.Possessions.Where(candidate => candidate.Source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded))
            {
                var beats = script.Beats.Skip(possession.FirstBeat).Take(possession.LastBeat - possession.FirstBeat + 1).ToList();
                var placement = beats.FindIndex(beat => beat.Kind == BeatKind.Placement && beat.Formation == FormationMode.Corner);

                placement.Should().BeGreaterThan(0, "the ball is put at the flag after it has gone out");

                var behind = beats[placement - 1];
                var defending = MatchInputV1.OpponentOf(possession.Source.Side);

                behind.Kind.Should().Be(BeatKind.Clearance, "somebody played it behind, it did not leave on its own");
                behind.Side.Should().Be(defending, "it is the defence who put it behind");
                FilmSpace.Attacking(behind.To, possession.Source.Side).Should().BeApproximately(FilmSpace.Length, 0.01, "it goes out over the goal line");
                (behind.Actor is null).Should().Be(
                    behind.ActorSource is ActorSource.NearestToBall or ActorSource.PreviousReceiver,
                    "a named player is the keeper, and the rest is whoever gets there, or whoever won the held ball");

                if (placement >= 3 && beats[placement - 3].Kind == BeatKind.Shot && beats[placement - 2].Kind == BeatKind.Save)
                {
                    tipped++;
                    beats[placement - 3].Strike.Should().Be(StrikeResult.Saved);
                    behind.Actor.Should().Be(beats[placement - 2].Actor, "the keeper who saved it turns it behind");
                }
                else if (behind.ZFrom > 0)
                {
                    headed++;
                }
                else
                {
                    blocked++;
                }

                corners++;
            }
        }

        // A header is the rare one: few corners follow a ball in the air.
        _ = headed;

        corners.Should().BeGreaterThan(20);
        blocked.Should().BeGreaterThan(0, "a ball along the ground is blocked behind");
        tipped.Should().BeGreaterThan(0, "a strike the keeper gets a hand to goes round the post");
    }

    [Fact]
    public void A_ball_is_played_towards_the_line_and_a_defender_touches_it_just_short_of_it_before_it_goes_behind()
    {
        int corners = 0, played = 0, tipped = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed)));

            foreach (var possession in script.Possessions.Where(candidate => candidate.Source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded))
            {
                var beats = script.Beats.Skip(possession.FirstBeat).Take(possession.LastBeat - possession.FirstBeat + 1).ToList();
                var placement = beats.FindIndex(beat => beat.Kind == BeatKind.Placement && beat.Formation == FormationMode.Corner);
                var behind = beats[placement - 1];
                var before = beats[placement - 2];
                var attacking = possession.Source.Side;

                corners++;

                behind.Distance.Should().BeLessThanOrEqualTo(14.0 + 1e-6, "it is turned behind a few metres, not sent the length of the pitch to the flag");
                beats[placement].From.Should().Be(behind.To, "the ball is put down from where it went out");
                before.Formation.Should().NotBe(FormationMode.Corner, "the sides do not set for the corner until the ball is out");

                if (before.Kind == BeatKind.Save)
                {
                    tipped++;
                    Math.Abs(behind.To.Y - (FilmSpace.Width / 2)).Should().BeInRange(3.66 + 1.0 - 1e-6, 3.66 + 1.0 + 1.5 + 1e-6, "a hand turns it round the post, not along the goal line");

                    continue;
                }

                if (before.Contested)
                {
                    // The ball played in along the ground is received and held first (`replay-v11`), and then won.
                    played++;
                    before.From.Should().Be(behind.From, "the defender wins it where it was held");
                    FilmSpace.Attacking(before.To, attacking).Should().BeLessThan(FilmSpace.Length, "it is held short of the line, and goes over it");
                    behind.Distance.Should().BeLessThanOrEqualTo(5.5 + 1e-6, "his touch is within a few metres of where the ball leaves play");

                    continue;
                }

                if (before.Kind == BeatKind.LoftedPass && before.Side == attacking)
                {
                    played++;
                    before.To.Should().Be(behind.From, "the defender touches it where it arrives");
                    before.Receiver.Should().BeNull("nobody on the attacking side gets to it first");
                    FilmSpace.Attacking(before.To, attacking).Should().BeLessThan(FilmSpace.Length, "he touches it short of the line, and it goes over it");
                    behind.Distance.Should().BeLessThanOrEqualTo(5.5 + 1e-6, "his touch is within a few metres of where the ball leaves play");
                }
            }
        }

        corners.Should().BeGreaterThan(20);
        played.Should().BeGreaterThan(corners / 2, "most corners follow a ball played in towards the line");
        tipped.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_ball_played_in_along_the_ground_is_held_by_the_man_who_gets_it_and_won_by_a_defender_and_a_ball_in_the_air_is_not()
    {
        int held = 0, aerial = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed)));

            foreach (var possession in script.Possessions.Where(candidate => candidate.Source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded))
            {
                var beats = script.Beats.Skip(possession.FirstBeat).Take(possession.LastBeat - possession.FirstBeat + 1).ToList();
                var placement = beats.FindIndex(beat => beat.Kind == BeatKind.Placement && beat.Formation == FormationMode.Corner);
                var behind = beats[placement - 1];
                var before = beats[placement - 2];

                if (before.Contested)
                {
                    held++;

                    var played = beats[placement - 3];

                    before.Kind.Should().Be(BeatKind.Duel);
                    before.Side.Should().Be(possession.Source.Side, "the side with the ball is the one held up");
                    before.ActorSource.Should().Be(ActorSource.PreviousReceiver, "the man who has it is the one the ball was played to");
                    before.Distance.Should().Be(0, "the ball stays at his feet while he is closed down");
                    played.Kind.Should().Be(BeatKind.Pass, "it is played in along the ground");
                    played.ReceiverPending.Should().BeTrue("an attacker, found as it is played, is there to receive it");
                    played.To.Should().Be(before.From, "he is held where he received it");
                    behind.Side.Should().Be(MatchInputV1.OpponentOf(possession.Source.Side), "the defence put it behind");
                    behind.ActorSource.Should().Be(ActorSource.PreviousReceiver, "the defender who closed him down is the one who wins it");
                }
                else
                {
                    beats.Skip(Math.Max(0, placement - 4)).Take(4).Should().NotContain(beat => beat.Contested, "a ball in the air, a header and a keeper's save are left as they were");

                    if (before.Kind == BeatKind.LoftedPass)
                    {
                        aerial++;
                    }
                }
            }
        }

        held.Should().BeGreaterThan(0, "a ball along the ground is held before it goes behind");
        aerial.Should().BeGreaterThan(0, "a ball played in the air is still a defender's touch");
    }

    [Fact]
    public void The_film_holds_the_ball_still_for_about_a_second_while_a_defender_closes_the_man_down()
    {
        int held = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed)));

            foreach (var passage in presentation.Passages)
            {
                var ball = passage.Tracks.Single(track => track.EntityId == "ball").Keyframes;

                // The ball goes out over the goal line, and is then put at the flag: the touch that did it is the keyframe before.
                for (var index = 3; index + 1 < ball.Count; index++)
                {
                    var restart = ball[index];

                    if (restart.Action != "restart" || (restart.X > 5 && restart.X < 9_995) || (ball[index + 1].Y > 5 && ball[index + 1].Y < 9_995))
                    {
                        continue;
                    }

                    // A held ball is a ball that does not move from the end of the pass until the touch: two keyframes in one
                    // place, the first tagged as the man carrying it (a keeper's hand on a shot is tagged as a save).
                    var touch = ball[index - 2];
                    var arrived = ball[index - 3];

                    if (touch.Action != "clearance" || arrived.Action != "carry" || arrived.X != touch.X || arrived.Y != touch.Y)
                    {
                        continue;
                    }

                    held++;

                    var seconds = (touch.TimeMilliseconds - arrived.TimeMilliseconds) / 1000.0;

                    seconds.Should().BeInRange(0.7, 2.2, "about a second of film: long enough to see him closed down, not a pause");
                }
            }
        }

        held.Should().BeGreaterThan(0, "some corners are won off a held ball");
    }

    [Fact]
    public void The_taker_is_at_the_ball_when_the_corner_is_delivered_and_no_ball_is_sent_the_length_of_the_line()
    {
        int matches = 0;
        double outLeg = 0, takerGap = 0;

        for (var seed = 1UL; seed <= 16; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            if (shape.Corners == 0)
            {
                continue;
            }

            matches++;
            outLeg = Math.Max(outLeg, shape.CornerOutLegP95);
            takerGap = Math.Max(takerGap, shape.CornerTakerGapP95);
        }

        matches.Should().BeGreaterThan(8, "most matches have a corner");
        outLeg.Should().BeLessThanOrEqualTo(14.5, "the touch that puts the ball behind is short");
        takerGap.Should().BeLessThanOrEqualTo(2.0, "the taker is at the ball when it is struck, he is not still running up to it");
    }

    [Fact]
    public void The_keeper_stays_on_his_line_for_a_strike_and_does_not_meet_a_shot_that_goes_wide()
    {
        int matches = 0;
        double offGoal = 0, atWideBall = 0;

        for (var seed = 1UL; seed <= 16; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            matches++;
            offGoal = Math.Max(offGoal, shape.KeeperOffGoalP95);
            atWideBall += shape.KeeperAtWideBallP05;
        }

        matches.Should().Be(16);
        atWideBall /= matches;
        offGoal.Should().BeLessThanOrEqualTo(7.0, "he is on his line as the ball arrives, not out of the goal on his way to take the kick");
        atWideBall.Should().BeGreaterThanOrEqualTo(2.5, "on average the low end of it is a few metres: he does not run to where a wide shot ends, as if the ball were sent to him");
    }

    [Theory]
    [InlineData(MatchSide.Home)]
    [InlineData(MatchSide.Away)]
    public void A_corner_is_set_by_role_and_the_other_flag_is_its_mirror_image(MatchSide taking)
    {
        var (context, shape) = ShapeOf(3);
        var roster = context.Starters;
        var taker = Outfield(context, taking).First(entity => context.Slots[entity].Family == MatchPositionFamily.Midfield);
        var goal = FilmSpace.AttackedGoal(taking);

        var high = Targets(shape, roster, taking, new Vec(goal.X, FilmSpace.Width), taker, arrived: true);
        var low = Targets(shape, roster, taking, new Vec(goal.X, 0), taker, arrived: true);

        high[taker].Should().Be(new Vec(goal.X, FilmSpace.Width), "the taker is on the flag");
        low[taker].Should().Be(new Vec(goal.X, 0));

        // The same places, mirrored; who takes which does not matter here.
        var mirrored = Places(high, context).Select(point => new Vec(point.X, FilmSpace.Width - point.Y)).OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
        var other = Places(low, context);

        mirrored.Should().HaveCount(other.Count);

        foreach (var (mine, theirs) in mirrored.Zip(other))
        {
            mine.X.Should().BeApproximately(theirs.X, 1e-6);
            mine.Y.Should().BeApproximately(theirs.Y, 1e-6, "the other flank is the mirror image across the pitch");
        }

        var attackers = Outfield(context, taking).Where(entity => entity != taker).Select(entity => high[entity]).ToList();
        var defenders = Outfield(context, MatchInputV1.OpponentOf(taking)).Select(entity => high[entity]).ToList();

        double Along(Vec point) => Math.Abs(point.X - goal.X);
        bool InBox(Vec point) => Along(point) <= 16.5 && Math.Abs(point.Y - goal.Y) <= 20.2;

        attackers.Count(point => Along(point) >= 40).Should().BeInRange(2, 3, "two or three stay back against a break");
        attackers.Count(InBox).Should().Be(4, "four go into the box");
        attackers.Count(point => !InBox(point) && Along(point) < 40).Should().Be(2, "two wait at its edge for the second ball");

        defenders.Count(point => Along(point) >= 40).Should().Be(1, "one is left up the pitch as an outlet");
        defenders.Count(point => Along(point) <= 6 && Math.Abs(point.Y - goal.Y) <= 9.2).Should().BeGreaterThanOrEqualTo(5, "two on the posts and three holding the six-yard line");
        defenders.Count(InBox).Should().BeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void The_runners_wait_five_metres_short_of_their_places_and_no_two_team_mates_share_one()
    {
        var (context, shape) = ShapeOf(5);
        var roster = context.Starters;
        var taker = Outfield(context, MatchSide.Home).First();
        var flag = new Vec(FilmSpace.Length, FilmSpace.Width);

        var waiting = Targets(shape, roster, MatchSide.Home, flag, taker, arrived: false);
        var arrived = Targets(shape, roster, MatchSide.Home, flag, taker, arrived: true);

        var runners = Outfield(context, MatchSide.Home).Where(entity => entity != taker && InTheBox(arrived[entity])).ToList();

        runners.Should().HaveCount(4);

        foreach (var runner in runners)
        {
            (FilmSpace.Length - waiting[runner].X).Should().BeApproximately(FilmSpace.Length - arrived[runner].X + 5.0, 1e-6, "a runner waits five metres short of the place he runs to");
            waiting[runner].Y.Should().BeApproximately(arrived[runner].Y, 1e-6);
        }

        foreach (var shown in new[] { waiting, arrived })
        {
            foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
            {
                var players = Outfield(context, side).Where(entity => entity != taker).ToList();

                foreach (var entity in players)
                {
                    var nearest = players.Where(other => other != entity).Min(other => shown[entity].DistanceTo(shown[other]));

                    nearest.Should().BeGreaterThan(1.6, "two team-mates are not set on one spot");
                }
            }
        }
    }

    [Fact]
    public void A_corner_arrives_into_a_box_with_runners_and_defenders_and_a_few_held_back()
    {
        int matches = 0;
        double attackers = 0, defenders = 0, held = 0, sixYard = 0;

        for (var seed = 1UL; seed <= 16; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            if (shape.Corners == 0)
            {
                continue;
            }

            matches++;
            attackers += shape.CornerAttackersP50;
            defenders += shape.CornerDefendersP50;
            held += shape.CornerGuardsP50;
            sixYard = Math.Max(sixYard, shape.CornerSixYardP95);
        }

        matches.Should().BeGreaterThan(8, "most matches have a corner");
        (attackers / matches).Should().BeGreaterThanOrEqualTo(3, "four runners, give or take one who is still arriving");
        (defenders / matches).Should().BeGreaterThanOrEqualTo(6, "the posts, the six-yard line and the markers");
        (held / matches).Should().BeInRange(1.5, 3.5, "two or three are held back");
        sixYard.Should().BeLessThanOrEqualTo(8, "a crowded six-yard box is a handful of players, not a queue");
    }

    private static bool InTheBox(Vec point) =>
        Math.Abs(point.X - FilmSpace.Length) <= 16.5 && Math.Abs(point.Y - (FilmSpace.Width / 2)) <= 20.2;

    private static (FilmContext Context, FilmShape Shape) ShapeOf(ulong seed)
    {
        var input = TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed));
        var (result, _, _) = TestMatchFactory.Script(input);
        var context = new FilmContext(input, result, TestMatchFactory.Rules, new HighlightOptionsV1());

        return (context, new FilmShape(context));
    }

    private static Vec[] Targets(FilmShape shape, FilmRoster roster, MatchSide taking, Vec flag, int taker, bool arrived)
    {
        var targets = new Vec[FilmRoster.Size];

        shape.Fill(roster, new ShapeState(FormationMode.Corner, taking, flag, Taker: taker, Arrived: arrived), FilmSpace.Centre, targets);

        return targets;
    }

    private static List<int> Outfield(FilmContext context, MatchSide side)
    {
        var entities = new List<int>(10);

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
            {
                entities.Add(entity);
            }
        }

        return entities;
    }

    /// <summary>The outfield players' places, sorted, so that two layouts can be compared whoever stands in them.</summary>
    private static List<Vec> Places(Vec[] targets, FilmContext context) =>
    [
        .. Outfield(context, MatchSide.Home).Concat(Outfield(context, MatchSide.Away))
            .Select(entity => targets[entity])
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y),
    ];
}
