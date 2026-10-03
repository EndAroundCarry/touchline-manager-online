using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Measures a film's motion directly, before it is sampled and compressed (`replay-v4`).
/// </summary>
/// <remarks>
/// The tests and the benchmark ask the same questions of the film — does the ball ever jump, how fast do the
/// players move, how much of the film is the ball standing still — and the honest place to answer them is the
/// simulated record, which has none of the rounding or compression a payload adds.
/// </remarks>
internal static class FilmMeasure
{
    /// <summary>The slowest the ball may move to still count as moving, in metres per second of real time.</summary>
    private const double StillSpeed = 0.3;

    /// <summary>How near the ball a player has to be to count as at it, in metres.</summary>
    private const double AtTheBall = 1.5;

    /// <summary>Half the width of the goal mouth the engine aims inside, in metres.</summary>
    private const double GoalHalfWidth = 4.9;

    /// <summary>Measures the film.</summary>
    public static FilmDiagnostics Measure(
        FilmContext context,
        FilmScriptResult script,
        FilmMotionResult motion,
        IReadOnlyList<FilmRoster> rosters,
        FilmPace used,
        double target,
        int filmMilliseconds,
        int rung)
    {
        var options = context.Options;
        var ballLimit = Math.Max(options.ShotMetresPerSecond, options.ClearanceMetresPerSecond) * 1.05;
        var speeds = new Dictionary<BeatKind, List<double>>();
        var teleports = 0;
        double still = 0;
        double moving = 0;
        double natural = 0;
        long holdMs = 0;
        var cuts = 0;

        for (var index = 0; index < script.Beats.Count; index++)
        {
            var beat = script.Beats[index];
            var span = motion.Spans[index];

            if (beat.Cut && index > 0)
            {
                cuts++;
            }

            if (beat.IsHold)
            {
                holdMs += (long)Math.Round(span.Seconds * 1000.0 / used.Pace);

                continue;
            }

            natural += beat.NaturalSeconds;

            if (!speeds.TryGetValue(beat.Kind, out var list))
            {
                list = [];
                speeds[beat.Kind] = list;
            }

            for (var record = span.FirstRecord + 1; record <= span.LastRecord; record++)
            {
                var dt = motion.TimeOf(record) - motion.TimeOf(record - 1);

                if (dt <= 1e-9)
                {
                    continue;
                }

                var dx = motion.BallX(record) - motion.BallX(record - 1);
                var dy = motion.BallY(record) - motion.BallY(record - 1);
                var speed = Math.Sqrt((dx * dx) + (dy * dy)) / dt;

                list.Add(speed * used.Pace);

                if (speed > ballLimit)
                {
                    teleports++;
                }

                if (speed < StillSpeed)
                {
                    still += dt;
                }

                moving += dt;
            }
        }

        var gaps = MeasureGaps(context, script, motion, rosters);

        double maxPlayer = 0;
        double maxKeeper = 0;

        for (var record = 1; record < motion.Count; record++)
        {
            var dt = motion.TimeOf(record) - motion.TimeOf(record - 1);

            if (dt <= 1e-9)
            {
                continue;
            }

            for (var entity = 0; entity < FilmRoster.Size; entity++)
            {
                var dx = motion.PlayerX(record, entity) - motion.PlayerX(record - 1, entity);
                var dy = motion.PlayerY(record, entity) - motion.PlayerY(record - 1, entity);
                var speed = Math.Sqrt((dx * dx) + (dy * dy)) / dt * used.Pace;

                if (context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                {
                    maxKeeper = Math.Max(maxKeeper, speed);
                }
                else
                {
                    maxPlayer = Math.Max(maxPlayer, speed);
                }
            }
        }

        return new FilmDiagnostics
        {
            Pace = used.Pace,
            HoldScale = used.HoldScale,
            TargetMilliseconds = (int)Math.Round(target * 1000.0),
            FilmMilliseconds = filmMilliseconds,
            HoldMilliseconds = (int)holdMs,
            CondensedPossessions = used.Condensed,
            Possessions = script.Possessions.Count,
            ExtensionShare = natural <= 0 ? 0.0 : motion.ExtensionSeconds / natural,
            Cuts = cuts,
            Teleports = teleports,
            StillBallShare = moving <= 0 ? 0.0 : still / moving,
            MaxPlayerFilmSpeed = maxPlayer,
            MaxKeeperFilmSpeed = maxKeeper,
            BallSpeeds = speeds
                .Where(pair => pair.Value.Count > 0)
                .ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => Summarize(pair.Value),
                    StringComparer.Ordinal),
            PayloadRung = rung,
            CarryBeats = gaps.CarryBeats,
            CarriesWithBall = gaps.CarriesWithBall,
            Receptions = gaps.Receptions,
            ReceiversAtBall = gaps.ReceiversAtBall,
            WorstReceiverGap = gaps.WorstReceiverGap,
            Saves = gaps.Saves,
            WorstKeeperGap = gaps.WorstKeeperGap,
            GoalStrikes = gaps.GoalStrikes,
            GoalsInNet = gaps.GoalsInNet,
        };
    }

