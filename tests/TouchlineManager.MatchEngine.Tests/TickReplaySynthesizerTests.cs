using System.Text.Json;
using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>Verifies the film cut from a tick match's recording: its shape, its joins, its clock and what it carries (Milestone 8).</summary>
public sealed class TickReplaySynthesizerTests
{
    private static readonly HighlightOptionsV1 Options = new();
    private static readonly Lazy<MatchPresentationV1> Standard = new(() => Film(TickPlay.Even));

    private static MatchPresentationV1 Film(TickSample sample) =>
        ReplayDirector.Build(sample.Input, sample.Result, sample.Recorder, Options, sample.Metrics.Metrics);

    private static MatchPresentationV1 Presentation => Standard.Value;

    [Fact]
    public void The_film_is_the_length_of_a_match_film_and_its_schedule_is_one_contiguous_run()
    {
        var presentation = Presentation;

        presentation.PresentationVersion.Should().Be(TickReplaySynthesizer.Version);
        presentation.PaceMilli.Should().Be(2_000);
        presentation.TotalPlaybackMilliseconds.Should().BeInRange(Options.TickMinFilmMilliseconds - 5_000, Options.TickMaxFilmMilliseconds);
        presentation.Playback.Should().HaveCount(presentation.Passages.Count);
        presentation.Passages.Count.Should().BeLessThanOrEqualTo(Options.TickMaxPassages);

        var expected = 0;

        for (var index = 0; index < presentation.Passages.Count; index++)
        {
            presentation.Playback[index].StartMilliseconds.Should().Be(expected);
            presentation.Playback[index].DurationMilliseconds.Should().Be(presentation.Passages[index].DurationMilliseconds);
            presentation.Passages[index].DurationMilliseconds.Should().BeGreaterThan(0);
            expected += presentation.Passages[index].DurationMilliseconds;
        }
    }

    [Fact]
    public void The_film_fits_the_payload_budget()
    {
        Presentation.EstimatedPayloadBytes.Should().BeLessThanOrEqualTo(Options.TickPayloadBudgetBytes);
    }

    [Fact]
    public void Every_passage_has_the_ball_and_the_players_on_the_pitch_in_the_viewers_slots()
    {
        var expected = Enumerable.Range(1, 11).SelectMany(slot => new[] { $"H{slot}", $"A{slot}" }).Append("ball").Order(StringComparer.Ordinal).ToList();

        foreach (var passage in Presentation.Passages)
        {
            var ids = passage.Entities.Select(entity => entity.EntityId).ToList();

            ids.Should().BeInAscendingOrder(StringComparer.Ordinal);
            ids.Should().BeEquivalentTo(expected);
            passage.Entities.Where(entity => !entity.IsBall).Should().OnlyContain(entity => entity.ParticipantId != null && entity.Name != null && entity.Position != null);
            passage.Entities.Single(entity => entity.IsBall).ParticipantId.Should().BeNull();
        }
    }

    [Fact]
    public void Tracks_start_and_end_with_the_passage_and_stay_on_the_pitch_in_time_order()
    {
        foreach (var passage in Presentation.Passages.Where(passage => passage.OutcomeCode != "half_time"))
        {
            passage.Tracks.Select(track => track.EntityId).Should().BeInAscendingOrder(StringComparer.Ordinal);
            passage.Tracks.Should().HaveCount(passage.Entities.Count);

            foreach (var track in passage.Tracks)
            {
                track.Keyframes[0].TimeMilliseconds.Should().Be(0);
                track.Keyframes[^1].TimeMilliseconds.Should().Be(passage.DurationMilliseconds);
                track.Keyframes.Select(keyframe => keyframe.TimeMilliseconds).Should().BeInAscendingOrder();
                track.Keyframes.Should().OnlyContain(keyframe =>
                    keyframe.X >= 0 && keyframe.X <= 10_000 && keyframe.Y >= 0 && keyframe.Y <= 10_000 && keyframe.Z >= 0 && keyframe.Z <= 100);
            }
        }
    }

    [Fact]
    public void A_passage_starts_exactly_where_the_one_before_ended_unless_the_film_cuts()
    {
        var passages = Presentation.Passages;
        var checkedBoundaries = 0;

        for (var index = 1; index < passages.Count; index++)
        {
            var previous = passages[index - 1];
            var next = passages[index];

            if (next.Cuts.Count > 0 || previous.OutcomeCode == "half_time")
            {
                continue;
            }

            foreach (var track in next.Tracks)
            {
                var before = previous.Tracks.Single(candidate => candidate.EntityId == track.EntityId).Keyframes[^1];
                var after = track.Keyframes[0];

                // Passages that share a frame meet exactly; a change of player leaves one tick of motion between them.
                var step = Math.Sqrt(Math.Pow(after.X - before.X, 2) + Math.Pow(after.Y - before.Y, 2));

                step.Should().BeLessThanOrEqualTo(track.EntityId == "ball" ? 450 : 200, $"{track.EntityId} at passage {index}");
            }

            checkedBoundaries++;
        }

        checkedBoundaries.Should().BeGreaterThan(3);
    }

