using FluentAssertions;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's restart state machine: which state the match is in, how long each restart is held, what a goal
/// and half-time do, and where each restart puts the ball (Milestone 7).
/// </summary>
public sealed class TickMatchStateMachineTests
{
    [Fact]
    public void A_new_machine_is_in_open_play_and_asks_for_nothing()
    {
        var machine = new TickMatchStateMachine();

        machine.State.Should().Be(TickPlayState.OpenPlay);
        machine.IsDeadBall.Should().BeFalse();
        machine.Step(takerReady: true).Should().Be(TickRestartSignal.None);
    }

    [Theory]
    [InlineData("KickOff", "KickOffPending")]
    [InlineData("GoalKick", "GoalKickPending")]
    [InlineData("Corner", "CornerPending")]
    [InlineData("ThrowIn", "ThrowInPending")]
    [InlineData("FreeKick", "FreeKickPending")]
    [InlineData("Penalty", "PenaltyPending")]
    public void Beginning_a_restart_puts_the_match_in_the_matching_pending_state(string kind, string expected)
    {
        var machine = new TickMatchStateMachine();

        machine.Begin(Restart(Enum.Parse<TickRestartKind>(kind)));

        machine.State.ToString().Should().Be(expected);
        machine.IsDeadBall.Should().BeTrue();
        machine.Restart.Kind.Should().Be(Enum.Parse<TickRestartKind>(kind));
    }

    [Theory]
    [InlineData("ThrowIn", 8)]
    [InlineData("GoalKick", 10)]
    [InlineData("KickOff", 12)]
    [InlineData("Corner", 15)]
    [InlineData("FreeKick", 15)]
    [InlineData("Penalty", 15)]
    public void A_restart_is_held_for_its_setup_time_even_when_the_taker_is_already_there(string kind, int ticks)
    {
        var machine = new TickMatchStateMachine();

        machine.Begin(Restart(Enum.Parse<TickRestartKind>(kind)));

        for (var tick = 1; tick < ticks; tick++)
        {
            machine.Step(takerReady: true).Should().Be(TickRestartSignal.Waiting, $"tick {tick} is inside the hold");
        }

        machine.Step(takerReady: true).Should().Be(TickRestartSignal.Execute);
        machine.State.Should().Be(TickPlayState.OpenPlay);
    }

    [Fact]
    public void Every_hold_is_between_eight_tenths_of_a_second_and_a_second_and_a_half()
    {
        foreach (var kind in Enum.GetValues<TickRestartKind>())
        {
            TickMatchStateMachine.SetupTicks(kind).Should().BeInRange(8, 15, "the plan holds a restart for 0.8 s to 1.5 s");
        }
    }

    [Fact]
    public void A_restart_waits_for_a_taker_who_is_not_at_the_ball_and_then_goes_the_tick_he_arrives()
    {
        var machine = new TickMatchStateMachine();

        machine.Begin(Restart(TickRestartKind.Corner));

        for (var tick = 1; tick <= 40; tick++)
        {
            machine.Step(takerReady: false).Should().Be(TickRestartSignal.Waiting);
        }

        machine.Step(takerReady: true).Should().Be(TickRestartSignal.Execute, "the taker arrived after the minimum hold");
    }

    [Fact]
    public void A_taker_who_never_arrives_does_not_stall_the_match()
    {
        var machine = new TickMatchStateMachine();

        machine.Begin(Restart(TickRestartKind.FreeKick));

        for (var tick = 1; tick < TickMatchStateMachine.MaximumSetupTicks; tick++)
        {
            machine.Step(takerReady: false).Should().Be(TickRestartSignal.Waiting);
        }

        machine.Step(takerReady: false).Should().Be(TickRestartSignal.Execute, "10 s is the longest a restart is held");
    }

    [Fact]
    public void The_restart_just_taken_stays_readable_so_the_loop_can_hand_the_taker_his_kick()
    {
        var machine = new TickMatchStateMachine();
        var restart = TickRestart.Corner(homeTakes: true, ballY: 900);

        machine.Begin(restart);

        while (machine.Step(takerReady: true) != TickRestartSignal.Execute)
        {
        }

        machine.State.Should().Be(TickPlayState.OpenPlay);
        machine.Restart.Should().Be(restart);
        machine.Step(takerReady: true).Should().Be(TickRestartSignal.None, "open play asks for nothing more");
    }

