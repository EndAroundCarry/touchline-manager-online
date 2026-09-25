using FluentAssertions;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Tests.World.Generation;

/// <summary>
/// The generator's PRNG contract.
/// </summary>
/// <remarks>
/// The sequence is pinned rather than merely asserted to be self-consistent. A goldfish-level test
/// ("two generators with one seed agree") would pass just as happily after an accidental change to the
/// algorithm, and every world generated under the old version would silently stop being reproducible
/// (`FIC-7`). These literals are the algorithm.
/// </remarks>
public sealed class Pcg32Tests
{
    [Fact]
    public void The_sequence_for_a_seed_is_pinned()
    {
        var generator = new Pcg32(12345UL);

        var drawn = new uint[8];

        for (var index = 0; index < drawn.Length; index++)
        {
            drawn[index] = generator.NextUInt32();
        }

        drawn.Should().Equal(
            1321476956u,
            17539747u,
            3348728241u,
            2863338820u,
            85463406u,
            1024873269u,
            4179236141u,
            1040420088u);
    }

    [Fact]
    public void The_bounded_sequence_for_a_seed_is_pinned()
    {
        var generator = new Pcg32(99UL);

        var drawn = new int[8];

        for (var index = 0; index < drawn.Length; index++)
        {
            drawn[index] = generator.NextInt(1000);
        }

        drawn.Should().Equal(532, 996, 355, 935, 400, 318, 450, 620);
    }

    [Fact]
    public void Two_generators_with_the_same_seed_and_stream_agree()
    {
        var first = new Pcg32(42UL);
        var second = new Pcg32(42UL);

        for (var index = 0; index < 100; index++)
        {
            first.NextUInt32().Should().Be(second.NextUInt32());
        }
    }

    [Fact]
    public void A_different_seed_or_stream_produces_a_different_sequence()
    {
        var baseline = new Pcg32(42UL);
        var otherSeed = new Pcg32(43UL);
        var otherStream = new Pcg32(42UL, stream: 7UL);

        var expected = baseline.NextUInt32();

        otherSeed.NextUInt32().Should().NotBe(expected);
        otherStream.NextUInt32().Should().NotBe(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(18)]
    [InlineData(220)]
    [InlineData(1000)]
    public void A_bounded_draw_stays_inside_its_bound(int bound)
    {
        var generator = new Pcg32(7UL);

        for (var index = 0; index < 5_000; index++)
        {
            generator.NextInt(bound).Should().BeInRange(0, bound - 1);
        }
    }

    [Fact]
    public void A_bounded_draw_refuses_a_non_positive_bound()
    {
        var generator = new Pcg32(1UL);

        var zero = () => generator.NextInt(0);
        var negative = () => generator.NextInt(-1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_double_draw_stays_in_the_unit_interval()
    {
        var generator = new Pcg32(11UL);

        for (var index = 0; index < 5_000; index++)
        {
            generator.NextDouble().Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(1.0);
        }
    }
}