    [Fact]
    public void The_film_cuts_only_where_it_jumps_and_says_why()
    {
        var passages = Presentation.Passages;

        passages[0].Cuts.Should().BeEmpty("the film opens on the kick-off");

        var cuts = passages.SelectMany(passage => passage.Cuts).ToList();

        cuts.Should().NotBeEmpty();
        cuts.Should().OnlyContain(cut => cut.TimeMilliseconds == 0 && cut.DurationMilliseconds == Options.CutMilliseconds);
        cuts.Select(cut => cut.Kind).Distinct().Should().BeSubsetOf([TickReplaySynthesizer.JumpCut, TickReplaySynthesizer.KickOffCut, TickReplaySynthesizer.HalfTimeCut]);
        passages.Count(passage => passage.Cuts.Any(cut => cut.Kind == TickReplaySynthesizer.HalfTimeCut)).Should().Be(1);
    }

    [Fact]
    public void The_interval_is_one_card_between_the_halves()
    {
        var passages = Presentation.Passages;
        var half = passages.Select((passage, index) => (passage, index)).Single(entry => entry.passage.OutcomeCode == "half_time");

        half.passage.DurationMilliseconds.Should().Be((int)(Options.HalfTimeHoldSeconds * 1_000));
        half.passage.Tracks.Should().BeEmpty("nothing moves in the interval");
        passages.Take(half.index).Should().OnlyContain(passage => passage.Period == 1);
        passages.Skip(half.index + 1).Should().OnlyContain(passage => passage.Period == 2);
        passages[half.index + 1].Cuts.Should().BeEmpty("the interval card ends on the state the second half starts in");
    }

    [Fact]
    public void Each_half_is_on_its_own_clock_and_the_clock_runs_forward_inside_a_passage()
    {
        foreach (var passage in Presentation.Passages)
        {
            passage.Clock.Should().NotBeEmpty();
            passage.Clock[0].TimeMilliseconds.Should().Be(0);
            passage.Clock[^1].TimeMilliseconds.Should().Be(passage.DurationMilliseconds);
            passage.Clock.Select(point => point.TimeMilliseconds).Should().BeInAscendingOrder();
            passage.Clock.Select(point => point.MatchSecond).Should().BeInAscendingOrder();
            passage.StartMatchSecond.Should().Be(passage.Clock[0].MatchSecond);
            passage.EndMatchSecond.Should().Be(passage.Clock[^1].MatchSecond);

            if (passage.Period == 2 && passage.OutcomeCode != "half_time")
            {
                passage.StartMatchSecond.Should().BeGreaterThanOrEqualTo(45 * 60);
            }
        }
    }

    [Fact]
    public void Every_goal_is_in_one_passage_that_is_marked_a_goal_and_tags_the_strike()
    {
        var sample = TickPlay.Even;
        var presentation = Presentation;
        var goals = sample.Result.Events.Where(matchEvent => matchEvent.IsGoal).ToList();

        goals.Should().NotBeEmpty();

        foreach (var goal in goals)
        {
            var owners = presentation.Passages.Where(passage => passage.EventSequences.Contains(goal.Sequence)).ToList();

            owners.Should().ContainSingle($"goal {goal.Sequence} belongs to one passage");
            owners[0].OutcomeCode.Should().BeOneOf("goal", "penalty_goal");
            owners[0].SourceEventSequence.Should().Be(goal.Sequence);

            var strikes = owners[0].Tracks
                .Where(track => track.EntityId != "ball")
                .SelectMany(track => track.Keyframes)
                .Count(keyframe => keyframe.Action is "shot" or "penalty" or "free_kick");

            strikes.Should().BeGreaterThan(0, "the viewer finds the strike on the shooter's track");
            owners[0].Commentary.Should().Contain(line => line.TemplateKey == "match.goal" || line.TemplateKey == "match.penalty.goal");
        }

        goals.Should().OnlyContain(goal => TestMatchFactory.ReelCovers(presentation, goal), "a reel always carries every goal");
    }

    [Fact]
    public void A_booking_the_film_does_not_show_is_carried_to_the_next_passage_and_never_names_one()
    {
        var sample = TickPlay.Reduced;
        var presentation = Film(sample);
        var bookings = sample.Result.Events.Where(matchEvent => matchEvent.Type == EngineEventType.YellowCard).ToList();

        bookings.Should().NotBeEmpty();
        presentation.Passages.Should().NotContain(passage => passage.OutcomeCode == "yellow_card", "a foul and its booking are not filmed for themselves");

        // Each is in exactly one passage, so the viewer can put the card on the man's token from there.
        foreach (var booking in bookings)
        {
            presentation.Passages.Count(passage => passage.EventSequences.Contains(booking.Sequence)).Should().Be(1, $"booking {booking.Sequence}");
        }
    }

