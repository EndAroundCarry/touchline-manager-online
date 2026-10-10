using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the stamps that say who erred and who was beaten, on a whole recorded match, and that the film carries them
/// (`tick-film-v1`, Milestone 3).
/// </summary>
public sealed class TickBlameTests
{
    private const int Half = TickMatchRecording.Entities / 2;

    private static readonly PassageAction[] Blame =
        [PassageAction.Beaten, PassageAction.Dispossessed, PassageAction.Misplaced, PassageAction.Bypassed];

    private static readonly Lazy<TickSample[]> Matches = new(() => [TickPlay.Even, TickPlay.Play(TickPlay.Fixture(31))]);

    private static IEnumerable<TickMatchRecording> Recordings => Matches.Value.Select(sample => sample.Recording);

    private static IEnumerable<TickActionStamp> Stamps(TickMatchRecording recording, PassageAction action) =>
        recording.Actions.Where(stamp => stamp.Action == action);

    [Fact]
    public void A_match_stamps_every_kind_of_blame()
    {
        foreach (var action in Blame)
        {
            Recordings.Sum(recording => Stamps(recording, action).Count()).Should().BeGreaterThan(0, $"a match has {action} moments");
        }
    }

    [Fact]
    public void A_man_dispossessed_was_on_the_ball_and_the_tackler_is_stamped_with_him()
    {
        foreach (var recording in Recordings)
        {
            foreach (var lost in Stamps(recording, PassageAction.Dispossessed))
            {
                recording.Controller(lost.Frame - 1).Should().Be(lost.Entity, "the carrier had the ball when the tackle went in");
                recording.Actions
                    .Any(other => other.Frame == lost.Frame && other.Action == PassageAction.Tackle && other.Entity / Half != lost.Entity / Half)
                    .Should().BeTrue("the man who won it is stamped at the same frame");
            }
        }
    }

    [Fact]
    public void A_man_beaten_is_on_the_other_side_to_the_man_who_still_has_the_ball()
    {
        foreach (var recording in Recordings)
        {
            foreach (var beaten in Stamps(recording, PassageAction.Beaten))
            {
                var carrier = recording.Controller(Math.Min(beaten.Frame, recording.FrameCount - 1));

                carrier.Should().BeGreaterThanOrEqualTo(0, "the carrier went past him with the ball");
                (carrier / Half).Should().NotBe(beaten.Entity / Half);
            }
        }
    }

    [Fact]
    public void A_misplaced_pass_was_cut_out_or_left_the_pitch_at_the_moment_it_is_stamped()
    {
        foreach (var recording in Recordings)
        {
            foreach (var lost in Stamps(recording, PassageAction.Misplaced))
            {
                var cutOut = recording.Actions.Any(other =>
                    other.Frame == lost.Frame && other.Action == PassageAction.Interception && other.Entity / Half != lost.Entity / Half);

                (cutOut || recording.State(Math.Min(lost.Frame, recording.FrameCount - 1)) != TickPlayState.OpenPlay)
                    .Should().BeTrue("the pass ended in an interception or in a restart");
            }
        }
    }

    [Fact]
    public void A_man_bypassed_is_behind_the_carrier_who_ran_past_him()
    {
        foreach (var recording in Recordings)
        {
            foreach (var passed in Stamps(recording, PassageAction.Bypassed))
            {
                var frame = Math.Min(passed.Frame, recording.FrameCount - 1);
                var carrier = recording.Controller(frame);

                carrier.Should().BeGreaterThanOrEqualTo(0);
                (carrier / Half).Should().NotBe(passed.Entity / Half);

                // The home side attacks towards a higher X.
                var forward = carrier / Half == 0 ? 1 : -1;

                ((recording.PlayerX(frame, passed.Entity) - recording.PlayerX(frame, carrier)) * forward)
                    .Should().BeLessThan(0, "he was left on the wrong side of the man with the ball");
            }
        }
    }

    [Fact]
    public void A_man_is_not_shown_beaten_or_bypassed_twice_within_three_seconds()
    {
        foreach (var recording in Recordings)
        {
            var groups = recording.Actions
                .Where(stamp => stamp.Action is PassageAction.Beaten or PassageAction.Bypassed)
                .GroupBy(stamp => stamp.Entity);

            foreach (var group in groups)
            {
                var frames = group.Select(stamp => stamp.Frame).Order().ToList();

                for (var index = 1; index < frames.Count; index++)
                {
                    (frames[index] - frames[index - 1]).Should().BeGreaterThanOrEqualTo(30);
                }
            }
        }
    }

    [Fact]
    public void Blame_is_not_a_touch_of_the_ball_so_it_does_not_move_where_a_move_began()
    {
        // Home hold the ball from 100, an away man pokes it loose at 300 and the home carrier is stamped as dispossessed after him;
        // the ball is loose for two seconds, so that is a regain, and the move began when home got it back.
        var recording = new TickMatchRecording();
        (int Frame, int Entity, PassageAction Action)[] actions =
        [
            (100, 9, PassageAction.Tackle),
            (300, 16, PassageAction.Tackle),
            (300, 9, PassageAction.Dispossessed),
            (446, 9, PassageAction.Shot),
        ];

        for (var frame = 0; frame < 600; frame++)
        {
            foreach (var stamp in actions.Where(stamp => stamp.Frame == frame))
            {
                recording.AddAction(stamp.Entity, stamp.Action);
            }

            var controller = frame is (>= 100 and < 300) or (>= 320 and < 450) ? 9 : -1;

            recording.BeginFrame(frame / 10, 1, TickPlayState.OpenPlay, controller, TickFrameFlags.None);
        }

        TickMoveFinder.StartOf(recording, 450, side: 0).Should().Be(290);
    }

    [Fact]
    public void The_film_carries_the_stamps_on_keyframes_and_narrates_them()
    {
        var sample = TickPlay.Even;
        var presentation = ReplayDirector.Build(sample.Input, sample.Result, sample.Recorder, new HighlightOptionsV1(), sample.Metrics.Metrics);
        var codes = Blame.Select(action => action.Code()).ToHashSet();
        var tagged = presentation.Passages
            .SelectMany(passage => passage.Tracks)
            .SelectMany(track => track.Keyframes)
            .Where(keyframe => keyframe.Action is not null && codes.Contains(keyframe.Action))
            .ToList();

        tagged.Should().NotBeEmpty("an action keyframe is never dropped by the compressor");
        tagged.Select(keyframe => keyframe.Action).Distinct().Should().OnlyContain(code => codes.Contains(code!));

        presentation.Passages
            .SelectMany(passage => passage.Commentary)
            .Where(line => line.TemplateKey is "match.build.loses_ball" or "match.build.skipped")
            .Should().NotBeEmpty("the film says who lost it and who was skipped");
    }
}
