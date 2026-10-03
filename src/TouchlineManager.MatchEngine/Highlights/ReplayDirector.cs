using System.Globalization;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns a simulated match and its recorded passages into one continuous condensed film and a highlights
/// reel over the same data (`replay-v3`, master plan §9.2–§9.3, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// A possession in `engine-v4` is a real passage of play: the ball starts where the last one left it, moves
/// through a chain of touches, and ends at an outcome-appropriate point. This director merges those recorded
/// possessions into film passages of roughly equal playback length — splitting at substitutions, half-time,
/// and bookings of personnel so a passage's eleven is stable — and lays them on a single playback clock. The
/// time warp compresses ninety minutes into a ten-minute film, weighted so chances are readable and dull
/// spells fly by.
/// </para>
/// <para>
/// The ball's track is the recorded path, and the eleven's tracks are the shape the tactical resolver gives
/// them over that real path, overwritten where a recorded touch names them (the carrier, the passer, the
/// shooter, the keeper). Boundary frames are copied exactly between consecutive passages, so the film joins
/// seamlessly rather than cutting. The reel is a server-side playlist of chance clips over the same passages,
/// so a viewer can watch the whole match or just the chances that decided it.
/// </para>
/// </remarks>
public static class ReplayDirector
{
    /// <summary>The version label of this presentation.</summary>
    public const string Version = "replay-v3";

    /// <summary>The pitch coordinate scale the presentation speaks, on both axes.</summary>
    private const int Pitch = 10_000;

    /// <summary>How far a deterministic per-player jitter may move an uninvolved token, in normalized units.</summary>
    private const int JitterMagnitude = 60;

    /// <summary>The pitch coordinate scale a pitch Y is normalized from.</summary>
    private const int PitchWidth = SpatialPitch.PitchWidth;

    /// <summary>The ball's entity identifier.</summary>
    private const string BallEntityId = "ball";

    /// <summary>Builds the match's presentation from the passages recorded while it was simulated.</summary>
    /// <param name="input">The frozen snapshot, which supplies the eleven and their positions.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="passages">
    /// The recorded possessions, in order. The film is built by merging them; a caller that recorded nothing
    /// gets an empty presentation rather than an invented one.
    /// </param>
    /// <param name="options">How much is worth showing, and the film's pacing.</param>
    /// <param name="liveMetrics">The minute-by-minute condition and rating curve, or null.</param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        IReadOnlyList<MatchPassageV1> passages,
        HighlightOptionsV1? options = null,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(passages);

        var settings = options ?? new HighlightOptionsV1();
        var rules = EngineRulesV2.Default;
        var names = Names(input);
        var homeLineup = MatchLineupBuilder.Build(input, result, MatchSide.Home);
        var awayLineup = MatchLineupBuilder.Build(input, result, MatchSide.Away);
        var colours = Colours(input);

        if (passages.Count == 0)
        {
            return new MatchPresentationV1
            {
                PresentationVersion = Version,
                EngineVersion = result.EngineVersion,
                HomeGoals = result.HomeGoals,
                AwayGoals = result.AwayGoals,
                Passages = [],
                Reel = [],
                Playback = [],
                HomeLineup = homeLineup,
                AwayLineup = awayLineup,
                LiveMetrics = liveMetrics,
            };
        }

        // Match seconds restart at the second half since engine-v5, so the director reads them on a continuous
        // clock — the second half's seconds carry on from where the first half's stoppage left off — which is
        // what the film, its personnel changes, and the viewer's clock have always assumed.
        var secondHalfShift = SecondHalfShiftSeconds(passages, rules);

        passages = OnContinuousClock(passages, secondHalfShift);

        var bySequence = result.Events.ToDictionary(matchEvent => matchEvent.Sequence);
        var changes = PersonnelChanges(result.Events, rules, secondHalfShift);
        var totalMatchSeconds = Math.Max(1, passages[^1].EndClockSeconds);

        var targetFilmMilliseconds = int.Clamp(
            (int)((long)totalMatchSeconds * 1_000 / Math.Max(1, settings.FilmMatchSecondsPerFilmSecond)),
            settings.MinFilmMilliseconds,
            settings.MaxFilmMilliseconds);

        var groups = Merge(passages, bySequence, rules, settings, targetFilmMilliseconds, changes);
        var durations = Allocate(groups, targetFilmMilliseconds, settings);

