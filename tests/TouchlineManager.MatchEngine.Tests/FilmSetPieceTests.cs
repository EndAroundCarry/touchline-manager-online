using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// How free kicks, penalties and goal kicks are shown (`replay-v6`): a free kick is set for what it is, a penalty keeps
/// everybody out of the box and the arc, and a goal kick spreads one side and steps the other up.
/// </summary>
public sealed class FilmSetPieceTests
{
    private const double Gap = 9.15;
    private const int Seeds = 24;

    [Theory]
    [InlineData(10.0, 5)]
    [InlineData(19.9, 5)]
    [InlineData(22.0, 4)]
    [InlineData(27.0, 3)]
    [InlineData(33.0, 2)]
    [InlineData(40.0, 2)]
    public void The_wall_is_longer_the_nearer_the_kick_is(double distance, int expected) =>
        FilmShape.WallSize(distance).Should().Be(expected);

    [Theory]
    [InlineData(MatchSide.Home)]
    [InlineData(MatchSide.Away)]
    public void A_struck_free_kick_has_a_wall_on_the_near_post_and_the_keeper_on_the_far_side(MatchSide taking)
    {
        var (context, shape) = ShapeOf(3);
        var goal = FilmSpace.AttackedGoal(taking);
        var defending = MatchInputV1.OpponentOf(taking);
        var direction = FilmSpace.Direction(taking);

        foreach (var (along, across) in new[] { (18.0, 6.0), (22.0, -5.0), (27.0, 9.0), (33.0, 0.0) })
        {
            var spot = new Vec(goal.X - (direction * along), goal.Y + across);
            var taker = Outfield(context, taking).First(entity => context.Slots[entity].Family == MatchPositionFamily.Midfield);
            var targets = Targets(shape, context.Starters, FormationMode.FreeKickShot, taking, spot, taker);
            var distance = spot.DistanceTo(goal);

            targets[taker].Should().Be(spot, "the taker is on the ball");

            var wall = Outfield(context, defending).Where(entity => targets[entity].DistanceTo(spot) is >= 9.45 and <= 9.9).ToList();

            wall.Count.Should().BeGreaterThanOrEqualTo(FilmShape.WallSize(distance), "the wall is the size the distance asks for");

            foreach (var entity in Outfield(context, defending))
            {
                targets[entity].DistanceTo(spot).Should().BeGreaterThanOrEqualTo(Gap, "the defence stands ten yards off");
            }

            // On the line to the near post: the wall is between the ball and the goal, towards the ball's side of it.
            foreach (var entity in wall.Take(FilmShape.WallSize(distance)))
            {
                FilmSpace.Attacking(targets[entity], taking).Should().BeGreaterThan(FilmSpace.Attacking(spot, taking), "the wall stands further up than the ball is, not behind it");
            }

            var keeper = Keeper(context, defending);

            if (Math.Abs(across) >= 1.0)
            {
                Math.Sign(targets[keeper].Y - goal.Y).Should().Be(-Math.Sign(across), "the keeper covers the far side");
            }

            var rebound = Outfield(context, taking)
                .Where(entity => entity != taker && Math.Abs(FilmSpace.Attacking(targets[entity], MatchInputV1.OpponentOf(taking)) - 20.5) < 0.5 && Math.Abs(targets[entity].Y - goal.Y) < 14)
                .ToList();

            rebound.Count.Should().BeInRange(2, 3, "two or three wait at the edge of the box for the rebound");

            foreach (var entity in Outfield(context, taking).Where(entity => entity != taker && !rebound.Contains(entity)))
            {
                InTheBox(targets[entity], goal).Should().BeFalse("the rest of the attack is held outside the box");
            }
        }
    }

