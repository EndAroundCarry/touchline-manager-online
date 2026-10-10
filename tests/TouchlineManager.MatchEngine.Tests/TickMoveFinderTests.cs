using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>Verifies where a move is found to have begun, on recordings built to have one known shape (`tick-film-v1`, Milestone 2).</summary>
public sealed class TickMoveFinderTests
{
    private const int Home = 0;
    private const int HomeForward = 9;
    private const int AwayWinger = 15;

    private static TickMatchRecording Record(
        int frames,
        Func<int, int> controller,
        Func<int, TickPlayState>? state = null,
        Func<int, int>? period = null,
        params (int Frame, int Entity, PassageAction Action)[] actions)
    {
        var recording = new TickMatchRecording();

        for (var frame = 0; frame < frames; frame++)
        {
            foreach (var stamp in actions.Where(stamp => stamp.Frame == frame))
            {
                recording.AddAction(stamp.Entity, stamp.Action);
            }

            recording.BeginFrame(
                frame / 10,
                period?.Invoke(frame) ?? 1,
                state?.Invoke(frame) ?? TickPlayState.OpenPlay,
                controller(frame),
                TickFrameFlags.None);
        }

        return recording;
    }

    [Fact]
    public void A_move_that_began_with_a_tackle_twenty_five_seconds_before_the_goal_opens_three_seconds_before_the_tackle()
    {
        var recording = Record(
            700,
            frame => (frame is >= 10 and < 300) ? 11 : (frame is >= 300 and < 550) ? HomeForward : -1,
            frame => frame < 10 ? TickPlayState.KickOffPending : TickPlayState.OpenPlay,
            actions: [(100, AwayWinger, PassageAction.Pass), (300, HomeForward, PassageAction.Tackle), (546, HomeForward, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 550, Home).Should().Be(270, "the regain was at 300 and the film opens three seconds before it");
    }

    [Fact]
    public void The_move_is_found_from_the_strike_and_not_from_the_event_that_follows_it()
    {
        // The shot is struck at 500; the ball is in the net at 540, after the other side's keeper has had it and let go.
        var recording = Record(
            800,
            frame => (frame is >= 200 and < 500) ? HomeForward : (frame is >= 500 and < 520) ? 11 : -1,
            actions: [(200, HomeForward, PassageAction.Tackle), (500, HomeForward, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 540, Home).Should().Be(170);
    }

    [Fact]
    public void A_long_possession_is_capped_at_forty_seconds_and_opens_on_a_pass()
    {
        // Seventy-five seconds of possession, with a pass every 1.7 s.
        var passes = Enumerable.Range(3, 43).Select(index => (Frame: index * 17, Entity: 8, Action: PassageAction.Pass)).ToList();
        var recording = Record(
            1_000,
            frame => (frame is >= 50 and < 800) ? 8 : -1,
            actions: [.. passes, (797, 8, PassageAction.Shot)]);

        var start = TickMoveFinder.StartOf(recording, 800, Home);

        start.Should().Be(391, "the last pass at or before the cap, which is frame 400");
        (800 - start).Should().BeInRange(TickMoveFinder.MaxLead, TickMoveFinder.MaxLead + TickMoveFinder.SnapReach);
    }

    [Fact]
    public void A_capped_move_with_no_pass_near_the_cap_opens_at_the_cap()
    {
        var recording = Record(1_000, frame => (frame is >= 50 and < 800) ? 8 : -1, actions: [(797, 8, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 800, Home).Should().Be(800 - TickMoveFinder.MaxLead);
    }

    [Fact]
    public void A_short_move_is_shown_for_at_least_the_minimum_lead()
    {
        var recording = Record(
            600,
            frame => (frame is >= 450 and < 496) ? HomeForward : frame < 450 ? 11 : -1,
            actions: [(450, HomeForward, PassageAction.Interception), (496, HomeForward, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 500, Home).Should().Be(500 - TickMoveFinder.MinLead);
        TickMoveFinder.StartOf(recording, 500, Home, minLead: 180).Should().Be(500 - 180, "a caller can ask for more");
    }

    [Fact]
    public void The_lead_never_reaches_into_the_half_before()
    {
        var recording = Record(
            900,
            frame => (frame is >= 450 and < 800) ? HomeForward : -1,
            period: frame => frame < 600 ? 1 : 2,
            actions: [(696, HomeForward, PassageAction.Shot)]);

        recording.SecondHalfFrame.Should().Be(600);
        TickMoveFinder.StartOf(recording, 700, Home).Should().Be(600, "the move began in the first half, and the film does not cross the interval");
        TickMoveFinder.StartOf(recording, 650, Home).Should().Be(600, "even when that leaves less than the minimum lead");
    }

    [Fact]
    public void A_move_that_began_at_a_restart_opens_a_second_before_the_kick()
    {
        var recording = Record(
            600,
            frame => (frame is >= 205 and < 330) ? 7 : -1,
            frame => frame < 200 ? TickPlayState.ThrowInPending : TickPlayState.OpenPlay,
            actions: [(205, 7, PassageAction.Pass), (326, 7, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 330, Home).Should().Be(195, "the walk stops at the stoppage, and the play before it is not the move");
    }

    [Fact]
    public void An_opponents_touch_of_half_a_second_is_not_a_regain_but_one_of_more_is()
    {
        TickMatchRecording Deflected(int frames) => Record(
            600,
            frame => (frame is (>= 100 and < 300) or (>= 306 and < 450)) ? HomeForward : (frame >= 300 && frame < 300 + frames) ? 11 : -1,
            actions: [(100, HomeForward, PassageAction.Tackle), (446, HomeForward, PassageAction.Shot)]);

        TickMoveFinder.StartOf(Deflected(5), 450, Home).Should().Be(70, "five frames of the other side's control are a deflection");
        TickMoveFinder.StartOf(Deflected(6), 450, Home).Should().Be(276, "six are a regain, and the move began when it was won back");
    }

    [Fact]
    public void A_ball_nobody_holds_for_a_second_and_a_half_does_not_end_a_move_but_longer_does()
    {
        TickMatchRecording Loose(int frames) => Record(
            600,
            frame => ((frame is >= 100 and < 300) || (frame >= 300 + frames && frame < 450)) ? HomeForward : -1,
            actions: [(100, HomeForward, PassageAction.Tackle), (299, AwayWinger, PassageAction.Pass), (446, HomeForward, PassageAction.Shot)]);

        TickMoveFinder.StartOf(Loose(12), 450, Home).Should().Be(70, "a loose ball for 1.2 s is forgiven");
        TickMoveFinder.StartOf(Loose(20), 450, Home).Should().Be(290, "two seconds is a different move");
    }

    [Fact]
    public void The_away_side_is_found_by_its_own_entities()
    {
        var recording = Record(
            600,
            frame => (frame is >= 100 and < 450) ? 16 : -1,
            actions: [(100, 16, PassageAction.Interception), (446, 16, PassageAction.Shot)]);

        TickMoveFinder.StartOf(recording, 450, side: 1).Should().Be(70);
        TickMoveFinder.StartOf(recording, 450, side: 0).Should().Be(350, "the home side never had the ball, so its move is the minimum lead");
    }

    [Fact]
    public void Every_goal_in_a_real_match_has_a_start_that_is_in_range_and_before_the_goal()
    {
        var sample = TickPlay.Even;
        var recording = sample.Recording;
        var frameOf = recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, recording.FrameCount - 1));

        foreach (var goal in sample.Result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            var frame = frameOf[goal.Sequence];
            var start = TickMoveFinder.StartOf(recording, frame, (int)goal.Side);
            var floor = recording.SecondHalfFrame > 0 && frame >= recording.SecondHalfFrame ? recording.SecondHalfFrame : 0;

            start.Should().BeGreaterThanOrEqualTo(floor);
            (frame - start).Should().BeLessThanOrEqualTo(TickMoveFinder.MaxLead + TickMoveFinder.SnapReach);
            (frame - start).Should().BeGreaterThanOrEqualTo(Math.Min(TickMoveFinder.MinLead, frame - floor));
            TickMoveFinder.StartOf(recording, frame, (int)goal.Side).Should().Be(start, "the answer is a pure function of the recording");
        }
    }
}
