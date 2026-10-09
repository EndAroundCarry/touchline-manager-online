using FluentAssertions;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the integer square root the tick engine's geometry rests on: the floor of the root, for the whole
/// range the fixed-point pitch can produce (`tick-engine-v1`, Milestone 9).
/// </summary>
public sealed class SpatialMathTests
{
    [Fact]
    public void The_square_root_is_the_floor_of_the_true_root()
    {
        for (var value = 0L; value <= 200_000; value++)
        {
            var root = SpatialMath.Sqrt(value);

            (root * root).Should().BeLessThanOrEqualTo(value, $"sqrt({value}) is a lower bound");
            ((root + 1) * (root + 1)).Should().BeGreaterThan(value, $"sqrt({value}) is the greatest lower bound");
        }
    }

    [Fact]
    public void The_square_root_of_the_widest_pitch_distance_is_the_floor_of_its_root()
    {
        // Two opposite corners of the fixed-point pitch: wider than any distance the simulation takes.
        var corner = ((long)TickSpatialUnits.PitchLengthFixed * TickSpatialUnits.PitchLengthFixed)
            + ((long)TickSpatialUnits.PitchWidthFixed * TickSpatialUnits.PitchWidthFixed);

        var root = SpatialMath.Sqrt(corner);

        (root * root).Should().BeLessThanOrEqualTo(corner);
        ((root + 1) * (root + 1)).Should().BeGreaterThan(corner);
    }

    [Fact]
    public void The_square_root_is_the_floor_across_the_whole_range()
    {
        // The seed tables only apply from 128 upwards, and the value the tick loop takes roots of never
        // exceeds the widest pitch distance, but the function is one function: every bit length, the numbers
        // around each power of two, and a spread of large values are all checked.
        var probes = new List<long>();

        for (var value = 0L; value <= 1_000_000; value++)
        {
            probes.Add(value);
        }

        for (var bit = 0; bit < 62; bit++)
        {
            var power = 1L << bit;
            probes.Add(power - 2);
            probes.Add(power - 1);
            probes.Add(power);
            probes.Add(power + 1);
            probes.Add(power + 2);
        }

        // A deterministic sweep across the widest values the fixed-point pitch can produce, whose squares are
        // the largest inputs the simulation ever takes a root of.
        for (var seed = 0L; seed < 10_000; seed++)
        {
            probes.Add((seed * 2654435761L) % ((long)TickSpatialUnits.PitchLengthFixed * TickSpatialUnits.PitchLengthFixed + 1));
        }

        foreach (var value in probes.Where(value => value >= 0).Distinct())
        {
            var root = SpatialMath.Sqrt(value);

            (root * root).Should().BeLessThanOrEqualTo(value, $"sqrt({value}) is a lower bound");
            ((root + 1) * (root + 1)).Should().BeGreaterThan(value, $"sqrt({value}) is the greatest lower bound");
        }
    }
}
