using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's front door: the same frozen snapshot plays the same match and the same film
/// byte for byte, the score and the statistics reconcile, and deriving the film changes no result
/// (`tick-engine-v1`, Milestone 9; `MAT-5`, `MAT-8`).
/// </summary>
public sealed class TickEngineDeterminismTests
{
    private const ulong Seed = 20_260_925;

    private static MatchInputV1 TickInput(ulong seed = Seed) =>
        TestMatchFactory.ForTickEngine(TestMatchFactory.Even(seed));

    [Fact]
    public void The_same_tick_snapshot_produces_the_same_output_hash_twice()
    {
        var input = TickInput();

        var first = MatchSimulator.Simulate(input);
        var second = MatchSimulator.Simulate(input);

        second.OutputHash.Should().Be(first.OutputHash);
        second.InputHash.Should().Be(first.InputHash);
    }

    [Fact]
    public void The_same_tick_snapshot_produces_byte_identical_canonical_output()
    {
        var input = TickInput();

        var first = MatchSimulator.Simulate(input);
        var second = MatchSimulator.Simulate(input);

        CanonicalMatchSerializer.CanonicalText(second)
            .Should().Be(CanonicalMatchSerializer.CanonicalText(first), "the canonical output is byte-identical between runs");
        CanonicalMatchSerializer.CanonicalText(second).Should().Contain("engineVersion=engine-v12");
    }

    [Fact]
    public void A_different_seed_plays_a_different_tick_match()
    {
        var first = MatchSimulator.Simulate(TickInput(1));
        var second = MatchSimulator.Simulate(TickInput(2));

        second.OutputHash.Should().NotBe(first.OutputHash);
    }

    [Fact]
    public void A_tick_match_keeps_the_score_and_the_statistics_reconciled()
    {
        var result = MatchSimulator.Simulate(TickInput());

        var goals = result.Events.Count(matchEvent => matchEvent.IsGoal);
        var attributed = result.Events.Count(matchEvent => matchEvent.IsGoal && matchEvent.ParticipantId is not null);

        (result.HomeGoals + result.AwayGoals).Should().Be(goals, "MAT-5: the score is the goal events");
        result.Home.Shots.Should().Be(
            result.Home.ShotsOnTarget + result.Home.ShotsOffTarget + result.Home.ShotsBlocked + result.Home.WoodworkHits,
            "every shot is one of the recorded outcomes");
        result.Away.Shots.Should().Be(
            result.Away.ShotsOnTarget + result.Away.ShotsOffTarget + result.Away.ShotsBlocked + result.Away.WoodworkHits);
        result.PlayerLines.Sum(line => line.Goals).Should().Be(attributed, "a player's line counts the goals the events credited him");
        result.PlayerLines.Should().OnlyContain(line => line.PassesCompleted <= line.PassesAttempted);
    }

    [Fact]
    public void Deriving_the_film_changes_no_result()
    {
        var input = TickInput();

        var plain = MatchSimulator.Simulate(input);
        var film = MatchSimulator.SimulateFilm(input);

        film.Result.OutputHash.Should().Be(plain.OutputHash, "recording the trace is a by-product that consumes no draw");
        film.Presentation.Passages.Should().NotBeEmpty("a whole match was recorded");
        film.Presentation.Playback.Should().HaveCount(film.Presentation.Passages.Count, "one playback segment per passage");
        film.Presentation.Playback.Should().OnlyContain(segment => segment.Kind == "passage");
    }

    [Fact]
    public void The_same_tick_snapshot_produces_the_same_film_twice()
    {
        var input = TickInput();

        var first = MatchSimulator.SimulateFilm(input);
        var second = MatchSimulator.SimulateFilm(input);

        second.Presentation.Should().BeEquivalentTo(first.Presentation, options => options.WithStrictOrdering());
    }

    [Fact]
    public void The_film_keeps_the_playback_schedule_contiguous()
    {
        var film = MatchSimulator.SimulateFilm(TickInput());
        var elapsed = 0;

        foreach (var segment in film.Presentation.Playback)
        {
            segment.StartMilliseconds.Should().Be(elapsed, "each segment starts where the one before it ended");
            segment.DurationMilliseconds.Should().BePositive();
            elapsed += segment.DurationMilliseconds;
        }

        film.Presentation.TotalPlaybackMilliseconds.Should().Be(elapsed);
    }

    [Fact]
    public void Every_goal_is_on_the_reel_under_a_clip_that_covers_its_passage()
    {
        var film = MatchSimulator.SimulateFilm(TickInput());
        var goals = film.Result.Events.Where(matchEvent => matchEvent.IsGoal).ToArray();

        goals.Should().NotBeEmpty("the seed's match has goals to carry a reel");
        film.Presentation.Reel.Should().NotBeEmpty("§9.2: every goal is on the reel");

        foreach (var goal in goals)
        {
            var index = Enumerable.Range(0, film.Presentation.Passages.Count)
                .Single(position => film.Presentation.Passages[position].EventSequences.Contains(goal.Sequence));
            var start = film.Presentation.Playback[index].StartMilliseconds;
            var end = start + film.Presentation.Playback[index].DurationMilliseconds;

            film.Presentation.Reel
                .Should().Contain(clip => clip.StartMilliseconds <= start && clip.EndMilliseconds >= end,
                    "§9.2: the clip around a goal shows the moment it happened in");
            film.Presentation.Passages[index].OutcomeCode.Should().BeOneOf(["goal", "penalty_goal"]);
        }
    }

    [Fact]
    public void Every_passage_carries_a_sorted_track_for_every_entity_it_draws()
    {
        var film = MatchSimulator.SimulateFilm(TickInput());

        foreach (var passage in film.Presentation.Passages)
        {
            passage.Tracks.Should().HaveCount(passage.Entities.Count);
            passage.Tracks.Select(track => track.EntityId).Should()
                .BeEquivalentTo(passage.Entities.Select(entity => entity.EntityId));
            passage.Tracks.Select(track => track.EntityId).Should()
                .BeInAscendingOrder(StringComparer.Ordinal, "the client reads the tracks in identifier order");

            foreach (var track in Visited(passage))
            {
                track.Keyframes.Should().NotBeEmpty("the renderer draws a dot from its track");
                track.Keyframes.Select(keyframe => keyframe.TimeMilliseconds).Should().BeInAscendingOrder();
                track.Keyframes.Should().OnlyContain(keyframe =>
                    keyframe.X >= 0 && keyframe.X <= FilmSpace.Normalized
                    && keyframe.Y >= 0 && keyframe.Y <= FilmSpace.Normalized
                    && keyframe.Z >= 0 && keyframe.Z <= 100);
                track.Keyframes[0].TimeMilliseconds.Should().Be(0, "a track opens with its passage");
            }
        }
    }

    /// <summary>Gets the passage's tracks, for the assertions that read them one at a time.</summary>
    /// <param name="passage">The passage.</param>
    private static IReadOnlyList<HighlightTrackV1> Visited(PassageV1 passage) => passage.Tracks;
}
