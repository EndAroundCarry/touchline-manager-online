using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The clock of `engine-v5` (`MAT-3`): the second half is played from 45:00 with its own stoppage, instead of
/// starting a few minutes late because the first half's stoppage was never given back.
/// </summary>
public sealed class HalfTimeClockTests
{
    private const int Seeds = 40;

    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    private static readonly int HalfTimeSeconds = Rules.HalfTimeMinute * Rules.SecondsPerMinute;

    [Fact]
    public void MAT_3_The_second_half_kicks_off_at_forty_six_minutes()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);
            var kickOff = match.Result.Events.Single(matchEvent => matchEvent.Type == EngineEventType.SecondHalfStart);

            kickOff.Minute.Should().Be(Rules.HalfTimeMinute + 1, "the second half starts at 46', not at 48' or 49'");
            kickOff.StoppageMinute.Should().Be(0);

            var firstSecondHalfPossession = match.Passages.First(passage => passage.Period == 2);

            firstSecondHalfPossession.StartClockSeconds.Should().Be(HalfTimeSeconds);
        }
    }

    [Fact]
    public void MAT_3_Each_half_plays_its_own_regulation_time_and_its_own_stoppage()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            var firstHalf = match.Passages.Where(passage => passage.Period == 1).ToList();
            var secondHalf = match.Passages.Where(passage => passage.Period == 2).ToList();

            // The first half ends only after its regulation 45 minutes and the minute or more of stoppage on top.
            firstHalf[^1].EndClockSeconds.Should().BeGreaterThanOrEqualTo(
                HalfTimeSeconds + (Rules.MinStoppageMinutes * Rules.SecondsPerMinute));

            // The second half is the other 45 minutes of regulation, and then its own stoppage.
            secondHalf[^1].EndClockSeconds.Should().BeGreaterThanOrEqualTo(
                (Rules.RegulationMinutes * Rules.SecondsPerMinute) + (Rules.MinStoppageMinutes * Rules.SecondsPerMinute));

            var secondHalfSeconds = secondHalf[^1].EndClockSeconds - secondHalf[0].StartClockSeconds;

            secondHalfSeconds.Should().BeGreaterThanOrEqualTo(
                (Rules.RegulationMinutes - Rules.HalfTimeMinute) * Rules.SecondsPerMinute,
                "a half is never shorter than its 45 minutes");
        }
    }

    [Fact]
    public void MAT_3_Event_minutes_are_regulation_minutes_in_each_half_and_stoppage_after_them()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);
            var secondHalfStarted = false;

            foreach (var matchEvent in match.Result.Events)
            {
                if (matchEvent.Type == EngineEventType.SecondHalfStart)
                {
                    secondHalfStarted = true;
                }

                if (!secondHalfStarted)
                {
                    matchEvent.Minute.Should().BeLessThanOrEqualTo(
                        Rules.HalfTimeMinute,
                        "the first half is 1'…45', with its stoppage written as 45+N'");
                }
                else if (matchEvent.Type != EngineEventType.SecondHalfStart)
                {
                    matchEvent.Minute.Should().BeInRange(
                        Rules.HalfTimeMinute + 1,
                        Rules.RegulationMinutes,
                        "the second half is 46'…90', with its stoppage written as 90+N'");
                }

                if (matchEvent.StoppageMinute > 0)
                {
                    matchEvent.Minute.Should().BeOneOf(
                        [Rules.HalfTimeMinute, Rules.RegulationMinutes],
                        "stoppage is only ever added to the end of a half");
                }
            }
        }
    }

    [Fact]
    public void MAT_3_Total_minutes_played_counts_only_the_stoppage_the_clock_used()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            var firstHalfEnd = match.Passages.Last(passage => passage.Period == 1).EndClockSeconds;
            var secondHalf = match.Passages.Where(passage => passage.Period == 2).ToList();

            // What the clock really ran: the whole first half, and the second half from 45:00.
            var played = firstHalfEnd + (secondHalf[^1].EndClockSeconds - secondHalf[0].StartClockSeconds);
            var reported = match.Result.TotalMinutesPlayed * Rules.SecondsPerMinute;

            // The minutes are whole and the last possession of each half overshoots its end, so the clock is a
            // little ahead of the report — never behind it by a first half's stoppage, which is what counting the
            // first half's added time twice used to do.
            (played - reported).Should().BeInRange(
                0,
                170,
                "the reported minutes are the minutes the clock ran, not regulation plus stoppage counted twice");
        }
    }

    [Fact]
    public void MAT_3_Substitutions_are_made_at_the_planners_windows_in_both_halves()
    {
        var windows = Rules.SubstitutionWindows;
        var sawSecondHalfChange = false;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            foreach (var substitution in match.Result.Events.Where(matchEvent => matchEvent.Type == EngineEventType.Substitution))
            {
                substitution.Minute.Should().BeInRange(1, Rules.RegulationMinutes);

                if (substitution.SubstitutionReason == MatchSubstitutionReason.Fatigue)
                {
                    // A possession can straddle the end of a window's minute, so the change lands in it or the next.
                    // The first window is the 46th minute: it is the second half's, and it is not skipped or delayed
                    // by the first half's stoppage any more.
                    windows.Any(window => substitution.Minute == window || substitution.Minute == window + 1)
                        .Should().BeTrue($"a fatigue change is made at a window, was minute {substitution.Minute}");
                }

                sawSecondHalfChange |= substitution.Minute > Rules.HalfTimeMinute;
            }
        }

        sawSecondHalfChange.Should().BeTrue("the engine makes changes in the second half");
    }

    [Fact]
    public void MAT_3_The_live_metrics_cover_every_minute_of_both_halves()
    {
        var input = TestMatchFactory.Even();
        var metrics = new PlayerLiveMetricsRecorder();

        MatchSimulator.Simulate(input, Rules, liveMetrics: metrics);

        var minutes = metrics.Metrics.Select(metric => metric.Minute).Distinct().Order().ToList();

        // 1…90 with nothing missing: the second half starts at 46', so there is no gap where the first half's
        // stoppage used to be.
        minutes.Should().Equal(Enumerable.Range(minutes[0], minutes[^1] - minutes[0] + 1));
        minutes[^1].Should().Be(Rules.RegulationMinutes);
        minutes.Should().Contain(Rules.HalfTimeMinute + 1);
    }
}
