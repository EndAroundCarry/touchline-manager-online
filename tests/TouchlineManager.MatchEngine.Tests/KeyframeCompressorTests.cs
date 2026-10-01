using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Delta compression for semantic keyframes (master plan Stage 3, §9.3, ADR-0006).
/// </summary>
/// <remarks>
/// The compressor is what makes the plan's "emit keyframes only on changes in velocity or direction" true.
/// These tests pin the two halves of that promise: a track with no change loses its redundant frames, and a
/// track with a change keeps enough of them to draw it — including the semantic action the renderer styles.
/// </remarks>
public sealed class KeyframeCompressorTests
{
    [Fact]
    public void The_sampler_authors_enough_frames_to_see_a_bend()
    {
        var sampled = KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 0, 0),
            new HighlightKeyframeV1(10_000, 5_000, 0),
        ]);

        sampled.Should().HaveCountGreaterThan(10, "a ten-second run holds twenty-five samples at 400 ms");
        sampled[0].TimeMilliseconds.Should().Be(0);
        sampled[^1].TimeMilliseconds.Should().Be(10_000);
        sampled.Select(keyframe => keyframe.TimeMilliseconds).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_straight_run_is_reduced_to_its_endpoints()
    {
        var dense = KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 0, 0),
            new HighlightKeyframeV1(10_000, 5_000, 2_500),
        ]);

        var compressed = KeyframeCompressor.Compress(dense);

        compressed.Should().HaveCount(2, "a straight line needs no interior keyframe");
        compressed[0].X.Should().Be(0);
        compressed[^1].X.Should().Be(5_000);
        compressed[^1].Y.Should().Be(2_500);
    }

    [Fact]
    public void A_bend_survives_with_enough_keyframes_to_draw_it()
    {
        var compressed = KeyframeCompressor.Compress(KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 0, 0, 0),
            new HighlightKeyframeV1(5_000, 5_000, 0, 30),
            new HighlightKeyframeV1(10_000, 10_000, 0, 0),
        ]));

        compressed.Count.Should().BeGreaterThan(2, "a flight is not a ground pass");
        compressed[0].X.Should().Be(0);
        compressed[^1].X.Should().Be(10_000);
    }

    [Fact]
    public void A_semantic_action_is_never_dropped_or_moved()
    {
        var compressed = KeyframeCompressor.Compress(KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 0, 0),
            new HighlightKeyframeV1(4_000, 2_000, 0, Action: "run"),
            new HighlightKeyframeV1(8_000, 4_000, 0),
        ]));

        var action = compressed.Single(keyframe => keyframe.Action is not null);

        action.Action.Should().Be("run");
        action.TimeMilliseconds.Should().Be(4_000, "the strike happens when it was authored to");
        action.X.Should().Be(2_000);
    }

    [Fact]
    public void A_stationary_track_costs_two_keyframes()
    {
        var compressed = KeyframeCompressor.Compress(KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 3_000, 4_000),
            new HighlightKeyframeV1(20_000, 3_000, 4_000),
        ]));

        compressed.Should().HaveCount(2);
        compressed.Should().OnlyContain(keyframe => keyframe.Speed == 0);
    }

    [Fact]
    public void The_speed_is_derived_from_the_movement_that_was_kept()
    {
        var compressed = KeyframeCompressor.Compress(
        [
            new HighlightKeyframeV1(0, 0, 0),
            new HighlightKeyframeV1(1_000, 1_000, 0),
            new HighlightKeyframeV1(2_000, 2_000, 0),
        ]);

        compressed.Should().HaveCount(2);
        compressed[0].Speed.Should().Be(1_000, "a thousand units of the pitch in one second");
        compressed[^1].Speed.Should().Be(0, "the track ends where the movement stops");
    }

    [Fact]
    public void A_same_time_pair_collapses_to_the_state_the_moment_ends_in()
    {
        var sampled = KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 100, 100),
            new HighlightKeyframeV1(0, 200, 200, Action: "shot"),
        ]);

        sampled.Should().HaveCount(1);
        sampled[0].X.Should().Be(200);
        sampled[0].Action.Should().Be("shot");
    }

    [Fact]
    public void Compression_is_idempotent_and_deterministic()
    {
        // A track that has already been compressed has nothing left to shed, and the result is the same on
        // every run: the presentation is part of what a match replays as.
        var track = KeyframeCompressor.Sample(
        [
            new HighlightKeyframeV1(0, 0, 0),
            new HighlightKeyframeV1(6_000, 4_000, 0, 25),
            new HighlightKeyframeV1(12_000, 2_000, 0, Action: "save"),
            new HighlightKeyframeV1(18_000, 2_000, 0),
        ]);

        var once = KeyframeCompressor.Compress(track);
        var twice = KeyframeCompressor.Compress(once);

        twice.Should().BeEquivalentTo(once, options => options.WithStrictOrdering());
        KeyframeCompressor.Compress(track).Should().BeEquivalentTo(once, options => options.WithStrictOrdering());
    }
}