        var authored = new List<AuthoredPassage>(groups.Count);
        Dictionary<string, HighlightKeyframeV1>? previousEnds = null;

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var occupants = OccupantsAt(input, changes, group.StartSecond);
            var passage = AuthorPassage(
                input,
                group,
                bySequence,
                names,
                colours,
                rules,
                occupants,
                durations[index],
                previousEnds,
                index,
                secondHalfShift);

            authored.Add(passage);
            previousEnds = passage.Ends;
        }

        var playback = Schedule(authored, durations);
        var reel = ReelBuilder.Build(Candidates(authored, bySequence, playback, settings), settings);

        var built = new List<PassageV1>(authored.Count);

        for (var rung = 0; rung < settings.TrackTolerances.Count; rung++)
        {
            var tolerance = settings.TrackTolerances[rung];
            var interval = settings.TrackSampleIntervals[Math.Min(rung, settings.TrackSampleIntervals.Count - 1)];

            built = [.. authored.Select(passage => passage.Compress(tolerance, interval))];

            if (Estimate(built, reel, playback, homeLineup, awayLineup, liveMetrics) <= settings.PayloadBudgetBytes)
            {
                break;
            }
        }

        return new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Passages = built,
            Reel = reel,
            Playback = playback,
            HomeLineup = homeLineup,
            AwayLineup = awayLineup,
            LiveMetrics = liveMetrics,
        };
    }

    /// <summary>
    /// Merges recorded possessions into film passages of roughly equal playback weight.
    /// </summary>
    /// <remarks>
    /// Passages are split at substitutions, half-time, and bookings of personnel so a passage's eleven is
    /// stable, and otherwise accumulated until the group is worth about one passage of film. That is what
    /// takes a few hundred possessions down to the fifty-odd passages the per-passage entity overhead and the
    /// ten-minute film both want.
    /// </remarks>
    private static List<FilmPassage> Merge(
        IReadOnlyList<MatchPassageV1> passages,
        Dictionary<int, EngineEventV1> bySequence,
        EngineRulesV2 rules,
        HighlightOptionsV1 settings,
        int targetFilmMilliseconds,
        List<(int Second, MatchSide Side, Guid Off, Guid? On)> changes)
    {
        var weights = passages.ToDictionary(passage => passage.Ordinal, passage => WeightOf(passage, bySequence, rules));
        var totalWeight = Math.Max(1, weights.Values.Sum());

        var desired = (int)Math.Clamp(
            targetFilmMilliseconds / Math.Max(1, settings.TargetPassageMilliseconds),
            1,
            settings.MaxPassages);
        desired = Math.Min(desired, passages.Count);

        var perPassage = (double)totalWeight / desired;
        var changeSeconds = changes.Select(change => change.Second).Distinct().OrderBy(second => second).ToList();

        var groups = new List<FilmPassage>();
        var current = new List<MatchPassageV1>();
        long currentWeight = 0;

        foreach (var possession in passages)
        {
            if (current.Count > 0)
            {
                var last = current[^1];
                var personnel = ChangeBetween(changeSeconds, last.EndClockSeconds, possession.StartClockSeconds);

                // A film passage never straddles half-time: the period says so, where the clock cannot.
                var crossedHalf = last.Period != possession.Period;

                if (personnel || crossedHalf || currentWeight >= perPassage)
                {
                    groups.Add(Flush(current, currentWeight));
                    current = [];
                    currentWeight = 0;
                }
            }

            current.Add(possession);
            currentWeight += weights[possession.Ordinal];
        }

        if (current.Count > 0)
        {
            groups.Add(Flush(current, currentWeight));
        }

        return groups;
    }

    private static FilmPassage Flush(List<MatchPassageV1> possessions, long weight) => new(
        [.. possessions],
        possessions[0].StartClockSeconds,
        possessions[^1].EndClockSeconds,
        possessions[0].Side,
        weight,
        possessions[0].Period);

    /// <summary>
    /// Gets how long the first half's stoppage ran past regulation, which is how far the second half's seconds
    /// are moved on to sit after it on the continuous clock.
    /// </summary>
    private static int SecondHalfShiftSeconds(IReadOnlyList<MatchPassageV1> passages, EngineRulesV2 rules)
    {
        var firstHalfEnd = 0;

        foreach (var passage in passages)
        {
            if (passage.Period == 1)
            {
                firstHalfEnd = Math.Max(firstHalfEnd, passage.EndClockSeconds);
            }
        }

        return Math.Max(0, firstHalfEnd - (rules.HalfTimeMinute * rules.SecondsPerMinute));
    }

    /// <summary>Moves the second half's possessions on to the continuous clock.</summary>
    private static IReadOnlyList<MatchPassageV1> OnContinuousClock(IReadOnlyList<MatchPassageV1> passages, int secondHalfShift) =>
        secondHalfShift == 0
            ? passages
            : [.. passages.Select(passage => passage.Period == 2
                ? passage with
                {
                    StartClockSeconds = passage.StartClockSeconds + secondHalfShift,
                    EndClockSeconds = passage.EndClockSeconds + secondHalfShift,
                }
                : passage)];

    /// <summary>How much film time each passage is worth, normalised to the target with a floor.</summary>
    private static List<int> Allocate(List<FilmPassage> groups, int targetFilmMilliseconds, HighlightOptionsV1 settings)
    {
        var totalWeight = Math.Max(1, groups.Sum(group => group.Weight));
        var durations = groups
            .Select(group => Math.Max(
                settings.MinPassageMilliseconds,
                (int)((long)targetFilmMilliseconds * group.Weight / totalWeight)))
            .ToList();

        // The floor can push the total past the ceiling on a match with very many short passages, so any
        // excess is taken back from the passages that are still above the floor.
        var excess = durations.Sum() - settings.MaxFilmMilliseconds;
        var index = 0;

        while (excess > 0 && index < durations.Count)
        {
            var room = durations[index] - settings.MinPassageMilliseconds;

            if (room > 0)
            {
                var take = Math.Min(room, excess);
                durations[index] -= take;
                excess -= take;
            }

            index++;
        }

        return durations;
    }

    /// <summary>Lays the passages out on one contiguous film clock, one segment per passage.</summary>
    private static List<PlaybackSegmentV1> Schedule(IReadOnlyList<AuthoredPassage> passages, List<int> durations)
    {
        var schedule = new List<PlaybackSegmentV1>(passages.Count);
        var cursor = 0;

        for (var index = 0; index < passages.Count; index++)
        {
            schedule.Add(new PlaybackSegmentV1("passage", passages[index].SourceEventSequence, cursor, durations[index]));
            cursor += durations[index];
        }

        return schedule;
    }

    /// <summary>The chance candidates the reel is built from, located on the film clock.</summary>
    private static List<ReelCandidateV1> Candidates(
        IReadOnlyList<AuthoredPassage> passages,
        Dictionary<int, EngineEventV1> bySequence,
        List<PlaybackSegmentV1> playback,
        HighlightOptionsV1 settings)
    {
        var candidates = new List<ReelCandidateV1>();

        for (var index = 0; index < passages.Count; index++)
        {
            var passage = passages[index];

            foreach (var sequence in passage.EventSequences)
            {
                if (!bySequence.TryGetValue(sequence, out var matchEvent) || !IsWorthShowing(matchEvent, settings))
                {
                    continue;
                }

                candidates.Add(new ReelCandidateV1(
                    sequence,
                    OutcomeCode(matchEvent.Type),
                    matchEvent.Minute,
                    matchEvent.StoppageMinute,
                    playback[index].StartMilliseconds,
                    playback[index].StartMilliseconds + playback[index].DurationMilliseconds,
                    matchEvent.QualityBasisPoints ?? 0,
                    matchEvent.IsGoal));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Whether an event is worth a reel clip, before any cap is applied.
    /// </summary>
    /// <remarks>
    /// Every goal and penalty, then shots whose goal probability cleared the threshold, and the direct free
    /// kicks the spatial play model struck at goal. A penalty award or a free-kick award is not selected on its
    /// own: the goal or the miss that follows it a moment later is the thing worth watching.
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

    private static long WeightOf(
        MatchPassageV1 passage,
        Dictionary<int, EngineEventV1> bySequence,
        EngineRulesV2 rules)
    {
        var events = passage.EventSequences
            .Where(bySequence.ContainsKey)
            .Select(sequence => bySequence[sequence])
            .ToList();

        if (events.Any(matchEvent => matchEvent.IsGoal))
        {
            return 200;
        }

        if (events.Any(matchEvent => IsShot(matchEvent.Type)))
        {
            return 150;
        }

        return passage.AttackingEndX >= rules.ShotFinalThirdXMinBasisPoints ? 125 : 80;
    }

    private static bool IsShot(EngineEventType type) => type is
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
        or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget
        or EngineEventType.Woodwork or EngineEventType.FreeKickShot;

    /// <summary>Authorises one film passage: its eleven, the ball path, the players' tracks, and the beats.</summary>
    private static AuthoredPassage AuthorPassage(
        MatchInputV1 input,
        FilmPassage group,
        Dictionary<int, EngineEventV1> bySequence,
        Dictionary<Guid, string> names,
        (string Home, string Away) colours,
        EngineRulesV2 rules,
        Dictionary<MatchSide, Dictionary<int, Guid>> occupants,
        int durationMilliseconds,
        Dictionary<string, HighlightKeyframeV1>? previousEnds,
        int ordinal,
        int secondHalfShift)
    {
        var windows = Windows(group, durationMilliseconds);
        var events = group.Possessions
            .SelectMany(possession => possession.EventSequences)
            .Where(bySequence.ContainsKey)
            .Select(sequence => bySequence[sequence])
            .OrderBy(matchEvent => matchEvent.Sequence)
            .ToList();

        var principal = events.FirstOrDefault(matchEvent => matchEvent.IsGoal)
            ?? events.FirstOrDefault(matchEvent => IsShot(matchEvent.Type))
            ?? events.FirstOrDefault();

        var (minute, stoppage) = ClockOf(group, events, rules, secondHalfShift);
        var homeColour = colours.Home;
        var awayColour = colours.Away;

        var entities = new List<HighlightEntityV1>();
        var entitiesBySide = new Dictionary<MatchSide, Dictionary<Guid, int>>();
        var slotBySide = new Dictionary<MatchSide, Dictionary<int, MatchSlotV1>>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var frozen = input.SideOf(side);
            var isHome = side == MatchSide.Home;
            var squad = frozen.Squad.ToDictionary(participant => participant.ParticipantId);
            var bySlot = frozen.Slots.ToDictionary(slot => slot.SlotNumber);
            var slotOfParticipant = new Dictionary<Guid, int>();
            slotBySide[side] = bySlot;

            foreach (var slot in frozen.Slots.OrderBy(slot => slot.SlotNumber))
            {
                if (!occupants[side].TryGetValue(slot.SlotNumber, out var participantId)
                    || !squad.TryGetValue(participantId, out var participant))
                {
                    continue;
                }

                var anchor = Anchor(slot, isHome, group.PrimarySide == side, 0, windows, rules, 0, 0);

                entities.Add(new HighlightEntityV1
                {
                    EntityId = EntityId(isHome, slot.SlotNumber),
                    IsBall = false,
                    Side = side,
                    ParticipantId = participantId,
                    ShirtNumber = participant.ShirtNumber,
                    Family = slot.Family,
                    X = anchor.X,
                    Y = anchor.Y,
                    Name = names.TryGetValue(participantId, out var name) ? name : null,
                    Position = PositionCode(slot),
                });

                slotOfParticipant[participantId] = slot.SlotNumber;
            }

            entitiesBySide[side] = slotOfParticipant;
        }

        var ballStart = BallAt(0, windows);
        entities.Add(new HighlightEntityV1
        {
            EntityId = BallEntityId,
            IsBall = true,
            X = NormalizeX(ballStart.X),
            Y = NormalizeY(ballStart.Y),
        });

        var ballWaypoints = BallWaypoints(windows, durationMilliseconds, previousEnds);
        var waypoints = new Dictionary<string, IReadOnlyList<HighlightKeyframeV1>>(StringComparer.Ordinal)
        {
            [BallEntityId] = ballWaypoints,
        };

        foreach (var entity in entities.Where(entity => !entity.IsBall))
        {
            var side = entity.Side!.Value;
            var isHome = side == MatchSide.Home;
            var slot = slotBySide[side][int.Parse(entity.EntityId[1..], CultureInfo.InvariantCulture)];
            var touches = Touches(windows, entity.ParticipantId!.Value, durationMilliseconds);

            waypoints[entity.EntityId] = PlayerWaypoints(
                slot,
                isHome,
                group,
                windows,
                durationMilliseconds,
                touches,
                previousEnds,
                ordinal,
                entity.EntityId,
                rules);
        }

        var beats = Beats(group, windows, events, entitiesBySide, durationMilliseconds, ordinal, rules, secondHalfShift);

        return new AuthoredPassage(
            Version,
            principal?.Sequence ?? 0,
            minute,
            stoppage,
            group.StartSecond,
            group.EndSecond,
            durationMilliseconds,
            principal is null ? "play" : OutcomeCode(principal.Type),
            Narration(principal, names, minute, stoppage),
            homeColour,
            awayColour,
            [.. events.Select(matchEvent => matchEvent.Sequence)],
            [.. entities.OrderBy(entity => entity.EntityId, StringComparer.Ordinal)],
            waypoints,
            CommentaryTokenBuilder.BuildPassageCommentary(input, beats, durationMilliseconds));
    }

    /// <summary>The film windows, in the passage, of each recorded possession it merged.</summary>
    private static List<(MatchPassageV1 Possession, int Start, int Length)> Windows(FilmPassage group, int duration)
    {
        var total = Math.Max(1, group.EndSecond - group.StartSecond);
        var windows = new List<(MatchPassageV1, int, int)>(group.Possessions.Count);
        var elapsed = 0;

        foreach (var possession in group.Possessions)
        {
            var span = Math.Max(1, possession.EndClockSeconds - possession.StartClockSeconds);
            var start = (int)((long)elapsed * duration / total);
            var length = Math.Max(1, (int)((long)span * duration / total));

            windows.Add((possession, start, length));
            elapsed += span;
        }

        return windows;
    }

    /// <summary>Builds the ball's track from the recorded waypoints, at their film times.</summary>
    private static List<HighlightKeyframeV1> BallWaypoints(
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows,
        int duration,
        Dictionary<string, HighlightKeyframeV1>? previousEnds)
    {
        var waypoints = new List<HighlightKeyframeV1>();

        foreach (var (possession, start, length) in windows)
        {
            foreach (var waypoint in possession.Waypoints)
            {
                var time = start + (int)((long)waypoint.FractionBasisPoints * length / EngineRulesV2.Certain);

                waypoints.Add(new HighlightKeyframeV1(
                    time,
                    Clamp(NormalizeX(waypoint.X)),
                    Clamp(NormalizeY(waypoint.Y)),
                    int.Clamp(waypoint.Z, 0, 100),
                    Action: waypoint.Kind.Code()));
            }
        }

        if (waypoints.Count == 0)
        {
            waypoints.Add(new HighlightKeyframeV1(0, Pitch / 2, Pitch / 2));
        }

        if (previousEnds is not null && previousEnds.TryGetValue(BallEntityId, out var previous))
        {
            waypoints[0] = new HighlightKeyframeV1(0, previous.X, previous.Y, previous.Z, Action: waypoints[0].Action);
        }

        return Order(waypoints, duration, (Pitch / 2, Pitch / 2));
    }

    /// <summary>Builds one player's track: the shape over the ball's real path, plus their recorded touches.</summary>
    private static List<HighlightKeyframeV1> PlayerWaypoints(
        MatchSlotV1 slot,
        bool isHome,
        FilmPassage group,
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows,
        int duration,
        IReadOnlyList<HighlightKeyframeV1> touches,
        Dictionary<string, HighlightKeyframeV1>? previousEnds,
        int ordinal,
        string entityId,
        EngineRulesV2 rules)
    {
        var hasPossession = group.PrimarySide == (isHome ? MatchSide.Home : MatchSide.Away);
        var (jitterX, jitterY) = Jitter($"{ordinal}:{entityId}");

        var waypoints = new List<HighlightKeyframeV1>
        {
            Anchor(slot, isHome, hasPossession, 0, windows, rules, jitterX, jitterY),
            Anchor(slot, isHome, hasPossession, duration / 2, windows, rules, jitterX, jitterY),
            Anchor(slot, isHome, hasPossession, duration, windows, rules, jitterX, jitterY),
        };

        waypoints.AddRange(touches);

        if (previousEnds is not null && previousEnds.TryGetValue(entityId, out var previous))
        {
            waypoints[0] = new HighlightKeyframeV1(0, previous.X, previous.Y, previous.Z, Action: waypoints[0].Action);
        }

        return Order(waypoints, duration, (waypoints[0].X, waypoints[0].Y));
    }

    /// <summary>Resolves a slot's presentation position at one film time, over the real ball path.</summary>
    private static HighlightKeyframeV1 Anchor(
        MatchSlotV1 slot,
        bool isHome,
        bool hasPossession,
        int time,
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows,
        EngineRulesV2 rules,
        int jitterX,
        int jitterY)
    {
        var ball = BallAt(time, windows);
        var resolved = TacticalFormationResolver.ResolvePosition(
            slot,
            isHome,
            hasPossession,
            new SpatialPoint(ball.X, ball.Y),
            new MatchInstructionsV1(),
            rules);

        return new HighlightKeyframeV1(
            time,
            Clamp(NormalizeX(resolved.X) + jitterX),
            Clamp(NormalizeY(resolved.Y) + jitterY));
    }

    /// <summary>The recorded touches of one player, mapped to their film times.</summary>
    private static List<HighlightKeyframeV1> Touches(
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows,
        Guid participantId,
        int duration)
    {
        var touches = new List<HighlightKeyframeV1>();

        foreach (var (possession, start, length) in windows)
        {
            foreach (var touch in possession.Touches)
            {
                if (touch.ParticipantId != participantId)
                {
                    continue;
                }

                var time = start + (int)((long)touch.FractionBasisPoints * length / EngineRulesV2.Certain);

                touches.Add(new HighlightKeyframeV1(
                    Math.Min(time, duration),
                    Clamp(NormalizeX(touch.X)),
                    Clamp(NormalizeY(touch.Y)),
                    int.Clamp(touch.Z, 0, 100),
                    Action: touch.Action.Code()));
            }
        }

        return touches;
    }

    /// <summary>The narrated beats of one passage: its build-up touches and its events, on the film clock.</summary>
    private static List<PassageBeatV1> Beats(
        FilmPassage group,
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows,
        List<EngineEventV1> events,
        Dictionary<MatchSide, Dictionary<Guid, int>> entitiesBySide,
        int duration,
        int ordinal,
        EngineRulesV2 rules,
        int secondHalfShift)
    {
        var beats = new List<PassageBeatV1>();
        var sideOfParticipant = new Dictionary<Guid, MatchSide>();

        foreach (var (side, slots) in entitiesBySide)
        {
            foreach (var participant in slots.Keys)
            {
                sideOfParticipant[participant] = side;
            }
        }

        var seed = ordinal * 100;

        foreach (var (possession, start, length) in windows)
        {
            foreach (var touch in possession.Touches)
            {
                var kind = CommentaryTokenBuilder.KindOf(touch.Action);

                if (kind is null || !sideOfParticipant.TryGetValue(touch.ParticipantId, out var side))
                {
                    continue;
                }

                var time = start + (int)((long)touch.FractionBasisPoints * length / EngineRulesV2.Certain);
                beats.Add(new PassageBeatV1(Math.Min(time, duration), side, touch.ParticipantId, kind.Value, null, seed++));
            }
        }

        foreach (var matchEvent in events)
        {
            var time = FilmTimeForSecond(EventSecond(matchEvent, rules, secondHalfShift), group, duration);
            beats.Add(new PassageBeatV1(time, matchEvent.Side, matchEvent.ParticipantId, PassageBeatKind.Event, matchEvent, matchEvent.Sequence));
        }

        return beats;
    }

    private static (int Minute, int Stoppage) ClockOf(
        FilmPassage group,
        List<EngineEventV1> events,
        EngineRulesV2 rules,
        int secondHalfShift)
    {
        if (events.Count > 0)
        {
            return (events[0].Minute, events[0].StoppageMinute);
        }

        // A passage with no event is labelled the way the engine labels the minute it began in: from the
        // half's own clock, which is the continuous one less the shift in the second half.
        var halfSeconds = group.Period == 2 ? group.StartSecond - secondHalfShift : group.StartSecond;
        var played = halfSeconds / Math.Max(1, rules.SecondsPerMinute);
        var regulation = group.Period == 2 ? rules.RegulationMinutes : rules.HalfTimeMinute;

        return (
            int.Clamp(played + 1, 1, regulation),
            Math.Max(0, played + 1 - regulation));
    }

    /// <summary>
    /// Gets the continuous-clock second an event is stamped at: its minute, read on the same clock the passages
    /// are, so a substitution lands in the passage it happened in.
    /// </summary>
    private static int EventSecond(EngineEventV1 matchEvent, EngineRulesV2 rules, int secondHalfShift) =>
        ((matchEvent.Minute + matchEvent.StoppageMinute) * rules.SecondsPerMinute)
        + (matchEvent.Minute > rules.HalfTimeMinute ? secondHalfShift : 0);

    private static int FilmTimeForSecond(int second, FilmPassage group, int duration)
    {
        var total = Math.Max(1, group.EndSecond - group.StartSecond);

        return int.Clamp((int)((long)(second - group.StartSecond) * duration / total), 0, duration);
    }

    /// <summary>The ball's pitch position at one film time, interpolated over the recorded path.</summary>
    private static (int X, int Y) BallAt(
        int time,
        IReadOnlyList<(MatchPassageV1 Possession, int Start, int Length)> windows)
    {
        (int X, int Y)? previous = null;
        var previousTime = 0;

        foreach (var (possession, start, length) in windows)
        {
            foreach (var waypoint in possession.Waypoints)
            {
                var at = start + (int)((long)waypoint.FractionBasisPoints * length / EngineRulesV2.Certain);

                if (at >= time)
                {
                    if (previous is null || at == previousTime)
                    {
                        return (waypoint.X, waypoint.Y);
                    }

                    var span = at - previousTime;
                    var ratio = span <= 0 ? 0 : (time - previousTime) / (double)span;

                    return (
                        (int)(previous.Value.X + ((waypoint.X - previous.Value.X) * ratio)),
                        (int)(previous.Value.Y + ((waypoint.Y - previous.Value.Y) * ratio)));
                }

                previous = (waypoint.X, waypoint.Y);
                previousTime = at;
            }
        }

        return previous ?? (SpatialPitch.PitchLength / 2, SpatialPitch.PitchWidth / 2);
    }

    /// <summary>The eleven, with substitutions and dismissals applied, as they stood at one match second.</summary>
    private static Dictionary<MatchSide, Dictionary<int, Guid>> OccupantsAt(
        MatchInputV1 input,
        List<(int Second, MatchSide Side, Guid Off, Guid? On)> changes,
        int second)
    {
        var occupants = new Dictionary<MatchSide, Dictionary<int, Guid>>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            occupants[side] = input.SideOf(side).Slots.ToDictionary(slot => slot.SlotNumber, slot => slot.ParticipantId);
        }

        foreach (var (at, side, off, on) in changes.OrderBy(change => change.Second))
        {
            if (at > second)
            {
                break;
            }

            var slots = occupants[side];
            var slot = slots.FirstOrDefault(pair => pair.Value == off).Key;

            if (slot == 0)
            {
                continue;
            }

            if (on is Guid incoming)
            {
                slots[slot] = incoming;
            }
            else
            {
                slots.Remove(slot);
            }
        }

        return occupants;
    }

    /// <summary>Substitutions and dismissals, which change who is on the pitch and split a film passage.</summary>
    private static List<(int Second, MatchSide Side, Guid Off, Guid? On)> PersonnelChanges(
        IReadOnlyList<EngineEventV1> events,
        EngineRulesV2 rules,
        int secondHalfShift)
    {
        var changes = new List<(int Second, MatchSide Side, Guid Off, Guid? On)>();

        foreach (var matchEvent in events.OrderBy(matchEvent => matchEvent.Sequence))
        {
            switch (matchEvent.Type)
            {
                case EngineEventType.Substitution when matchEvent.ParticipantId is Guid off:
                    changes.Add((EventSecond(matchEvent, rules, secondHalfShift), matchEvent.Side, off, matchEvent.SecondaryParticipantId));
                    break;

                case EngineEventType.RedCard or EngineEventType.SecondYellowCard when matchEvent.ParticipantId is Guid sent:
                    changes.Add((EventSecond(matchEvent, rules, secondHalfShift), matchEvent.Side, sent, null));
                    break;

                default:
                    break;
            }
        }

        return changes;
    }

    /// <summary>Whether any personnel change fell in the gap between two consecutive possessions.</summary>
    private static bool ChangeBetween(List<int> changeSeconds, int fromExclusive, int toInclusive) =>
        changeSeconds.Any(second => second > fromExclusive && second <= toInclusive);

    /// <summary>Ensures a track's keyframes are ordered, strictly increasing, and span the whole passage.</summary>
    private static List<HighlightKeyframeV1> Order(
        List<HighlightKeyframeV1> waypoints,
        int duration,
        (int X, int Y) fallback)
    {
        var ordered = waypoints
            .OrderBy(keyframe => keyframe.TimeMilliseconds)
            .ThenBy(keyframe => keyframe.Action is null ? 0 : 1)
            .ToList();

        var result = new List<HighlightKeyframeV1>(ordered.Count);
        var previous = -1;

        foreach (var keyframe in ordered)
        {
            var time = int.Clamp(keyframe.TimeMilliseconds, 0, Math.Max(0, duration - 1));

            if (time <= previous)
            {
                time = previous + 1;
            }

            if (time >= duration && result.Count > 0)
            {
                time = Math.Max(0, duration - 1);
            }

            result.Add(keyframe with { TimeMilliseconds = time });
            previous = time;
        }

        if (result.Count == 0)
        {
            result.Add(new HighlightKeyframeV1(0, fallback.X, fallback.Y));
        }

        if (result[^1].TimeMilliseconds != duration)
        {
            result.Add(new HighlightKeyframeV1(duration, result[^1].X, result[^1].Y));
        }

        return result;
    }

    private static int Estimate(
        IReadOnlyList<PassageV1> passages,
        IReadOnlyList<ReelClipV1> reel,
        List<PlaybackSegmentV1> playback,
        MatchLineupV1 homeLineup,
        MatchLineupV1 awayLineup,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics) =>
        passages.Sum(passage => passage.EstimatedPayloadBytes)
        + reel.Sum(clip => clip.EstimatedPayloadBytes)
        + (playback.Count * MatchPresentationV1.ScheduleSegmentBytes)
        + homeLineup.EstimatedPayloadBytes
        + awayLineup.EstimatedPayloadBytes
        + ((liveMetrics?.Count ?? 0) * MatchPresentationV1.LiveMetricBytes);

    private static int NormalizeX(int pitchX) => Clamp(pitchX);

    private static int NormalizeY(int pitchY) => Clamp((pitchY * Pitch) / PitchWidth);

    private static int Clamp(int value) => int.Clamp(value, 0, Pitch);

    private static string EntityId(bool isHome, int slotNumber) => $"{(isHome ? "H" : "A")}{slotNumber}";

    private static (int X, int Y) Jitter(string seed)
    {
        var hash = StableHash(seed);
        var span = (2 * JitterMagnitude) + 1;

        return ((int)(hash % span) - JitterMagnitude, (int)((hash / span) % span) - JitterMagnitude);
    }

    private static uint StableHash(string value)
    {
        var hash = 2166136261u;

        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
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
        _ => "play",
    };

    private static string Narration(EngineEventV1? matchEvent, Dictionary<Guid, string> names, int minute, int stoppage)
    {
        if (matchEvent is null)
        {
            return $"Play continues, {ClockLabel(minute, stoppage)}.";
        }

        var player = matchEvent.ParticipantId is Guid id && names.TryGetValue(id, out var name)
            ? name
            : "the attacker";

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

        return $"{outcome} — {player}, {ClockLabel(matchEvent.Minute, matchEvent.StoppageMinute)}.";
    }

    private static string ClockLabel(int minute, int stoppage) =>
        stoppage > 0 ? $"{minute}+{stoppage}" : minute.ToString(CultureInfo.InvariantCulture);

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

    private static (string Home, string Away) Colours(MatchInputV1 input) =>
        (ClubPalette.PrimaryOf(input.Home.ClubId), ClubPalette.PrimaryOf(input.Away.ClubId));

    /// <summary>One film passage, before its tracks are compressed to the payload ladder's rung.</summary>
    private sealed record AuthoredPassage(
        string PresentationVersion,
        int SourceEventSequence,
        int Minute,
        int StoppageMinute,
        int StartMatchSecond,
        int EndMatchSecond,
        int DurationMilliseconds,
        string OutcomeCode,
        string Narration,
        string HomeColour,
        string AwayColour,
        IReadOnlyList<int> EventSequences,
        IReadOnlyList<HighlightEntityV1> Entities,
        IReadOnlyDictionary<string, IReadOnlyList<HighlightKeyframeV1>> Waypoints,
        IReadOnlyList<HighlightCommentaryV1> Commentary)
    {
        /// <summary>Gets the last authored keyframe of each entity, for the next passage to join onto.</summary>
        public Dictionary<string, HighlightKeyframeV1> Ends =>
            Waypoints.ToDictionary(pair => pair.Key, pair => pair.Value[^1], StringComparer.Ordinal);

        /// <summary>Compresses the tracks at one rung of the payload ladder.</summary>
        public PassageV1 Compress(int tolerance, int sampleInterval) => new()
        {
            PresentationVersion = PresentationVersion,
            SourceEventSequence = SourceEventSequence,
            Minute = Minute,
            StoppageMinute = StoppageMinute,
            StartMatchSecond = StartMatchSecond,
            EndMatchSecond = EndMatchSecond,
            DurationMilliseconds = DurationMilliseconds,
            OutcomeCode = OutcomeCode,
            Narration = Narration,
            HomeColour = HomeColour,
            AwayColour = AwayColour,
            EventSequences = EventSequences,
            Entities = Entities,
            Tracks =
            [
                .. Waypoints
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new HighlightTrackV1(
                        pair.Key,
                        KeyframeCompressor.Compress(
                            KeyframeCompressor.Sample(pair.Value, sampleInterval),
                            tolerance))),
            ],
            Commentary = Commentary,
        };
    }

    private sealed record FilmPassage(
        IReadOnlyList<MatchPassageV1> Possessions,
        int StartSecond,
        int EndSecond,
        MatchSide PrimarySide,
        long Weight,
        int Period);
}
