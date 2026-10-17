using Tactical11Manager.MatchEngine.Commentary;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Highlights;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;

namespace Tactical11Manager.MatchEngine.Tick;

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
    public const string Version = "tick-replay-v4";

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
            // Not the goal itself: a passage cut on that frame hands the goal to the next one, which then holds the celebration and not the strike.
            if (stoppage.State != TickPlayState.GoalCelebration)
            {
                frames.Add(stoppage.Frame);
            }
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

        for (var rung = 0; rung < _options.TickPlayerTolerances.Count; rung++)
        {
            var tolerance = _options.TickPlayerTolerances[rung];
            var interval = _options.TickPlayerSampleIntervals[Math.Min(rung, _options.TickPlayerSampleIntervals.Count - 1)];

            built = [.. items.Select(item => ToPassage(item, Tracks(item, interval, tolerance), homeColour, awayColour))];

            if (Estimate(built, reel, playback, homeLineup, awayLineup, liveMetrics) <= _options.TickPayloadBudgetBytes)
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

            if (items.Count <= _options.TickMaxPassages || relax == RelaxationSteps - 1)
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

            if (owner < 0 && matchEvent.Type == EngineEventType.YellowCard)
            {
                // The foul that earned a booking is not worth filming, but the man carries the card from then on: it goes to the next
                // passage the film shows, where the viewer puts it on his token.
                owner = items.FindIndex(item => !item.IsHalfTime && item.First > frame);
            }

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

            // A booking carried over from a stretch the film skipped says nothing about this passage.
            var inPlay = item.Events
                .Where(matchEvent => !matchEvent.IsPeriodBoundary
                    && matchEvent.Type is not (EngineEventType.Substitution or EngineEventType.TacticalChange)
                    && !(matchEvent.Type == EngineEventType.YellowCard && _eventFrame.GetValueOrDefault(matchEvent.Sequence) < item.First))
                .ToList();

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

        // The half-time card stands on the first half's last clock second, so it belongs to the first half; its frame
        // is the second half's first, which would put a second-half passage's clock behind it.
        var period = item.IsHalfTime ? 1 : _recording.Period(item.First);
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
        PassageAction.Beaten,
        PassageAction.Dive,
        PassageAction.Cross,
        PassageAction.Header,
        PassageAction.Tackle,
        PassageAction.Interception,
        PassageAction.Dispossessed,
        PassageAction.Misplaced,
        PassageAction.Bypassed,
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
            // Intent stamps feed the commentary only: a renderer has no pose for them and they must not force a keyframe.
            if (stamp.Entity != entity || stamp.Action is PassageAction.TakeOn or PassageAction.Break)
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

    /// <summary>A line for the banner while the passage's commentary is being chosen.</summary>
    private sealed class Candidate
    {
        /// <summary>The film time the line is shown at, in milliseconds from the passage's first frame.</summary>
        public int Time;

        /// <summary>The frame the action or event happened at, before any shift for display.</summary>
        public int Frame;

        /// <summary>How much the line matters: the stronger one stays when two fall close, and 60 and over are key lines.</summary>
        public int Priority;

        public PassageBeatV1 Beat = null!;
        public PassageBeatKind Kind;

        /// <summary>The event a key line narrates, or null for a line cut from an action stamp.</summary>
        public EngineEventV1? Event;

        public bool IsKey => Priority >= KeyPriority;

        /// <summary>Whether the line says how a shot ended, so it must not cover the line that says it was struck.</summary>
        public bool IsShotOutcome =>
            Kind is PassageBeatKind.SaveHeld or PassageBeatKind.SaveParried or PassageBeatKind.SaveTipped or PassageBeatKind.Save or PassageBeatKind.Block
            || Event is { Type: EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork
                or EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed };

        public bool IsStrike => Kind is PassageBeatKind.Chance or PassageBeatKind.ShotLong;
    }

    /// <summary>The priority from which a line is a key line: it holds the banner for a moment and is never thinned for a routine one.</summary>
    private const int KeyPriority = 60;

    private const int EventPriority = 100;

    /// <summary>How far before its strike a shot's outcome may fall and still be that shot's (3 s of film).</summary>
    private const int StrikeReachMilliseconds = 3_000;

    /// <summary>How far before a bypass a carry past the same man may be for the bypass to be the same moment told twice (2 s of film).</summary>
    private const int BypassEchoMilliseconds = 2_000;

    /// <summary>How near in frames a save or a block must be to the event that reports it for the save to be the line that is shown.</summary>
    private const int SaveEventFrames = 2;

    private static int PriorityOf(PassageBeatKind kind) => kind switch
    {
        PassageBeatKind.Chance or PassageBeatKind.ShotLong => 80,
        PassageBeatKind.SaveHeld or PassageBeatKind.SaveParried or PassageBeatKind.SaveTipped or PassageBeatKind.Smother or PassageBeatKind.Save => 75,
        PassageBeatKind.Cross => 70,
        PassageBeatKind.Block => 65,
        PassageBeatKind.Header => 60,
        PassageBeatKind.RoundsKeeper => 55,
        PassageBeatKind.Intercepted or PassageBeatKind.Interception => 50,
        PassageBeatKind.Tackled or PassageBeatKind.PokedAway or PassageBeatKind.LosesBall or PassageBeatKind.Tackle => 45,
        PassageBeatKind.Break => 42,
        PassageBeatKind.TakeOn => 40,
        PassageBeatKind.Beat or PassageBeatKind.Skipped => 38,
        PassageBeatKind.PassOut => 30,
        PassageBeatKind.ThroughBall => 26,
        PassageBeatKind.LongPass => 24,
        PassageBeatKind.Clearance => 22,
        PassageBeatKind.LooseBall => 18,
        PassageBeatKind.HaveBall => 10,
        _ => 20,
    };

    /// <summary>
    /// Chooses the lines the banner shows for a passage: one for every action as it starts (`tick-replay-v4`).
    /// </summary>
    /// <remarks>
    /// A pass is told when it is kicked, and again only if it fails; a completed pass has no line of its own, because the next action is
    /// the next line. Where an outcome is stamped twice (a tackle and the dispossession, a cut-out pass and the misplaced one) it is told
    /// once. A key line (an event, a shot, a save, a header, a cross) holds the banner for <see cref="HighlightOptionsV1.CommentaryKeyDwellMilliseconds"/>,
    /// and a shot's outcome waits <see cref="HighlightOptionsV1.CommentaryStrikeHoldMilliseconds"/> after the strike so "shoots!" can be read.
    /// </remarks>
    private IReadOnlyList<HighlightCommentaryV1> Commentary(Item item)
    {
        var candidates = new List<Candidate>();

        foreach (var matchEvent in item.Events)
        {
            if (!_eventFrame.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            var shown = Math.Clamp(ShownFrameOf(item, matchEvent, frame), item.First, item.Last);
            var time = (shown - item.First) * _msPerFrame;

            candidates.Add(new Candidate
            {
                Time = time,
                Frame = Math.Clamp(frame, item.First, item.Last),
                Priority = EventPriority,
                Kind = PassageBeatKind.Event,
                Event = matchEvent,
                Beat = new PassageBeatV1(time, matchEvent.Side, matchEvent.ParticipantId, PassageBeatKind.Event, matchEvent, matchEvent.Sequence),
            });
        }

        for (var index = 0; index < item.Marks.Count; index++)
        {
            if (StampCandidate(item, index) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        ResolveSameFrame(candidates);
        HoldStrikes(candidates);

        var chosen = Thin(candidates);

        if (item.CutKind is not null && !item.IsHalfTime && chosen.All(line => line.Time >= _options.CommentaryOpenerWithinMilliseconds)
            && Opener(item) is { } opener)
        {
            chosen.Insert(0, opener);
        }

        var ordered = chosen
            .Select(line => line.Beat with { TimeMilliseconds = int.Clamp(line.Time, 0, item.DurationMs) })
            .ToList();

        return [.. CommentaryTokenBuilder.BuildPassageCommentary(_input, ordered, Math.Max(1, item.DurationMs))];
    }

    /// <summary>
    /// The frame an event's line is shown at. A shot that missed is reported when the engine gives up on it, which can be long after the
    /// ball left the pitch; the line is shown on the first frame the ball is outside, when that frame is in the passage.
    /// </summary>
    private int ShownFrameOf(Item item, EngineEventV1 matchEvent, int frame)
    {
        if (matchEvent.Type != EngineEventType.ShotOffTarget)
        {
            return frame;
        }

        var strike = -1;

        foreach (var stamp in item.Marks)
        {
            if (stamp.Frame > frame)
            {
                break;
            }

            if (stamp.Action is PassageAction.Shot or PassageAction.Penalty or PassageAction.FreeKick
                && stamp.Entity >= 0
                && item.Occupants[stamp.Entity] == matchEvent.ParticipantId)
            {
                strike = stamp.Frame;
            }
        }

        if (strike < 0)
        {
            return frame;
        }

        for (var at = strike + 1; at < frame; at++)
        {
            if (_recording.BallX(at) < 0
                || _recording.BallX(at) > SpatialPitch.PitchLength
                || _recording.BallY(at) < 0
                || _recording.BallY(at) > SpatialPitch.PitchWidth)
            {
                return at;
            }
        }

        return frame;
    }

    /// <summary>The line an action stamp starts, or null when the stamp is not narrated or is told by another.</summary>
    private Candidate? StampCandidate(Item item, int index)
    {
        var stamp = item.Marks[index];

        if (stamp.Entity < 0 || item.Occupants[stamp.Entity] == Guid.Empty || CommentaryTokenBuilder.KindOf(stamp.Action, stamp.Detail) is not { } kind)
        {
            return null;
        }

        var actor = item.Occupants[stamp.Entity];
        var side = FilmRoster.SideOf(stamp.Entity);
        var named = stamp.Target >= 0 && item.Occupants[stamp.Target] != Guid.Empty ? item.Occupants[stamp.Target] : (Guid?)null;
        Guid? target = named;

        switch (kind)
        {
            case PassageBeatKind.Pass or PassageBeatKind.LongPass or PassageBeatKind.Cross when target is null:
                // A stamp with no named receiver is told by who took the ball next, and said without a name when nobody did.
                target = Receiver(item, index, side, actor);

                break;

            case PassageBeatKind.Beat when stamp.Action == PassageAction.Carry:
                // The carrier went past the man the stamp names; past the goalkeeper is a different line.
                if (named is null)
                {
                    return null;
                }

                if (_slots[stamp.Target].Family == MatchPositionFamily.Goalkeeper)
                {
                    kind = PassageBeatKind.RoundsKeeper;
                }

                break;

            case PassageBeatKind.Beat:
                // A bypass is stamped on the man left behind, later than the carry that left him; the beat is about the man who went past.
                if (EchoesCarry(item, index) || BypassedBy(stamp) is not { } carrier || item.Occupants[carrier] == Guid.Empty)
                {
                    return null;
                }

                target = actor;
                actor = item.Occupants[carrier];
                side = FilmRoster.SideOf(carrier);

                break;

            case PassageBeatKind.LosesBall:
                // Told with the tackle that took the ball, if there was one.
                if (item.Marks.Any(other => other.Frame == stamp.Frame && other.Action == PassageAction.Tackle && other.Target == stamp.Entity))
                {
                    return null;
                }

                target = named ?? (WinnerOf(item, stamp) is { } won ? item.Occupants[won] : null);

                break;

            case PassageBeatKind.PassOut:
                // A pass cut out is told by the man who cut it out.
                if (item.Marks.Any(other => other.Frame == stamp.Frame && other.Action == PassageAction.Interception && other.Target == stamp.Entity))
                {
                    return null;
                }

                target = null;

                break;

            case PassageBeatKind.SaveHeld or PassageBeatKind.SaveParried or PassageBeatKind.SaveTipped or PassageBeatKind.Save or PassageBeatKind.Clearance:
                target = null;

                break;
        }

        if (target == actor)
        {
            target = null;
        }

        var time = (stamp.Frame - item.First) * _msPerFrame;

        return new Candidate
        {
            Time = time,
            Frame = stamp.Frame,
            Priority = PriorityOf(kind),
            Kind = kind,
            Beat = new PassageBeatV1(time, side, actor, kind, null, item.First + stamp.Frame + index, target),
        };
    }

    /// <summary>Whether a bypass is the carry that left the same man told twice: a carry past him, a moment before.</summary>
    private bool EchoesCarry(Item item, int index)
    {
        var bypass = item.Marks[index];
        var reach = BypassEchoMilliseconds / _msPerFrame;

        for (var before = index - 1; before >= 0 && bypass.Frame - item.Marks[before].Frame <= reach; before--)
        {
            if (item.Marks[before].Action == PassageAction.Carry && item.Marks[before].Target == bypass.Entity)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The slot entity that was on the ball when a man was bypassed, or null when the other side had it.</summary>
    private int? BypassedBy(TickActionStamp bypass)
    {
        var carrier = _recording.Controller(Math.Min(bypass.Frame, _recording.FrameCount - 1));

        return carrier < 0 || FilmRoster.SideOf(carrier) == FilmRoster.SideOf(bypass.Entity) ? null : carrier;
    }

    /// <summary>
    /// Settles the lines that tell one frame twice. An event wins over the action beside it (a foul over the tackle), except that a save
    /// or a block that says how the keeper or the defender stopped it wins over the generic line that reports the same shot.
    /// </summary>
    private static void ResolveSameFrame(List<Candidate> candidates)
    {
        var events = candidates.Where(candidate => candidate.Event is not null).ToList();
        var dropped = new HashSet<Candidate>();

        foreach (var report in events)
        {
            foreach (var action in candidates)
            {
                if (action.Event is not null)
                {
                    continue;
                }

                var saves = report.Event!.Type == EngineEventType.ShotSaved
                    && action.Kind is PassageBeatKind.SaveHeld or PassageBeatKind.SaveParried or PassageBeatKind.SaveTipped
                    && Math.Abs(action.Frame - report.Frame) <= SaveEventFrames;
                var blocks = report.Event.Type == EngineEventType.ShotBlocked
                    && action.Kind == PassageBeatKind.Block
                    && Math.Abs(action.Frame - report.Frame) <= SaveEventFrames;

                if (saves || blocks)
                {
                    dropped.Add(report);
                }
                else if (action.Frame == report.Frame)
                {
                    dropped.Add(action);
                }
            }
        }

        candidates.RemoveAll(dropped.Contains);
    }

    /// <summary>Moves the line that says how a shot ended to a little after the line that says it was struck, so both can be read.</summary>
    private void HoldStrikes(List<Candidate> candidates)
    {
        var strikes = candidates.Where(candidate => candidate.IsStrike).OrderBy(candidate => candidate.Time).ToList();

        foreach (var outcome in candidates.Where(candidate => candidate.IsShotOutcome))
        {
            var strike = strikes.LastOrDefault(shot => shot.Time <= outcome.Time && outcome.Time - shot.Time <= StrikeReachMilliseconds);

            if (strike is not null)
            {
                outcome.Time = Math.Max(outcome.Time, strike.Time + _options.CommentaryStrikeHoldMilliseconds);
            }
        }
    }

    /// <summary>
    /// Thins the lines to those that can be read: a routine line inside a key line's dwell is dropped, and of two lines that fall
    /// closer than the minimum gap the stronger stays, or the later on a tie.
    /// </summary>
    private List<Candidate> Thin(List<Candidate> candidates)
    {
        var ordered = candidates.OrderBy(candidate => candidate.Time).ThenBy(candidate => candidate.Priority).ToList();
        var held = new List<Candidate>(ordered.Count);
        var lastKey = int.MinValue / 2;

        foreach (var candidate in ordered)
        {
            if (candidate.IsKey)
            {
                lastKey = candidate.Time;
            }
            else if (candidate.Time > lastKey && candidate.Time - lastKey < _options.CommentaryKeyDwellMilliseconds)
            {
                continue;
            }

            held.Add(candidate);
        }

        var kept = new List<Candidate>(held.Count);

        foreach (var candidate in held)
        {
            if (kept.Count > 0 && candidate.Time - kept[^1].Time < _options.CommentaryMinGapMilliseconds)
            {
                if (candidate.Priority < kept[^1].Priority)
                {
                    continue;
                }

                kept.RemoveAt(kept.Count - 1);
            }

            kept.Add(candidate);
        }

        return kept;
    }

    /// <summary>
    /// The line that opens a passage the film cut to: the restart about to be taken, or whose ball it is. Without it the banner would
    /// still show the last line of what came before the cut.
    /// </summary>
    private Candidate? Opener(Item item)
    {
        var frame = item.First;
        var state = _recording.State(frame);

        if (state is TickPlayState.GoalCelebration or TickPlayState.HalfTime)
        {
            return null;
        }

        var taker = TakerAt(frame);
        var controller = _recording.Controller(frame);
        var side = taker is { } home ? (home ? MatchSide.Home : MatchSide.Away) : FilmRoster.SideOf(controller >= 0 ? controller : NearestToBall(item, null));
        PassageBeatKind kind;
        int? actor = null;

        if (state == TickPlayState.GoalKickPending)
        {
            kind = PassageBeatKind.GoalKick;
            actor = KeeperOf(item, side);
        }
        else if (state == TickPlayState.FreeKickPending)
        {
            kind = PassageBeatKind.FreeKick;
            actor = NearestToBall(item, side);
        }
        else if (state == TickPlayState.OpenPlay && controller >= 0 && _slots[controller].Family == MatchPositionFamily.Goalkeeper)
        {
            kind = PassageBeatKind.KeeperBall;
            actor = controller;
        }
        else
        {
            kind = PassageBeatKind.HaveBall;
        }

        if (kind != PassageBeatKind.HaveBall && (actor is null || item.Occupants[actor.Value] == Guid.Empty))
        {
            kind = PassageBeatKind.HaveBall;
            actor = null;
        }

        var player = actor is { } entity ? item.Occupants[entity] : (Guid?)null;

        return new Candidate
        {
            Time = 0,
            Frame = frame,
            Priority = PriorityOf(kind),
            Kind = kind,
            Beat = new PassageBeatV1(0, side, player, kind, null, item.First),
        };
    }

    /// <summary>Whether the home side takes the restart that is set up at a frame, or null when none is.</summary>
    private bool? TakerAt(int frame)
    {
        if (_recording.State(frame) is TickPlayState.OpenPlay)
        {
            return null;
        }

        TickStoppageStamp? latest = null;

        foreach (var stoppage in _recording.Stoppages)
        {
            if (stoppage.Frame > frame)
            {
                break;
            }

            latest = stoppage;
        }

        return latest?.TakerIsHome;
    }

    private int? KeeperOf(Item item, MatchSide side)
    {
        for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
        {
            if (FilmRoster.SideOf(entity) == side && _slots[entity].Family == MatchPositionFamily.Goalkeeper && item.Occupants[entity] != Guid.Empty)
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>The outfield slot entity nearest the ball at the passage's first frame, of one side or of either.</summary>
    private int NearestToBall(Item item, MatchSide? side)
    {
        var best = 0;
        var bestSquared = long.MaxValue;

        for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
        {
            if (item.Occupants[entity] == Guid.Empty
                || _recording.PlayerX(item.First, entity) < 0
                || (side is { } wanted && FilmRoster.SideOf(entity) != wanted)
                || _slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            long dx = _recording.PlayerX(item.First, entity) - _recording.BallX(item.First);
            long dy = _recording.PlayerY(item.First, entity) - _recording.BallY(item.First);
            var squared = (dx * dx) + (dy * dy);

            if (squared < bestSquared)
            {
                bestSquared = squared;
                best = entity;
            }
        }

        return best;
    }

    /// <summary>Finds the slot entity that took the ball off a man who lost it: the other side's tackle or interception at the same frame.</summary>
    private static int? WinnerOf(Item item, TickActionStamp loss)
    {
        foreach (var other in item.Marks)
        {
            if (other.Frame == loss.Frame
                && other.Entity >= 0
                && FilmRoster.SideOf(other.Entity) != FilmRoster.SideOf(loss.Entity)
                && other.Action is PassageAction.Tackle or PassageAction.Interception
                && item.Occupants[other.Entity] != Guid.Empty)
            {
                return other.Entity;
            }
        }

        return null;
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

            if (next.Action == PassageAction.Receive
                && next.Detail != TickActionDetail.LooseBall
                && next.Entity >= 0
                && FilmRoster.SideOf(next.Entity) == side)
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
