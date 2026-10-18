using System.Globalization;
using System.Text;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// H3 of the shape study (plan snoopy-toasting-hamster, Milestone 4): does a shape's extra numbers in an area become chances? It reads the zone counts the
/// kick-off grid already stored, so it plays nothing.
/// </summary>
internal static partial class TickShapeStudy
{
    /// <summary>
    /// How many neighbouring samples carry about one sample's worth of information: the ball is sampled every half second and a shot counts if it comes
    /// within ten seconds, so the samples of one passage overlap. The intervals divide the sample count by this.
    /// </summary>
    private const double SampleDependence = 20;

    /// <summary>The zones the surplus is read in: the middle and attacking thirds, in each of the three lanes.</summary>
    private static readonly (int Lane, int Third, string Name)[] SurplusZones =
    [
        (0, 1, "left lane, middle third"),
        (1, 1, "centre lane, middle third"),
        (2, 1, "right lane, middle third"),
        (0, 2, "left lane, attacking third"),
        (1, 2, "centre lane, attacking third"),
        (2, 2, "right lane, attacking third"),
    ];

    private static class H3
    {
        /// <summary>One shape's zone counts, in the shape's own frame, over every opponent and both home and away.</summary>
        private sealed record ShapeCounts(string Name, long[] Sums);

        public static string Read(string directory, ulong seed, string[] keys)
        {
            var zoneFile = Path.Combine(directory, $"zones-{seed}.tsv");

            if (!File.Exists(zoneFile))
            {
                return $"No {zoneFile}: play the grid first (tickshape <matches> {seed} grid).\n";
            }

            var rows = new Dictionary<string, long[]>();

            foreach (var line in File.ReadLines(zoneFile).Skip(1))
            {
                var fields = line.Split('\t');

                if (fields[1] == "kickoff")
                {
                    rows[fields[0]] = [.. fields.Skip(3).Select(value => long.Parse(value, CultureInfo.InvariantCulture))];
                }
            }

            var shapes = new List<ShapeCounts>();

            foreach (var key in keys)
            {
                var sums = new long[Zones.Length];

                foreach (var other in keys)
                {
                    if (rows.TryGetValue($"kick:{key}-{other}", out var home))
                    {
                        Zones.AddSide(sums, home, 0);
                    }

                    if (rows.TryGetValue($"kick:{other}-{key}", out var away))
                    {
                        Zones.AddSide(sums, away, 1);
                    }
                }

                shapes.Add(new ShapeCounts(Shapes[key].ToCode(), sums));
            }

            var text = new StringBuilder();

            text.Append(CultureInfo.InvariantCulture, $"== tickshape h3, seed {seed}: {keys.Length} shapes from kick-off, every pairing, home and away pooled; the figures are the side in possession ==\n");
            text.Append("Surplus = the side's players minus the opponent's in the ball's lane and third, the keeper and the ball carrier included, sampled every half second\n");
            text.Append(CultureInfo.InvariantCulture, $"while a side has the ball in open play. Intervals are 95% with the samples divided by {SampleDependence:F0} for their overlap.\n\n");

            AppendMeans(text, shapes);
            AppendSlopes(text, shapes, field: 1, judged: 0, "a shot within 10 s", "Does a surplus predict a shot?");
            AppendSlopes(text, shapes, field: 3, judged: 2, "the ball kept 10 s later", "Does a surplus predict keeping the ball?");
            AppendBetween(text, shapes);
            AppendFlanks(text, shapes);

            return text.ToString();
        }

        /// <summary>The mean surplus of a shape in each zone: does the shape change the numbers around the ball?</summary>
        private static void AppendMeans(StringBuilder text, List<ShapeCounts> shapes)
        {
            text.Append("1. Does the shape change the surplus? Mean surplus (± 95%) when the shape has the ball in the zone, and the share of its possession samples that fall in the zone:\n");
            text.Append("   zone".PadRight(34));

            foreach (var shape in shapes)
            {
                text.Append(shape.Name.PadLeft(16));
            }

            text.Append('\n');

            foreach (var (lane, third, name) in SurplusZones)
            {
                text.Append("   " + name.PadRight(31));

                foreach (var shape in shapes)
                {
                    var (mean, error, samples) = MeanOf(shape.Sums, lane, third);
                    var share = 100.0 * samples / TotalSamples(shape.Sums);

                    text.Append(string.Create(CultureInfo.InvariantCulture, $"{mean:+0.00;-0.00} ±{error:F2} {share:F0}%").PadLeft(16));
                }

                text.Append('\n');
            }

            text.Append('\n');
        }