    [Fact]
    public void A_free_kick_into_the_box_has_no_wall_and_markers_on_the_runners()
    {
        var (context, shape) = ShapeOf(5);
        var taking = MatchSide.Home;
        var defending = MatchSide.Away;
        var goal = FilmSpace.AttackedGoal(taking);
        var spot = new Vec(goal.X - 34.0, goal.Y + 24.0);
        var taker = Outfield(context, taking).First();

        var waiting = Targets(shape, context.Starters, FormationMode.FreeKickCross, taking, spot, taker, arrived: false);
        var arrived = Targets(shape, context.Starters, FormationMode.FreeKickCross, taking, spot, taker, arrived: true);

        foreach (var shown in new[] { waiting, arrived })
        {
            Outfield(context, defending).Count(entity => shown[entity].DistanceTo(spot) < 10.5).Should().BeLessThanOrEqualTo(1, "there is no wall");

            foreach (var entity in Outfield(context, defending))
            {
                shown[entity].DistanceTo(spot).Should().BeGreaterThanOrEqualTo(Gap);
            }
        }

        var attackers = Outfield(context, taking).Where(entity => entity != taker).ToList();
        var defenders = Outfield(context, defending);

        attackers.Count(entity => InTheBox(arrived[entity], goal)).Should().BeInRange(4, 6, "four to six runners go into the box");
        defenders.Count(entity => InTheBox(arrived[entity], goal)).Should().BeGreaterThanOrEqualTo(5, "the markers are in it with them");
        defenders.Count(entity => FilmSpace.Attacking(arrived[entity], MatchInputV1.OpponentOf(taking)) is >= 17.0 and <= 20.5 && !InTheBox(arrived[entity], goal)).Should().BeGreaterThanOrEqualTo(1, "the line is at the edge of the box");

        var runners = attackers.Where(entity => InTheBox(arrived[entity], goal)).ToList();

        foreach (var runner in runners)
        {
            (FilmSpace.Attacking(waiting[runner], MatchInputV1.OpponentOf(taking)) - FilmSpace.Attacking(arrived[runner], MatchInputV1.OpponentOf(taking))).Should().BeApproximately(5.0, 1e-6, "a runner waits five metres short");
        }

        // Each runner has a defender within two metres of him, goal-side.
        foreach (var runner in runners)
        {
            defenders.Min(entity => arrived[entity].DistanceTo(arrived[runner])).Should().BeLessThan(2.5, "every runner has a marker");
        }
    }

    [Fact]
    public void A_free_kick_into_the_box_is_the_mirror_image_on_the_other_flank()
    {
        var (context, shape) = ShapeOf(7);
        var goal = FilmSpace.AttackedGoal(MatchSide.Home);
        var taker = Outfield(context, MatchSide.Home).First();
        var high = Targets(shape, context.Starters, FormationMode.FreeKickCross, MatchSide.Home, new Vec(goal.X - 30, goal.Y + 22), taker, arrived: true);
        var low = Targets(shape, context.Starters, FormationMode.FreeKickCross, MatchSide.Home, new Vec(goal.X - 30, goal.Y - 22), taker, arrived: true);

        var mirrored = Places(high, context).Select(point => new Vec(point.X, FilmSpace.Width - point.Y)).OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
        var other = Places(low, context);

        foreach (var (mine, theirs) in mirrored.Zip(other))
        {
            mine.X.Should().BeApproximately(theirs.X, 1e-6);
            mine.Y.Should().BeApproximately(theirs.Y, 1e-6);
        }
    }

    [Fact]
    public void The_three_free_kicks_are_not_set_alike()
    {
        var (context, shape) = ShapeOf(9);
        var goal = FilmSpace.AttackedGoal(MatchSide.Home);
        var spot = new Vec(goal.X - 28.0, goal.Y + 6.0);
        var taker = Outfield(context, MatchSide.Home).First();

        var shot = Targets(shape, context.Starters, FormationMode.FreeKickShot, MatchSide.Home, spot, taker);
        var cross = Targets(shape, context.Starters, FormationMode.FreeKickCross, MatchSide.Home, spot, taker);
        var quick = Targets(shape, context.Starters, FormationMode.FreeKickQuick, MatchSide.Home, spot, taker);

        var defenders = Outfield(context, MatchSide.Away);

        defenders.Count(entity => shot[entity].DistanceTo(spot) is >= 9.45 and <= 9.9).Should().BeGreaterThanOrEqualTo(3, "a shot has a wall");
        defenders.Count(entity => cross[entity].DistanceTo(spot) < 10.5).Should().BeLessThan(3, "a delivery has no wall");

        // A quick one keeps open play's shape, only with nobody within ten yards.
        foreach (var entity in defenders)
        {
            quick[entity].DistanceTo(spot).Should().BeGreaterThanOrEqualTo(Gap);
        }

        defenders.Count(entity => shot[entity].DistanceTo(cross[entity]) > 1.0).Should().BeGreaterThan(4);
        defenders.Count(entity => shot[entity].DistanceTo(quick[entity]) > 1.0).Should().BeGreaterThan(1);
    }

