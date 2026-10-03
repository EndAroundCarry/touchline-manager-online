using FluentAssertions;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The replay director of `replay-v4`: the constant-pace film's schedule, its one pace, its passages and their
/// match clock, boundary continuity and cuts, the reel over the same data, and the synchronized commentary.
/// </summary>
public sealed class ReplayDirectorTests
{
    private const int Seeds = 24;

    private static readonly HighlightOptionsV1 Defaults = new();

    [Fact]
    public void The_film_is_one_contiguous_schedule_with_one_segment_per_passage()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            presentation.PresentationVersion.Should().Be("replay-v4");
            presentation.Passages.Should().NotBeEmpty();
            presentation.Playback.Should().HaveCount(presentation.Passages.Count);
            presentation.Playback[0].StartMilliseconds.Should().Be(0);

            var cursor = 0;

            for (var index = 0; index < presentation.Playback.Count; index++)
            {
                var segment = presentation.Playback[index];

                segment.Kind.Should().Be("passage");
                segment.StartMilliseconds.Should().Be(cursor, "segments run back to back");
                segment.DurationMilliseconds.Should().Be(presentation.Passages[index].DurationMilliseconds);
                segment.SourceEventSequence.Should().Be(presentation.Passages[index].SourceEventSequence);

                cursor += segment.DurationMilliseconds;
            }

