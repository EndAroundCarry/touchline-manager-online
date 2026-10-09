using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Turns a tick match's continuous 10 Hz recording into the presentation the 2D viewer plays (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// The possession engine's replay has to <em>invent</em> motion: it knows where the ball went and writes the players a plausible way
/// to be there. The tick engine has nothing to invent. Every slot and the ball have a position at every tenth of a second, so the
/// film is a view of what the simulation did: <see cref="TickFilmSelector"/> chooses the stretches worth showing, each stretch is
/// cut into passages of about ten seconds of film, and a passage's tracks are the recording's frames, sampled and run through
/// <see cref="KeyframeCompressor"/> so a straight run is two keyframes and a flighted ball the few its arc bends around.
/// </para>
/// <para>
/// The film is played at the one pace <see cref="HighlightOptionsV1.TickFilmPaceMilli"/> sets, 50 ms of film for every tick, so a
/// sprint on screen is twice a sprint on a pitch and no more. Two passages that follow on share their boundary frame, so the state
/// one ends on is the state the next begins with; where the film jumps (to the next stretch, across a ball put down on a restart's
/// spot, to the second half) the passage after the jump carries a <see cref="PassageCutV1"/> and the viewer fades over it. Between
/// the halves the film holds an interval card (<c>half_time</c>).
/// </para>
/// <para>
/// The match clock, the events, the commentary and the lineups are the ones the possession engine's replay carries, built by the same
/// code, so the viewer, the report and the reel need no change. Everything here is a pure function of the recording, the result and
/// the options, in integers, so a film is the same on every machine.
/// </para>
/// </remarks>
internal sealed class TickReplaySynthesizer
{
    /// <summary>The version label of a film made from a tick recording.</summary>
    public const string Version = "tick-replay-v1";

    /// <summary>The cut that covers a jump to a stretch of the match that is not the one before it.</summary>
    public const string JumpCut = "jump";

    /// <summary>The cut that covers the ball being put on the centre spot for a kick-off.</summary>
    public const string KickOffCut = "kick_off";

    /// <summary>The cut that covers the walk from the first half into the interval.</summary>
    public const string HalfTimeCut = "half_time";

    /// <summary>Passages are sampled for a command of at most this many passages before the cap is relaxed.</summary>
    private const int RelaxationSteps = 5;

    private readonly MatchInputV1 _input;
    private readonly MatchResultV1 _result;
    private readonly TickMatchRecording _recording;
    private readonly HighlightOptionsV1 _options;
    private readonly EngineRulesV2 _rules = EngineRulesV2.Default;
    private readonly int _msPerFrame;
    private readonly Dictionary<Guid, string> _names;
    private readonly Dictionary<Guid, MatchParticipantV1> _participants = [];
    private readonly MatchSlotV1[] _slots = new MatchSlotV1[TickMatchRecording.Entities];
    private readonly Dictionary<int, EngineEventV1> _events;
    private readonly Dictionary<int, int> _eventFrame = [];
    private readonly int[] _naturals;

    private TickReplaySynthesizer(
        MatchInputV1 input,
        MatchResultV1 result,
        TickMatchRecording recording,
        HighlightOptionsV1 options)
    {
        _input = input;
        _result = result;
        _recording = recording;
        _options = options;
        _msPerFrame = TickFilmSelector.MillisecondsPerFrame(options);
        _names = FilmLabels.Names(input);
        _events = result.Events.ToDictionary(matchEvent => matchEvent.Sequence);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            foreach (var participant in input.SideOf(side).Squad)
            {
                _participants[participant.ParticipantId] = participant;
            }

            foreach (var slot in input.SideOf(side).Slots)
            {
                _slots[FilmRoster.Index(side, slot.SlotNumber)] = slot;
            }
        }

        foreach (var stamp in recording.Events)
        {
            _eventFrame[stamp.Sequence] = Math.Min(stamp.Frame, recording.FrameCount - 1);
        }