    [Theory]
    [InlineData(MatchSide.Home)]
    [InlineData(MatchSide.Away)]
    public void Nobody_stands_inside_the_arc_or_the_box_at_a_penalty(MatchSide taking)
    {
        var (context, shape) = ShapeOf(11);
        var goal = FilmSpace.AttackedGoal(taking);
        var defending = MatchInputV1.OpponentOf(taking);
        var spot = new Vec(goal.X - (FilmSpace.Direction(taking) * 11.0), goal.Y);
        var taker = Outfield(context, taking).First(entity => context.Slots[entity].Family == MatchPositionFamily.Attack);
        var targets = Targets(shape, context.Starters, FormationMode.Penalty, taking, spot, taker);

        targets[taker].Should().Be(spot);

        var keeper = Keeper(context, defending);

        Math.Abs(targets[keeper].X - goal.X).Should().BeLessThan(1.0, "the keeper is on his line");

        var others = Outfield(context, taking).Where(entity => entity != taker).Concat(Outfield(context, defending)).ToList();

        others.Should().HaveCount(19);

        foreach (var entity in others)
        {
            targets[entity].DistanceTo(spot).Should().BeGreaterThanOrEqualTo(Gap, "nobody is inside the arc");
            InTheBox(targets[entity], goal).Should().BeFalse("nobody is in the box");
        }

        var rows = others.GroupBy(entity => Math.Round(FilmSpace.Attacking(targets[entity], MatchInputV1.OpponentOf(taking)), 1)).ToList();

        rows.Should().OnlyContain(row => row.Count() <= 9, "at most nine to a row");

        // The two sides alternate along a row, so neither is stacked behind the other.
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(entity => targets[entity].Y).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                FilmRoster.SideOf(ordered[index]).Should().NotBe(FilmRoster.SideOf(ordered[index - 1]), "neighbours in a row are on different sides");
            }
        }
    }

    [Theory]
    [InlineData(MatchSide.Home)]
    [InlineData(MatchSide.Away)]
    public void A_goal_kick_spreads_the_kicking_side_and_steps_the_other_up(MatchSide kicking)
    {
        var (context, shape) = ShapeOf(13);
        var own = FilmSpace.OwnGoal(kicking);
        var receiving = MatchInputV1.OpponentOf(kicking);
        var keeper = Keeper(context, kicking);
        var spot = new Vec(own.X + (FilmSpace.Direction(kicking) * 5.5), own.Y);
        var targets = Targets(shape, context.Starters, FormationMode.GoalKick, kicking, spot, keeper);

        double Depth(int entity) => FilmSpace.Attacking(targets[entity], kicking);

        targets[keeper].Should().Be(spot, "the keeper is on the ball");

        var kickers = Outfield(context, kicking);
        var back = kickers.Where(entity => Depth(entity) <= 19.0).ToList();

        back.Count.Should().BeGreaterThanOrEqualTo(3, "the back line is at the edge of the box");
        (back.Max(entity => targets[entity].Y) - back.Min(entity => targets[entity].Y)).Should().BeGreaterThan(30.0, "and spread across the pitch");

        // The side receiving it is up around the halfway line, in its own half or just over it.
        foreach (var entity in Outfield(context, receiving))
        {
            FilmSpace.Attacking(targets[entity], receiving).Should().BeInRange(44.0, 66.0, "the other side has stepped up");
        }
    }

    [Fact]
    public void Every_set_piece_in_a_match_is_set_for_what_it_turns_out_to_be()
    {
        int struck = 0, crossed = 0, goalKicks = 0, penalties = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed)));

            foreach (var possession in script.Possessions)
            {
                var beats = script.Beats.Skip(possession.FirstBeat).Take(possession.LastBeat - possession.FirstBeat + 1).ToList();

                foreach (var hold in beats.Where(beat => beat.IsHold))
                {
                    switch (hold.Hold)
                    {
                        case HoldKind.FreeKick when possession.Source.Outcome == PassageOutcome.FreeKickCrossed && hold.Formation != FormationMode.Open:
                            hold.Formation.Should().Be(FormationMode.FreeKickCross);
                            crossed++;
                            break;

                        case HoldKind.FreeKick when possession.Source.Outcome == PassageOutcome.FreeKickStruck && hold.Formation != FormationMode.Open:
                            hold.Formation.Should().Be(FormationMode.FreeKickShot);
                            struck++;
                            break;

                        case HoldKind.GoalKick or HoldKind.KeeperBall:
                            hold.Formation.Should().Be(FormationMode.GoalKick);
                            goalKicks++;
                            break;

                        case HoldKind.Penalty:
                            hold.Formation.Should().Be(FormationMode.Penalty);
                            penalties++;
                            break;
                    }
                }
            }
        }

        goalKicks.Should().BeGreaterThan(10);
        (struck + crossed).Should().BeGreaterThan(0, "the matches have free kicks");
        _ = penalties;
    }

    [Fact]
    public void The_defence_is_off_the_ball_and_the_box_is_clear_when_a_free_kick_or_a_penalty_is_taken()
    {
        int matches = 0, penaltyMatches = 0;
        double intruders = 0, penaltyIntruders = 0, wall = 0;

        for (var seed = 1UL; seed <= 16; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            if (shape.FreeKicks > 0)
            {
                matches++;
                intruders += shape.FreeKickIntrudersP95;
                wall = Math.Max(wall, shape.FreeKickWallP50);
            }

            if (shape.Penalties > 0)
            {
                penaltyMatches++;
                penaltyIntruders = Math.Max(penaltyIntruders, shape.PenaltyIntrudersP95);
            }
        }

        matches.Should().BeGreaterThan(8, "most matches have a free kick");
        (intruders / matches).Should().BeLessThanOrEqualTo(3, "the players have taken up their places by the time it is taken, give or take a late runner");
        wall.Should().BeInRange(2, 5, "a wall is two to five players");

        if (penaltyMatches > 0)
        {
            penaltyIntruders.Should().BeLessThanOrEqualTo(1, "the box and the arc are clear");
        }
    }

    private static bool InTheBox(Vec point, Vec goal) =>
        Math.Abs(point.X - goal.X) <= 16.5 && Math.Abs(point.Y - goal.Y) <= 20.2;

    private static (FilmContext Context, FilmShape Shape) ShapeOf(ulong seed)
    {
        var input = TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed));
        var (result, _, _) = TestMatchFactory.Script(input);
        var context = new FilmContext(input, result, TestMatchFactory.Rules, new HighlightOptionsV1());

        return (context, new FilmShape(context));
    }

    private static Vec[] Targets(FilmShape shape, FilmRoster roster, FormationMode mode, MatchSide taking, Vec spot, int taker, bool arrived = true)
    {
        var targets = new Vec[FilmRoster.Size];

        shape.Fill(roster, new ShapeState(mode, taking, spot, Taker: taker, Arrived: arrived), spot, targets);

        return targets;
    }

    private static int Keeper(FilmContext context, MatchSide side) =>
        Enumerable.Range(1, 11).Select(slot => FilmRoster.Index(side, slot)).First(entity => context.Slots[entity].Family == MatchPositionFamily.Goalkeeper);

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

    private static List<Vec> Places(Vec[] targets, FilmContext context) =>
    [
        .. Outfield(context, MatchSide.Home).Concat(Outfield(context, MatchSide.Away))
            .Select(entity => targets[entity])
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y),
    ];
}
