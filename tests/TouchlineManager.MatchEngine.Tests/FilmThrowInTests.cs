using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Throw-ins (`replay-v16`): the engine records none, so the film has the ball run out where a side lost it wide, holds
/// play, and has the other side throw it in. These pin where it happens, who takes it, and how the sides stand.
/// </summary>
public sealed class FilmThrowInTests
{
    private const int Seeds = 16;

    [Fact]
    public void A_throw_in_is_the_ball_put_out_on_a_touchline_a_hold_there_and_a_short_throw_by_the_side_that_won_it()
    {
        var throwIns = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            for (var index = 1; index + 1 < script.Beats.Count; index++)
            {
                var hold = script.Beats[index];

                if (hold.Hold != HoldKind.ThrowIn)
                {
                    continue;
                }

                var runOut = script.Beats[index - 1];
                var thrown = script.Beats[index + 1];

                runOut.Kind.Should().Be(BeatKind.Placement);
                runOut.Formation.Should().Be(FormationMode.ThrowIn, "the sides set as the ball runs out");
                hold.Formation.Should().Be(FormationMode.ThrowIn);
                Math.Min(hold.To.Y, FilmSpace.Width - hold.To.Y).Should().Be(0.0, "it is taken from the touchline");
                runOut.To.Should().Be(hold.To);
                runOut.Distance.Should().BeLessThanOrEqualTo(9.0, "the ball ran out from the wide part of the pitch");

                thrown.Kind.Should().Be(BeatKind.Pass);
                thrown.From.Should().Be(hold.To);
                (thrown.Actor == hold.Actor).Should().BeTrue("the thrower throws it");
                (thrown.Receiver != thrown.Actor).Should().BeTrue("he does not throw it to himself");
                thrown.Distance.Should().BeInRange(3.0, 9.1);
                thrown.ZFrom.Should().BeGreaterThan(0, "a throw leaves the hands");
                thrown.Side.Should().Be(hold.Side);
                Math.Abs(hold.To.X - (FilmSpace.Length / 2)).Should().BeLessThanOrEqualTo((FilmSpace.Length / 2) - 12.0, "none near a goal line, where the film has corners");
                throwIns++;
            }
        }

        throwIns.Should().BeGreaterThan(Seeds * 2, "a few a match are lost wide");
    }

    [Theory]
    [InlineData(MatchSide.Home, 0.0)]
    [InlineData(MatchSide.Home, 68.0)]
    [InlineData(MatchSide.Away, 0.0)]
    [InlineData(MatchSide.Away, 68.0)]
    public void The_thrower_is_on_the_line_and_each_man_in_front_of_him_has_a_marker(MatchSide taking, double touchline)
    {
        var input = TestMatchFactory.OnTheBoard(TestMatchFactory.Even(5));
        var (result, _, _) = TestMatchFactory.Script(input);
        var context = new FilmContext(input, result, TestMatchFactory.Rules, new HighlightOptionsV1());
        var shape = new FilmShape(context);
        var defending = MatchInputV1.OpponentOf(taking);
        var spot = new Vec(40.0, touchline);
        var taker = Outfield(context, taking).First();
        var targets = new Vec[FilmRoster.Size];

        shape.Fill(context.Starters, new ShapeState(FormationMode.ThrowIn, taking, spot, Taker: taker), spot, targets);

        targets[taker].Should().Be(spot);

        var pocket = Outfield(context, taking).Where(entity => entity != taker && targets[entity].DistanceTo(spot) <= 16.5).ToList();

        pocket.Count.Should().Be(4, "four of the side stand in front of the thrower");

        foreach (var entity in pocket)
        {
            Outfield(context, defending)
                .Any(other => targets[other].DistanceTo(targets[entity]) <= 1.5)
                .Should().BeTrue("a defender stands on each of them");
        }

        foreach (var entity in Outfield(context, taking).Concat(Outfield(context, defending)).Where(entity => entity != taker))
        {
            targets[entity].DistanceTo(spot).Should().BeGreaterThanOrEqualTo(3.9, "nobody crowds the thrower");
        }
    }

    [Fact]
    public void Throw_ins_are_measured_and_the_thrower_stands_on_the_line()
    {
        var throwIns = 0;

        for (var seed = 1UL; seed <= 8; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            throwIns += shape.ThrowIns;

            if (shape.ThrowIns > 0)
            {
                shape.ThrowInPack10mP50.Should().BeInRange(1, 20);
                shape.ThrowInPack25mP50.Should().BeInRange(shape.ThrowInPack10mP50, 20);
                shape.ThrowInMarkedShare.Should().BeInRange(0, 1);
                shape.ThrowInOffLineP95.Should().BeLessThanOrEqualTo(1.0, "the thrower is at the touchline as he throws");
            }
        }

        throwIns.Should().BeGreaterThan(0, "the film has throw-ins");
    }

    private static List<int> Outfield(FilmContext context, MatchSide side) =>
    [
        .. Enumerable.Range(1, 11)
            .Select(slot => FilmRoster.Index(side, slot))
            .Where(entity => context.Slots[entity].Family != MatchPositionFamily.Goalkeeper),
    ];
}
