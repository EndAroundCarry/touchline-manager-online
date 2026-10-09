using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>A tick match played once and shared by the tests that only read it.</summary>
/// <param name="Input">The snapshot.</param>
/// <param name="Result">The result.</param>
/// <param name="Recorder">The recorder the match was played with.</param>
/// <param name="Metrics">The live metrics.</param>
internal sealed record TickSample(
    MatchInputV1 Input,
    MatchResultV1 Result,
    MatchPassageRecorder Recorder,
    PlayerLiveMetricsRecorder Metrics)
{
    /// <summary>Gets the recording of the match.</summary>
    public TickMatchRecording Recording => Recorder.Tick!;
}

/// <summary>Plays tick matches for the tests.</summary>
internal static class TickPlay
{
    private static readonly Lazy<TickSample> Standard = new(() => Play(Fixture()));
    private static readonly Lazy<TickSample> Carnage = new(() =>
    {
        var input = CarnageFixture(out var rules);

        return Play(input, rules);
    });

    /// <summary>Gets a match between two even sides, played once.</summary>
    public static TickSample Even => Standard.Value;

    /// <summary>Gets a match in which cards come out almost every foul, so that sides are reduced to a handful of players.</summary>
    public static TickSample Reduced => Carnage.Value;

    /// <summary>Builds a snapshot on the tactics board's own shape, so the film is not laid on its side.</summary>
    /// <param name="seed">The match seed.</param>
    public static MatchInputV1 Fixture(ulong seed = 20_260_925) => TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed));

    /// <summary>Builds a snapshot, and the rules it is frozen against, in which most fouls draw a red card.</summary>
    /// <param name="rules">The rules.</param>
    public static MatchInputV1 CarnageFixture(out EngineRulesV2 rules)
    {
        rules = TestMatchFactory.Rules with { StraightRedPerFoulBasisPoints = 6_500, BaseFoulBasisPoints = 6_000 };

        return Fixture(7) with
        {
            FormulaConfigurationHash = EngineConfiguration.HashOf(rules, EngineVersions.LegacyRuleSetLabel),
        };
    }

    /// <summary>Plays a snapshot on the tick loop.</summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="rules">The rules, or the defaults.</param>
    /// <param name="record">Whether to record the match for a film.</param>
    public static TickSample Play(MatchInputV1 input, EngineRulesV2? rules = null, bool record = true)
    {
        var recorder = new MatchPassageRecorder();
        var metrics = new PlayerLiveMetricsRecorder();
        var result = MatchSimulator.Simulate(input, rules ?? TestMatchFactory.Rules, metrics, record ? recorder : null, TickMatchEngine.Instance);

        return new TickSample(input, result, recorder, metrics);
    }
}

/// <summary>Verifies the loop that plays a whole tick match: what it emits, what it credits and that it is the same match twice (Milestone 8).</summary>
public sealed class TickMatchLoopTests
{
    [Fact]
    public void A_whole_match_is_played_with_both_halves_and_the_whistles()
    {
        var sample = TickPlay.Even;
        var types = sample.Result.Events.Select(matchEvent => matchEvent.Type).ToList();

        types[0].Should().Be(EngineEventType.KickOff);
        types[^1].Should().Be(EngineEventType.FullTime);
        types.Count(type => type == EngineEventType.HalfTime).Should().Be(1);
        types.Count(type => type == EngineEventType.SecondHalfStart).Should().Be(1);
        sample.Recording.FrameCount.Should().BeInRange(55_000, 70_000, "ninety minutes and stoppage at ten ticks a second");
        sample.Result.TotalMinutesPlayed.Should().BeInRange(94, 110);
    }