        /// <summary>A figure with a sign and one decimal; one that rounds to nothing is a plain 0.0.</summary>
        private static string Signed(double value) =>
            Math.Round(value, 1) == 0 ? "0.0" : string.Create(CultureInfo.InvariantCulture, $"{value:+0.0;-0.0}");

        private static double TotalSamples(long[] sums)
        {
            var total = 0.0;

            for (var lane = 0; lane < Zones.Lanes; lane++)
            {
                for (var third = 0; third < Zones.Thirds; third++)
                {
                    for (var bucket = 0; bucket < Zones.Buckets; bucket++)
                    {
                        total += sums[Zones.SurplusIndex(0, lane, third, bucket)];
                    }
                }
            }

            return total;
        }

        private static (double Mean, double Error, double Samples) MeanOf(long[] sums, int lane, int third)
        {
            double n = 0, total = 0, squares = 0;

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                var count = sums[Zones.SurplusIndex(0, lane, third, bucket)];
                var surplus = bucket - 4;

                n += count;
                total += count * surplus;
                squares += count * surplus * surplus;
            }

            var mean = n == 0 ? 0 : total / n;
            var variance = n > 1 ? (squares / n) - (mean * mean) : 0;

            return (mean, 1.96 * Math.Sqrt(variance / (n / SampleDependence)), n);
        }

        /// <summary>
        /// Within a shape and a zone the ball's place is fixed, so a slope of the outcome on the surplus is not the box's packing. The slopes of the
        /// shapes are pooled by their weight; this is still observational (a sample with defenders around is a contested one), which the text says.
        /// </summary>
        private static void AppendSlopes(StringBuilder text, List<ShapeCounts> shapes, int field, int judged, string outcome, string title)
        {
            text.Append($"{(field == 1 ? 2 : 3)}. {title} Change in the chance of {outcome} for one more player of the side than of the opponent in the zone, in percentage points,\n");
            text.Append("   fitted within each shape and zone and pooled over the shapes by their weight (the rate at surplus 0 for scale):\n");
            text.Append("   zone".PadRight(34) + "rate at 0".PadLeft(11) + "pooled slope".PadLeft(20));

            foreach (var shape in shapes)
            {
                text.Append(shape.Name.PadLeft(10));
            }

            text.Append('\n');

            foreach (var (lane, third, name) in SurplusZones)
            {
                var perShape = shapes.Select(shape => Slope(shape.Sums, lane, third, field)).ToArray();
                var weight = perShape.Where(slope => slope.Variance > 0).Sum(slope => 1 / slope.Variance);
                var pooled = weight == 0 ? 0 : perShape.Where(slope => slope.Variance > 0).Sum(slope => slope.Value / slope.Variance) / weight;
                var error = weight == 0 ? 0 : 1.96 / Math.Sqrt(weight);
                var allSums = new long[Zones.Length];

                foreach (var shape in shapes)
                {
                    for (var index = 0; index < allSums.Length; index++)
                    {
                        allSums[index] += shape.Sums[index];
                    }
                }

                var at = Zones.SurplusIndex(0, lane, third, 4);
                var rate = allSums[at + judged] == 0 ? 0 : 100.0 * allSums[at + field] / allSums[at + judged];

                text.Append(string.Create(CultureInfo.InvariantCulture, $"   {name,-31}{rate,9:F1} %").PadRight(45) + (Signed(100 * pooled) + string.Create(CultureInfo.InvariantCulture, $" ±{100 * error:F1}")).PadLeft(20));

                foreach (var slope in perShape)
                {
                    text.Append(Signed(100 * slope.Value).PadLeft(10));
                }

                text.Append('\n');
            }

            text.Append('\n');
        }