    /// <summary>How near the ball the players who should be at it are, at the moments they should be.</summary>
    private static Gaps MeasureGaps(
        FilmContext context,
        FilmScriptResult script,
        FilmMotionResult motion,
        IReadOnlyList<FilmRoster> rosters)
    {
        var gaps = new Gaps();

        for (var index = 0; index < script.Beats.Count; index++)
        {
            var beat = script.Beats[index];

            if (beat.IsHold || beat.Possession < 0)
            {
                continue;
            }

            var roster = rosters[beat.Possession];
            var last = motion.Spans[index].LastRecord;
            var ball = new Vec(motion.BallX(last), motion.BallY(last));

            double GapOf(Guid? participant)
            {
                var entity = participant is Guid id ? roster.EntityOf(id) : -1;

                return entity < 0
                    ? double.NaN
                    : new Vec(motion.PlayerX(last, entity), motion.PlayerY(last, entity)).DistanceTo(ball);
            }

            switch (beat.Kind)
            {
                case BeatKind.Carry or BeatKind.Duel when beat.Receiver == beat.Actor:
                    {
                        var gap = GapOf(beat.Actor);

                        if (!double.IsNaN(gap))
                        {
                            gaps.CarryBeats++;
                            gaps.CarriesWithBall += gap <= AtTheBall ? 1 : 0;
                        }

                        break;
                    }

                case BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Cross:
                    {
                        var gap = GapOf(beat.Receiver);

                        if (!double.IsNaN(gap))
                        {
                            gaps.Receptions++;
                            gaps.ReceiversAtBall += gap <= AtTheBall ? 1 : 0;
                            gaps.WorstReceiverGap = Math.Max(gaps.WorstReceiverGap, gap);
                        }

                        break;
                    }

                case BeatKind.Save:
                    {
                        var gap = GapOf(beat.Actor);

                        if (!double.IsNaN(gap))
                        {
                            gaps.Saves++;
                            gaps.WorstKeeperGap = Math.Max(gaps.WorstKeeperGap, gap);
                        }

                        break;
                    }

                case BeatKind.Shot when beat.Strike == StrikeResult.Goal:
                    {
                        // Inside the goal mouth: on the goal line, between the posts.
                        var onLine = ball.X >= FilmSpace.Length - 0.1 || ball.X <= 0.1;
                        var mouth = Math.Abs(ball.Y - (FilmSpace.Width / 2)) <= GoalHalfWidth;

                        gaps.GoalStrikes++;
                        gaps.GoalsInNet += onLine && mouth ? 1 : 0;
                        break;
                    }

                default:
                    break;
            }
        }

        _ = context;

        return gaps;
    }

    private sealed class Gaps
    {
        public int CarryBeats { get; set; }

        public int CarriesWithBall { get; set; }

        public int Receptions { get; set; }

        public int ReceiversAtBall { get; set; }

        public double WorstReceiverGap { get; set; }

        public int Saves { get; set; }

        public double WorstKeeperGap { get; set; }

        public int GoalStrikes { get; set; }

        public int GoalsInNet { get; set; }
    }

    private static BallSpeedSummary Summarize(List<double> values)
    {
        values.Sort();

        return new BallSpeedSummary(Percentile(values, 0.5), Percentile(values, 0.95));
    }

    private static double Percentile(List<double> sorted, double fraction) =>
        sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(fraction * sorted.Count))];
}