    [Fact]
    public void A_goal_is_celebrated_for_eight_seconds_and_then_the_conceding_side_kicks_off()
    {
        var machine = new TickMatchStateMachine();

        machine.BeginGoalCelebration(scoredByHome: true);

        machine.State.Should().Be(TickPlayState.GoalCelebration);

        for (var tick = 1; tick < TickMatchStateMachine.CelebrationTicks; tick++)
        {
            machine.Step(takerReady: true).Should().Be(TickRestartSignal.Celebrating);
        }

        machine.Step(takerReady: true).Should().Be(TickRestartSignal.SetUp);
        machine.State.Should().Be(TickPlayState.KickOffPending);
        machine.Restart.Should().Be(TickRestart.KickOff(homeTakes: false), "the away side conceded");
        machine.TicksInState.Should().Be(0);
    }

    [Fact]
    public void After_an_away_goal_the_home_side_kicks_off()
    {
        var machine = new TickMatchStateMachine();

        machine.BeginGoalCelebration(scoredByHome: false);

        TickRestartSignal signal;

        do
        {
            signal = machine.Step(takerReady: false);
        }
        while (signal == TickRestartSignal.Celebrating);

        signal.Should().Be(TickRestartSignal.SetUp);
        machine.Restart.TakerIsHome.Should().BeTrue();
    }

    [Fact]
    public void The_kick_off_after_a_goal_is_then_held_like_any_other()
    {
        var machine = new TickMatchStateMachine();

        machine.BeginGoalCelebration(scoredByHome: true);

        while (machine.Step(takerReady: true) != TickRestartSignal.SetUp)
        {
        }

        for (var tick = 1; tick < TickMatchStateMachine.SetupTicks(TickRestartKind.KickOff); tick++)
        {
            machine.Step(takerReady: true).Should().Be(TickRestartSignal.Waiting);
        }

        machine.Step(takerReady: true).Should().Be(TickRestartSignal.Execute);
    }

    [Fact]
    public void Half_time_holds_until_the_second_half_is_begun()
    {
        var machine = new TickMatchStateMachine();

        machine.BeginHalfTime();

        machine.State.Should().Be(TickPlayState.HalfTime);
        machine.IsDeadBall.Should().BeTrue();

        for (var tick = 0; tick < 5_000; tick++)
        {
            machine.Step(takerReady: true).Should().Be(TickRestartSignal.Break);
        }

        machine.Begin(TickRestart.KickOff(homeTakes: false));

        machine.State.Should().Be(TickPlayState.KickOffPending);
        machine.Step(takerReady: false).Should().Be(TickRestartSignal.Waiting);
    }

    [Fact]
    public void A_new_restart_overrides_whatever_the_match_was_doing()
    {
        var machine = new TickMatchStateMachine();

        machine.BeginGoalCelebration(scoredByHome: true);
        machine.Step(takerReady: false);
        machine.Begin(TickRestart.Penalty(homeTakes: false));

        machine.State.Should().Be(TickPlayState.PenaltyPending);
        machine.TicksInState.Should().Be(0);
    }

    [Fact]
    public void Offside_can_only_be_given_from_a_free_kick()
    {
        foreach (var kind in Enum.GetValues<TickRestartKind>())
        {
            TickMatchStateMachine.OffsideApplies(kind).Should().Be(kind == TickRestartKind.FreeKick, kind.ToString());
        }
    }

    [Fact]
    public void The_kick_off_is_on_the_centre_spot()
    {
        TickRestart.KickOff(true).Should().Be(new TickRestart(TickRestartKind.KickOff, true, 5_000, 3_500));
        TickRestart.KickOff(false).TakerIsHome.Should().BeFalse();
    }

