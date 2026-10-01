using System.Globalization;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns a simulated match into replayable highlights (master plan §9.2, §9.3, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// Selection is a pure function of the event stream. Every goal is always shown — a manager who scores and
/// cannot watch it has been given a worse product than one whose post-and-in was omitted — and the rest of
/// the budget goes to the best chances, measured by the goal probability the shot was resolved against.
/// </para>
/// <para>
/// The limits are a count, a payload budget, and the length of the condensed replay, and they are applied in
/// that order: a match with fifty chances is trimmed to the most important ones, trimmed again if the result
/// still exceeds what a phone should download, and trimmed once more if watching it would take longer than
/// the plan allows. Goals survive all three, so the requirement "all goals always remain" holds even for a
/// nine-goal game on a bad connection.
/// </para>
/// <para>
/// Since `replay-v2`, a highlight is a whole passage of play (Stage 3): its 22 players move through the
/// passage with the coordinated shape the play model gave them — a defensive block dropping off, a
/// full-back overlapping, a goalkeeper angling towards the ball — and the ball flies with the altitude the
/// flight model computed. Tracks are sampled at the delta-compression interval and stored only where the
/// movement changes, and each passage carries commentary tokens pinned to the milliseconds at which they
/// happen, so the ticker and the pitch tell the same story at the same moment.
/// </para>
/// <para>
/// Between consecutive highlights the director bridges the recycling passage, and a bounded schedule lays
/// every segment out on the playback clock so the client never has to guess when anything plays. The whole
/// replay condenses ninety minutes into the plan's five-to-ten-minute viewing experience.
/// </para>
/// </remarks>
public static class HighlightDirector
{
    /// <summary>The version label of this presentation.</summary>
    public const string Version = "replay-v2";

    /// <summary>The pitch coordinate scale the presentation speaks, on both axes.</summary>
    private const int Pitch = 10_000;

    /// <summary>Builds the match's presentation.</summary>
    /// <param name="input">The frozen snapshot, which supplies the eleven and their positions.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="options">How much is worth showing.</param>
    /// <param name="liveMetrics">
    /// The minute-by-minute condition and rating curve captured while the match was simulated, or null when
    /// the caller has no use for the match center's panels.
    /// </param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        HighlightOptionsV1? options = null,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);

        var settings = options ?? new HighlightOptionsV1();
        var names = Names(input);
        var homeLineup = MatchLineupBuilder.Build(input, result, MatchSide.Home);
        var awayLineup = MatchLineupBuilder.Build(input, result, MatchSide.Away);

        var candidates = result.Events
            .OrderBy(matchEvent => matchEvent.Sequence)
            .Where(matchEvent => IsWorthShowing(matchEvent, settings))
            .ToList();

        var selected = Trim(candidates, settings);
        var colours = Colours(input);

        var built = selected
            .Select(matchEvent => (Event: matchEvent, Highlight: BuildHighlight(input, matchEvent, names, colours, settings)))
            .ToList();

        TrimToPayload(built, settings, ReservedBytes(settings, homeLineup, awayLineup, liveMetrics));
        TrimToPlayback(built, settings);

        var highlights = built.Select(pair => pair.Highlight).ToList();
        var bridges = BuildBridges(input, built, PlanBridges(built, settings), settings);

        return new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Highlights = highlights,
            Bridges = bridges,
            Playback = Schedule(highlights, bridges),
            HomeLineup = homeLineup,
            AwayLineup = awayLineup,
            LiveMetrics = liveMetrics,
        };
    }

    /// <summary>
    /// The bytes the highlights may not spend, because the rest of the presentation already owns them
    /// (`match_presentation_payload_budget_kb`, ADR-0006).
    /// </summary>
    /// <remarks>
    /// The budget is the whole replay's, not the highlights': a client that downloads a match downloads the
    /// lineups and the live curve too. The bridges and the schedule are reserved at their worst case — every
    /// bridge at its own budget and every highlight with both a bridge and a highlight segment — so the
    /// estimate the trim fits against can only be larger than what is finally built.
    /// </remarks>
    private static int ReservedBytes(
        HighlightOptionsV1 settings,
        MatchLineupV1 homeLineup,
        MatchLineupV1 awayLineup,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics) =>
        homeLineup.EstimatedPayloadBytes
        + awayLineup.EstimatedPayloadBytes
        + ((liveMetrics?.Count ?? 0) * MatchPresentationV1.LiveMetricBytes)
        + settings.BridgeBudgetBytes
        + (settings.MaxHighlights * 2 * MatchPresentationV1.ScheduleSegmentBytes);

    /// <summary>
    /// Sheds the lowest-quality chances until the estimated payload fits its budget.
    /// </summary>
    /// <remarks>
    /// A count cap is not a size cap: twenty-four highlights are small if they are twenty-four tap-ins and
    /// large if they are twenty-four long-range efforts with a full set of tracks. The payload budget is what
    /// actually protects a phone on a train, so it is applied after the count and it yields the
    /// lowest-quality chances first — never a goal. What the rest of the presentation has already reserved
    /// comes off the budget first, so the match fits as a whole rather than the highlights fitting alone.
    /// </remarks>
    private static void TrimToPayload(
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built,
        HighlightOptionsV1 settings,
        int reservedBytes)
    {
        while (reservedBytes + built.Sum(pair => pair.Highlight.EstimatedPayloadBytes) > settings.PayloadBudgetBytes)
        {
            var least = LowestQuality(built);

            if (least.Highlight is null)
            {
                // Only goals are left. They stay, however large the payload, because a result a manager cannot
                // watch is a worse failure than a large download.
                return;
            }

            built.Remove(least);
        }
    }

    /// <summary>
    /// Sheds the lowest-quality chances until the condensed replay fits its length (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// The plan's viewing experience is five to ten minutes at normal speed. The bridge allowance is
    /// deliberately the worst case — every remaining gap bridged at its maximum length — so the schedule
    /// that is actually built can only be shorter than the estimate it was fitted against. Goals are never
    /// shed; a nine-goal match is the one replay allowed to run long.
    /// </remarks>
    private static void TrimToPlayback(
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built,
        HighlightOptionsV1 settings)
    {
        while (built.Count > 0)
        {
            var replay = built.Sum(pair => pair.Highlight.DurationMilliseconds)
                + (Math.Max(0, built.Count - 1) * settings.MaxBridgeDurationMilliseconds);

            if (replay <= settings.MaxPlaybackMilliseconds)
            {
                return;
            }

            var least = LowestQuality(built);

            if (least.Highlight is null)
            {
                return;
            }

            built.Remove(least);
        }
    }

    /// <summary>The least valuable chance still in the set, or a default pair when only goals remain.</summary>
    private static (EngineEventV1 Event, HighlightPresentationV1 Highlight) LowestQuality(
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built) =>
        built
            .Where(pair => !pair.Event.IsGoal)
            .OrderBy(pair => pair.Event.QualityBasisPoints ?? 0)
            .ThenByDescending(pair => pair.Event.Sequence)
            .FirstOrDefault();

    /// <summary>
    /// Lays the highlights and their bridges out on one contiguous playback clock (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// A bridge belongs immediately before the highlight it leads into, so the client plays a single ordered
    /// list rather than threading two collections together — and every highlight's event sequence is on the
    /// schedule, which is what lets a commentary line seek to the passage that narrates it.
    /// </remarks>
    private static List<PlaybackSegmentV1> Schedule(
        List<HighlightPresentationV1> highlights,
        List<BridgeV1> bridges)
    {
        var bridged = bridges.ToDictionary(bridge => bridge.AfterEventSequence);
        var schedule = new List<PlaybackSegmentV1>();
        var cursor = 0;

        foreach (var highlight in highlights)
        {
            if (bridged.TryGetValue(highlight.SourceEventSequence, out var bridge))
            {
                schedule.Add(new PlaybackSegmentV1("bridge", bridge.AfterEventSequence, cursor, bridge.DurationMilliseconds));
                cursor += bridge.DurationMilliseconds;
            }

            schedule.Add(new PlaybackSegmentV1("highlight", highlight.SourceEventSequence, cursor, highlight.DurationMilliseconds));
            cursor += highlight.DurationMilliseconds;
        }

        return schedule;
    }

    /// <summary>
    /// Decides how long each recycling passage should run (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// Each gap is worth time in proportion to the match time it covers — a minute of football is a second
    /// of condensed replay — and then the whole schedule is stretched evenly, up to the per-bridge maximum,
    /// until the replay reaches the plan's five-minute floor. Stretching the recycling rather than the
    /// passage keeps the highlights honest: a chance stays twenty seconds however quiet the match was, and
    /// the difference between a quiet match and a busy one is how much of the rest of it a viewer sees.
    /// </remarks>
    private static List<int> PlanBridges(
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built,
        HighlightOptionsV1 settings)
    {
        var durations = new List<int>();

        for (var index = 0; index < built.Count - 1; index++)
        {
            var gapMinutes = Math.Max(0, built[index + 1].Event.Minute - built[index].Event.Minute);

            durations.Add(int.Clamp(
                gapMinutes * 1_000,
                settings.MinBridgeDurationMilliseconds,
                settings.MaxBridgeDurationMilliseconds));
        }

        var total = built.Sum(pair => pair.Highlight.DurationMilliseconds) + durations.Sum();
        var deficit = settings.MinPlaybackMilliseconds - total;

        while (deficit > 0)
        {
            var grew = false;

            for (var index = 0; index < durations.Count && deficit > 0; index++)
            {
                if (durations[index] >= settings.MaxBridgeDurationMilliseconds)
                {
                    continue;
                }

                durations[index] += BridgeStretchStepMilliseconds;
                deficit -= BridgeStretchStepMilliseconds;
                grew = true;
            }

            if (!grew)
            {
                // Every bridge is at its maximum: this match simply does not hold five minutes of football.
                break;
            }
        }

        return durations;
    }

    /// <summary>
    /// Builds the recycling passages between consecutive highlights (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// A bridge is fitted between a highlight and its successor only when both exist in the trimmed set and
    /// the pair's payload still fits the bridge budget; it is dropped otherwise, and the cut it would have
    /// smoothed remains a cut. No bridge precedes the first highlight or follows the last.
    /// </remarks>
    private static List<BridgeV1> BuildBridges(
        MatchInputV1 input,
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built,
        List<int> durations,
        HighlightOptionsV1 settings)
    {
        if (built.Count < 2)
        {
            return [];
        }

        var bridges = new List<BridgeV1>();
        var budget = settings.BridgeBudgetBytes;

        for (var index = 0; index < built.Count - 1; index++)
        {
            var bridge = BuildBridge(input, built[index], built[index + 1], durations[index]);

            if (bridge.EstimatedPayloadBytes > budget)
            {
                continue;
            }

            budget -= bridge.EstimatedPayloadBytes;
            bridges.Add(bridge);
        }

        return bridges;
    }

    private static BridgeV1 BuildBridge(
        MatchInputV1 input,
        (EngineEventV1 Event, HighlightPresentationV1 Highlight) current,
        (EngineEventV1 Event, HighlightPresentationV1 Highlight) next,
        int duration)
    {
        var tracks = new List<HighlightTrackV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var isHome = side == MatchSide.Home;

            foreach (var slot in input.SideOf(side).Slots.OrderBy(slot => slot.SlotNumber))
            {
                var entityId = EntityId(isHome, slot.SlotNumber);

                // The bridge starts exactly where the previous passage left the player and ends exactly where
                // the next one picks them up, so a cut between highlights is a movement rather than a jump.
                var anchor = FormationAnchor(slot, isHome);
                var resting = new HighlightKeyframeV1(0, anchor.X, anchor.Y);
                var from = EdgeOf(current.Highlight, entityId, last: true) ?? resting;
                var to = EdgeOf(next.Highlight, entityId, last: false) ?? resting;

                // The recycling path bends through the shape: both teams return towards their formation as
                // the ball is worked back out, which is what a passage between chances looks like.
                var mid = new HighlightKeyframeV1(
                    duration / 2,
                    Clamp((from.X + to.X + anchor.X) / 3),
                    Clamp((from.Y + to.Y + anchor.Y) / 3));

                tracks.Add(new HighlightTrackV1(
                    entityId,
                    Track(
                    [
                        new HighlightKeyframeV1(0, from.X, from.Y, from.Z),
                        mid,
                        new HighlightKeyframeV1(duration, to.X, to.Y, to.Z),
                    ])));
            }
        }

        // The ball crosses with the recycling pass: out to the middle third, then onwards to where the next
        // passage begins. It comes down onto the ground between the two passages and is picked up in the air
        // only if the next passage starts with it there.
        var fromBall = EdgeOf(current.Highlight, BallEntityId, last: true) ?? BallAt(NormalizedX(current.Event), NormalizedY(current.Event));
        var toBall = EdgeOf(next.Highlight, BallEntityId, last: false) ?? BallAt(NormalizedX(next.Event), NormalizedY(next.Event));

        tracks.Add(new HighlightTrackV1(
            BallEntityId,
            Track(
            [
                new HighlightKeyframeV1(0, fromBall.X, fromBall.Y, fromBall.Z),
                new HighlightKeyframeV1(duration / 2, Clamp((fromBall.X + toBall.X) / 2), Clamp(((fromBall.Y + toBall.Y) / 2 + (Pitch / 2)) / 2)),
                new HighlightKeyframeV1(duration, toBall.X, toBall.Y, toBall.Z),
            ])));

        return new BridgeV1(
            next.Event.Sequence,
            duration,
            [.. tracks.OrderBy(track => track.EntityId, StringComparer.Ordinal)]);
    }

    /// <summary>The first or last keyframe an entity's track ends on, or null when it has none.</summary>
    private static HighlightKeyframeV1? EdgeOf(HighlightPresentationV1 highlight, string entityId, bool last)
    {
        var track = highlight.Tracks.FirstOrDefault(candidate => string.Equals(candidate.EntityId, entityId, StringComparison.Ordinal));

        if (track is null || track.Keyframes.Count == 0)
        {
            return null;
        }

        return last ? track.Keyframes[^1] : track.Keyframes[0];
    }

    /// <summary>
    /// Whether an event is worth a highlight at all, before any cap is applied.
    /// </summary>
    /// <remarks>
    /// Every goal and every penalty, then shots whose goal probability cleared the threshold, and the direct
    /// free kicks the spatial play model struck at goal (`replay-v2`). A penalty award or a free-kick award
    /// is not selected on its own: the goal or the miss that follows it a moment later is the thing worth
    /// watching, and showing both would put the same passage of play on screen twice.
    /// </remarks>
    private static bool IsWorthShowing(EngineEventV1 matchEvent, HighlightOptionsV1 options) => matchEvent.Type switch
    {
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => true,
        EngineEventType.FreeKickShot when options.IncludeFreeKicks => true,
        EngineEventType.Woodwork => options.IncludeWoodwork,
        EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget =>
            (matchEvent.QualityBasisPoints ?? 0) >= options.MinQualityForShotBasisPoints,
        _ => false,
    };

    /// <summary>Applies the count cap, keeping every goal and the best of the rest.</summary>
    private static List<EngineEventV1> Trim(List<EngineEventV1> candidates, HighlightOptionsV1 options)
    {
        var goals = candidates.Where(matchEvent => matchEvent.IsGoal).ToList();

        if (candidates.Count <= options.MaxHighlights && goals.Count <= options.MaxHighlights)
        {
            return candidates;
        }

        var others = candidates
            .Where(matchEvent => !matchEvent.IsGoal)
            .OrderByDescending(matchEvent => matchEvent.QualityBasisPoints ?? 0)
            .ThenBy(matchEvent => matchEvent.Sequence)
            .Take(Math.Max(0, options.MaxHighlights - goals.Count));

        // Back into event order, because a presentation that jumped around the match would be unwatchable
        // however good the chances in it were.
        return [.. goals.Concat(others).OrderBy(matchEvent => matchEvent.Sequence)];
    }

    private static HighlightPresentationV1 BuildHighlight(
        MatchInputV1 input,
        EngineEventV1 matchEvent,
        Dictionary<Guid, string> names,
        (string Home, string Away) colours,
        HighlightOptionsV1 options)
    {
        var duration = DurationFor(matchEvent, options);
        var attackingSide = matchEvent.Side;

        var entities = new List<HighlightEntityV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var isHome = side == MatchSide.Home;

            foreach (var slot in input.SideOf(side).Slots.OrderBy(slot => slot.SlotNumber))
            {
                var participant = input.SideOf(side).Squad
                    .First(candidate => candidate.ParticipantId == slot.ParticipantId);

                var anchor = FormationAnchor(slot, isHome);

                entities.Add(new HighlightEntityV1
                {
                    EntityId = EntityId(isHome, slot.SlotNumber),
                    IsBall = false,
                    Side = side,
                    ParticipantId = participant.ParticipantId,
                    ShirtNumber = participant.ShirtNumber,
                    Family = slot.Family,
                    X = anchor.X,
                    Y = anchor.Y,
                    Name = names.TryGetValue(participant.ParticipantId, out var name) ? name : null,
                    Position = PositionCode(slot),
                });
            }
        }

        // The ball is where the play model put it: the event carries its own pitch location (`engine-v3`),
        // expressed on the presentation's two 0..10_000 axes.
        var ballX = NormalizedX(matchEvent);
        var ballY = NormalizedY(matchEvent);

        entities.Add(new HighlightEntityV1
        {
            EntityId = BallEntityId,
            IsBall = true,
            X = ballX,
            Y = ballY,
        });

        // One track per entity, keyed by identity. An involved player's track replaces the stationary one rather
        // than joining it: two tracks for one entity would leave a renderer choosing between them, and the
        // player would appear to stand still and move at the same time.
        var tracks = new Dictionary<string, List<HighlightKeyframeV1>>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(entity => !entity.IsBall))
        {
            // Uninvolved players shift as their shape shifts: the block moves as one, towards the ball's
            // location, compressed slightly out of possession (`replay-v2` coordinated movement).
            tracks[entity.EntityId] = Track(ShapeShift(entity, attackingSide, ballX, ballY, duration));
        }

        // The ball's track is a flight from the passage's build-up point to the strike point: rising, arcing,
        // and landing where the event was located (`replay-v2` ball aerodynamics).
        tracks[BallEntityId] = BallTrack(matchEvent, attackingSide, duration, ballX, ballY);

        var strikeTime = StrikeTime(matchEvent.Type, duration);

        // The striker closes on the ball before striking it, which is what a shot looks like from the stands.
        var shooter = entities.FirstOrDefault(
            entity => entity.ParticipantId is not null && entity.ParticipantId == matchEvent.ParticipantId);

        if (shooter is not null)
        {
            tracks[shooter.EntityId] = ShooterTrack(shooter, matchEvent, duration, ballX, ballY, strikeTime);
        }

        var keeper = entities.FirstOrDefault(
            entity => entity.ParticipantId is not null
                && entity.ParticipantId == matchEvent.SecondaryParticipantId);

        if (keeper is not null)
        {
            tracks[keeper.EntityId] = KeeperTrack(keeper, duration, ballY, attackingSide);
        }

        return new HighlightPresentationV1
        {
            PresentationVersion = Version,
            SourceEventSequence = matchEvent.Sequence,
            Minute = matchEvent.Minute,
            StoppageMinute = matchEvent.StoppageMinute,
            DurationMilliseconds = duration,
            OutcomeCode = OutcomeCode(matchEvent.Type),
            Narration = Narration(matchEvent, names),
            HomeColour = colours.Home,
            AwayColour = colours.Away,
            Commentary = CommentaryTokenBuilder.BuildPassage(input, matchEvent, duration, strikeTime),
            Entities = [.. entities.OrderBy(entity => entity.EntityId, StringComparer.Ordinal)],
            Tracks =
            [
                .. tracks
                    .OrderBy(track => track.Key, StringComparer.Ordinal)
                    .Select(track => new HighlightTrackV1(track.Key, track.Value)),
            ],
        };
    }

    /// <summary>
    /// Samples a waypoint path at the delta-compression interval and keeps only its direction changes
    /// (`replay-v2`, Stage 3).
    /// </summary>
    private static List<HighlightKeyframeV1> Track(IReadOnlyList<HighlightKeyframeV1> waypoints) =>
        KeyframeCompressor.Compress(KeyframeCompressor.Sample(waypoints));

    /// <summary>
    /// Builds a coordinated shape-shift track for an uninvolved player (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 22 shift with the play, and they do not all shift the same way. The defensive line pushes up in
    /// possession and drops off without it; the midfield tracks the ball across the pitch with the runners;
    /// the front line pushes highest; a full-back on the ball's side overlaps beyond the man in front; and
    /// the goalkeeper stays near his goal but angles his position towards the ball's line. Every movement is
    /// a bounded share of the way, so no player can leave the shape the replay began in.
    /// </para>
    /// <para>
    /// A player never advances past the ball: if the play is behind the block, the line turns and drops
    /// instead, which is what makes a passage read as football rather than as a magnet.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<HighlightKeyframeV1> ShapeShift(
        HighlightEntityV1 entity,
        MatchSide attackingSide,
        int ballX,
        int ballY,
        int duration)
    {
        var isAttackingSide = entity.Side == attackingSide;
        var family = entity.Family ?? MatchPositionFamily.Midfield;

        // The direction this player attacks in. A block that drops off moves the other way.
        var attackDirection = entity.Side == MatchSide.Home ? 1 : -1;

        var (push, track, drop) = family switch
        {
            MatchPositionFamily.Goalkeeper => (120, 2, 60),
            MatchPositionFamily.Defence => (360, 4, 260),
            MatchPositionFamily.Midfield => (520, 3, 360),
            _ => (680, 3, 420),
        };

        // Whether the ball is in front of the player, seen from the goal the player attacks. A block whose
        // ball has gone past it does not keep running forward.
        var ballAhead = attackDirection > 0 ? ballX > entity.X : ballX < entity.X;
        var depthShift = (isAttackingSide && ballAhead ? push : -drop) * attackDirection;

        // A full-back level with the ball overlaps beyond his winger rather than holding the line.
        if (isAttackingSide
            && family == MatchPositionFamily.Defence
            && Math.Abs(entity.Y - ballY) < OverlapBand)
        {
            depthShift += OverlapPush * attackDirection;
        }

        var lateralShift = (ballY - entity.Y) / track;

        // Out of possession every player drifts a little towards the centre, which is how a block compresses.
        if (!isAttackingSide)
        {
            lateralShift += (SpatialPitch.GoalYCenter - (entity.Y * SpatialPitch.PitchWidth / Pitch)) / 12;
        }

        lateralShift = family == MatchPositionFamily.Goalkeeper
            ? int.Clamp(lateralShift, -KeeperAngle, KeeperAngle)
            : int.Clamp(lateralShift, -LateralLimit, LateralLimit);

        var shiftedX = Clamp(entity.X + depthShift);
        var shiftedY = Clamp(entity.Y + lateralShift);

        // A three-phase shift: move as the passage builds, then hold the block where the play has taken it.
        // The bridge after the highlight carries the block back to its shape, so the hold is not a snap back.
        return
        [
            new HighlightKeyframeV1(0, entity.X, entity.Y),
            new HighlightKeyframeV1((duration * 2) / 3, shiftedX, shiftedY),
            new HighlightKeyframeV1(duration, shiftedX, shiftedY),
        ];
    }

    /// <summary>
    /// Builds the ball's track: a build-up, a rise, and the strike (`replay-v2`).
    /// </summary>
    /// <remarks>
    /// The trajectory is deterministic: a ground pass from the halfway-line anchor into the passage's fight
    /// point, then — for a strike — an arcing climb and fall resolved with the flight model's own
    /// parabola, so a lofted cross and a driven shot read differently without the client owning physics.
    /// It is then compressed like every other track, so the arc costs the keyframes its curvature needs and
    /// not one per animation tick.
    /// </remarks>
    private static List<HighlightKeyframeV1> BallTrack(
        EngineEventV1 matchEvent,
        MatchSide attackingSide,
        int duration,
        int ballX,
        int ballY)
    {
        var isSetPiece = matchEvent.Type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
            or EngineEventType.FreeKickShot;

        var start = isSetPiece
            ? new HighlightKeyframeV1(0, ballX, ballY)
            : new HighlightKeyframeV1(0, BuildUpX(attackingSide), ballY);

        var peak = PeakFor(matchEvent, isSetPiece);
        var flightDuration = Math.Max(1, duration - (duration / 3));
        var steps = Math.Max(2, flightDuration / KeyframeCompressor.SampleIntervalMilliseconds);

        var flight = BallPhysics.Trajectory(
            new BallState(start.X, start.Y, 0),
            new BallState(ballX, ballY, 0),
            flightDuration,
            peak,
            steps);

        var waypoints = new List<HighlightKeyframeV1>(flight.Count + 2) { start };

        foreach (var (timeMs, position) in flight)
        {
            if (timeMs == 0)
            {
                continue;
            }

            waypoints.Add(new HighlightKeyframeV1(
                start.TimeMilliseconds + timeMs,
                Clamp(position.X),
                Clamp(position.Y),
                position.Z));
        }

        // The ball comes to rest where the event happened, which is where the next bridge picks it up.
        waypoints.Add(new HighlightKeyframeV1(duration, Clamp(ballX), Clamp(ballY)));

        return Track(waypoints);
    }

    /// <summary>The striker's run: start, close on the ball, strike, follow through (`replay-v2`).</summary>
    private static List<HighlightKeyframeV1> ShooterTrack(
        HighlightEntityV1 shooter,
        EngineEventV1 matchEvent,
        int duration,
        int ballX,
        int ballY,
        int strikeTime)
    {
        return Track(
        [
            new HighlightKeyframeV1(0, shooter.X, shooter.Y),
            new HighlightKeyframeV1(
                Math.Max(0, strikeTime - SetPieceRunInMs),
                Midpoint(shooter.X, ballX),
                Midpoint(shooter.Y, ballY),
                Action: "run"),
            new HighlightKeyframeV1(strikeTime, ballX, ballY, Action: ActionFor(matchEvent.Type)),
            new HighlightKeyframeV1(
                duration,
                FollowThrough(shooter.X, ballX),
                FollowThrough(shooter.Y, ballY),
                Action: matchEvent.IsGoal ? "celebrate" : null),
        ]);
    }

    /// <summary>The goalkeeper's angle and dive: hold, come for the ball's line, recover.</summary>
    private static List<HighlightKeyframeV1> KeeperTrack(
        HighlightEntityV1 keeper,
        int duration,
        int ballY,
        MatchSide attackingSide)
    {
        return Track(
        [
            new HighlightKeyframeV1(0, keeper.X, keeper.Y),
            new HighlightKeyframeV1(duration / 2, KeeperDiveX(keeper.X, attackingSide), Midpoint(keeper.Y, ballY), Action: "save"),
            new HighlightKeyframeV1(duration, keeper.X, keeper.Y),
        ]);
    }

    private static int PeakFor(EngineEventV1 matchEvent, bool isSetPiece)
    {
        if (isSetPiece)
        {
            return 35;
        }

        return matchEvent.Type switch
        {
            EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.Woodwork => 25,
            EngineEventType.ShotBlocked => 12,
            _ => 8,
        };
    }

    /// <summary>When in a passage the ball is struck (`replay-v2`).</summary>
    private static int StrikeTime(EngineEventType type, int duration) =>
        type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed or EngineEventType.FreeKickShot
            ? 0
            : (int)(duration * 0.55);

    /// <summary>The X the build-up starts from: the attacking third's edge, on the attacking side's own axis.</summary>
    private static int BuildUpX(MatchSide attackingSide) =>
        attackingSide == MatchSide.Home ? Pitch / 3 : Pitch - (Pitch / 3);

    /// <summary>The event's X on the presentation's axis.</summary>
    private static int NormalizedX(EngineEventV1 matchEvent) => Clamp(matchEvent.X ?? (Pitch / 2));

    /// <summary>
    /// The event's Y on the presentation's axis.
    /// </summary>
    /// <remarks>
    /// The play model speaks a 10_000 × 7_000 pitch; the presentation speaks 0..10_000 on both axes, which is
    /// what the client scales onto a canvas of any shape. The conversion happens once, here, so no track can
    /// mix the two spaces.
    /// </remarks>
    private static int NormalizedY(EngineEventV1 matchEvent) =>
        NormalizeY(matchEvent.Y ?? (SpatialPitch.PitchWidth / 2));

    private static int NormalizeY(int pitchY) => Clamp((pitchY * Pitch) / SpatialPitch.PitchWidth);

    /// <summary>The formation anchor for a slot, on the presentation's axes, at the scale the resolver speaks.</summary>
    private static SpatialPoint FormationAnchor(MatchSlotV1 slot, bool isHome)
    {
        // The resolver maps the tactics board's own axes onto the 10_000 x 7_000 pitch and mirrors for the
        // away side, which is the same mapping the simulation used when it located the play.
        var anchor = TacticalFormationResolver.ResolvePosition(
            slot,
            isHome,
            hasPossession: false,
            new SpatialPoint(Pitch / 2, SpatialPitch.PitchWidth / 2),
            new MatchInstructionsV1(),
            EngineRulesV2.Default);

        return new SpatialPoint(anchor.X, NormalizeY(anchor.Y));
    }

    private const string BallEntityId = "ball";
    private const int SetPieceRunInMs = 700;
    private const int BridgeStretchStepMilliseconds = 500;
    private const int OverlapPush = 320;
    private const int OverlapBand = 1_600;
    private const int LateralLimit = 900;
    private const int KeeperAngle = 1_200;

    private static string EntityId(bool isHome, int slotNumber) => $"{(isHome ? "H" : "A")}{slotNumber}";

    private static int Clamp(int value) => int.Clamp(value, 0, Pitch);

    private static int Midpoint(int from, int to) => (from + to) / 2;

    private static int FollowThrough(int from, int to) => Clamp(from + ((to - from) / 4));

    private static HighlightKeyframeV1 BallAt(int x, int y) => new(0, x, y);

    private static int KeeperDiveX(int keeperX, MatchSide attackingSide)
    {
        // The keeper steps towards the ball's line, one step, and recovers.
        return attackingSide == MatchSide.Home
            ? Clamp(keeperX - 300)
            : Clamp(keeperX + 300);
    }

    private static string ActionFor(EngineEventType type) => type switch
    {
        EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => "penalty",
        EngineEventType.FreeKickShot => "free_kick",
        EngineEventType.Goal => "shot",
        EngineEventType.ShotSaved => "shot",
        EngineEventType.ShotBlocked => "shot",
        EngineEventType.ShotOffTarget => "shot",
        EngineEventType.Woodwork => "shot",
        _ => "pass",
    };

    private static int DurationFor(EngineEventV1 matchEvent, HighlightOptionsV1 options)
    {
        // A goal is worth the longest look. Everything else sits inside the same band so the player's speed
        // control means the same thing on every highlight.
        var length = matchEvent.IsGoal
            ? options.MaxDurationMilliseconds
            : options.MinDurationMilliseconds + ((options.MaxDurationMilliseconds - options.MinDurationMilliseconds) * 2 / 3);

        return int.Clamp(length, options.MinDurationMilliseconds, options.MaxDurationMilliseconds);
    }

    private static string OutcomeCode(EngineEventType type) => type switch
    {
        EngineEventType.Goal => "goal",
        EngineEventType.PenaltyGoal => "penalty_goal",
        EngineEventType.PenaltyMissed => "penalty_missed",
        EngineEventType.FreeKickShot => "free_kick_shot",
        EngineEventType.Woodwork => "woodwork",
        EngineEventType.ShotSaved => "saved",
        EngineEventType.ShotBlocked => "blocked",
        EngineEventType.ShotOffTarget => "off_target",
        _ => "chance",
    };

    private static string Narration(EngineEventV1 matchEvent, Dictionary<Guid, string> names)
    {
        var player = matchEvent.ParticipantId is Guid id && names.TryGetValue(id, out var name)
            ? name
            : "the attacker";

        var clock = matchEvent.StoppageMinute > 0
            ? $"{matchEvent.Minute}+{matchEvent.StoppageMinute}"
            : matchEvent.Minute.ToString(CultureInfo.InvariantCulture);

        var outcome = matchEvent.Type switch
        {
            EngineEventType.Goal => "Goal",
            EngineEventType.PenaltyGoal => "Penalty scored",
            EngineEventType.PenaltyMissed => "Penalty missed",
            EngineEventType.FreeKickShot => "Free kick struck",
            EngineEventType.Woodwork => "Shot against the woodwork",
            EngineEventType.ShotSaved => "Shot saved",
            EngineEventType.ShotBlocked => "Shot blocked",
            EngineEventType.ShotOffTarget => "Shot off target",
            _ => "Chance",
        };

        return $"{outcome} — {player}, {clock}.";
    }

    private static string PositionCode(MatchSlotV1 slot) => slot.Family switch
    {
        MatchPositionFamily.Goalkeeper => "GK",
        MatchPositionFamily.Defence => "DF",
        MatchPositionFamily.Midfield => "MF",
        MatchPositionFamily.Attack => "FW",
        _ => string.Empty,
    };

    private static Dictionary<Guid, string> Names(MatchInputV1 input)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var side in new[] { input.Home, input.Away })
        {
            foreach (var participant in side.Squad)
            {
                names[participant.ParticipantId] = participant.DisplayName;
            }
        }

        return names;
    }

    /// <summary>
    /// The two sides' colours, from the palette the lineups draw the same clubs in.
    /// </summary>
    private static (string Home, string Away) Colours(MatchInputV1 input) =>
        (ClubPalette.PrimaryOf(input.Home.ClubId), ClubPalette.PrimaryOf(input.Away.ClubId));
}