        _naturals = Naturals();
    }

    /// <summary>Builds the presentation of a tick match.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="recording">The continuous trace the loop recorded.</param>
    /// <param name="options">The film's pace and length, or the defaults.</param>
    /// <param name="liveMetrics">The minute-by-minute condition and rating curve, or null.</param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        TickMatchRecording recording,
        HighlightOptionsV1? options = null,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(recording);

        var settings = options ?? new HighlightOptionsV1();
        var homeLineup = MatchLineupBuilder.Build(input, result, MatchSide.Home);
        var awayLineup = MatchLineupBuilder.Build(input, result, MatchSide.Away);

        if (recording.FrameCount < 2)
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

        return new TickReplaySynthesizer(input, result, recording, settings).Run(homeLineup, awayLineup, liveMetrics);
    }

    /// <summary>Gets the frames the film can end a passage on: where play stopped, and a little after each shot.</summary>
    private int[] Naturals()
    {
        var frames = new SortedSet<int>();

        foreach (var stoppage in _recording.Stoppages)
        {
            frames.Add(stoppage.Frame);
        }

        foreach (var matchEvent in _result.Events)
        {
            if (!_eventFrame.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            if (FilmLabels.IsShot(matchEvent.Type))
            {
                frames.Add(frame + (matchEvent.IsGoal ? 80 : 30));
            }
        }

        return [.. frames];
    }

    private MatchPresentationV1 Run(MatchLineupV1 homeLineup, MatchLineupV1 awayLineup, IReadOnlyList<PlayerLiveMetricV1>? liveMetrics)
    {
        var windows = TickFilmSelector.Select(_recording, _result.Events, _options);
        var items = Plan(windows);

        Own(items);

        var playback = Schedule(items);
        var reel = ReelBuilder.Build(Candidates(items), _options);
        var built = new List<PassageV1>(items.Count);
        var homeColour = FilmLabels.Colours(_input).Home;
        var awayColour = FilmLabels.Colours(_input).Away;

        for (var rung = 0; rung < _options.PlayerTolerances.Count; rung++)
        {
            var tolerance = _options.PlayerTolerances[rung];
            var interval = _options.PlayerSampleIntervals[Math.Min(rung, _options.PlayerSampleIntervals.Count - 1)];

            built = [.. items.Select(item => ToPassage(item, Tracks(item, interval, tolerance), homeColour, awayColour))];

            if (Estimate(built, reel, playback, homeLineup, awayLineup, liveMetrics) <= _options.PayloadBudgetBytes)
            {
                break;
            }
        }

        return new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = _result.EngineVersion,
            HomeGoals = _result.HomeGoals,
            AwayGoals = _result.AwayGoals,
            Passages = built,
            Reel = reel,
            Playback = playback,
            HomeLineup = homeLineup,
            AwayLineup = awayLineup,
            LiveMetrics = liveMetrics,
            PaceMilli = _options.TickFilmPaceMilli,
        };
    }

    // ---- Planning: windows into passages -------------------------------------------------------------------------------------------------

    /// <summary>One passage of the film while it is being planned: which frames it shows and what is true of it.</summary>
    private sealed class Item
    {
        public int First;
        public int Last;

        /// <summary>Whether the next passage starts on this one's last frame, so the two share that frame.</summary>
        public bool SharedNext;

        public string? CutKind;
        public int Window;
        public bool IsHalfTime;
        public int StartMs;
        public int DurationMs;
        public Guid[] Occupants = [];
        public List<EngineEventV1> Events = [];
        public EngineEventV1? Principal;
        public List<TickActionStamp> Marks = [];
    }

    private List<Item> Plan(IReadOnlyList<TickFilmWindow> windows)
    {
        for (var relax = 0; relax < RelaxationSteps; relax++)
        {
            var scale = relax switch { 0 => 100, 1 => 125, 2 => 150, 3 => 200, _ => 300 };
            var items = PlanAt(windows, scale);

            if (items.Count <= _options.MaxPassages || relax == RelaxationSteps - 1)
            {
                return items;
            }
        }

        return [];
    }

    private List<Item> PlanAt(IReadOnlyList<TickFilmWindow> windows, int scalePercent)
    {
        var minFrames = Math.Max(4, _options.MinPassageMilliseconds * scalePercent / 100 / _msPerFrame);
        var targetFrames = Math.Max(minFrames + 1, _options.TargetPassageMilliseconds * scalePercent / 100 / _msPerFrame);
        var maxFrames = Math.Max(targetFrames + 1, _options.MaxPassageMilliseconds * scalePercent / 100 / _msPerFrame);
        var items = new List<Item>();
        var halfTimeDone = _recording.SecondHalfFrame <= 0;

        for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            var window = windows[windowIndex];

            var afterInterval = false;

            if (!halfTimeDone && window.Start >= _recording.SecondHalfFrame)
            {
                items.Add(new Item { IsHalfTime = true, First = _recording.SecondHalfFrame, Last = _recording.SecondHalfFrame, CutKind = HalfTimeCut, Window = windowIndex });
                halfTimeDone = true;
                afterInterval = window.Start == _recording.SecondHalfFrame;
            }

            var segmentStart = window.Start;

            // The interval card ends on the state the second half starts in, so the second half's first stretch joins it.
            var cut = windowIndex == 0 || afterInterval ? null : JumpCut;
            var forced = ForcedSplits(window);

            foreach (var (frame, kind) in forced)
            {
                Subdivide(items, segmentStart, frame - 1, cut, windowIndex, minFrames, targetFrames, maxFrames);
                segmentStart = frame;
                cut = kind;
            }

            Subdivide(items, segmentStart, window.End, cut, windowIndex, minFrames, targetFrames, maxFrames);
        }

        // A match that ends in the first half (a recording cut short) has no interval to hold.
        var running = 0;

        foreach (var item in items)
        {
            item.DurationMs = item.IsHalfTime ? (int)(_options.HalfTimeHoldSeconds * 1_000) : (item.Last - item.First) * _msPerFrame;
            item.StartMs = running;
            running += item.DurationMs;
        }

        return items;
    }

    /// <summary>The ball moving this far (4.7 m) between two ticks is a ball put down, not a ball struck.</summary>
    public const int BallJumpUnits = 450;

    private int BallMovedUnits(int frame)
    {
        long dx = _recording.BallX(frame) - _recording.BallX(frame - 1);
        long dy = _recording.BallY(frame) - _recording.BallY(frame - 1);

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Finds the frames inside a window that start a new stretch of film that cannot join the one before: a ball or a team put
    /// down where it was not (a cut), and a change of who stands in a slot (no cut: a dead ball, so little moved).
    /// </summary>
    private List<(int Frame, string? Cut)> ForcedSplits(TickFilmWindow window)
    {
        var splits = new SortedDictionary<int, string?>();

        for (var frame = window.Start + 1; frame <= window.End; frame++)
        {
            var flags = _recording.Flags(frame);

            if ((flags & TickFrameFlags.PlayersPlaced) != 0)
            {
                splits[frame] = JumpCut;
            }
            else if ((flags & TickFrameFlags.BallPlaced) != 0 || BallMovedUnits(frame) > BallJumpUnits)
            {
                splits[frame] = _recording.State(frame) == TickPlayState.KickOffPending ? KickOffCut : JumpCut;
            }
        }

        foreach (var stamp in _recording.Rosters)
        {
            if (stamp.Frame > window.Start && stamp.Frame <= window.End && !splits.ContainsKey(stamp.Frame))
            {
                splits[stamp.Frame] = null;
            }
        }

        return [.. splits.Select(pair => (pair.Key, pair.Value))];
    }

    /// <summary>Cuts a run of frames that can be played as one into passages of about ten seconds that share their boundary frames.</summary>
    private void Subdivide(
        List<Item> items,
        int from,
        int to,
        string? cut,
        int window,
        int minFrames,
        int targetFrames,
        int maxFrames)
    {
        if (to - from < 1)
        {
            return;
        }

        var cursor = from;
        var first = true;

        while (true)
        {
            var end = to;

            if (to - cursor > maxFrames + (maxFrames / 5))
            {
                end = NaturalNear(cursor + minFrames, cursor + maxFrames, cursor + targetFrames);

                if (end < 0)
                {
                    end = cursor + targetFrames;
                }
            }

            items.Add(new Item
            {
                First = cursor,
                Last = end,
                SharedNext = end < to,
                CutKind = first ? cut : null,
                Window = window,
            });

            if (end >= to)
            {
                return;
            }

            cursor = end;
            first = false;
        }
    }

    private int NaturalNear(int low, int high, int target)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        var index = Array.BinarySearch(_naturals, low);

        if (index < 0)
        {
            index = ~index;
        }

        for (; index < _naturals.Length && _naturals[index] <= high; index++)
        {
            var distance = Math.Abs(_naturals[index] - target);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = _naturals[index];
            }
        }

        return best;
    }

    // ---- Owning the facts of the match --------------------------------------------------------------------------------------------------

    /// <summary>Gives each passage the events, the occupants and the actions that belong to its frames.</summary>
    private void Own(List<Item> items)
    {
        var occupants = (Guid[])_recording.Starters.Clone();
        var rosterCursor = 0;

        foreach (var item in items)
        {
            while (rosterCursor < _recording.Rosters.Count && _recording.Rosters[rosterCursor].Frame <= item.First)
            {
                occupants[_recording.Rosters[rosterCursor].Entity] = _recording.Rosters[rosterCursor].Occupant;
                rosterCursor++;
            }

            item.Occupants = (Guid[])occupants.Clone();
        }

        var half = items.FindIndex(item => item.IsHalfTime);

        foreach (var matchEvent in _result.Events)
        {
            if (!_eventFrame.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            if (matchEvent.Type == EngineEventType.HalfTime && half >= 0)
            {
                items[half].Events.Add(matchEvent);

                continue;
            }

            var owner = items.FindIndex(item => !item.IsHalfTime
                && item.First <= frame
                && (frame < item.Last || (frame == item.Last && !item.SharedNext)));

            if (owner >= 0)
            {
                items[owner].Events.Add(matchEvent);
            }
        }

        foreach (var item in items)
        {
            if (item.IsHalfTime)
            {
                continue;
            }

            item.Marks = [.. _recording.Actions.Where(stamp => stamp.Frame >= item.First && stamp.Frame <= item.Last)];

            var inPlay = item.Events.Where(matchEvent => !matchEvent.IsPeriodBoundary && matchEvent.Type != EngineEventType.Substitution).ToList();

            item.Principal = inPlay.FirstOrDefault(matchEvent => matchEvent.IsGoal)
                ?? inPlay.FirstOrDefault(matchEvent => FilmLabels.IsShot(matchEvent.Type))
                ?? inPlay.FirstOrDefault();
        }
    }

    private static List<PlaybackSegmentV1> Schedule(List<Item> items) =>
        [.. items.Select(item => new PlaybackSegmentV1("passage", item.Principal?.Sequence ?? 0, item.StartMs, item.DurationMs))];

    // ---- The reel --------------------------------------------------------------------------------------------------------------------------

    private List<ReelCandidateV1> Candidates(List<Item> items)
    {
        var candidates = new List<ReelCandidateV1>();
        var windowStart = new Dictionary<int, int>();

        foreach (var item in items)
        {
            windowStart.TryAdd(item.Window, item.StartMs);
        }

        foreach (var item in items)
        {
            foreach (var matchEvent in item.Events)
            {
                if (!IsWorthShowing(matchEvent) || !_eventFrame.TryGetValue(matchEvent.Sequence, out var frame))
                {
                    continue;
                }

                var at = item.StartMs + (Math.Clamp(frame, item.First, item.Last) - item.First) * _msPerFrame;
                var end = at + _options.ReelReactionMilliseconds;

                if (matchEvent.IsGoal)
                {
                    end = Math.Max(end, at + 4_000);
                }

                var start = windowStart.GetValueOrDefault(item.Window, item.StartMs);

                candidates.Add(new ReelCandidateV1(
                    matchEvent.Sequence,
                    FilmLabels.OutcomeCode(matchEvent.Type),
                    matchEvent.Minute,
                    matchEvent.StoppageMinute,
                    start,
                    end,
                    matchEvent.QualityBasisPoints ?? 0,
                    matchEvent.IsGoal,
                    LeadFilmMilliseconds: 0,
                    PeriodFilmStart: start));
            }
        }

        return candidates;
    }

    private bool IsWorthShowing(EngineEventV1 matchEvent) => matchEvent.Type switch
    {
        EngineEventType.Goal or EngineEventType.PenaltyGoal => true,
        EngineEventType.Woodwork => _options.IncludeWoodwork,
        EngineEventType.FreeKickShot => _options.IncludeFreeKicks,
        EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.PenaltyMissed =>
            (matchEvent.QualityBasisPoints ?? 0) >= _options.MinQualityForShotBasisPoints,
        _ => false,
    };

    // ---- Describing a passage ---------------------------------------------------------------------------------------------------------

    private PassageV1 ToPassage(Item item, List<HighlightTrackV1> tracks, string homeColour, string awayColour)
    {
        var clock = ClockOf(item);
        var period = _recording.Period(item.First);
        var principal = item.Principal;
        var (minute, stoppage) = principal is null
            ? FilmLabels.ClockOf(clock[0].MatchSecond, period, _rules)
            : (principal.Minute, principal.StoppageMinute);

        var cuts = item.CutKind is null ? [] : new List<PassageCutV1> { new(0, _options.CutMilliseconds, item.CutKind) };

        return new PassageV1
        {
            PresentationVersion = Version,
            SourceEventSequence = principal?.Sequence ?? 0,
            Minute = minute,
            StoppageMinute = stoppage,
            Period = period,
            StartMatchSecond = clock[0].MatchSecond,
            EndMatchSecond = clock[^1].MatchSecond,
            Clock = clock,
            Cuts = cuts,
            DurationMilliseconds = item.DurationMs,
            OutcomeCode = item.IsHalfTime ? "half_time" : principal is null ? "play" : FilmLabels.OutcomeCode(principal.Type),
            Narration = item.IsHalfTime ? "Half-time." : FilmLabels.Narration(principal, _names, minute, stoppage),
            HomeColour = homeColour,
            AwayColour = awayColour,
            EventSequences = [.. item.Events.Select(matchEvent => matchEvent.Sequence).Order()],
            Entities = Entities(item),
            Tracks = tracks,
            Commentary = item.IsHalfTime ? [] : Commentary(item),
        };
    }

    /// <summary>The passage's clock: the match second at its first frame, at every frame the second turns over, and at its last.</summary>
    private List<ClockKeyframeV1> ClockOf(Item item)
    {
        var keyframes = new List<ClockKeyframeV1>();

        if (item.IsHalfTime)
        {
            var end = _recording.ClockSecond(Math.Max(0, _recording.SecondHalfFrame - 1));

            return [new ClockKeyframeV1(0, end), new ClockKeyframeV1(item.DurationMs, end)];
        }

        var previous = _recording.ClockSecond(item.First);

        keyframes.Add(new ClockKeyframeV1(0, previous));

        for (var frame = item.First + 1; frame <= item.Last; frame++)
        {
            var second = _recording.ClockSecond(frame);

            if (second != previous)
            {
                keyframes.Add(new ClockKeyframeV1((frame - item.First) * _msPerFrame, second));
                previous = second;
            }
        }

        if (keyframes[^1].TimeMilliseconds != item.DurationMs)
        {
            keyframes.Add(new ClockKeyframeV1(item.DurationMs, previous));
        }

        return keyframes;
    }

    private List<HighlightEntityV1> Entities(Item item)
    {
        var entities = new List<HighlightEntityV1>(TickMatchRecording.Entities + 1);
        var frame = item.IsHalfTime ? _recording.SecondHalfFrame : item.First;

        for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
        {
            var occupant = item.Occupants[entity];

            if (occupant == Guid.Empty || _recording.PlayerX(frame, entity) < 0)
            {
                continue;
            }

            var slot = _slots[entity];

            entities.Add(new HighlightEntityV1
            {
                EntityId = FilmRoster.EntityId(entity),
                IsBall = false,
                Side = FilmRoster.SideOf(entity),
                ParticipantId = occupant,
                ShirtNumber = _participants.TryGetValue(occupant, out var participant) ? participant.ShirtNumber : 0,
                Family = slot.Family,
                X = NormalizeX(_recording.PlayerX(frame, entity)),
                Y = NormalizeY(_recording.PlayerY(frame, entity)),
                Name = _names.TryGetValue(occupant, out var name) ? name : null,
                Position = FilmLabels.PositionCode(slot),
            });
        }

        entities.Add(new HighlightEntityV1
        {
            EntityId = "ball",
            IsBall = true,
            X = NormalizeX(_recording.BallX(frame)),
            Y = NormalizeY(_recording.BallY(frame)),
        });

        entities.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));

        return entities;
    }

    private static int NormalizeX(int x) => Math.Clamp(x, 0, FilmSpace.Normalized);

    private static int NormalizeY(int y) =>
        Math.Clamp((int)(((long)y * FilmSpace.Normalized) + (SpatialPitch.PitchWidth / 2)) / SpatialPitch.PitchWidth, 0, FilmSpace.Normalized);

    // ---- Tracks --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The order in which an action wins when several are tagged on one player at one frame.</summary>
    private static readonly PassageAction[] ActionPriority =
    [
        PassageAction.Penalty,
        PassageAction.FreeKick,
        PassageAction.Shot,
        PassageAction.Save,
        PassageAction.Dive,
        PassageAction.Cross,
        PassageAction.Header,
        PassageAction.Tackle,
        PassageAction.Interception,
        PassageAction.Pass,
        PassageAction.Receive,
        PassageAction.Carry,
        PassageAction.Celebrate,
        PassageAction.Run,
    ];

    private static readonly HashSet<PassageAction> BallActions =
    [
        PassageAction.Penalty,
        PassageAction.FreeKick,
        PassageAction.Shot,
        PassageAction.Save,
        PassageAction.Cross,
        PassageAction.Pass,
    ];

    private List<HighlightTrackV1> Tracks(Item item, int playerIntervalMs, int playerTolerance)
    {
        if (item.IsHalfTime)
        {
            return [];
        }

        var tracks = new List<HighlightTrackV1>(TickMatchRecording.Entities + 1);
        var step = Math.Max(1, playerIntervalMs / _msPerFrame);

        for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
        {
            if (item.Occupants[entity] == Guid.Empty || _recording.PlayerX(item.First, entity) < 0)
            {
                continue;
            }

            var tagged = Tagged(item, entity);
            var samples = new List<HighlightKeyframeV1>();

            for (var frame = item.First; frame <= item.Last; frame++)
            {
                var isGrid = (frame - item.First) % step == 0 || frame == item.Last;

                if (!isGrid && !tagged.ContainsKey(frame))
                {
                    continue;
                }

                if (_recording.PlayerX(frame, entity) < 0)
                {
                    continue;
                }

                samples.Add(new HighlightKeyframeV1(
                    (frame - item.First) * _msPerFrame,
                    NormalizeX(_recording.PlayerX(frame, entity)),
                    NormalizeY(_recording.PlayerY(frame, entity)),
                    Action: tagged.TryGetValue(frame, out var action) ? action.Code() : null));
            }

            tracks.Add(new HighlightTrackV1(FilmRoster.EntityId(entity), KeyframeCompressor.Compress(samples, playerTolerance)));
        }

        var ballTags = BallTagged(item);
        var ball = new List<HighlightKeyframeV1>(item.Last - item.First + 1);

        for (var frame = item.First; frame <= item.Last; frame++)
        {
            ball.Add(new HighlightKeyframeV1(
                (frame - item.First) * _msPerFrame,
                NormalizeX(_recording.BallX(frame)),
                NormalizeY(_recording.BallY(frame)),
                Math.Clamp(_recording.BallZ(frame), 0, 100),
                Action: ballTags.TryGetValue(frame, out var action) ? action.Code() : null));
        }

        tracks.Add(new HighlightTrackV1("ball", KeyframeCompressor.Compress(ball, _options.BallTolerance)));
        tracks.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));

        return tracks;
    }

    private static Dictionary<int, PassageAction> Tagged(Item item, int entity)
    {
        var tagged = new Dictionary<int, PassageAction>();

        foreach (var stamp in item.Marks)
        {
            if (stamp.Entity != entity)
            {
                continue;
            }

            if (!tagged.TryGetValue(stamp.Frame, out var existing)
                || Array.IndexOf(ActionPriority, stamp.Action) < Array.IndexOf(ActionPriority, existing))
            {
                tagged[stamp.Frame] = stamp.Action;
            }
        }

        return tagged;
    }

    private static Dictionary<int, PassageAction> BallTagged(Item item)
    {
        var tagged = new Dictionary<int, PassageAction>();

        foreach (var stamp in item.Marks)
        {
            if (!BallActions.Contains(stamp.Action))
            {
                continue;
            }

            if (!tagged.TryGetValue(stamp.Frame, out var existing)
                || Array.IndexOf(ActionPriority, stamp.Action) < Array.IndexOf(ActionPriority, existing))
            {
                tagged[stamp.Frame] = stamp.Action;
            }
        }

        return tagged;
    }

    // ---- Commentary --------------------------------------------------------------------------------------------------------------------------

    private IReadOnlyList<HighlightCommentaryV1> Commentary(Item item)
    {
        var candidates = new List<(int Time, int Priority, PassageBeatV1 Beat)>();

        foreach (var matchEvent in item.Events)
        {
            if (!_eventFrame.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            var time = (Math.Clamp(frame, item.First, item.Last) - item.First) * _msPerFrame;

            candidates.Add((time, 100, new PassageBeatV1(time, matchEvent.Side, matchEvent.ParticipantId, PassageBeatKind.Event, matchEvent, matchEvent.Sequence)));
        }

        for (var index = 0; index < item.Marks.Count; index++)
        {
            var stamp = item.Marks[index];

            if (CommentaryTokenBuilder.KindOf(stamp.Action) is not { } kind)
            {
                continue;
            }

            if (stamp.Entity < 0 || item.Occupants[stamp.Entity] == Guid.Empty || stamp.Action == PassageAction.Receive)
            {
                continue;
            }

            var actor = item.Occupants[stamp.Entity];
            var side = FilmRoster.SideOf(stamp.Entity);
            var time = (stamp.Frame - item.First) * _msPerFrame;
            Guid? target = null;

            if (kind == PassageBeatKind.Pass || kind == PassageBeatKind.Cross)
            {
                target = Receiver(item, index, side, actor);
            }

            if (kind == PassageBeatKind.Pass && target is null)
            {
                continue;
            }

            var priority = kind switch
            {
                PassageBeatKind.Chance => 80,
                PassageBeatKind.Cross => 70,
                PassageBeatKind.Header or PassageBeatKind.Save => 60,
                PassageBeatKind.Interception => 50,
                PassageBeatKind.Tackle => 40,
                PassageBeatKind.Carry => 30,
                _ => 20,
            };

            candidates.Add((time, priority, new PassageBeatV1(time, side, actor, kind, null, item.First + stamp.Frame + index, target)));
        }

        var accepted = new List<(int Time, PassageBeatV1 Beat)>();

        foreach (var candidate in candidates.OrderByDescending(entry => entry.Priority).ThenBy(entry => entry.Time))
        {
            var always = candidate.Priority >= 60;

            if (always || accepted.All(other => Math.Abs(other.Time - candidate.Time) >= _options.CommentaryGapMilliseconds))
            {
                accepted.Add((candidate.Time, candidate.Beat));
            }
        }

        var ordered = accepted
            .OrderBy(line => line.Time)
            .Select(line => line.Beat with { TimeMilliseconds = int.Clamp(line.Time, 0, item.DurationMs) })
            .ToList();

        return [.. CommentaryTokenBuilder.BuildPassageCommentary(_input, ordered, Math.Max(1, item.DurationMs))];
    }

    /// <summary>Finds who a pass was played to: the next teammate to receive it, within the next six seconds of play.</summary>
    private static Guid? Receiver(Item item, int markIndex, MatchSide side, Guid passer)
    {
        var from = item.Marks[markIndex];

        for (var index = markIndex + 1; index < item.Marks.Count; index++)
        {
            var next = item.Marks[index];

            if (next.Frame - from.Frame > 60)
            {
                break;
            }

            if (next.Action == PassageAction.Receive && next.Entity >= 0 && FilmRoster.SideOf(next.Entity) == side)
            {
                var receiver = item.Occupants[next.Entity];

                return receiver == passer ? null : receiver;
            }
        }

        return null;
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
}
