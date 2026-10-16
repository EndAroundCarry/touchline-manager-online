using FluentAssertions;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies the intent and detail the loop writes on its action stamps, so the commentary can say who a pass was for and what a save
/// was (`tick-replay-v4`, Milestone 1). The stamps are recording only: <c>Recording_the_match_for_a_film_does_not_change_it</c> and the
/// golden output hash in the engine tests are what hold the match itself still.
/// </summary>
public sealed class TickActionStampTests
{
    private const int Half = TickMatchRecording.Entities / 2;

    /// <summary>The frames a carrier waits before the commentary is told of another dribble.</summary>
    private const int DribbleGapFrames = 10;

    private static readonly Lazy<TickSample[]> Matches = new(() => [TickPlay.Even, TickPlay.Play(TickPlay.Fixture(31))]);

    private static IEnumerable<TickMatchRecording> Recordings => Matches.Value.Select(sample => sample.Recording);

    private static IEnumerable<TickActionStamp> All(Func<TickActionStamp, bool> where) =>
        Recordings.SelectMany(recording => recording.Actions).Where(where);

    [Fact]
    public void A_stamp_carries_the_target_and_detail_it_was_given_and_none_by_default()
    {
        var recording = new TickMatchRecording();

        recording.BeginFrame(0, 1, TickPlayState.OpenPlay, -1, TickFrameFlags.None);
        recording.AddAction(4, PassageAction.Pass, 7, TickActionDetail.ThroughBall);
        recording.AddAction(5, PassageAction.Run);

        recording.Actions[0].Should().Be(new TickActionStamp(1, 4, PassageAction.Pass, 7, TickActionDetail.ThroughBall));
        recording.Actions[1].Target.Should().Be(-1);
        recording.Actions[1].Detail.Should().Be(TickActionDetail.None);
    }

    [Fact]
    public void A_pass_in_open_play_is_stamped_with_the_team_mate_it_was_meant_for()
    {
        var passes = All(stamp => stamp.Action == PassageAction.Pass && stamp.Detail is TickActionDetail.None or TickActionDetail.ThroughBall or TickActionDetail.LongBall).ToList();
        var named = passes.Where(stamp => stamp.Target >= 0).ToList();

        passes.Should().NotBeEmpty();
        named.Count.Should().BeGreaterThan(passes.Count * 8 / 10, "a pass is for somebody; only a ball played to a point has no receiver");
        named.Should().OnlyContain(stamp => stamp.Target / Half == stamp.Entity / Half && stamp.Target != stamp.Entity);
    }

    [Fact]
    public void Every_kind_of_pass_and_shot_detail_turns_up_in_a_match()
    {
        foreach (var detail in new[] { TickActionDetail.ThroughBall, TickActionDetail.LongBall, TickActionDetail.Clear, TickActionDetail.Recycle })
        {
            All(stamp => stamp.Action == PassageAction.Pass && stamp.Detail == detail).Should().NotBeEmpty($"a match has a {detail}");
        }
    }

