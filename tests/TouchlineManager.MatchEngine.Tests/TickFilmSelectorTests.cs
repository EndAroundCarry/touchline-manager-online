using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>Verifies which stretches of a tick match the film shows (Milestone 8).</summary>
public sealed class TickFilmSelectorTests
{
    private static readonly HighlightOptionsV1 Options = new();

    private static IReadOnlyList<TickFilmWindow> Select(TickSample sample) =>
        TickFilmSelector.Select(sample.Recording, sample.Result.Events, Options);

    [Fact]
    public void The_film_plays_at_fifty_milliseconds_a_tick_by_default()
    {
        TickFilmSelector.MillisecondsPerFrame(Options).Should().Be(50);
        TickFilmSelector.MillisecondsPerFrame(Options with { TickFilmPaceMilli = 1_000 }).Should().Be(100);
        TickFilmSelector.MillisecondsPerFrame(Options with { TickFilmPaceMilli = 4_000 }).Should().Be(25);
    }

    [Fact]
    public void The_stretches_are_in_order_and_never_overlap()
    {
        var windows = Select(TickPlay.Even);

        windows.Should().NotBeEmpty();

        for (var index = 0; index < windows.Count; index++)
        {
            windows[index].Start.Should().BeLessThanOrEqualTo(windows[index].End);

            if (index > 0)
            {
                windows[index].Start.Should().BeGreaterThan(windows[index - 1].End + TickFilmSelector.JoinGap, "stretches that near each other are one");
            }
        }
    }

    [Fact]
    public void No_stretch_crosses_the_interval()
    {
        var sample = TickPlay.Even;
        var secondHalf = sample.Recording.SecondHalfFrame;

        Select(sample).Should().OnlyContain(window => window.End < secondHalf || window.Start >= secondHalf);
    }

    [Fact]
    public void The_film_opens_on_the_kick_off_restarts_with_the_second_half_and_ends_on_the_final_whistle()
    {
        var sample = TickPlay.Even;
        var windows = Select(sample);

        windows[0].Start.Should().Be(0);
        windows.Should().Contain(window => window.Start == sample.Recording.SecondHalfFrame);
        windows[^1].End.Should().Be(sample.Recording.FrameCount - 1);
    }

    [Fact]
    public void Every_goal_is_shown_with_the_play_that_built_it()
    {
        var sample = TickPlay.Even;
        var windows = Select(sample);
        var frameOf = sample.Recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, sample.Recording.FrameCount - 1));
        var goals = sample.Result.Events.Where(matchEvent => matchEvent.IsGoal).ToList();

        goals.Should().NotBeEmpty();

        foreach (var goal in goals)
        {
            var frame = frameOf[goal.Sequence];

            windows.Should().Contain(
                window => window.Start <= frame - 120 && window.End >= frame + 40,
                $"goal {goal.Sequence} is shown with at least twelve seconds of build-up and the celebration");
        }
    }

    [Fact]
    public void The_film_is_about_ten_minutes_whatever_the_match()
    {
        var sample = TickPlay.Even;
        var windows = Select(sample);
        var frames = windows.Sum(window => window.Length);
        var milliseconds = frames * TickFilmSelector.MillisecondsPerFrame(Options);

        milliseconds.Should().BeInRange(Options.MinFilmMilliseconds - 3_000, Options.MaxFilmMilliseconds - ((int)(Options.HalfTimeHoldSeconds * 1_000)));
    }

    [Fact]
    public void A_red_card_is_shown_too()
    {
        var sample = TickPlay.Reduced;
        var windows = Select(sample);
        var frameOf = sample.Recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, sample.Recording.FrameCount - 1));
        var reds = sample.Result.Events.Where(matchEvent => matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard).ToList();

        reds.Should().NotBeEmpty();

        // A film that is full of goals drops the lesser cards, but never a goal.
        var shown = reds.Count(red => windows.Any(window => window.Start <= frameOf[red.Sequence] && window.End >= frameOf[red.Sequence]));

        shown.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_choice_is_a_pure_function_of_the_recording()
    {
        var sample = TickPlay.Even;

        Select(sample).Should().Equal(Select(sample));
    }
}
