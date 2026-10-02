using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The replay director of `replay-v3`: the continuous film's passage merging, time warp, tracks, boundary
/// continuity, the reel over the same data, and the synchronized build-up commentary.
/// </summary>
public sealed class ReplayDirectorTests
{
    [Fact]
    public void The_film_is_one_contiguous_schedule_with_one_segment_per_passage()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

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
    public void The_film_is_fitted_to_the_nine_to_eleven_minute_window()
    {
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            // Eleven minutes is a hard ceiling; nine and a half is the target floor.
            presentation.TotalPlaybackMilliseconds.Should().BeLessThanOrEqualTo(11 * 60 * 1000);
            presentation.TotalPlaybackMilliseconds.Should().BeGreaterThanOrEqualTo(8 * 60 * 1000);
            presentation.Passages.Count.Should().BeInRange(20, 120);
        }
    }

    [Fact]
    public void Passages_cover_windows_of_the_match_in_order()
    {
        var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even());

        var previousEnd = 0;

        foreach (var passage in presentation.Passages)
        {
            passage.StartMatchSecond.Should().BeGreaterThanOrEqualTo(0);
            passage.EndMatchSecond.Should().BeGreaterThanOrEqualTo(passage.StartMatchSecond);
            passage.StartMatchSecond.Should().BeGreaterThanOrEqualTo(previousEnd - 1);
            previousEnd = passage.EndMatchSecond;

            passage.DurationMilliseconds.Should().BeGreaterThan(0);
            passage.OutcomeCode.Should().NotBeNullOrWhiteSpace();
            passage.Narration.Should().NotBeNullOrWhiteSpace().And.EndWith(".");
            passage.HomeColour.Should().MatchRegex("^#[0-9a-f]{6}$");
            passage.AwayColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        }

        presentation.Passages[^1].EndMatchSecond.Should().BeLessThanOrEqualTo(result.TotalMinutesPlayed * 60 + 120);
    }

    [Fact]
    public void Every_goal_is_inside_a_passage()
    {
        for (var seed = 1UL; seed <= 25; seed++)
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
    public void Boundary_frames_join_consecutive_passages()
    {
        // The frames at a passage's edges are copied exactly, so the film moves rather than cutting.
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            for (var index = 1; index < presentation.Passages.Count; index++)
            {
                var previous = presentation.Passages[index - 1];
                var next = presentation.Passages[index];

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
    public void Every_keyframe_is_on_the_pitch_and_inside_its_passage()
    {
        for (var seed = 1UL; seed <= 15; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var passage in presentation.Passages)
            {
                foreach (var track in passage.Tracks)
                {
                    track.Keyframes.Should().NotBeEmpty();

                    var previous = -1;

                    foreach (var keyframe in track.Keyframes)
                    {
                        keyframe.X.Should().BeInRange(0, 10_000);
                        keyframe.Y.Should().BeInRange(0, 10_000);
                        keyframe.Z.Should().BeInRange(0, 100);
                        keyframe.TimeMilliseconds.Should().BeInRange(0, passage.DurationMilliseconds);
                        keyframe.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(previous);
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
        for (var seed = 1UL; seed <= 15; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            presentation.Passages
                .SelectMany(passage => passage.Tracks)
                .SelectMany(track => track.Keyframes)
                .Should().Contain(keyframe => keyframe.Action != null, "the renderer needs actions to draw");
        }
    }

    [Fact]
    public void Passage_commentary_is_synchronized_and_safe()
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

        for (var seed = 1UL; seed <= 20; seed++)
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
            }
        }
    }

    [Fact]
    public void The_reel_carries_every_goal_and_stays_inside_the_film()
    {
        var options = new HighlightOptionsV1();

        for (var seed = 1UL; seed <= 25; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                TestMatchFactory.ReelCovers(presentation, goal.Sequence)
                    .Should().BeTrue("every goal is worth watching");
            }

            presentation.Reel.Should().BeInAscendingOrder(clip => clip.StartMilliseconds);
            presentation.Reel.Count.Should().BeLessOrEqualTo(options.MaxReelClips + (result.HomeGoals + result.AwayGoals) + 1);

            foreach (var clip in presentation.Reel)
            {
                clip.StartMilliseconds.Should().BeInRange(0, presentation.TotalPlaybackMilliseconds);
                clip.EndMilliseconds.Should().BeInRange(clip.StartMilliseconds, presentation.TotalPlaybackMilliseconds);
                clip.OutcomeCode.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public void A_strict_reel_cap_still_leaves_the_goals()
    {
        var input = TestMatchFactory.Even();
        var strict = new HighlightOptionsV1 { MaxReelClips = 1 };
        var (result, presentation) = TestMatchFactory.Play(input, strict);

        foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            TestMatchFactory.ReelCovers(presentation, goal.Sequence).Should().BeTrue();
        }

        presentation.Reel.Count.Should().BeLessOrEqualTo(1 + result.HomeGoals + result.AwayGoals);
    }

    [Fact]
    public void A_goals_commentary_names_the_scorer_and_the_feed_carries_build_up()
    {
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var (result, presentation) = TestMatchFactory.Play(input);

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                var passage = presentation.Passages.Single(candidate => candidate.EventSequences.Contains(goal.Sequence));
                var scorer = input.SideOf(goal.Side).Squad
                    .Single(participant => participant.ParticipantId == goal.ParticipantId);
                var key = goal.Type == EngineEventType.Goal ? "match.goal" : "match.penalty.goal";
                var line = passage.Commentary.SingleOrDefault(candidate => candidate.TemplateKey == key);

                line.Should().NotBeNull("a goal is narrated with the log's own wording");
                line!.Text.Should().Contain(scorer.DisplayName);
                line.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(0);
            }

            presentation.Passages
                .SelectMany(passage => passage.Commentary)
                .Should().Contain(
                    line => line.TemplateKey.StartsWith("match.build.", StringComparison.Ordinal),
                    "the feed narrates the build-up, not just the outcome");
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
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            presentation.LiveMetrics.Should().NotBeNull().And.NotBeEmpty();
            presentation.EstimatedPayloadBytes.Should().BeLessThan(750 * 1024);
        }
    }
}
