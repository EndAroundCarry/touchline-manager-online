using FluentAssertions;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Pins the generator's sequence, which is the foundation every other determinism claim rests on.
/// </summary>
/// <remarks>
/// If this sequence changes, every golden output hash changes with it, and every historical match stops being
/// reproducible. The test exists so that failure is immediate and obvious rather than discovered as a scoreline
/// nobody can explain (ADR-0004).
/// </remarks>
public sealed class Pcg32Tests
{
    [Fact]
    public void Produces_the_pinned_sequence_for_a_known_seed()
    {
        var random = new Pcg32(42UL);

        var values = new uint[8];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = random.NextUInt32();
        }

        values.Should().Equal(PinnedSequence);
    }

    [Fact]
    public void Repeats_itself_for_the_same_seed_and_stream()
    {
        var first = new Pcg32(12_345UL);
        var second = new Pcg32(12_345UL);

        var left = new uint[64];
        var right = new uint[64];

        for (var index = 0; index < left.Length; index++)
        {
            left[index] = first.NextUInt32();
            right[index] = second.NextUInt32();
        }

        left.Should().Equal(right);
    }

    [Fact]
    public void Different_streams_produce_different_sequences_from_one_seed()
    {
        var first = new Pcg32(7UL, stream: 1UL);
        var second = new Pcg32(7UL, stream: 2UL);

        var left = first.NextUInt64();
        var right = second.NextUInt64();

        left.Should().NotBe(right, "a stream selector exists so one seed can drive independent sequences");
    }

    [Fact]
    public void Different_seeds_produce_different_sequences()
    {
        var first = new Pcg32(1UL);
        var second = new Pcg32(2UL);

        first.NextUInt64().Should().NotBe(second.NextUInt64());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(10_000)]
    public void Stays_inside_the_exclusive_bound(int bound)
    {
        var random = new Pcg32(99UL);

        for (var index = 0; index < 10_000; index++)
        {
            random.NextInt(bound).Should().BeInRange(0, bound - 1);
        }
    }

    [Fact]
    public void Stays_inside_an_inclusive_range()
    {
        var random = new Pcg32(5UL);

        for (var index = 0; index < 10_000; index++)
        {
            random.NextRange(3, 9).Should().BeInRange(3, 9);
        }
    }

    [Fact]
    public void Rolls_inside_the_probability_it_is_given()
    {
        var random = new Pcg32(11UL);

        var never = 0;
        var always = 0;

        for (var index = 0; index < 1_000; index++)
        {
            never += random.RollBasisPoints(0) ? 1 : 0;
            always += random.RollBasisPoints(10_000) ? 1 : 0;
        }

        never.Should().Be(0, "a zero probability is impossible");
        always.Should().Be(1_000, "certainty is certain");
    }

    [Fact]
    public void Clamps_a_probability_beyond_certainty_rather_than_inverting_it()
    {
        var random = new Pcg32(13UL);

        for (var index = 0; index < 1_000; index++)
        {
            random.RollBasisPoints(50_000).Should().BeTrue();
            random.RollBasisPoints(-10).Should().BeFalse();
        }
    }

    [Fact]
    public void Distributes_basis_points_evenly_around_the_middle()
    {
        // A generator that always returned the same value, or drifted low, would still pass every bound check
        // above. This is what catches that.
        var random = new Pcg32(20_260_925UL);
        var buckets = new int[10];

        const int draws = 100_000;

        for (var index = 0; index < draws; index++)
        {
            buckets[random.NextBasisPoints() / 1_000]++;
        }

        foreach (var bucket in buckets)
        {
            var share = (double)bucket / draws;

            share.Should().BeApproximately(0.1, 0.01);
        }
    }

    [Fact]
    public void Rejects_a_non_positive_bound()
    {
        var random = new Pcg32(1UL);

        var act = () => random.NextInt(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static readonly uint[] PinnedSequence =
    [
        492_690_617u,
        1_919_685_028u,
        3_561_993_920u,
        683_038_915u,
        1_183_706_632u,
        413_921_556u,
        222_559_498u,
        436_142_503u,
    ];
}
