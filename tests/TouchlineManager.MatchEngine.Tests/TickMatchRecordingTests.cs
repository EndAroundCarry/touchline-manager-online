using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>Verifies the continuous trace a tick match is recorded into (Milestone 8).</summary>
public sealed class TickMatchRecordingTests
{
    [Fact]
    public void A_new_frame_has_every_slot_absent_until_it_is_set()
    {
        var recording = new TickMatchRecording();

        recording.BeginFrame(0, 1, TickPlayState.KickOffPending, -1, TickFrameFlags.None);
        recording.SetPlayer(3, 1_200, 3_400);
        recording.SetBall(5_000, 3_500, 0);

        recording.FrameCount.Should().Be(1);
        recording.PlayerX(0, 3).Should().Be(1_200);
        recording.PlayerY(0, 3).Should().Be(3_400);
        recording.PlayerX(0, 4).Should().Be(TickMatchRecording.Absent);
        recording.BallX(0).Should().Be(5_000);
        recording.BallY(0).Should().Be(3_500);
        recording.State(0).Should().Be(TickPlayState.KickOffPending);
        recording.Controller(0).Should().Be(-1);
    }

    [Fact]
    public void A_frame_does_not_inherit_the_slots_of_the_one_before()
    {
        var recording = new TickMatchRecording();

        recording.BeginFrame(0, 1, TickPlayState.OpenPlay, 5, TickFrameFlags.None);
        recording.SetPlayer(5, 100, 200);
        recording.BeginFrame(0, 1, TickPlayState.OpenPlay, -1, TickFrameFlags.None);

        recording.PlayerX(0, 5).Should().Be(100);
        recording.PlayerX(1, 5).Should().Be(TickMatchRecording.Absent, "a slot nobody sets in a frame is empty in it");
        recording.Controller(0).Should().Be(5);
    }

    [Fact]
    public void The_recording_grows_past_its_first_capacity_and_keeps_every_frame()
    {
        var recording = new TickMatchRecording();

        for (var frame = 0; frame < 20_000; frame++)
        {
            recording.BeginFrame(frame / 10, 1, TickPlayState.OpenPlay, -1, TickFrameFlags.None);
            recording.SetPlayer(0, frame % 10_000, frame % 7_000);
            recording.SetBall(frame % 10_000, 0, frame % 100);
        }

        recording.FrameCount.Should().Be(20_000);
        recording.PlayerX(0, 0).Should().Be(0);
        recording.PlayerX(8_191, 0).Should().Be(8_191);
        recording.PlayerX(8_192, 0).Should().Be(8_192);
        recording.PlayerY(19_999, 0).Should().Be(19_999 % 7_000);
        recording.ClockSecond(19_999).Should().Be(1_999);
        recording.BallZ(12_345).Should().Be(12_345 % 100);
    }

    [Fact]
    public void The_second_half_is_found_by_its_first_frame()
    {
        var recording = new TickMatchRecording();

        recording.SecondHalfFrame.Should().Be(-1);

        for (var frame = 0; frame < 10; frame++)
        {
            recording.BeginFrame(frame, frame < 6 ? 1 : 2, TickPlayState.OpenPlay, -1, TickFrameFlags.None);
        }

        recording.SecondHalfFrame.Should().Be(6);
        recording.Period(5).Should().Be(1);
        recording.Period(6).Should().Be(2);
    }

    [Fact]
    public void Stamps_belong_to_the_frame_about_to_be_recorded()
    {
        var recording = new TickMatchRecording();

        recording.AddEvent(1);
        recording.BeginFrame(0, 1, TickPlayState.OpenPlay, -1, TickFrameFlags.None);
        recording.AddAction(4, PassageAction.Pass);
        recording.AddRoster(2, Guid.Empty);
        recording.AddStoppage(TickPlayState.CornerPending, TickRestartKind.Corner, takerIsHome: true);
        recording.BeginFrame(0, 1, TickPlayState.OpenPlay, -1, TickFrameFlags.None);

        recording.Events.Should().ContainSingle().Which.Frame.Should().Be(0);
        recording.Actions.Should().ContainSingle().Which.Frame.Should().Be(1);
        recording.Rosters.Should().ContainSingle().Which.Frame.Should().Be(1);
        recording.Stoppages.Should().ContainSingle().Which.Should().Be(
            new TickStoppageStamp(1, TickPlayState.CornerPending, TickRestartKind.Corner, true));
    }
}