    [Fact]
    public void Events_are_in_sequence_and_the_clock_never_goes_back()
    {
        var events = TickPlay.Even.Result.Events;

        events.Select(matchEvent => matchEvent.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

        var previous = 0;

        foreach (var matchEvent in events)
        {
            var key = (matchEvent.Minute * 1_000) + matchEvent.StoppageMinute;

            key.Should().BeGreaterThanOrEqualTo(previous, $"event {matchEvent.Sequence} ({matchEvent.Type})");
            previous = key;
        }
    }

    [Fact]
    public void The_score_the_statistics_and_the_players_lines_agree()
    {
        var result = TickPlay.Even.Result;

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var stats = side == MatchSide.Home ? result.Home : result.Away;
            var goalEvents = result.Events.Count(matchEvent => matchEvent.Side == side && matchEvent.IsGoal);
            var lines = result.PlayerLines.Where(line => line.Side == side).ToList();

            stats.Goals.Should().Be(goalEvents);
            lines.Sum(line => line.Goals).Should().Be(goalEvents, "every goal is credited to a scorer");
            lines.Sum(line => line.Assists).Should().BeLessThanOrEqualTo(goalEvents);
            lines.Sum(line => line.PassesCompleted).Should().BeLessThanOrEqualTo(lines.Sum(line => line.PassesAttempted));
            lines.Sum(line => line.PassesAttempted).Should().BeGreaterThan(100, "a side plays a lot of passes in ninety minutes");
            stats.Shots.Should().BeGreaterThanOrEqualTo(stats.ShotsOnTarget);
        }

        result.HomeGoals.Should().Be(result.Home.Goals);
        result.AwayGoals.Should().Be(result.Away.Goals);
    }

    [Fact]
    public void Every_shot_event_names_a_shooter_a_zone_and_a_chance()
    {
        var shots = TickPlay.Even.Result.Events.Where(matchEvent => matchEvent.Type
            is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked
            or EngineEventType.ShotOffTarget or EngineEventType.Woodwork).ToList();

        shots.Should().NotBeEmpty();
        shots.Should().OnlyContain(matchEvent => matchEvent.ParticipantId != null && matchEvent.Zone != null && matchEvent.QualityBasisPoints > 0);
    }

    [Fact]
    public void The_same_snapshot_plays_the_same_match()
    {
        var first = TickPlay.Play(TickPlay.Fixture(11), record: false);
        var second = TickPlay.Play(TickPlay.Fixture(11), record: false);
        var other = TickPlay.Play(TickPlay.Fixture(12), record: false);

        second.Result.OutputHash.Should().Be(first.Result.OutputHash);
        other.Result.OutputHash.Should().NotBe(first.Result.OutputHash);
    }

    [Fact]
    public void Recording_the_match_changes_nothing_about_it()
    {
        var recorded = TickPlay.Play(TickPlay.Fixture(13), record: true);
        var bare = TickPlay.Play(TickPlay.Fixture(13), record: false);

        recorded.Result.OutputHash.Should().Be(bare.Result.OutputHash);
        recorded.Metrics.Metrics.Should().BeEquivalentTo(bare.Metrics.Metrics);
    }

