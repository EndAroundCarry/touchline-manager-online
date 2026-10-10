using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies that a man finds where to meet a ball on its way (`tick-film-v1`, Milestone 4).
/// </summary>
public sealed class TickInterceptionTests
{
    private const int Row = 3_500;

    [Fact]
    public void A_firm_ground_pass_is_met_near_its_target_and_not_where_it_comes_to_rest()
    {
        var ball = PassedBall(fromX: 3_000, toX: 5_000);
        var rest = RestOf(ball);
        var receiver = TickPlayerState.Standing(5_000, Row + 300, heading: 0, conditionBasisPoints: 10_000);

        var ticks = TickInterception.EarliestReach(Copy(ball), receiver, ProfileOf(pace: 12), out var point);

        ticks.Should().BeGreaterThan(0).And.BeLessThan(25);
        rest.X.Should().BeGreaterThan(point.X + 1_000, "the ball runs on well past the man it was played to, and he should not wait for that");
        point.X.Should().BeInRange(4_000, 5_700);
    }

    [Fact]
    public void A_ball_already_at_his_feet_is_reached_on_the_first_tick()
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(4_000, Row);

        var man = TickPlayerState.Standing(4_050, Row, heading: 0, conditionBasisPoints: 10_000);

        TickInterception.EarliestReach(Copy(ball), man, ProfileOf(pace: 10), out var point).Should().Be(1);
        point.X.Should().Be(4_000);
    }

    [Fact]
    public void A_quicker_man_reaches_a_ball_a_slower_one_cannot()
    {
        var ball = PassedBall(fromX: 3_000, toX: 5_000);
        var runner = TickPlayerState.Standing(5_000, Row + 1_400, heading: 0, conditionBasisPoints: 10_000);

        var fast = TickInterception.EarliestReach(Copy(ball), runner, ProfileOf(pace: 20, acceleration: 20), out _);
        var slow = TickInterception.EarliestReach(Copy(ball), runner, ProfileOf(pace: 1, acceleration: 1), out _);

        fast.Should().BeGreaterThan(0);
        (slow < 0 || slow > fast).Should().BeTrue("a slow man gets there later, if at all");
    }

    [Fact]
    public void A_man_who_cannot_get_to_the_ball_is_told_so_and_given_the_point_it_ends_at()
    {
        var ball = PassedBall(fromX: 3_000, toX: 5_000);
        var faraway = TickPlayerState.Standing(500, 500, heading: 0, conditionBasisPoints: 10_000);
        Span<TickBallPathPoint> path = stackalloc TickBallPathPoint[TickInterception.PathTicks];

        TickInterception.Trace(Copy(ball), path);

        TickInterception.EarliestReach(path, faraway, ProfileOf(pace: 10), out var point).Should().Be(-1);
        point.X.Should().Be(TickSpatialUnits.ToUnits(path[^1].X));
        point.Y.Should().Be(TickSpatialUnits.ToUnits(path[^1].Y));
    }

    [Fact]
    public void A_man_facing_away_loses_time_to_the_turn()
    {
        var ball = PassedBall(fromX: 3_000, toX: 5_000);
        var facing = TickPlayerState.Standing(5_000, Row + 800, heading: TickTrigonometry.HalfTurn + TickTrigonometry.QuarterTurn, conditionBasisPoints: 10_000);
        var away = TickPlayerState.Standing(5_000, Row + 800, heading: TickTrigonometry.QuarterTurn, conditionBasisPoints: 10_000);
        var profile = ProfileOf(pace: 10);

        var toward = TickInterception.EarliestReach(Copy(ball), facing, profile, out _);
        var backwards = TickInterception.EarliestReach(Copy(ball), away, profile, out _);

        toward.Should().BeGreaterThan(0);
        backwards.Should().BeGreaterThanOrEqualTo(toward);
    }

    [Fact]
    public void A_ball_too_high_to_play_is_not_met_until_it_comes_down()
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(3_000, Row);
        var flight = ball.LaunchLofted(5_500, Row, apexZUnits: 60);

        var under = TickPlayerState.Standing(5_300, Row, heading: 0, conditionBasisPoints: 10_000);

        var ticks = TickInterception.EarliestReach(Copy(ball), under, ProfileOf(pace: 10), out var point);

        ticks.Should().BeGreaterThan(flight / 2, "it is over his head for most of its flight");
        point.X.Should().BeInRange(4_900, 5_700, "he meets it as it comes down, not while it is overhead");
    }

    [Fact]
    public void Tracing_a_path_does_not_disturb_the_real_ball()
    {
        var ball = PassedBall(fromX: 3_000, toX: 5_000);
        var before = (ball.X, ball.Y, ball.VelocityX);
        Span<TickBallPathPoint> path = stackalloc TickBallPathPoint[TickInterception.PathTicks];

        TickInterception.Trace(Copy(ball), path);

        (ball.X, ball.Y, ball.VelocityX).Should().Be(before);
        path[0].X.Should().BeGreaterThan(ball.X);
        path[^1].X.Should().BeGreaterThanOrEqualTo(path[10].X);
    }

    private static TickBallPhysics PassedBall(int fromX, int toX)
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(fromX, Row);
        ball.LaunchRolling(toX, Row, TickSpatialUnits.SpeedToFixedPerTick(1_000));

        return ball;
    }

    private static TickBallPhysics Copy(TickBallPhysics ball)
    {
        var copy = new TickBallPhysics();

        copy.CopyFrom(ball);

        return copy;
    }

    private static SpatialPoint RestOf(TickBallPhysics ball)
    {
        var copy = Copy(ball);

        for (var tick = 0; tick < 200 && (copy.GroundSpeed > 0 || copy.IsAirborne); tick++)
        {
            copy.Step();
        }

        return new SpatialPoint(copy.UnitX, copy.UnitY);
    }

    private static TickPlayerProfile ProfileOf(int pace = 10, int acceleration = 10)
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();

        values[(int)MatchAttributeName.Pace] = pace;
        values[(int)MatchAttributeName.Acceleration] = acceleration;

        return TickPlayerProfile.From(PlayerAttributesV1.From(values));
    }
}