        /// <summary>The least-squares slope of an outcome rate on the surplus within one shape's zone, and its variance (samples divided by their overlap).</summary>
        private static (double Value, double Variance) Slope(long[] sums, int lane, int third, int field)
        {
            double n = 0, sx = 0, sy = 0;
            var buckets = new List<(double Count, double Positive, double Surplus)>();

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                var at = Zones.SurplusIndex(0, lane, third, bucket);
                var count = (double)sums[at + (field == 1 ? 0 : 2)];
                var positive = (double)sums[at + field];

                if (count == 0)
                {
                    continue;
                }

                buckets.Add((count, positive, bucket - 4));
                n += count;
                sx += count * (bucket - 4);
                sy += positive;
            }

            if (n < 2)
            {
                return (0, 0);
            }

            var meanX = sx / n;
            var meanY = sy / n;
            var sxx = buckets.Sum(entry => entry.Count * (entry.Surplus - meanX) * (entry.Surplus - meanX));
            var sxy = buckets.Sum(entry => (entry.Surplus - meanX) * (entry.Positive - (entry.Count * meanY)));

            if (sxx <= 0)
            {
                return (0, 0);
            }

            var slope = sxy / sxx;
            var variance = meanY * (1 - meanY) / sxx * SampleDependence;

            return (slope, variance);
        }

        /// <summary>Between shapes: the shape that has the most players in a zone, does it shoot more from it?</summary>
        private static void AppendBetween(StringBuilder text, List<ShapeCounts> shapes)
        {
            text.Append("4. Between shapes: in each zone, the shape's mean surplus against the chance of a shot within 10 s of the ball being there (the shapes as the points):\n");

            foreach (var (lane, third, name) in SurplusZones)
            {
                var points = shapes
                    .Select(shape =>
                    {
                        double n = 0, shots = 0;

                        for (var bucket = 0; bucket < Zones.Buckets; bucket++)
                        {
                            var at = Zones.SurplusIndex(0, lane, third, bucket);

                            n += shape.Sums[at];
                            shots += shape.Sums[at + 1];
                        }

                        return (Surplus: MeanOf(shape.Sums, lane, third).Mean, Rate: n == 0 ? 0 : 100.0 * shots / n, Name: shape.Name);
                    })
                    .ToArray();
                var meanX = points.Average(point => point.Surplus);
                var meanY = points.Average(point => point.Rate);
                var sxx = points.Sum(point => (point.Surplus - meanX) * (point.Surplus - meanX));
                var syy = points.Sum(point => (point.Rate - meanY) * (point.Rate - meanY));
                var sxy = points.Sum(point => (point.Surplus - meanX) * (point.Rate - meanY));
                var correlation = sxx <= 0 || syy <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);

                text.Append("   " + name.PadRight(31) + string.Join("  ", points.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.Name} {point.Surplus:+0.00;-0.00}/{point.Rate:F1}%"))) + string.Create(CultureInfo.InvariantCulture, $"   r = {correlation:+0.00;-0.00} (5 points)\n"));
            }

            text.Append('\n');
        }

        /// <summary>Crosses from a flank where the opponent has no player in the lane within 30 m of the crosser.</summary>
        private static void AppendFlanks(StringBuilder text, List<ShapeCounts> shapes)
        {
            text.Append("5. Crosses from a flank (lanes 0 and 2) with no opposing outfield player in the lane within 30 m of the crosser:\n");
            text.Append("   shape".PadRight(12) + "crosses".PadLeft(10) + "no marker".PadLeft(11) + "shot 10 s: all".PadLeft(17) + "no marker".PadLeft(12) + "marked".PadLeft(10) + "\n");

            foreach (var shape in shapes)
            {
                var at = Zones.FlankAt;
                double crosses = shape.Sums[at], open = shape.Sums[at + 1], shots = shape.Sums[at + 2], openShots = shape.Sums[at + 3];
                var marked = crosses - open;

                text.Append("   " + shape.Name.PadRight(9) + $"{crosses,10:F0}" + $"{100 * open / crosses,9:F1} %" + $"{100 * shots / crosses,18:F1} %"
                    + $"{(open == 0 ? 0 : 100 * openShots / open),10:F1} %" + $"{(marked == 0 ? 0 : 100 * (shots - openShots) / marked),8:F1} %\n");
            }

            text.Append('\n');
        }
    }
}