    [Fact]
    public void No_event_is_in_two_passages_and_every_one_named_exists()
    {
        var known = TickPlay.Even.Result.Events.Select(matchEvent => matchEvent.Sequence).ToHashSet();
        var owned = Presentation.Passages.SelectMany(passage => passage.EventSequences).ToList();

        owned.Should().OnlyHaveUniqueItems();
        owned.Should().OnlyContain(sequence => known.Contains(sequence));
    }

    [Fact]
    public void The_commentary_is_pinned_inside_its_passage_and_in_order()
    {
        var presentation = Presentation;

        presentation.Passages.Sum(passage => passage.Commentary.Count).Should().BeGreaterThan(100);

        foreach (var passage in presentation.Passages)
        {
            passage.Commentary.Select(line => line.TimeMilliseconds).Should().BeInAscendingOrder();
            passage.Commentary.All(line => line.TimeMilliseconds >= 0 && line.TimeMilliseconds <= passage.DurationMilliseconds && line.Text.Length > 0)
                .Should().BeTrue();
        }
    }

    [Fact]
    public void A_goal_reads_the_minute_the_engine_stamped_on_it_at_the_moment_it_is_scored()
    {
        var sample = TickPlay.Even;
        var presentation = Presentation;

        foreach (var goal in sample.Result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            var moment = TestMatchFactory.MomentOf(presentation, goal, sample.Result.Events);

            moment.Should().NotBeNull();

            var index = presentation.Playback.ToList().FindLastIndex(segment => segment.StartMilliseconds <= moment);
            var passage = presentation.Passages[index];
            var second = TestMatchFactory.ClockAt(passage, moment!.Value - presentation.Playback[index].StartMilliseconds);
            var (minute, stoppage) = FilmLabels.ClockOf(second, passage.Period, Configuration.EngineRulesV2.Default);

            (minute, stoppage).Should().Be((goal.Minute, goal.StoppageMinute), $"goal {goal.Sequence}");
        }
    }

    [Fact]
    public void The_lineups_and_the_curve_the_viewer_reads_are_carried()
    {
        var presentation = Presentation;

        presentation.HomeLineup.Should().NotBeNull();
        presentation.HomeLineup!.Starters.Should().HaveCount(11);
        presentation.AwayLineup!.Starters.Should().HaveCount(11);
        presentation.LiveMetrics.Should().NotBeNullOrEmpty();
        presentation.HomeGoals.Should().Be(TickPlay.Even.Result.HomeGoals);
        presentation.AwayGoals.Should().Be(TickPlay.Even.Result.AwayGoals);
    }

    [Fact]
    public void The_same_recording_makes_the_same_film()
    {
        var sample = TickPlay.Even;

        JsonSerializer.Serialize(Film(sample)).Should().Be(JsonSerializer.Serialize(Presentation));
    }

    [Fact]
    public void A_sent_off_player_is_no_longer_a_passage_entity_after_he_goes()
    {
        var sample = TickPlay.Reduced;
        var presentation = Film(sample);
        var counts = presentation.Passages.Select(passage => passage.Entities.Count(entity => !entity.IsBall)).ToList();

        counts.Should().BeInDescendingOrder("nobody comes on for a man sent off unless the bench allows it, and a side can only get smaller");
        counts[^1].Should().BeLessThan(20);
        presentation.Passages.Should().OnlyContain(passage => passage.Entities.Count(entity => !entity.IsBall) >= 2);
    }

    [Fact]
    public void A_possession_matchs_recorder_is_still_filmed_by_the_possession_director()
    {
        var input = TestMatchFactory.OnTheBoard(TestMatchFactory.Even(3));
        var recorder = new MatchPassageRecorder();
        var metrics = new PlayerLiveMetricsRecorder();
        var result = MatchSimulator.Simulate(input, TestMatchFactory.Rules, metrics, recorder);

        recorder.Tick.Should().BeNull();

        var viaRecorder = ReplayDirector.Build(input, result, recorder, liveMetrics: metrics.Metrics);
        var direct = ReplayDirector.Build(input, result, recorder.Passages, liveMetrics: metrics.Metrics);

        viaRecorder.PresentationVersion.Should().Be(ReplayDirector.Version);
        JsonSerializer.Serialize(viaRecorder).Should().Be(JsonSerializer.Serialize(direct));
    }

    [Fact]
    public void A_recording_with_no_frames_is_an_empty_film_with_its_lineups()
    {
        var sample = TickPlay.Even;
        var empty = ReplayDirector.Build(sample.Input, sample.Result, new MatchPassageRecorder(), Options);

        empty.Passages.Should().BeEmpty();
        empty.Playback.Should().BeEmpty();
        empty.HomeLineup.Should().NotBeNull();
    }
}
