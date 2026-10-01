namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns motion into the smallest track that still describes it (Stage 3, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// A highlight's movement is authored as waypoints — a build-up, a strike, a dive — and then sampled finely
/// enough that a direction change cannot fall between two samples. The compressed track keeps only the
/// samples where the velocity or the direction actually changed: a straight run is two keyframes, a flighted
/// ball is the handful of keyframes its arc bends around. That is the delta compression the plan asks for
/// (the sampling interval is the 300–500 ms band; the output is only the changes), and it is what keeps a
/// twenty-two-entity passage in the low kilobytes.
/// </para>
/// <para>
/// The compressor is pure integer arithmetic over the keyframes it is given, so a replay is byte-identical
/// on every machine and the payload estimate can be taken from the compressed track rather than a guess.
/// A keyframe that carries an <see cref="HighlightKeyframeV1.Action"/> is never dropped: it is the semantic
/// anchor a renderer styles and a commentary token synchronizes against, and simplifying it away would make
/// the animation poorer while saving twenty-four bytes.
/// </para>
/// </remarks>
public static class KeyframeCompressor
{
    /// <summary>The sampling interval a track is authored at, in milliseconds.</summary>
    /// <remarks>
    /// Twelve and a half samples a second, inside the plan's 300–500 ms band. Finer sampling would let the
    /// compressor see smaller direction changes than a viewer can; coarser sampling could miss the moment a
    /// run or a flight turns.
    /// </remarks>
    public const int SampleIntervalMilliseconds = 400;

    /// <summary>
    /// Gets the default tolerance for dropping a keyframe, in normalized position units.
    /// </summary>
    /// <remarks>
    /// Twenty-four units of ten thousand is about a quarter of a metre on a real pitch: below what a token a
    /// few pixels wide can show, and above the noise a rounded interpolation introduces.
    /// </remarks>
    public const int DefaultTolerance = 24;

    /// <summary>
    /// Samples a waypoint path onto the animation clock at <see cref="SampleIntervalMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// Linear between the path's own waypoints: the waypoints are the movement the director decided on, and
    /// a spline through them would invent motion between them. A waypoint is always kept at its own time, so
    /// a semantic action — the strike, the dive — lands exactly where it was authored.
    /// </remarks>
    /// <param name="waypoints">The path, in time order.</param>
    /// <param name="sampleIntervalMilliseconds">How finely to sample between waypoints.</param>
    public static List<HighlightKeyframeV1> Sample(
        IReadOnlyList<HighlightKeyframeV1> waypoints,
        int sampleIntervalMilliseconds = SampleIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleIntervalMilliseconds, 1);

        if (waypoints.Count == 0)
        {
            return [];
        }

        if (waypoints.Count == 1)
        {
            return [waypoints[0]];
        }

        var samples = new List<HighlightKeyframeV1> { waypoints[0] };

        for (var index = 1; index < waypoints.Count; index++)
        {
            var from = waypoints[index - 1];
            var to = waypoints[index];
            var span = to.TimeMilliseconds - from.TimeMilliseconds;

            if (span <= 0)
            {
                // A same-time pair is a state change inside one moment — an action tag at the strike time.
                // The later keyframe wins, because it is the state the moment ends in.
                samples[^1] = to;

                continue;
            }

            for (var time = from.TimeMilliseconds + sampleIntervalMilliseconds; time < to.TimeMilliseconds; time += sampleIntervalMilliseconds)
            {
                samples.Add(new HighlightKeyframeV1(
                    time,
                    Interpolate(from.X, to.X, time - from.TimeMilliseconds, span),
                    Interpolate(from.Y, to.Y, time - from.TimeMilliseconds, span),
                    Interpolate(from.Z, to.Z, time - from.TimeMilliseconds, span),
                    Action: null));
            }

            samples.Add(to);
        }

        return samples;
    }

    /// <summary>
    /// Drops every keyframe the interpolation does not need, and derives each kept keyframe's speed.
    /// </summary>
    /// <param name="keyframes">A sampled track, in time order.</param>
    /// <param name="tolerance">How far a dropped keyframe may sit from the line that replaces it.</param>
    public static List<HighlightKeyframeV1> Compress(
        IReadOnlyList<HighlightKeyframeV1> keyframes,
        int tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, 0);

        if (keyframes.Count <= 2)
        {
            return [.. keyframes];
        }

        // Semantic keyframes survive at any tolerance, so each run between them is simplified on its own and
        // the anchors join the result untouched.
        var kept = new List<int> { 0 };
        var segmentStart = 0;

        for (var index = 1; index < keyframes.Count; index++)
        {
            if (keyframes[index].Action is null)
            {
                continue;
            }

            Simplify(keyframes, segmentStart, index, tolerance, kept);
            kept.Add(index);
            segmentStart = index;
        }

        Simplify(keyframes, segmentStart, keyframes.Count - 1, tolerance, kept);

        if (kept[^1] != keyframes.Count - 1)
        {
            kept.Add(keyframes.Count - 1);
        }

        var compressed = new List<HighlightKeyframeV1>(kept.Count);

        for (var position = 0; position < kept.Count; position++)
        {
            var keyframe = keyframes[kept[position]];

            compressed.Add(keyframe with
            {
                Speed = position + 1 < kept.Count
                    ? SpeedBetween(keyframe, keyframes[kept[position + 1]])
                    : 0,
            });
        }

        return compressed;
    }

    /// <summary>Simplifies one run, appending the indices it keeps to <paramref name="kept"/> in order.</summary>
    private static void Simplify(
        IReadOnlyList<HighlightKeyframeV1> keyframes,
        int start,
        int end,
        int tolerance,
        List<int> kept)
    {
        if (end <= start || kept[^1] == end)
        {
            return;
        }

        var worstIndex = -1;
        var worstDistance = 0L;

        for (var index = start + 1; index < end; index++)
        {
            var distance = DistanceFromLine(keyframes[start], keyframes[end], keyframes[index]);

            if (distance > worstDistance)
            {
                worstDistance = distance;
                worstIndex = index;
            }
        }

        if (worstIndex < 0 || worstDistance <= tolerance)
        {
            return;
        }

        Simplify(keyframes, start, worstIndex, tolerance, kept);

        if (kept[^1] != worstIndex)
        {
            kept.Add(worstIndex);
        }

        Simplify(keyframes, worstIndex, end, tolerance, kept);
    }

    /// <summary>
    /// How far a keyframe sits from the straight line between two others, altitude included.
    /// </summary>
    /// <remarks>
    /// Altitude is scaled the same way the renderer scales it — one altitude unit is one percent of the
    /// pitch's length — so a ball climbing to head height is as far from a flat line as a player who has run
    /// a tenth of the pitch. A track that ignored altitude would compress a cross into a pass.
    /// </remarks>
    private static long DistanceFromLine(
        HighlightKeyframeV1 from,
        HighlightKeyframeV1 to,
        HighlightKeyframeV1 point)
    {
        var span = to.TimeMilliseconds - from.TimeMilliseconds;

        if (span <= 0)
        {
            return Distance(from, point);
        }

        var elapsed = point.TimeMilliseconds - from.TimeMilliseconds;
        var expectedX = Interpolate(from.X, to.X, elapsed, span);
        var expectedY = Interpolate(from.Y, to.Y, elapsed, span);
        var expectedZ = Interpolate(from.Z, to.Z, elapsed, span);

        var dx = point.X - expectedX;
        var dy = point.Y - expectedY;
        var dz = (point.Z - expectedZ) * (10_000 / 100);

        return (long)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static long Distance(HighlightKeyframeV1 from, HighlightKeyframeV1 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = (to.Z - from.Z) * (10_000 / 100);

        return (long)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>The ground speed between two keyframes, in normalized units per second.</summary>
    private static int SpeedBetween(HighlightKeyframeV1 from, HighlightKeyframeV1 to)
    {
        var elapsed = to.TimeMilliseconds - from.TimeMilliseconds;

        if (elapsed <= 0)
        {
            return 0;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        return (int)Math.Round(distance * 1_000 / elapsed);
    }

    private static int Interpolate(int from, int to, int elapsed, int span) =>
        from + (int)(((long)(to - from) * elapsed) / span);
}