    [Fact]
    public void A_goal_kick_is_inside_the_six_yard_box_at_the_end_the_ball_left()
    {
        var home = TickRestart.GoalKick(homeTakes: true, ballY: 3_300);
        var away = TickRestart.GoalKick(homeTakes: false, ballY: 3_300);

        home.SpotX.Should().Be(TickRestart.GoalKickDepth);
        away.SpotX.Should().Be(10_000 - TickRestart.GoalKickDepth);
        home.SpotY.Should().Be(3_300, "taken from the side the ball left on");
        TickRestart.GoalKick(true, 100).SpotY.Should().Be(3_500 - TickRestart.GoalKickHalfWidth, "never wider than the box");
        TickRestart.GoalKick(true, 6_900).SpotY.Should().Be(3_500 + TickRestart.GoalKickHalfWidth);
    }

    [Fact]
    public void A_corner_is_at_the_flag_nearest_where_the_ball_left()
    {
        var upper = TickRestart.Corner(homeTakes: true, ballY: 1_200);
        var lower = TickRestart.Corner(homeTakes: true, ballY: 5_800);
        var away = TickRestart.Corner(homeTakes: false, ballY: 5_800);

        upper.Should().Be(new TickRestart(TickRestartKind.Corner, true, 10_000 - TickRestart.CornerInset, TickRestart.CornerInset));
        lower.SpotY.Should().Be(7_000 - TickRestart.CornerInset);
        away.SpotX.Should().Be(TickRestart.CornerInset, "the away side attacks the low-X end");
    }

    [Fact]
    public void A_throw_in_is_on_the_touchline_the_ball_crossed()
    {
        TickRestart.ThrowIn(true, 4_200, 3).Should().Be(new TickRestart(TickRestartKind.ThrowIn, true, 4_200, 0));
        TickRestart.ThrowIn(false, 4_200, 6_990).SpotY.Should().Be(7_000);
        TickRestart.ThrowIn(true, 0, 0).SpotX.Should().Be(TickRestart.FreeKickInset, "kept off the goal line");
    }

    [Fact]
    public void A_free_kick_is_where_the_foul_was_and_is_kept_inside_the_pitch()
    {
        TickRestart.FreeKick(true, 6_200, 2_900).Should().Be(new TickRestart(TickRestartKind.FreeKick, true, 6_200, 2_900));
        TickRestart.FreeKick(true, 9_990, -4).Should().Be(new TickRestart(TickRestartKind.FreeKick, true, 9_900, 100));
    }

    [Fact]
    public void A_penalty_is_on_the_spot_at_the_end_the_taker_attacks()
    {
        TickRestart.Penalty(homeTakes: true).Should().Be(new TickRestart(TickRestartKind.Penalty, true, SpatialPitch.PenaltySpotAwayX, 3_500));
        TickRestart.Penalty(homeTakes: false).Should().Be(new TickRestart(TickRestartKind.Penalty, false, SpatialPitch.PenaltySpotHomeX, 3_500));
    }

    [Fact]
    public void Stepping_the_machine_through_a_whole_match_of_restarts_allocates_nothing()
    {
        static int Run(TickMatchStateMachine machine)
        {
            var hash = 0;

            for (var tick = 0; tick < 54_000; tick++)
            {
                switch (tick % 400)
                {
                    case 0:
                        machine.Begin(TickRestart.Corner(true, tick));
                        break;
                    case 100:
                        machine.BeginGoalCelebration(tick % 800 == 0);
                        break;
                    case 300:
                        machine.BeginHalfTime();
                        break;
                }

                hash = unchecked((hash * 31) + (int)machine.Step(tick % 3 != 0) + (int)machine.State);
            }

            return hash;
        }

        var first = Run(new TickMatchStateMachine());
        var machine = new TickMatchStateMachine();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(machine);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256);
    }

    private static TickRestart Restart(TickRestartKind kind) => kind switch
    {
        TickRestartKind.KickOff => TickRestart.KickOff(true),
        TickRestartKind.GoalKick => TickRestart.GoalKick(true, 3_500),
        TickRestartKind.Corner => TickRestart.Corner(true, 100),
        TickRestartKind.ThrowIn => TickRestart.ThrowIn(true, 4_000, 0),
        TickRestartKind.FreeKick => TickRestart.FreeKick(true, 5_000, 3_000),
        _ => TickRestart.Penalty(true),
    };
}