            cursor.Should().Be(presentation.TotalPlaybackMilliseconds);
        }
    }

    [Fact]
    public void The_film_runs_between_nine_and_eleven_minutes_and_never_longer()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            // Eleven minutes is a hard ceiling that includes the half-time card.
            presentation.TotalPlaybackMilliseconds.Should().BeLessThanOrEqualTo(11 * 60 * 1000);
            presentation.TotalPlaybackMilliseconds.Should().BeGreaterThanOrEqualTo(9 * 60 * 1000);
            presentation.Passages.Count.Should().BeInRange(20, Defaults.MaxPassages);
        }
    }

    [Fact]
    public void The_median_film_is_about_ten_minutes()
    {
        // The calibration: a match is about 101 clock minutes with its stoppage, ten to one makes that about ten
        // minutes, and the quiet play is condensed so the pace that takes stays near the top of its band.
        var lengths = new List<int>();
        var paces = new List<int>();

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            lengths.Add(presentation.TotalPlaybackMilliseconds);
            paces.Add(presentation.PaceMilli);
        }

        lengths.Sort();
        paces.Sort();

        lengths[lengths.Count / 2].Should().BeInRange(9 * 60_000 + 40_000, 10 * 60_000 + 30_000, "the median match is about ten minutes");
        paces[paces.Count / 2].Should().BeInRange(Defaults.MinPaceMilli, Defaults.MaxPaceMilli, "and is played inside the pace band");
    }

    [Fact]
    public void A_busy_match_raises_the_pace_before_it_ever_lengthens_the_film()
    {
        // The ceiling is enforced whatever the match: a film that is given six minutes is played faster, not longer.
        var options = new HighlightOptionsV1 { MinFilmMilliseconds = 6 * 60 * 1000, MaxFilmMilliseconds = 6 * 60 * 1000 };

        for (var seed = 1UL; seed <= 6; seed++)
        {
            var (_, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed), options);

            build.Presentation.TotalPlaybackMilliseconds.Should().BeLessThanOrEqualTo(6 * 60 * 1000);
            build.Presentation.PaceMilli.Should().BeGreaterThan(Defaults.MaxPaceMilli, "a shorter film is a faster one");
            build.Diagnostics!.Teleports.Should().Be(0, "the pace rises without anything jumping");
        }
    }

    [Fact]
    public void The_whole_film_is_played_at_one_pace_inside_its_band()
    {
        var inBand = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed));
            var pace = build.Presentation.PaceMilli;

            pace.Should().BeGreaterThan(0);
            build.Diagnostics!.Pace.Should().BeApproximately(pace / 1000.0, 0.001);

            if (pace >= Defaults.MinPaceMilli - 5 && pace <= Defaults.MaxPaceMilli + 5)
            {
                inBand++;
            }
        }

        inBand.Should().BeGreaterThanOrEqualTo((int)(Seeds * 0.9), "the pace sits in its band for nearly every match");
    }

    [Fact]
    public void The_ball_never_moves_faster_than_a_strike_would_outside_a_cut()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var limit = Defaults.ShotMetresPerSecond * (presentation.PaceMilli / 1000.0) * 1.1;

            foreach (var passage in presentation.Passages)
            {
                var ball = passage.Tracks.Single(track => track.EntityId == "ball").Keyframes;

                for (var index = 1; index < ball.Count; index++)
                {
                    var seconds = (ball[index].TimeMilliseconds - ball[index - 1].TimeMilliseconds) / 1000.0;

                    if (seconds <= 0)
                    {
                        continue;
                    }

                    SpeedBetween(ball[index - 1], ball[index], seconds).Should().BeLessThanOrEqualTo(
                        limit,
                        "the ball moves at the speed of a strike at its fastest, times the pace; only a cut jumps");
                }
            }
        }
    }

    [Fact]
    public void Players_never_move_faster_than_their_caps_times_the_pace()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var pace = presentation.PaceMilli / 1000.0;

            foreach (var passage in presentation.Passages)
            {
                var keepers = passage.Entities
                    .Where(entity => entity.Family == MatchPositionFamily.Goalkeeper)
                    .Select(entity => entity.EntityId)
                    .ToHashSet();

                foreach (var track in passage.Tracks.Where(track => track.EntityId != "ball"))
                {
                    var cap = (keepers.Contains(track.EntityId) ? Defaults.DiveMetresPerSecond : Defaults.SprintMetresPerSecond) * pace * 1.1;

                    for (var index = 1; index < track.Keyframes.Count; index++)
                    {
                        var seconds = (track.Keyframes[index].TimeMilliseconds - track.Keyframes[index - 1].TimeMilliseconds) / 1000.0;

                        if (seconds > 0)
                        {
                            SpeedBetween(track.Keyframes[index - 1], track.Keyframes[index], seconds).Should().BeLessThanOrEqualTo(cap);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Each_half_runs_on_its_own_clock_and_the_second_starts_at_forty_five_minutes()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var passages = presentation.Passages;

            passages.Select(passage => passage.Period).Should().BeInAscendingOrder().And.OnlyContain(period => period == 1 || period == 2);
            passages.Where(passage => passage.OutcomeCode == "half_time").Should().ContainSingle("the half-time card is one hold");

            var secondHalf = passages.First(passage => passage.Period == 2);

            secondHalf.StartMatchSecond.Should().Be(45 * 60, "the second half's clock restarts at forty-five minutes");
            secondHalf.Cuts.Should().ContainSingle(cut => cut.Kind == "half_time" && cut.TimeMilliseconds == 0);

            for (var index = 1; index < passages.Count; index++)
            {
                var previous = passages[index - 1];
                var next = passages[index];

                if (previous.Period == next.Period)
                {
                    next.StartMatchSecond.Should().Be(previous.EndMatchSecond, "a passage picks the clock up where the last left it");
                }
            }
        }
    }

    [Fact]
    public void The_match_clock_is_monotonic_and_reads_the_minute_each_event_was_stamped_with()
    {
        var rules = EngineRulesV2.Default;
        var checkedEvents = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var passage in presentation.Passages)
            {
                passage.Clock.Should().NotBeEmpty();
                passage.Clock[0].TimeMilliseconds.Should().Be(0);
                passage.Clock[^1].TimeMilliseconds.Should().Be(passage.DurationMilliseconds);
                passage.Clock.Select(point => point.TimeMilliseconds).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
                passage.Clock.Select(point => point.MatchSecond).Should().BeInAscendingOrder("the clock only ever goes forward within a half");
                passage.StartMatchSecond.Should().Be(passage.Clock[0].MatchSecond);
                passage.EndMatchSecond.Should().Be(passage.Clock[^1].MatchSecond);
            }

            foreach (var matchEvent in result.Events.Where(candidate => TestMatchFactory.OutcomeTemplate(candidate.Type) is not null))
            {
                var moment = TestMatchFactory.MomentOf(presentation, matchEvent, result.Events);

                moment.Should().NotBeNull($"event {matchEvent.Sequence} is in the film");

                var index = presentation.Playback.ToList().FindLastIndex(segment => segment.StartMilliseconds <= moment!.Value);
                var passage = presentation.Passages[index];
                var second = TestMatchFactory.ClockAt(passage, moment!.Value - presentation.Playback[index].StartMilliseconds);
                var (minute, stoppage) = FilmLabels.ClockOf(second, passage.Period, rules);

                (minute, stoppage).Should().Be(
                    (matchEvent.Minute, matchEvent.StoppageMinute),
                    $"the scoreboard reads the minute event {matchEvent.Sequence} was stamped with when it is shown");

                checkedEvents++;
            }
        }

        checkedEvents.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Every_goal_is_inside_a_passage()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var shown = presentation.Passages.SelectMany(passage => passage.EventSequences).ToHashSet();

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                shown.Should().Contain(goal.Sequence, "every goal is part of the film");
            }
        }
    }

    [Fact]
    public void Boundary_frames_join_consecutive_passages_except_at_a_cut()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            for (var index = 1; index < presentation.Passages.Count; index++)
            {
                var previous = presentation.Passages[index - 1];
                var next = presentation.Passages[index];

                if (next.Cuts.Count > 0)
                {
                    continue;
                }

                foreach (var track in next.Tracks)
                {
                    var before = previous.Tracks.FirstOrDefault(candidate => candidate.EntityId == track.EntityId);

                    if (before is null)
                    {
                        continue;
                    }

                    track.Keyframes[0].X.Should().Be(before.Keyframes[^1].X);
                    track.Keyframes[0].Y.Should().Be(before.Keyframes[^1].Y);
                    track.Keyframes[0].Z.Should().Be(before.Keyframes[^1].Z);
                }
            }
        }
    }

    [Fact]
    public void The_only_cuts_are_the_kick_offs_a_player_could_not_run_to_and_the_second_half()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var cuts = presentation.Passages.SelectMany(passage => passage.Cuts).ToList();
            var goals = result.Events.Count(matchEvent => matchEvent.IsGoal);

            cuts.Should().OnlyContain(cut => cut.Kind == "kick_off" || cut.Kind == "half_time");
            cuts.Should().OnlyContain(cut => cut.TimeMilliseconds == 0 && cut.DurationMilliseconds == Defaults.CutMilliseconds);
            cuts.Count(cut => cut.Kind == "half_time").Should().Be(1);
            cuts.Count(cut => cut.Kind == "kick_off").Should().BeLessThanOrEqualTo(goals, "only a kick-off after a goal is a cut");
        }
    }

    [Fact]
    public void Every_keyframe_is_on_the_pitch_and_inside_its_passage()
    {
        for (var seed = 1UL; seed <= 12; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var passage in presentation.Passages)
            {
                foreach (var track in passage.Tracks)
                {
                    track.Keyframes.Should().NotBeEmpty();
                    track.Keyframes[0].TimeMilliseconds.Should().Be(0);
                    track.Keyframes[^1].TimeMilliseconds.Should().Be(passage.DurationMilliseconds);

                    var previous = -1;

                    foreach (var keyframe in track.Keyframes)
                    {
                        keyframe.X.Should().BeInRange(0, 10_000);
                        keyframe.Y.Should().BeInRange(0, 10_000);
                        keyframe.Z.Should().BeInRange(0, 100);
                        keyframe.TimeMilliseconds.Should().BeInRange(0, passage.DurationMilliseconds);
                        keyframe.TimeMilliseconds.Should().BeGreaterThan(previous, "keyframes run strictly forward");
                        previous = keyframe.TimeMilliseconds;
                    }
                }
            }
        }
    }

    [Fact]
    public void Every_passage_carries_its_players_and_the_ball_each_with_a_track()
    {
        var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even());

        foreach (var passage in presentation.Passages)
        {
            passage.Entities.Count(entity => entity.IsBall).Should().Be(1);
            passage.Entities.Count(entity => !entity.IsBall).Should().BeInRange(18, 22);
            passage.Tracks.Should().HaveCount(passage.Entities.Count);
            passage.Tracks.Select(track => track.EntityId)
                .Should().BeEquivalentTo(passage.Entities.Select(entity => entity.EntityId));
            passage.Entities.Select(entity => entity.EntityId).Should().OnlyHaveUniqueItems();

            passage.Entities.Select(entity => entity.EntityId)
                .Should().BeInAscendingOrder(StringComparer.Ordinal);
            passage.Tracks.Select(track => track.EntityId)
                .Should().BeInAscendingOrder(StringComparer.Ordinal);

            foreach (var entity in passage.Entities.Where(entity => !entity.IsBall))
            {
                entity.Side.Should().NotBeNull();
                entity.ParticipantId.Should().NotBeNull();
                entity.ShirtNumber.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void A_involved_player_carries_the_action_they_performed()
    {
        for (var seed = 1UL; seed <= 12; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));
            var actions = presentation.Passages
                .SelectMany(passage => passage.Tracks)
                .SelectMany(track => track.Keyframes)
                .Select(keyframe => keyframe.Action)
                .Where(action => action is not null)
                .ToHashSet();

            actions.Should().Contain(["pass", "carry", "receive", "shot"], "the renderer needs actions to draw");
        }
    }

    [Fact]
    public void Passage_commentary_is_synchronized_safe_and_not_a_wall_of_text()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "absenceFixtures",
            "club",
            "clubId",
            "opponent",
            "playerId",
            "second",
            "secondId",
            "shotZone",
        };

        string[] buildUp = ["match.build.pass", "match.build.carry", "match.build.interception"];

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var passage in presentation.Passages)
            {
                var previous = -1;

                foreach (var line in passage.Commentary)
                {
                    line.TimeMilliseconds.Should().BeInRange(0, passage.DurationMilliseconds);
                    line.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(previous);
                    previous = line.TimeMilliseconds;

                    line.TemplateKey.Should().StartWith("match.");
                    line.Text.Should().NotBeEmpty().And.NotContain("{");

                    foreach (var parameter in line.Parameters)
                    {
                        allowed.Should().Contain(parameter.Name, "MAT-11 forbids anything else reaching a manager");
                    }
                }

                // Events always get their line; a passing line is thinned to about one in two and a half seconds.
                foreach (var line in passage.Commentary.Where(candidate => buildUp.Contains(candidate.TemplateKey)))
                {
                    passage.Commentary
                        .Where(other => !ReferenceEquals(other, line))
                        .Should().NotContain(other => Math.Abs(other.TimeMilliseconds - line.TimeMilliseconds) < Defaults.CommentaryGapMilliseconds);
                }
            }
        }
    }

    [Fact]
    public void A_goals_commentary_names_the_scorer_and_the_feed_carries_build_up()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var (result, presentation) = TestMatchFactory.Play(input);

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                var passage = presentation.Passages.Single(candidate => candidate.EventSequences.Contains(goal.Sequence));
                var scorer = input.SideOf(goal.Side).Squad
                    .Single(participant => participant.ParticipantId == goal.ParticipantId);
                var key = goal.Type == EngineEventType.Goal ? "match.goal" : "match.penalty.goal";

                // A film passage can hold two possessions, so back-to-back goals can share one: the scorer's
                // own line has to be among its goal lines rather than the only one.
                var line = passage.Commentary.SingleOrDefault(candidate =>
                    candidate.TemplateKey == key && candidate.Text.Contains(scorer.DisplayName, StringComparison.Ordinal));

                line.Should().NotBeNull("a goal is narrated with the log's own wording, naming the scorer");
                line!.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(0);
            }

            presentation.Passages
                .SelectMany(passage => passage.Commentary)
                .Should().Contain(
                    line => line.TemplateKey.StartsWith("match.build.", StringComparison.Ordinal),
                    "the feed narrates the build-up, not just the outcome");
        }
    }

    [Fact]
    public void A_line_about_a_strike_is_read_when_it_is_struck_and_the_outcome_follows()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            for (var index = 0; index < presentation.Passages.Count; index++)
            {
                var passage = presentation.Passages[index];
                var next = index + 1 < presentation.Passages.Count ? presentation.Passages[index + 1] : null;

                // A passage can hold several strikes, so each outcome is paired with the strike line read just before it.
                HighlightCommentaryV1? chance = null;

                foreach (var line in passage.Commentary.OrderBy(candidate => candidate.TimeMilliseconds))
                {
                    if (line.TemplateKey == "match.build.chance")
                    {
                        chance = line;
                    }
                    else if (chance is not null
                        && line.TemplateKey is "match.goal" or "match.shot.saved" or "match.shot.off_target" or "match.shot.blocked" or "match.shot.woodwork")
                    {
                        if (line.TimeMilliseconds - chance.TimeMilliseconds < Defaults.CommentaryOutcomeDelayMilliseconds - 1)
                        {
                            // Only a forced break — the end of the film, half-time, a cut or a change of players — ends a
                            // passage before the line has had its 0.6 s, and the line is then read as the passage ends.
                            line.TimeMilliseconds.Should().Be(passage.DurationMilliseconds, "the outcome line follows the strike by 0.6 s, or is read as the passage ends");
                            (next is null
                                || next.Period != passage.Period
                                || next.OutcomeCode == "half_time"
                                || next.Cuts.Count > 0
                                || !next.Entities.Select(entity => entity.ParticipantId).SequenceEqual(passage.Entities.Select(entity => entity.ParticipantId)))
                                .Should().BeTrue("only a forced break ends a passage within 0.6 s of a strike");
                        }

                        chance = null;
                    }
                }
            }
        }
    }

    [Fact]
    public void The_presentation_is_deterministic()
    {
        var input = TestMatchFactory.Even();

        var first = TestMatchFactory.Play(input).Presentation;
        var second = TestMatchFactory.Play(input).Presentation;

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void The_payload_stays_inside_its_budget()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            presentation.LiveMetrics.Should().NotBeNull().And.NotBeEmpty();
            presentation.EstimatedPayloadBytes.Should().BeLessThanOrEqualTo(750 * 1024);
        }
    }

    /// <summary>The speed between two keyframes, in metres per second of film.</summary>
    private static double SpeedBetween(HighlightKeyframeV1 from, HighlightKeyframeV1 to, double seconds)
    {
        var dx = (to.X - from.X) * 105.0 / 10_000;
        var dy = (to.Y - from.Y) * 68.0 / 10_000;

        return Math.Sqrt((dx * dx) + (dy * dy)) / seconds;
    }
}
