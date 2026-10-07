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

    /// <summary>How long a pass has to be, in metres, for a player who plays it and receives it himself to be an error (`replay-v10`).</summary>
    private const double SelfPassDistance = 4.0;

    /// <summary>How near the ball a player is to be crowding it, in metres (`replay-v6`).</summary>
    private const double Crowding = 5.0;

    /// <summary>How many players within the crowding distance of the ball make it a crowd.</summary>
    private const int CrowdedCount = 5;

    /// <summary>How near the ball players are a pile, in metres.</summary>
    private const double Clustering = 3.0;

    /// <summary>How many players within the clustering distance of the ball make a pile.</summary>
    private const int ClusteredCount = 4;

    /// <summary>How deep the eighteen-yard box is, in metres.</summary>
    private const double BoxDepth = 16.5;

    /// <summary>Half the width of the eighteen-yard box, in metres.</summary>
    private const double BoxHalfWidth = 20.16;

    /// <summary>How deep the six-yard box is, in metres.</summary>
    private const double SixYardDepth = 5.5;

    /// <summary>How far from the goal line a taking side's player is to count as held back against a break at a corner, in metres (`replay-v6`).</summary>
    private const double GuardFrom = 40.0;

    /// <summary>How far from the ball the rules keep the defence at a free kick, and everybody at a penalty, in metres (`replay-v6`).</summary>
    private const double FreeKickClear = 9.15;

    /// <summary>How far from the ball a defender is, at the most, to count as part of the wall, in metres: it stands on the ten yards, and those nearer than that are intruders, counted as such (`replay-v6`, `replay-v13`).</summary>
    private const double FreeKickWallReach = 11.0;

    /// <summary>Half the width of the six-yard box, in metres.</summary>
    private const double SixYardHalfWidth = 9.16;

    /// <summary>How many of a side's furthest-forward outfield players are left out of the block's depth: its outlets.</summary>
    private const int OutletCount = 2;

    /// <summary>How far from its own goal line a side's block counts as deep, in metres.</summary>
    private const double DeepLimit = 30.0;

    /// <summary>How far from the goal line the ball is when it is in the defending side's own third, in metres.</summary>
    private const double OwnThird = 35.0;

    /// <summary>How near the ball a player is to be part of the local pack, in metres (`replay-v12` baseline).</summary>
    private const double PackNear = 10.0;

    /// <summary>How near the ball a player is to be part of the compressed game, in metres.</summary>
    private const double PackFar = 25.0;

    /// <summary>How far from the ball a player is to be far from the play, in metres.</summary>
    private const double FarFrom = 25.0;

    /// <summary>How near an attacker a defender stands to mark him, in metres: the two dots touch.</summary>
    private const double MarkGap = 1.5;

    /// <summary>How near the goal a player is to be in the pack at a set piece, in metres.</summary>
    private const double FreeKickPack = 25.0;

    /// <summary>How far from the middle of the pitch a carrier is to be running a wing, in metres: the film's own flank lane.</summary>
    private const double WingLane = 16.0;

    /// <summary>How far up the pitch the ball is, on its side's scale, for a wing carry to be an attack, in metres.</summary>
    private const double WingDepth = 50.0;

    /// <summary>How far from the carrier a defender is to be chasing him, in metres.</summary>
    private const double ChaseReach = 10.0;

    /// <summary>How far behind the carrier, away from the goal he attacks, a defender is to be chasing him, in metres.</summary>
    private const double ChaseBehind = 1.0;

    /// <summary>How many chasers make a chain.</summary>
    private const int ChaseCount = 3;

    /// <summary>How near the place he ends up a waiting player is to have settled, in metres: the two dots would touch.</summary>
    private const double SettledWithin = 1.0;

    /// <summary>How long a player has to have waited in his place to count as settled, in seconds of film.</summary>
    private const double SettledFor = 0.3;

    /// <summary>How fast a player is going, in metres per second of real time, to count as running at the strike rather than standing or walking about (`replay-v13`).</summary>
    private const double RunningAt = 5.0;

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
            SelfPasses = gaps.SelfPasses,
            GoalStrikes = gaps.GoalStrikes,
            GoalsInNet = gaps.GoalsInNet,
            Shape = MeasureShape(context, script, motion, rosters, used.Pace),
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
        IReadOnlyList<FilmRoster> rosters,
        double pace)
    {
        var near = new Histogram(1.0, FilmRoster.Size + 1);
        var spacing = new Histogram(0.1, 600);
        var inBox = new Histogram(1.0, FilmRoster.Size + 1);
        var sixYard = new Histogram(1.0, FilmRoster.Size + 1);
        var deliveryAttackers = new Histogram(1.0, FilmRoster.Size + 1);
        var deliveryDefenders = new Histogram(1.0, FilmRoster.Size + 1);
        var deliverySix = new Histogram(1.0, FilmRoster.Size + 1);
        var cornerAttackers = new Histogram(1.0, FilmRoster.Size + 1);
        var cornerDefenders = new Histogram(1.0, FilmRoster.Size + 1);
        var cornerSix = new Histogram(1.0, FilmRoster.Size + 1);
        var cornerGuards = new Histogram(1.0, FilmRoster.Size + 1);
        var cornerOutLeg = new Histogram(0.5, 200);
        var cornerTakerGap = new Histogram(0.5, 200);
        var keeperOffGoal = new Histogram(0.5, 200);
        var keeperAtWideBall = new Histogram(0.5, 200);
        var freeKickIntruders = new Histogram(1.0, FilmRoster.Size + 1);
        var freeKickWall = new Histogram(1.0, FilmRoster.Size + 1);
        var penaltyIntruders = new Histogram(1.0, FilmRoster.Size + 1);
        var pack10 = new Histogram(1.0, FilmRoster.Size + 1);
        var pack25 = new Histogram(1.0, FilmRoster.Size + 1);
        var marks = new MarkCount();
        var cornerMarks = new MarkCount();
        var freeKickMarks = new MarkCount();
        var farMetres = new Average();
        var farSeconds = new Average();
        var farCount = new Average();
        var attackKeeper = new[] { new Average(), new Average(), new Average() };
        var defendKeeper = new[] { new Average(), new Average(), new Average() };
        var waiting = new WaitingTally();
        var chasers = new Histogram(1.0, FilmRoster.Size + 1);
        var wingSamples = 0;
        var chained = 0;
        var keepers = new int[2];
        var depthWith = new Average();
        var widthWith = new Average();
        var depthWithout = new Average();
        var widthWithout = new Average();
        var back = new Average();
        var front = new Average();
        var deepCount = new Average();
        var deepBack = new Average();
        var samples = 0;
        var crowded = 0;
        var clustered = 0;

        // The occupied outfield entities of each side, home then away, gathered afresh for every record.
        int[][] outfield = [new int[11], new int[11]];
        var counts = new int[2];
        Span<double> reach = stackalloc double[11];

        for (var index = 0; index < script.Beats.Count; index++)
        {
            var beat = script.Beats[index];

            if (beat.Kind == BeatKind.Cross && beat.Formation == FormationMode.Corner && beat.Possession >= 0)
            {
                MeasureCorner(
                    context,
                    rosters[beat.Possession],
                    motion,
                    motion.Spans[index].LastRecord,
                    beat.Side,
                    cornerAttackers,
                    cornerDefenders,
                    cornerSix,
                    cornerGuards);

                MeasureCornerLead(script, rosters[beat.Possession], motion, index, cornerOutLeg, cornerTakerGap);
            }

            if (beat.Kind == BeatKind.Shot && beat.Possession >= 0)
            {
                MeasureKeeperAtShot(rosters[beat.Possession], motion, index, beat, keeperOffGoal, keeperAtWideBall);
            }

            if (beat.IsHold && beat.Possession >= 0 && beat.Formation is FormationMode.Corner or FormationMode.FreeKickShot or FormationMode.FreeKickCross)
            {
                MeasureWaiting(context, rosters[beat.Possession], motion, motion.Spans[index], beat, pace, cornerMarks, freeKickMarks, waiting);
            }

            if (beat.IsHold && beat.Possession >= 0 && beat.Formation is FormationMode.FreeKickShot or FormationMode.FreeKickCross or FormationMode.Penalty)
            {
                MeasureDeadBall(
                    context,
                    rosters[beat.Possession],
                    motion,
                    motion.Spans[index].LastRecord,
                    beat,
                    freeKickIntruders,
                    freeKickWall,
                    penaltyIntruders);
            }

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
                var tight = 0;

                counts[0] = 0;
                counts[1] = 0;
                keepers[0] = -1;
                keepers[1] = -1;

                for (var entity = 0; entity < FilmRoster.Size; entity++)
                {
                    if (!roster.IsOccupied(entity))
                    {
                        continue;
                    }

                    var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));

                    var gap = point.DistanceTo(ball);

                    crowd += gap <= Crowding ? 1 : 0;
                    tight += gap <= Clustering ? 1 : 0;

                    if (context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
                    {
                        var half = (int)FilmRoster.SideOf(entity);

                        outfield[half][counts[half]++] = entity;
                    }
                    else
                    {
                        keepers[(int)FilmRoster.SideOf(entity)] = entity;
                    }
                }

                MeasurePack(motion, record, ball, outfield, counts, span, pace, pack10, pack25, farMetres, farSeconds, farCount);
                MeasureOpenMarks(context, roster, motion, record, ball, attacking, marks);
                MeasureKeepers(motion, record, ball, keepers, attacking, attackKeeper, defendKeeper);

                if (beat.Kind == BeatKind.Carry)
                {
                    MeasureChasers(motion, record, ball, outfield, counts, attacking, chasers, ref wingSamples, ref chained);
                }

                near.Add(crowd);
                samples++;
                crowded += crowd >= CrowdedCount ? 1 : 0;
                clustered += tight >= ClusteredCount ? 1 : 0;

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

            if (beat.Kind == BeatKind.Cross)
            {
                MeasureDelivery(context, roster, motion, span.LastRecord, attacking, deliveryAttackers, deliveryDefenders, deliverySix);
            }
        }

        return new ShapeMetrics
        {
            Samples = samples,
            NearBallP50 = near.Percentile(0.5),
            NearBallP95 = near.Percentile(0.95),
            CrowdedShare = samples == 0 ? 0.0 : (double)crowded / samples,
            ClusteredShare = samples == 0 ? 0.0 : (double)clustered / samples,
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
            Deliveries = deliveryAttackers.Total,
            DeliveryAttackersP50 = deliveryAttackers.Percentile(0.5),
            DeliveryDefendersP50 = deliveryDefenders.Percentile(0.5),
            DeliverySixYardP95 = deliverySix.Percentile(0.95),
            Corners = cornerAttackers.Total,
            CornerAttackersP50 = cornerAttackers.Percentile(0.5),
            CornerDefendersP50 = cornerDefenders.Percentile(0.5),
            CornerSixYardP95 = cornerSix.Percentile(0.95),
            CornerGuardsP50 = cornerGuards.Percentile(0.5),
            CornerOutLegP95 = cornerOutLeg.Percentile(0.95),
            CornerTakerGapP95 = cornerTakerGap.Percentile(0.95),
            KeeperOffGoalP95 = keeperOffGoal.Percentile(0.95),
            KeeperAtWideBallP05 = keeperAtWideBall.Percentile(0.05),
            FreeKicks = freeKickIntruders.Total,
            FreeKickIntrudersP95 = freeKickIntruders.Percentile(0.95),
            FreeKickWallP50 = freeKickWall.Percentile(0.5),
            Penalties = penaltyIntruders.Total,
            PenaltyIntrudersP95 = penaltyIntruders.Percentile(0.95),
            PackWithin10mP50 = pack10.Percentile(0.5),
            PackWithin25mP50 = pack25.Percentile(0.5),
            MarkedShare = marks.Share,
            CornerMarkedShare = cornerMarks.Share,
            FreeKickMarkedShare = freeKickMarks.Share,
            FarPlayerMotion = farSeconds.Count == 0 ? 0.0 : farMetres.Sum / farSeconds.Sum,
            FarPlayerCount = farCount.Value,
            AttackKeeperOffLine = new DepthBands(attackKeeper[0].Value, attackKeeper[1].Value, attackKeeper[2].Value),
            DefendKeeperOffLine = new DepthBands(defendKeeper[0].Value, defendKeeper[1].Value, defendKeeper[2].Value),
            SetPieceHolds = waiting.HoldSeconds.Count,
            SetPieceHoldSeconds = waiting.HoldSeconds.Value,
            SetPieceSettledShare = waiting.SettledShare.Value,
            SetPieceSettledMotion = waiting.SettledMotion.Value,
            CornerRunningShare = waiting.CornerRunning.Value,
            FreeKickRunningShare = waiting.FreeKickRunning.Value,
            CornerGoalPackP50 = waiting.CornerGoalPack.Percentile(0.5),
            FreeKickGoalPackP50 = waiting.FreeKickGoalPack.Percentile(0.5),
            FreeKickShotGoalPackP50 = waiting.FreeKickShotGoalPack.Percentile(0.5),
            FreeKickCrossGoalPackP50 = waiting.FreeKickCrossGoalPack.Percentile(0.5),
            WingCarrySamples = wingSamples,
            ChasersP50 = chasers.Percentile(0.5),
            ChasersShare = wingSamples == 0 ? 0.0 : (double)chained / wingSamples,
        };
    }

    /// <summary>
    /// Counts the outfield players near the ball and how fast those far from it move (`replay-v12` baseline): how many are
    /// within ten and twenty-five metres, and the speed of the ones beyond twenty-five, in metres per second of film.
    /// </summary>
    private static void MeasurePack(
        FilmMotionResult motion,
        int record,
        Vec ball,
        int[][] outfield,
        int[] counts,
        BeatSpan span,
        double pace,
        Histogram pack10,
        Histogram pack25,
        Average farMetres,
        Average farSeconds,
        Average farCount)
    {
        int within10 = 0, within25 = 0, far = 0;
        var dt = record > span.FirstRecord ? motion.TimeOf(record) - motion.TimeOf(record - 1) : 0.0;

        for (var side = 0; side < 2; side++)
        {
            for (var member = 0; member < counts[side]; member++)
            {
                var entity = outfield[side][member];
                var gap = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity)).DistanceTo(ball);

                within10 += gap <= PackNear ? 1 : 0;
                within25 += gap <= PackFar ? 1 : 0;

                if (gap <= FarFrom)
                {
                    continue;
                }

                far++;

                if (dt > 1e-9)
                {
                    var step = new Vec(
                        motion.PlayerX(record, entity) - motion.PlayerX(record - 1, entity),
                        motion.PlayerY(record, entity) - motion.PlayerY(record - 1, entity)).Length;

                    farMetres.Add(step);
                    farSeconds.Add(dt / pace);
                }
            }
        }

        pack10.Add(within10);
        pack25.Add(within25);
        farCount.Add(far);
    }

    /// <summary>Whether a defender stands within a metre and a half of an attacker: the two dots touch, as the reference marks a man.</summary>
    private static bool HasMarker(FilmContext context, FilmRoster roster, FilmMotionResult motion, int record, int entity, MatchSide attacking)
    {
        var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));

        for (var other = 0; other < FilmRoster.Size; other++)
        {
            if (!roster.IsOccupied(other)
                || FilmRoster.SideOf(other) == attacking
                || context.Slots[other].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var mark = new Vec(motion.PlayerX(record, other), motion.PlayerY(record, other));

            if (mark.DistanceTo(point) <= MarkGap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Counts, in open play, the attackers within ten metres of the ball who have a marker (`replay-v12` baseline).</summary>
    private static void MeasureOpenMarks(
        FilmContext context,
        FilmRoster roster,
        FilmMotionResult motion,
        int record,
        Vec ball,
        MatchSide attacking,
        MarkCount marks)
    {
        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity)
                || FilmRoster.SideOf(entity) != attacking
                || context.Slots[entity].Family == MatchPositionFamily.Goalkeeper
                || new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity)).DistanceTo(ball) > PackNear)
            {
                continue;
            }

            marks.Add(HasMarker(context, roster, motion, record, entity, attacking));
        }
    }

    /// <summary>Records how far each keeper stands from his goal line, by how far up the pitch the ball is (`replay-v12` baseline).</summary>
    private static void MeasureKeepers(
        FilmMotionResult motion,
        int record,
        Vec ball,
        int[] keepers,
        MatchSide attacking,
        Average[] attackKeeper,
        Average[] defendKeeper)
    {
        var third = FilmSpace.Length / 3.0;
        var band = int.Clamp((int)Math.Floor(FilmSpace.Attacking(ball, attacking) / third), 0, 2);
        var defending = MatchInputV1.OpponentOf(attacking);

        foreach (var (side, sink) in new[] { (attacking, attackKeeper), (defending, defendKeeper) })
        {
            var entity = keepers[(int)side];

            if (entity >= 0)
            {
                sink[band].Add(Math.Abs(motion.PlayerX(record, entity) - FilmSpace.OwnGoal(side).X));
            }
        }
    }

    /// <summary>
    /// Counts the defenders chasing a carrier who runs a wing in the attacking half (`replay-v12` baseline): the reference
    /// strings three or four of them out behind him, within ten metres, between him and the goal he is running away from.
    /// </summary>
    private static void MeasureChasers(
        FilmMotionResult motion,
        int record,
        Vec ball,
        int[][] outfield,
        int[] counts,
        MatchSide attacking,
        Histogram chasers,
        ref int samples,
        ref int chained)
    {
        if (Math.Abs(ball.Y - (FilmSpace.Width / 2)) < WingLane || FilmSpace.Attacking(ball, attacking) < WingDepth)
        {
            return;
        }

        var defending = (int)MatchInputV1.OpponentOf(attacking);
        var behind = 0;

        for (var member = 0; member < counts[defending]; member++)
        {
            var entity = outfield[defending][member];
            var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));

            // Behind him is further from the goal he attacks than he is.
            behind += point.DistanceTo(ball) <= ChaseReach && FilmSpace.Attacking(point, attacking) <= FilmSpace.Attacking(ball, attacking) - ChaseBehind ? 1 : 0;
        }

        chasers.Add(behind);
        samples++;
        chained += behind >= ChaseCount ? 1 : 0;
    }

    /// <summary>
    /// Measures a set piece that is waiting to be taken (`replay-v12` baseline): how long the hold is, how many of the players
    /// have settled in their places before the kick, how fast those that have settled move about afterwards, and how many of the
    /// taking side's players in the pack have a marker.
    /// </summary>
    private static void MeasureWaiting(
        FilmContext context,
        FilmRoster roster,
        FilmMotionResult motion,
        BeatSpan span,
        FilmBeat hold,
        double pace,
        MarkCount cornerMarks,
        MarkCount freeKickMarks,
        WaitingTally tally)
    {
        var attacking = hold.Side;
        var goal = FilmSpace.AttackedGoal(attacking);
        var taker = hold.Actor is Guid actor ? roster.EntityOf(actor) : -1;
        var last = span.LastRecord;

        tally.HoldSeconds.Add(span.Seconds / pace);

        int players = 0, settled = 0, running = 0, nearGoal = 0;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity) || entity == taker || context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var point = new Vec(motion.PlayerX(last, entity), motion.PlayerY(last, entity));
            var arrived = span.FirstRecord;

            // A player has settled once he is within a metre of where he ends up, and has a moment left to wait there.
            while (arrived <= last && new Vec(motion.PlayerX(arrived, entity), motion.PlayerY(arrived, entity)).DistanceTo(point) > SettledWithin)
            {
                arrived++;
            }

            players++;
            nearGoal += point.DistanceTo(goal) <= FreeKickPack ? 1 : 0;

            // Running flat out as the ball is struck, which is not how a pack stands: the last step of the hold.
            if (last > span.FirstRecord)
            {
                var seconds = motion.TimeOf(last) - motion.TimeOf(last - 1);
                var before = new Vec(motion.PlayerX(last - 1, entity), motion.PlayerY(last - 1, entity));

                running += seconds > 0 && point.DistanceTo(before) / seconds >= RunningAt ? 1 : 0;
            }

            if (arrived <= last && motion.TimeOf(last) - motion.TimeOf(arrived) >= SettledFor * pace)
            {
                var path = 0.0;

                for (var record = arrived + 1; record <= last; record++)
                {
                    path += new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity))
                        .DistanceTo(new Vec(motion.PlayerX(record - 1, entity), motion.PlayerY(record - 1, entity)));
                }

                tally.SettledMotion.Add(path / ((motion.TimeOf(last) - motion.TimeOf(arrived)) / pace));
                settled++;
            }

            if (FilmRoster.SideOf(entity) != attacking)
            {
                continue;
            }

            if (hold.Formation == FormationMode.Corner)
            {
                if (Math.Abs(point.X - goal.X) <= BoxDepth && Math.Abs(point.Y - goal.Y) <= BoxHalfWidth)
                {
                    cornerMarks.Add(HasMarker(context, roster, motion, last, entity, attacking));
                }
            }
            else if (point.DistanceTo(goal) <= FreeKickPack)
            {
                freeKickMarks.Add(HasMarker(context, roster, motion, last, entity, attacking));
            }
        }

        if (players > 0)
        {
            tally.SettledShare.Add((double)settled / players);

            if (hold.Formation == FormationMode.Corner)
            {
                tally.CornerGoalPack.Add(nearGoal);
                tally.CornerRunning.Add((double)running / players);
            }
            else
            {
                tally.FreeKickGoalPack.Add(nearGoal);
                (hold.Formation == FormationMode.FreeKickShot ? tally.FreeKickShotGoalPack : tally.FreeKickCrossGoalPack).Add(nearGoal);
                tally.FreeKickRunning.Add((double)running / players);
            }
        }
    }

    /// <summary>
    /// Counts who stands where as a corner is delivered (`replay-v6`): the box as for a cross, and how many of the taking
    /// side's players are held back beyond forty metres from the goal line, against a break.
    /// </summary>
    private static void MeasureCorner(
        FilmContext context,
        FilmRoster roster,
        FilmMotionResult motion,
        int record,
        MatchSide attacking,
        Histogram attackers,
        Histogram defenders,
        Histogram sixYard,
        Histogram guards)
    {
        var goal = FilmSpace.AttackedGoal(attacking);
        var held = 0;

        MeasureDelivery(context, roster, motion, record, attacking, attackers, defenders, sixYard);

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity)
                || FilmRoster.SideOf(entity) != attacking
                || context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            held += Math.Abs(motion.PlayerX(record, entity) - goal.X) >= GuardFrom ? 1 : 0;
        }

        guards.Add(held);
    }

    /// <summary>
    /// Measures where the keeper is as a strike at his goal arrives (`replay-v9`): how far he is from the goal mouth,
    /// and, for a shot that goes wide, how far from the ball. A keeper who stands where the ball is coming, off his
    /// goal, is the second.
    /// </summary>
    private static void MeasureKeeperAtShot(
        FilmRoster roster,
        FilmMotionResult motion,
        int index,
        FilmBeat shot,
        Histogram offGoal,
        Histogram atWideBall)
    {
        if (shot.Strike is StrikeResult.None or StrikeResult.Goal || shot.Opponent is not Guid keeperId || roster.EntityOf(keeperId) is not (var entity and >= 0))
        {
            return;
        }

        var last = motion.Spans[index].LastRecord;
        var keeper = new Vec(motion.PlayerX(last, entity), motion.PlayerY(last, entity));
        var goal = FilmSpace.OwnGoal(MatchInputV1.OpponentOf(shot.Side));
        var across = Math.Max(0.0, Math.Abs(keeper.Y - goal.Y) - GoalHalfWidth);

        offGoal.Add(new Vec(keeper.X - goal.X, across).Length);

        if (shot.Strike == StrikeResult.OffTarget)
        {
            atWideBall.Add(keeper.DistanceTo(new Vec(motion.BallX(last), motion.BallY(last))));
        }
    }

    /// <summary>
    /// Measures how a corner came about and how it is taken (`replay-v8`): how far the ball travels in the touch that
    /// puts it behind, and how far the taker is from it as he delivers it. A ball sent the length of the pitch to where
    /// the taker waits is the first; a taker still running up as the ball leaves the flag is the second.
    /// </summary>
    private static void MeasureCornerLead(
        FilmScriptResult script,
        FilmRoster roster,
        FilmMotionResult motion,
        int cross,
        Histogram outLeg,
        Histogram takerGap)
    {
        var beats = script.Beats;

        // The beats before the delivery are the hold at the flag, the ball being put down, and what put it behind.
        if (cross < 3 || !beats[cross - 1].IsHold || beats[cross - 2].Kind != BeatKind.Placement)
        {
            return;
        }

        if (beats[cross - 3].Kind == BeatKind.Clearance)
        {
            outLeg.Add(beats[cross - 3].Distance);
        }

        if (beats[cross - 1].Actor is Guid takerId && roster.EntityOf(takerId) is var taker and >= 0)
        {
            var record = motion.Spans[cross].FirstRecord;
            var ball = new Vec(motion.BallX(record), motion.BallY(record));

            takerGap.Add(new Vec(motion.PlayerX(record, taker), motion.PlayerY(record, taker)).DistanceTo(ball));
        }
    }

    /// <summary>
    /// Counts who is where as a free kick or a penalty is about to be taken (`replay-v6`): for a free kick how many
    /// defenders are inside the ten yards the rules keep clear, and for one struck at goal how many make up the wall;
    /// for a penalty how many players other than the taker and the keepers are inside the arc or the box.
    /// </summary>
    private static void MeasureDeadBall(
        FilmContext context,
        FilmRoster roster,
        FilmMotionResult motion,
        int record,
        FilmBeat hold,
        Histogram freeKickIntruders,
        Histogram freeKickWall,
        Histogram penaltyIntruders)
    {
        var attacking = hold.Side;
        var goal = FilmSpace.AttackedGoal(attacking);
        var spot = hold.To;
        var taker = hold.Actor is Guid actor ? roster.EntityOf(actor) : -1;
        int inside = 0, wall = 0;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity) || entity == taker || context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));
            var distance = point.DistanceTo(spot);

            if (hold.Formation == FormationMode.Penalty)
            {
                var inBox = Math.Abs(point.X - goal.X) <= BoxDepth && Math.Abs(point.Y - goal.Y) <= BoxHalfWidth;

                inside += distance < FreeKickClear || inBox ? 1 : 0;
            }
            else if (FilmRoster.SideOf(entity) != attacking)
            {
                inside += distance < FreeKickClear ? 1 : 0;
                wall += distance < FreeKickWallReach && distance >= FreeKickClear - 0.1 ? 1 : 0;
            }
        }

        if (hold.Formation == FormationMode.Penalty)
        {
            penaltyIntruders.Add(inside);
        }
        else
        {
            freeKickIntruders.Add(inside);

            if (hold.Formation == FormationMode.FreeKickShot)
            {
                freeKickWall.Add(wall);
            }
        }
    }

    /// <summary>
    /// Counts who stands in the box a cross is played into, at the moment it arrives (`replay-v6`): the attackers, the
    /// defenders, and how many are in the six-yard box.
    /// </summary>
    private static void MeasureDelivery(
        FilmContext context,
        FilmRoster roster,
        FilmMotionResult motion,
        int record,
        MatchSide attacking,
        Histogram attackers,
        Histogram defenders,
        Histogram sixYard)
    {
        var goal = FilmSpace.AttackedGoal(attacking);
        int attackerCount = 0, defenderCount = 0, sixCount = 0;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity) || context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var point = new Vec(motion.PlayerX(record, entity), motion.PlayerY(record, entity));
            var along = Math.Abs(point.X - goal.X);
            var across = Math.Abs(point.Y - goal.Y);
            var side = FilmRoster.SideOf(entity);

            if (along > BoxDepth || across > BoxHalfWidth)
            {
                continue;
            }

            sixCount += along <= SixYardDepth && across <= SixYardHalfWidth ? 1 : 0;

            if (side == attacking)
            {
                attackerCount++;
            }
            else
            {
                defenderCount++;
            }
        }

        attackers.Add(attackerCount);
        defenders.Add(defenderCount);
        sixYard.Add(sixCount);
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
    /// <summary>What is counted at every set piece that waits to be taken (`replay-v13`).</summary>
    private sealed class WaitingTally
    {
        public Average HoldSeconds { get; } = new();

        public Average SettledShare { get; } = new();

        public Average SettledMotion { get; } = new();

        public Average CornerRunning { get; } = new();

        public Average FreeKickRunning { get; } = new();

        public Histogram CornerGoalPack { get; } = new(1.0, FilmRoster.Size + 1);

        public Histogram FreeKickGoalPack { get; } = new(1.0, FilmRoster.Size + 1);

        public Histogram FreeKickShotGoalPack { get; } = new(1.0, FilmRoster.Size + 1);

        public Histogram FreeKickCrossGoalPack { get; } = new(1.0, FilmRoster.Size + 1);
    }

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

    /// <summary>How many players were looked at for a marker, and how many had one.</summary>
    private sealed class MarkCount
    {
        private int _marked;
        private int _total;

        public double Share => _total == 0 ? 0.0 : (double)_marked / _total;

        public void Add(bool marked)
        {
            _marked += marked ? 1 : 0;
            _total++;
        }
    }

    /// <summary>A running mean.</summary>
    private sealed class Average
    {
        private double _sum;

        public int Count { get; private set; }

        public double Sum => _sum;

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

                        if (beat.Actor is not null && beat.Actor == beat.Receiver && beat.Distance >= SelfPassDistance)
                        {
                            gaps.SelfPasses++;
                        }

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

        public int SelfPasses { get; set; }

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
