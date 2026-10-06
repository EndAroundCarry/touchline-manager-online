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

    /// <summary>How near the ball a player is to be crowding it, in metres (`replay-v6`).</summary>
    private const double Crowding = 5.0;

    /// <summary>How deep the eighteen-yard box is, in metres.</summary>
    private const double BoxDepth = 16.5;

    /// <summary>Half the width of the eighteen-yard box, in metres.</summary>
    private const double BoxHalfWidth = 20.16;

    /// <summary>How deep the six-yard box is, in metres.</summary>
    private const double SixYardDepth = 5.5;

    /// <summary>Half the width of the six-yard box, in metres.</summary>
    private const double SixYardHalfWidth = 9.16;

    /// <summary>How many of a side's furthest-forward outfield players are left out of the block's depth: its outlets.</summary>
    private const int OutletCount = 2;

    /// <summary>How far from its own goal line a side's block counts as deep, in metres.</summary>
    private const double DeepLimit = 30.0;

    /// <summary>How far from the goal line the ball is when it is in the defending side's own third, in metres.</summary>
    private const double OwnThird = 35.0;

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
            Shape = MeasureShape(context, script, motion, rosters),
        };
    }

    /// <summary>
    /// How the twenty-two stand around the ball in open play (`replay-v6`): how many crowd it, how close team-mates
    /// stand, how deep and wide each block is, and how the box fills when the ball is in it. Dead balls are left out.
    /// </summary>
    private static ShapeMetrics MeasureShape(
        FilmContext context,
        FilmScriptResult script,
        FilmMotionResult motion,
        IReadOnlyList<FilmRoster> rosters)
    {
        var near = new Histogram(1.0, FilmRoster.Size + 1);
        var spacing = new Histogram(0.1, 600);
        var inBox = new Histogram(1.0, FilmRoster.Size + 1);
        var sixYard = new Histogram(1.0, FilmRoster.Size + 1);
        var depthWith = new Average();
        var widthWith = new Average();
        var depthWithout = new Average();
        var widthWithout = new Average();
        var back = new Average();
        var front = new Average();
        var deepCount = new Average();
        var deepBack = new Average();
        var samples = 0;

        // The occupied outfield entities of each side, home then away, gathered afresh for every record.
        int[][] outfield = [new int[11], new int[11]];
        var counts = new int[2];
        Span<double> reach = stackalloc double[11];

        for (var index = 0; index < script.Beats.Count; index++)
        {
            var beat = script.Beats[index];

            if (beat.IsHold || beat.Formation != FormationMode.Open || beat.Possession < 0)
            {
                continue;
            }

            var roster = rosters[beat.Possession];
            var span = motion.Spans[index];
            var attacking = beat.Side;
            var defending = MatchInputV1.OpponentOf(attacking);

            for (var record = span.FirstRecord; record <= span.LastRecord; record++)
            {
                var ball = new Vec(motion.BallX(record), motion.BallY(record));
                var crowd = 0;

                counts[0] = 0;
                counts[1] = 0;

                for (var entity = 0; entity < FilmRoster.Size; entity++)
                {
                    if (!roster.IsOccupied(entity))
                    {
                        continue;
                    }

                    var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));

                    crowd += point.DistanceTo(ball) <= Crowding ? 1 : 0;

                    if (context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
                    {
                        var half = (int)FilmRoster.SideOf(entity);

                        outfield[half][counts[half]++] = entity;
                    }
                }

                near.Add(crowd);
                samples++;

                foreach (var side in new[] { attacking, defending })
                {
                    var members = outfield[(int)side];
                    var count = counts[(int)side];

                    if (count == 0)
                    {
                        continue;
                    }

                    double minY = double.MaxValue, maxY = double.MinValue;
                    double backSum = 0, frontSum = 0;
                    int backCount = 0, frontCount = 0, deepPlayers = 0;

                    for (var member = 0; member < count; member++)
                    {
                        var entity = members[member];
                        var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));
                        var fromGoal = FilmSpace.Attacking(point, side);
                        var nearest = double.MaxValue;

                        reach[member] = fromGoal;
                        minY = Math.Min(minY, point.Y);
                        maxY = Math.Max(maxY, point.Y);
                        deepPlayers += fromGoal <= DeepLimit ? 1 : 0;

                        switch (context.Slots[entity].Family)
                        {
                            case MatchPositionFamily.Defence:
                                backSum += fromGoal;
                                backCount++;
                                break;

                            case MatchPositionFamily.Attack:
                                frontSum += fromGoal;
                                frontCount++;
                                break;

                            default:
                                break;
                        }

                        for (var other = 0; other < count; other++)
                        {
                            if (other != member)
                            {
                                var mate = members[other];

                                nearest = Math.Min(
                                    nearest,
                                    point.DistanceTo(new Vec(motion.PlayerX(record, mate), motion.PlayerY(record, mate))));
                            }
                        }

                        if (count > 1)
                        {
                            spacing.Add(nearest);
                        }
                    }

                    // The block is the deepest eight: the two furthest forward are outlets, left high on purpose.
                    var block = reach[..count];

                    block.Sort();

                    var depth = block[Math.Max(1, count - OutletCount) - 1] - block[0];
                    var width = maxY - minY;

                    if (side == attacking)
                    {
                        depthWith.Add(depth);
                        widthWith.Add(width);

                        continue;
                    }

                    depthWithout.Add(depth);
                    widthWithout.Add(width);

                    if (backCount > 0)
                    {
                        back.Add(backSum / backCount);
                    }

                    if (frontCount > 0)
                    {
                        front.Add(frontSum / frontCount);
                    }

                    // The ball is in the defending side's own third: how many of them are back, and how deep their line is.
                    if (FilmSpace.Attacking(ball, defending) <= OwnThird)
                    {
                        deepCount.Add(deepPlayers);

                        if (backCount > 0)
                        {
                            deepBack.Add(backSum / backCount);
                        }
                    }
                }

                MeasureBox(motion, record, ball, outfield, counts, inBox, sixYard);
            }
        }

        return new ShapeMetrics
        {
            Samples = samples,
            NearBallP50 = near.Percentile(0.5),
            NearBallP95 = near.Percentile(0.95),
            NeighbourSpacingP5 = spacing.Percentile(0.05),
            InPossessionDepth = depthWith.Value,
            InPossessionWidth = widthWith.Value,
            OutOfPossessionDepth = depthWithout.Value,
            OutOfPossessionWidth = widthWithout.Value,
            BackLineDepth = back.Value,
            FrontLineDepth = front.Value,
            DeepSamples = deepCount.Count,
            DeepBlockOutfield = deepCount.Value,
            DeepBlockBackLine = deepBack.Value,
            BoxSamples = inBox.Total,
            InBoxP50 = inBox.Percentile(0.5),
            InBoxP95 = inBox.Percentile(0.95),
            SixYardP95 = sixYard.Percentile(0.95),
        };
    }

    /// <summary>Counts the outfield players in the box the ball is in, and in its six-yard box, when it is in one.</summary>
    private static void MeasureBox(
        FilmMotionResult motion,
        int record,
        Vec ball,
        int[][] outfield,
        int[] counts,
        Histogram inBox,
        Histogram sixYard)
    {
        var low = ball.X <= BoxDepth;
        var high = ball.X >= FilmSpace.Length - BoxDepth;
        var middle = FilmSpace.Width / 2;

        if ((!low && !high) || Math.Abs(ball.Y - middle) > BoxHalfWidth)
        {
            return;
        }

        var goalLine = low ? 0.0 : FilmSpace.Length;
        int boxCount = 0, sixCount = 0;

        for (var side = 0; side < 2; side++)
        {
            for (var member = 0; member < counts[side]; member++)
            {
                var entity = outfield[side][member];
                var x = motion.PlayerX(record, entity);
                var y = motion.PlayerY(record, entity);
                var along = Math.Abs(x - goalLine);
                var across = Math.Abs(y - middle);

                boxCount += along <= BoxDepth && across <= BoxHalfWidth ? 1 : 0;
                sixCount += along <= SixYardDepth && across <= SixYardHalfWidth ? 1 : 0;
            }
        }

        inBox.Add(boxCount);
        sixYard.Add(sixCount);
    }

    /// <summary>A fixed-width histogram, for percentiles of many samples without keeping them.</summary>
    private sealed class Histogram(double width, int bins)
    {
        private readonly int[] _bins = new int[bins];

        public int Total { get; private set; }

        public void Add(double value)
        {
            var bin = int.Clamp((int)Math.Floor(value / width), 0, _bins.Length - 1);

            _bins[bin]++;
            Total++;
        }

        public double Percentile(double fraction)
        {
            if (Total == 0)
            {
                return 0.0;
            }

            var wanted = Math.Min(Total - 1, (int)Math.Floor(fraction * Total));
            var seen = 0;

            for (var bin = 0; bin < _bins.Length; bin++)
            {
                seen += _bins[bin];

                if (seen > wanted)
                {
                    return bin * width;
                }
            }

            return (_bins.Length - 1) * width;
        }
    }

    /// <summary>A running mean.</summary>
    private sealed class Average
    {
        private double _sum;

        public int Count { get; private set; }

        public double Value => Count == 0 ? 0.0 : _sum / Count;

        public void Add(double value)
        {
            _sum += value;
            Count++;
        }
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