    [Fact]
    public void Nobody_leaves_the_pitch()
    {
        var recording = TickPlay.Even.Recording;

        for (var frame = 0; frame < recording.FrameCount; frame += 7)
        {
            for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
            {
                var x = recording.PlayerX(frame, entity);

                if (x == TickMatchRecording.Absent)
                {
                    continue;
                }

                x.Should().BeInRange(0, 10_000);
                recording.PlayerY(frame, entity).Should().BeInRange(0, 7_000);
            }

            recording.BallX(frame).Should().BeInRange(0, 10_000);
            recording.BallY(frame).Should().BeInRange(0, 7_000);
            recording.BallZ(frame).Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public void Nothing_teleports_except_where_it_is_put_down()
    {
        var recording = TickPlay.Even.Recording;
        var playerJumps = 0;
        var ballJumps = 0;

        for (var frame = 1; frame < recording.FrameCount; frame++)
        {
            var flags = recording.Flags(frame);

            for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
            {
                if (recording.PlayerX(frame, entity) < 0 || recording.PlayerX(frame - 1, entity) < 0)
                {
                    continue;
                }

                var step = Distance(
                    recording.PlayerX(frame, entity),
                    recording.PlayerY(frame, entity),
                    recording.PlayerX(frame - 1, entity),
                    recording.PlayerY(frame - 1, entity));

                // A sprint is under a metre a tick; a goalkeeper's dive onto the ball is a few.
                if (step > 450 && !flags.HasFlag(TickFrameFlags.PlayersPlaced))
                {
                    playerJumps++;
                }
            }

            var ball = Distance(recording.BallX(frame), recording.BallY(frame), recording.BallX(frame - 1), recording.BallY(frame - 1));

            if (ball > 1_000 && !flags.HasFlag(TickFrameFlags.BallPlaced))
            {
                ballJumps++;
            }
        }

        playerJumps.Should().Be(0);
        ballJumps.Should().Be(0);
    }

    [Fact]
    public void Each_half_runs_on_its_own_clock_and_the_second_starts_at_45_00()
    {
        var recording = TickPlay.Even.Recording;

        recording.Period(0).Should().Be(1);
        recording.ClockSecond(0).Should().Be(0);
        recording.SecondHalfFrame.Should().BeGreaterThan(25_000);
        recording.Period(recording.SecondHalfFrame).Should().Be(2);
        recording.ClockSecond(recording.SecondHalfFrame).Should().Be(45 * 60);

        for (var frame = 1; frame < recording.FrameCount; frame++)
        {
            if (frame == recording.SecondHalfFrame)
            {
                continue;
            }

            recording.ClockSecond(frame).Should().BeGreaterThanOrEqualTo(recording.ClockSecond(frame - 1));
        }
    }

    [Fact]
    public void A_substitute_takes_the_slot_of_the_man_he_replaces_and_the_recording_says_when()
    {
        var sample = TickPlay.Even;
        var substitutions = sample.Result.Events.Where(matchEvent => matchEvent.Type == EngineEventType.Substitution).ToList();

        substitutions.Should().NotBeEmpty("a side tires over ninety minutes and the bench is looked at at every stoppage");

        foreach (var substitution in substitutions)
        {
            sample.Recording.Rosters.Should().Contain(
                stamp => stamp.Occupant == substitution.SecondaryParticipantId,
                $"the substitute of event {substitution.Sequence} is in the recording");
        }
    }

    [Fact]
    public void A_sent_off_player_leaves_his_slot_empty_for_the_rest_of_the_match()
    {
        var sample = TickPlay.Reduced;
        var sentOff = sample.Result.Events
            .Where(matchEvent => matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard)
            .ToList();

        sentOff.Should().NotBeEmpty();

        var last = sample.Recording.FrameCount - 1;
        var empty = sample.Recording.Rosters.Where(stamp => stamp.Occupant == Guid.Empty).ToList();

        empty.Should().NotBeEmpty();

        foreach (var stamp in empty)
        {
            sample.Recording.PlayerX(Math.Min(last, stamp.Frame), stamp.Entity).Should().Be(TickMatchRecording.Absent);
            sample.Recording.PlayerX(last, stamp.Entity).Should().Be(TickMatchRecording.Absent);
        }
    }

    [Fact]
    public void A_side_reduced_to_a_handful_of_players_still_plays_the_match_out()
    {
        var sample = TickPlay.Reduced;

        sample.Result.Events[^1].Type.Should().Be(EngineEventType.FullTime);
        (sample.Result.Home.RedCards + sample.Result.Away.RedCards).Should().BeGreaterThan(4);
        sample.Recording.FrameCount.Should().BeGreaterThan(55_000);
    }

    [Fact]
    public void Playing_a_match_allocates_next_to_nothing_per_tick()
    {
        var input = TickPlay.Fixture(14);

        // The first match builds the lazily made tables and warms the JIT.
        TickPlay.Play(input, record: false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sample = TickPlay.Play(input, record: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        sample.Result.Events.Should().NotBeEmpty();

        // The event log, the result, the planners' stoppages and the lineups allocate; sixty thousand ticks do not.
        allocated.Should().BeLessThan(6 * 1024 * 1024);
    }

    private static int Distance(int ax, int ay, int bx, int by)
    {
        var dx = (double)(ax - bx);
        var dy = (double)(ay - by);

        return (int)Math.Sqrt((dx * dx) + (dy * dy));
    }
}