    [Fact]
    public void A_shot_is_a_long_range_one_when_it_is_struck_from_more_than_22_metres()
    {
        var shots = 0;

        foreach (var recording in Recordings)
        {
            foreach (var shot in recording.Actions.Where(stamp => stamp.Action == PassageAction.Shot))
            {
                var frame = shot.Frame - 1;
                var goalX = shot.Entity < Half ? SpatialPitch.PitchLength : 0;
                var distance = Math.Sqrt(Math.Pow(recording.BallX(frame) - goalX, 2) + Math.Pow(recording.BallY(frame) - SpatialPitch.GoalYCenter, 2));

                shots++;

                if (shot.Detail == TickActionDetail.LongRange)
                {
                    distance.Should().BeGreaterThan(2_000);
                }
                else
                {
                    shot.Detail.Should().Be(TickActionDetail.None);
                    distance.Should().BeLessThan(2_500);
                }
            }
        }

        shots.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_dribble_is_stamped_as_a_take_on_or_a_break_at_most_once_a_second()
    {
        var dribbles = Recordings
            .SelectMany(recording => recording.Actions.Select(stamp => (Recording: recording, Stamp: stamp)))
            .Where(entry => entry.Stamp.Action is PassageAction.TakeOn or PassageAction.Break)
            .ToList();

        All(stamp => stamp.Action == PassageAction.TakeOn).Should().NotBeEmpty("a match has take-ons");
        All(stamp => stamp.Action == PassageAction.Break).Should().NotBeEmpty("a match has breaks");

        foreach (var (recording, stamp) in dribbles)
        {
            // A stamp belongs to the frame recorded when its tick ends, so the frame before is the one he decided in.
            recording.Controller(stamp.Frame - 1).Should().Be(stamp.Entity, "he was on the ball when he set off");

            if (stamp.Action == PassageAction.TakeOn)
            {
                stamp.Target.Should().BeGreaterThanOrEqualTo(0);
                (stamp.Target / Half).Should().NotBe(stamp.Entity / Half, "he runs at an opponent");
            }
        }

        foreach (var recording in Recordings)
        {
            foreach (var group in recording.Actions.Where(stamp => stamp.Action is PassageAction.TakeOn or PassageAction.Break).GroupBy(stamp => stamp.Entity))
            {
                var frames = group.Select(stamp => stamp.Frame).Order().ToList();

                for (var index = 1; index < frames.Count; index++)
                {
                    (frames[index] - frames[index - 1]).Should().BeGreaterThanOrEqualTo(DribbleGapFrames, "one run is not told twice");
                }
            }
        }
    }

    [Fact]
    public void A_tackle_says_who_it_was_on_and_whether_the_ball_was_won_or_poked_loose()
    {
        foreach (var recording in Recordings)
        {
            foreach (var lost in recording.Actions.Where(stamp => stamp.Action == PassageAction.Dispossessed))
            {
                var tackle = recording.Actions.Single(other =>
                    other.Frame == lost.Frame && other.Action == PassageAction.Tackle && other.Entity / Half != lost.Entity / Half);

                tackle.Target.Should().Be(lost.Entity);
                lost.Target.Should().Be(tackle.Entity);
                tackle.Detail.Should().BeOneOf(TickActionDetail.TackleWon, TickActionDetail.PokedLoose);
            }
        }

        All(stamp => stamp.Action == PassageAction.Tackle && stamp.Detail == TickActionDetail.TackleWon).Should().NotBeEmpty();
        All(stamp => stamp.Action == PassageAction.Tackle && stamp.Detail == TickActionDetail.PokedLoose).Should().NotBeEmpty();
    }

    [Fact]
    public void A_dribble_that_rides_a_challenge_is_stamped_on_the_carrier_with_the_defender_he_beat()
    {
        var carries = All(stamp => stamp.Action == PassageAction.Carry && stamp.Target >= 0).ToList();

        carries.Should().NotBeEmpty();
        carries.Should().OnlyContain(stamp => stamp.Target / Half != stamp.Entity / Half);
    }

    [Fact]
    public void A_save_says_what_the_keeper_did_and_whose_shot_it_was()
    {
        var saves = All(stamp => stamp.Action == PassageAction.Save).ToList();

        saves.Should().NotBeEmpty();
        saves.Should().OnlyContain(stamp => stamp.Target >= 0 && stamp.Target / Half != stamp.Entity / Half);
        saves.Should().OnlyContain(stamp => stamp.Detail != TickActionDetail.None);
        saves.Select(stamp => stamp.Detail).Distinct().Should().Contain([TickActionDetail.Caught, TickActionDetail.Parried]);
    }

    [Fact]
    public void An_interception_names_the_man_whose_ball_it_was()
    {
        var interceptions = All(stamp => stamp.Action == PassageAction.Interception).ToList();

        interceptions.Should().NotBeEmpty();
        interceptions.Should().OnlyContain(stamp => stamp.Target >= 0 && stamp.Target / Half != stamp.Entity / Half);
    }

    [Fact]
    public void A_ball_taken_with_no_pass_on_its_way_is_a_loose_ball_and_has_no_one_to_name()
    {
        var loose = All(stamp => stamp.Detail == TickActionDetail.LooseBall).ToList();

        loose.Should().NotBeEmpty();
        loose.Should().OnlyContain(stamp => stamp.Action == PassageAction.Receive && stamp.Target == -1);
    }

    [Fact]
    public void The_intent_stamps_are_not_a_touch_of_the_ball()
    {
        // Away hold the ball and are stamped as running at a man in the middle of a loose spell: that must not make the spell theirs.
        int StartWith(bool intent)
        {
            var recording = new TickMatchRecording();

            for (var frame = 0; frame < 600; frame++)
            {
                if (frame == 100)
                {
                    recording.AddAction(9, PassageAction.Receive);
                }

                if (frame == 300)
                {
                    recording.AddAction(16, PassageAction.Tackle, 9, TickActionDetail.PokedLoose);
                }

                if (frame == 340 && intent)
                {
                    recording.AddAction(16, PassageAction.TakeOn, 9);
                    recording.AddAction(16, PassageAction.Break);
                }

                if (frame == 446)
                {
                    recording.AddAction(9, PassageAction.Shot);
                }

                recording.BeginFrame(frame / 10, 1, TickPlayState.OpenPlay, frame is (>= 100 and < 300) or (>= 380 and < 450) ? 9 : -1, TickFrameFlags.None);
            }

            return TickMoveFinder.StartOf(recording, 450, side: 0);
        }

        StartWith(intent: true).Should().Be(StartWith(intent: false));
    }
}
