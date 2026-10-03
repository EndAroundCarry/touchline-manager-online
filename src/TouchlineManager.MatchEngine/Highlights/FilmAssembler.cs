using System.Globalization;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>One passage of the film, planned but not yet sampled (`replay-v4`).</summary>
internal sealed class FilmPassagePlan
{
    /// <summary>Gets the index of the passage's first beat.</summary>
    public required int FirstBeat { get; init; }

    /// <summary>Gets the index of the passage's last beat.</summary>
    public required int LastBeat { get; init; }

    /// <summary>Gets where the passage starts on the film clock, in milliseconds.</summary>
    public required int StartMs { get; init; }

    /// <summary>Gets where the passage ends on the film clock, in milliseconds.</summary>
    public required int EndMs { get; init; }

    /// <summary>Gets the half the passage is played in.</summary>
    public required int Period { get; init; }

    /// <summary>Gets who is on the pitch for the passage.</summary>
    public required FilmRoster Roster { get; init; }

    /// <summary>Gets how long the passage runs for, in milliseconds.</summary>
    public int DurationMs => EndMs - StartMs;

    /// <summary>Gets or sets the sequence of the principal event, or zero.</summary>
    public int SourceEventSequence { get; set; }

    /// <summary>Gets or sets the outcome code.</summary>
    public string OutcomeCode { get; set; } = "play";

    /// <summary>Gets or sets the narration.</summary>
    public string Narration { get; set; } = string.Empty;

    /// <summary>Gets or sets the minute the passage is labelled with.</summary>
    public int Minute { get; set; }

    /// <summary>Gets or sets the stoppage minute the passage is labelled with.</summary>
    public int StoppageMinute { get; set; }

    /// <summary>Gets or sets the match second the passage starts at.</summary>
    public int StartMatchSecond { get; set; }

    /// <summary>Gets or sets the match second the passage ends at.</summary>
    public int EndMatchSecond { get; set; }

    /// <summary>Gets the events the passage shows, in sequence order.</summary>
    public List<int> EventSequences { get; } = [];

    /// <summary>Gets the passage's commentary.</summary>
    public List<HighlightCommentaryV1> Commentary { get; } = [];

    /// <summary>Gets the passage's match clock.</summary>
    public List<ClockKeyframeV1> Clock { get; } = [];

    /// <summary>Gets the passage's cuts.</summary>
    public List<PassageCutV1> Cuts { get; } = [];

    /// <summary>Gets the entities, ordered by identifier.</summary>
    public List<HighlightEntityV1> Entities { get; } = [];
}

/// <summary>
/// The film's match clock: where the match clock stands at every moment of the film (`replay-v4`).
/// </summary>
/// <remarks>
/// A piecewise-linear function from film milliseconds to a half and a match second on that half's own clock. A
/// possession contributes its start, the second it reached at its first event — so the minute on the scoreboard is
/// the minute the event was stamped with — and its end.
/// </remarks>
internal sealed class FilmClock
{
    private readonly List<(int Ms, int Period, int Second)> _points = [];

    /// <summary>Adds a point, keeping the clock strictly forward in film time.</summary>
    /// <param name="ms">The film time.</param>
    /// <param name="period">The half.</param>
    /// <param name="second">The match second.</param>
    public void Add(int ms, int period, int second)
    {
        if (_points.Count > 0)
        {
            var last = _points[^1];

            if (last.Period == period)
            {
                ms = Math.Max(ms, last.Ms + 1);
                second = Math.Max(second, last.Second);
            }
            else
            {
                ms = Math.Max(ms, last.Ms + 1);
            }
        }

        _points.Add((ms, period, second));
    }

    /// <summary>Gets the match second at a film time, on a half's own clock.</summary>
    /// <param name="ms">The film time.</param>
    /// <param name="period">The half the asker is in, which decides which clock a moment on the seam between two halves reads.</param>
    public int SecondAt(int ms, int period)
    {
        if (_points.Count == 0)
        {
            return 0;
        }

        if (ms <= _points[0].Ms)
        {
            return _points[0].Second;
        }

        for (var index = 1; index < _points.Count; index++)
        {
            var previous = _points[index - 1];
            var next = _points[index];

            if (ms > next.Ms)
            {
                continue;
            }

            if (next.Period != previous.Period)
            {
                return period == next.Period ? next.Second : previous.Second;
            }

            var span = Math.Max(1, next.Ms - previous.Ms);

            return previous.Second + (int)((long)(next.Second - previous.Second) * (ms - previous.Ms) / span);
        }

        return _points[^1].Second;
    }

    /// <summary>Gets the points that fall strictly inside a window, as keyframes relative to its start.</summary>
    /// <param name="startMs">The window's start.</param>
    /// <param name="endMs">The window's end.</param>
    /// <param name="period">The half the window is in.</param>
    public List<ClockKeyframeV1> Window(int startMs, int endMs, int period)
    {
        var keyframes = new List<ClockKeyframeV1> { new(0, SecondAt(startMs, period)) };

        foreach (var (ms, _, second) in _points)
        {
            if (ms <= startMs || ms >= endMs)
            {
                continue;
            }

            var relative = ms - startMs;
            var last = keyframes[^1];

            if (relative <= last.TimeMilliseconds)
            {
                continue;
            }

            keyframes.Add(new ClockKeyframeV1(relative, Math.Max(second, last.MatchSecond)));
        }

        var end = Math.Max(SecondAt(endMs, period), keyframes[^1].MatchSecond);
        var duration = endMs - startMs;

        if (duration > keyframes[^1].TimeMilliseconds)
        {
            keyframes.Add(new ClockKeyframeV1(duration, end));
        }
        else
        {
            keyframes[^1] = keyframes[^1] with { MatchSecond = end };
        }

        return keyframes;
    }

    /// <summary>Gets the film time at which a half's clock reaches a match second, or the half's start when it never does.</summary>
    /// <param name="period">The half.</param>
    /// <param name="second">The match second on that half's clock.</param>
    public int FilmAt(int period, int second)
    {
        var found = -1;
        var first = -1;

        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];

            if (point.Period != period)
            {
                continue;
            }

            if (first < 0)
            {
                first = point.Ms;
            }

            if (point.Second >= second)
            {
                if (index > 0 && _points[index - 1].Period == period && _points[index - 1].Second < second)
                {
                    var previous = _points[index - 1];
                    var span = Math.Max(1, point.Second - previous.Second);

                    return previous.Ms + (int)((long)(point.Ms - previous.Ms) * (second - previous.Second) / span);
                }

                found = point.Ms;

                break;
            }
        }

        return found >= 0 ? found : Math.Max(0, first);
    }
}

/// <summary>
/// Cuts the film into passages, gives each its match clock and its commentary, and samples it (`replay-v4`).
/// </summary>
internal sealed class FilmAssembler
{
    private readonly FilmContext _context;
    private readonly FilmScriptResult _script;
    private readonly FilmMotionResult _motion;
    private readonly IReadOnlyList<FilmRoster> _rosters;
    private readonly double _pace;
    private readonly int[] _startMs;
    private readonly int[] _endMs;
    private readonly int[] _limitMs;

    /// <summary>Initializes the assembler.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="script">The script.</param>
    /// <param name="motion">The motion, played at the pace.</param>
    /// <param name="rosters">Who is on the pitch for each possession.</param>
    /// <param name="pace">The one pace the film is played at.</param>
    public FilmAssembler(
        FilmContext context,
        FilmScriptResult script,
        FilmMotionResult motion,
        IReadOnlyList<FilmRoster> rosters,
        double pace)
    {
        _context = context;
        _script = script;
        _motion = motion;
        _rosters = rosters;
        _pace = pace;
        _startMs = new int[script.Beats.Count];
        _endMs = new int[script.Beats.Count];

        for (var index = 0; index < script.Beats.Count; index++)
        {
            _startMs[index] = ToMs(motion.Spans[index].StartSeconds);
            _endMs[index] = ToMs(motion.Spans[index].EndSeconds);
        }

        // Until the film is cut into passages, nothing is shown later than the film ends.
        _limitMs = new int[script.Beats.Count];
        Array.Fill(_limitMs, TotalMs);
    }

    /// <summary>Gets the film's match clock, built when the passages were planned.</summary>
    public FilmClock Clock { get; } = new();

    /// <summary>Gets where a beat starts on the film clock, in milliseconds.</summary>
    /// <param name="beat">The beat's index.</param>
    public int BeatStartMs(int beat) => _startMs[beat];

    /// <summary>Gets where a beat ends on the film clock, in milliseconds.</summary>
    /// <param name="beat">The beat's index.</param>
    public int BeatEndMs(int beat) => _endMs[beat];

    /// <summary>Gets how long the whole film runs for, in milliseconds.</summary>
    public int TotalMs => _script.Beats.Count == 0 ? 0 : _endMs[^1];

    private int ToMs(double seconds) => (int)Math.Round(seconds * 1000.0 / _pace);

    // ---- Planning ---------------------------------------------------------------------------------------------

    /// <summary>Cuts the film into passages and fills in everything but the tracks.</summary>
    public List<FilmPassagePlan> Plan()
    {
        var groups = Chunk();

        // A passage shows nothing that happens after it ends, so the clock and the commentary are cut to it.
        foreach (var (first, last) in groups)
        {
            Array.Fill(_limitMs, EndOf(last), first, last - first + 1);
        }

        BuildClock();

        var plans = new List<FilmPassagePlan>(groups.Count);

        foreach (var (first, last) in groups)
        {
            var beat = _script.Beats[first];
            var roster = beat.Possession >= 0 ? _rosters[beat.Possession] : (plans.Count > 0 ? plans[^1].Roster : _context.Starters);

            var plan = new FilmPassagePlan
            {
                FirstBeat = first,
                LastBeat = last,
                StartMs = _startMs[first],
                EndMs = EndOf(last),
                Period = beat.Period,
                Roster = roster,
            };

            Describe(plan);
            plans.Add(plan);
        }

        // The passages tile the film, so each one ends where the next begins.
        for (var index = 0; index + 1 < plans.Count; index++)
        {
            if (plans[index].EndMs != plans[index + 1].StartMs)
            {
                plans[index] = ReEnd(plans[index], plans[index + 1].StartMs);
            }
        }

        foreach (var plan in plans)
        {
            AddEntities(plan);
        }

        return plans;
    }

    private static FilmPassagePlan ReEnd(FilmPassagePlan plan, int endMs) => new()
    {
        FirstBeat = plan.FirstBeat,
        LastBeat = plan.LastBeat,
        StartMs = plan.StartMs,
        EndMs = endMs,
        Period = plan.Period,
        Roster = plan.Roster,
        SourceEventSequence = plan.SourceEventSequence,
        OutcomeCode = plan.OutcomeCode,
        Narration = plan.Narration,
        Minute = plan.Minute,
        StoppageMinute = plan.StoppageMinute,
        StartMatchSecond = plan.StartMatchSecond,
        EndMatchSecond = plan.EndMatchSecond,
    };

    /// <summary>Gets where a passage whose last beat is the given one ends.</summary>
    private int EndOf(int last) =>
        last == _script.Beats.Count - 1 ? TotalMs : _startMs[last + 1] is var next && next >= _endMs[last] ? next : _endMs[last];

    /// <summary>
    /// Whether a strike in the last stretch of a run of beats is still too fresh to have its outcome read: the line that
    /// says where it went follows the strike by the commentary delay, and a passage that ended sooner would lose it.
    /// </summary>
    private bool StrikeTailOpen(int first, int last)
    {
        var delay = _context.Options.CommentaryOutcomeDelayMilliseconds;

        for (var index = last; index >= first; index--)
        {
            if (_endMs[last] - _startMs[index] >= delay)
            {
                return false;
            }

            if (_script.Beats[index].Kind == BeatKind.Shot)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Groups the beats into passages of about the target length, split at cuts, half-time and personnel changes.</summary>
    private List<(int First, int Last)> Chunk()
    {
        var options = _context.Options;
        var beats = _script.Beats;
        var minimum = options.MinPassageMilliseconds;
        List<(int, int)> groups = [];

        for (var attempt = 0; attempt < 8; attempt++)
        {
            groups = Chunk(minimum, minimum + (options.MaxPassageMilliseconds - options.MinPassageMilliseconds));

            if (groups.Count <= options.MaxPassages || beats.Count == 0)
            {
                break;
            }

            minimum += 1_500;
        }

        return groups;
    }

    private List<(int First, int Last)> Chunk(int minimumMs, int maximumMs)
    {
        var beats = _script.Beats;
        var groups = new List<(int, int)>();
        var first = 0;
        var deferred = false;

        for (var index = 1; index < beats.Count; index++)
        {
            var previous = beats[index - 1];
            var beat = beats[index];
            var elapsed = _endMs[index - 1] - _startMs[first];
            var newPossession = beat.Possession != previous.Possession;

            var forced = beat.Cut
                || beat.Hold == HoldKind.HalfTime
                || previous.Hold == HoldKind.HalfTime
                || (beat.Possession >= 0 && previous.Possession >= 0 && newPossession && !ReferenceEquals(_rosters[beat.Possession], _rosters[previous.Possession]));

            var natural = newPossession && elapsed >= minimumMs;
            var overlong = elapsed >= maximumMs;

            // A split that would leave a strike's outcome unread waits for the beat that gives it its 0.6 s.
            var wanted = natural || overlong || deferred;

            if (forced || (wanted && !StrikeTailOpen(first, index - 1)))
            {
                groups.Add((first, index - 1));
                first = index;
                deferred = false;
            }
            else
            {
                deferred = wanted;
            }
        }

        if (beats.Count > 0)
        {
            groups.Add((first, beats.Count - 1));
        }

        return groups;
    }

    // ---- The match clock --------------------------------------------------------------------------------------

    private void BuildClock()
    {
        var beats = _script.Beats;
        var halfTimes = new Queue<int>(Enumerable.Range(0, beats.Count).Where(index => beats[index].Hold == HoldKind.HalfTime));

        // Half-time: the clock stands where the first half ended. The points go in film order, among the possessions'.
        void AddHalfTimesBefore(int beatIndex)
        {
            while (halfTimes.Count > 0 && halfTimes.Peek() < beatIndex)
            {
                var index = halfTimes.Dequeue();
                var period = beats[index].Period;
                var ended = _script.Possessions.LastOrDefault(possession => possession.Source.Period == period);

                if (ended is not null)
                {
                    Clock.Add(_startMs[index], period, ended.Source.EndClockSeconds);
                }
            }
        }

        foreach (var possession in _script.Possessions)
        {
            var owned = Enumerable.Range(possession.FirstBeat, possession.LastBeat - possession.FirstBeat + 1)
                .Where(index => beats[index].Possession == possession.Index)
                .ToList();

            if (owned.Count == 0)
            {
                continue;
            }

            AddHalfTimesBefore(owned[0]);

            var source = possession.Source;
            var start = source.StartClockSeconds;
            var end = Math.Max(start, source.EndClockSeconds);

            // A substitution shown as the possession begins is stamped with the minute the possession ends in, so
            // the clock is brought up to the start of that minute while it is shown.
            foreach (var index in owned)
            {
                foreach (var matchEvent in beats[index].Events)
                {
                    if (_context.EventsBySequence.TryGetValue(matchEvent.Sequence, out var found)
                        && found.Type == EngineEventType.Substitution)
                    {
                        start = Math.Max(start, Math.Min(end, FloorSecond(found, source.Period)));
                    }
                }
            }

            var firstEvent = int.MaxValue;
            var lastEvent = 0;

            foreach (var index in owned)
            {
                foreach (var matchEvent in beats[index].Events)
                {
                    if (_context.EventsBySequence.TryGetValue(matchEvent.Sequence, out var found)
                        && found.Type != EngineEventType.Substitution)
                    {
                        var shown = EventMs(index, matchEvent);

                        firstEvent = Math.Min(firstEvent, shown);
                        lastEvent = Math.Max(lastEvent, shown);
                    }
                }
            }

            var from = _startMs[owned[0]];
            var to = _endMs[owned[^1]];

            Clock.Add(from, source.Period, start);

            if (firstEvent != int.MaxValue)
            {
                Clock.Add(Math.Max(firstEvent, from + 1), source.Period, end);
            }

            // An outcome is read up to 0.6 s after its strike, which can be after the possession has ended: the clock
            // stands where the possession ended until then, so the minute it reads is the one the event was stamped with.
            Clock.Add(Math.Max(Math.Max(to, lastEvent), from + 2), source.Period, end);
        }

        AddHalfTimesBefore(int.MaxValue);
    }

    /// <summary>Gets the first match second of the minute an event was stamped in.</summary>
    private int FloorSecond(EngineEventV1 matchEvent, int period)
    {
        var rules = _context.Rules;
        var regulation = period == 1 ? rules.HalfTimeMinute : rules.RegulationMinutes;
        var minutes = matchEvent.StoppageMinute > 0
            ? regulation + matchEvent.StoppageMinute - 1
            : matchEvent.Minute - 1;

        return minutes * rules.SecondsPerMinute;
    }

    /// <summary>Gets when an event is shown, on the film clock.</summary>
    /// <param name="beatIndex">The beat the event is attached to.</param>
    /// <param name="matchEvent">The event.</param>
    public int EventMs(int beatIndex, BeatEvent matchEvent) => Math.Min(RawEventMs(beatIndex, matchEvent), _limitMs[beatIndex]);

    private int RawEventMs(int beatIndex, BeatEvent matchEvent)
    {
        var beat = _script.Beats[beatIndex];
        var delay = _context.Options.CommentaryOutcomeDelayMilliseconds;

        // The line that says where a strike went follows the strike, and is never ahead of the ball: when the outcome
        // is on the hold or the save that follows the shot, it is read 0.6 s after the shot was struck.
        if (beatIndex > 0 && _script.Beats[beatIndex - 1].Kind == BeatKind.Shot && (beat.Hold == HoldKind.Goal || beat.Kind == BeatKind.Save))
        {
            return Math.Max(_startMs[beatIndex], _startMs[beatIndex - 1] + delay);
        }

        if (matchEvent.AtStart)
        {
            return _startMs[beatIndex];
        }

        return beat.Kind == BeatKind.Shot
            ? Math.Max(_endMs[beatIndex], _startMs[beatIndex] + delay)
            : _endMs[beatIndex];
    }

    // ---- Describing a passage ---------------------------------------------------------------------------------

    private void Describe(FilmPassagePlan plan)
    {
        var beats = _script.Beats;
        var events = new List<EngineEventV1>();

        for (var index = plan.FirstBeat; index <= plan.LastBeat; index++)
        {
            foreach (var matchEvent in beats[index].Events)
            {
                if (_context.EventsBySequence.TryGetValue(matchEvent.Sequence, out var found) && !plan.EventSequences.Contains(found.Sequence))
                {
                    events.Add(found);
                    plan.EventSequences.Add(found.Sequence);
                }
            }
        }

        plan.EventSequences.Sort();
        events = [.. events.OrderBy(matchEvent => matchEvent.Sequence)];

        var inPlay = events.Where(matchEvent => !matchEvent.IsPeriodBoundary && matchEvent.Type != EngineEventType.Substitution).ToList();
        var principal = inPlay.FirstOrDefault(matchEvent => matchEvent.IsGoal)
            ?? inPlay.FirstOrDefault(matchEvent => FilmLabels.IsShot(matchEvent.Type))
            ?? inPlay.FirstOrDefault();

        plan.Clock.AddRange(Clock.Window(plan.StartMs, plan.EndMs, plan.Period));
        plan.StartMatchSecond = plan.Clock[0].MatchSecond;
        plan.EndMatchSecond = plan.Clock[^1].MatchSecond;

        var (minute, stoppage) = principal is null
            ? FilmLabels.ClockOf(plan.StartMatchSecond, plan.Period, _context.Rules)
            : (principal.Minute, principal.StoppageMinute);

        plan.Minute = minute;
        plan.StoppageMinute = stoppage;
        plan.SourceEventSequence = principal?.Sequence ?? 0;

        var halfTime = beats[plan.FirstBeat].Hold == HoldKind.HalfTime;

        plan.OutcomeCode = halfTime ? "half_time" : principal is null ? "play" : FilmLabels.OutcomeCode(principal.Type);
        plan.Narration = halfTime
            ? "Half-time."
            : FilmLabels.Narration(principal, FilmLabels.Names(_context.Input), minute, stoppage);

        if (beats[plan.FirstBeat].Cut)
        {
            plan.Cuts.Add(new PassageCutV1(0, _context.Options.CutMilliseconds, beats[plan.FirstBeat].CutKind ?? "kick_off"));
        }

        plan.Commentary.AddRange(CommentaryFor(plan));
    }

    private void AddEntities(FilmPassagePlan plan)
    {
        var first = plan.FirstBeat;
        var record = _motion.Spans[first].FirstRecord;
        var names = FilmLabels.Names(_context.Input);
        var colours = FilmLabels.Colours(_context.Input);
        _ = colours;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!plan.Roster.IsOccupied(entity))
            {
                continue;
            }

            var side = FilmRoster.SideOf(entity);
            var participantId = plan.Roster.Occupants[entity];
            var participant = _context.ParticipantOf(participantId);
            var slot = _context.Slots[entity];

            plan.Entities.Add(new HighlightEntityV1
            {
                EntityId = FilmRoster.EntityId(entity),
                IsBall = false,
                Side = side,
                ParticipantId = participantId,
                ShirtNumber = participant?.ShirtNumber ?? 0,
                Family = slot.Family,
                X = FilmSpace.NormalizeX(_motion.PlayerX(record, entity)),
                Y = FilmSpace.NormalizeY(_motion.PlayerY(record, entity)),
                Name = names.TryGetValue(participantId, out var name) ? name : null,
                Position = FilmLabels.PositionCode(slot),
            });
        }

        plan.Entities.Add(new HighlightEntityV1
        {
            EntityId = "ball",
            IsBall = true,
            X = FilmSpace.NormalizeX(_motion.BallX(record)),
            Y = FilmSpace.NormalizeY(_motion.BallY(record)),
        });

        plan.Entities.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));
    }

    // ---- Commentary --------------------------------------------------------------------------------------------

    private List<HighlightCommentaryV1> CommentaryFor(FilmPassagePlan plan)
    {
        var options = _context.Options;
        var beats = _script.Beats;
        var candidates = new List<(int Time, int Priority, PassageBeatV1 Beat)>();
        var seed = plan.FirstBeat * 97;

        for (var index = plan.FirstBeat; index <= plan.LastBeat; index++)
        {
            var beat = beats[index];
            var start = _startMs[index] - plan.StartMs;

            foreach (var recorded in beat.Events)
            {
                if (!_context.EventsBySequence.TryGetValue(recorded.Sequence, out var found))
                {
                    continue;
                }

                var time = EventMs(index, recorded) - plan.StartMs;

                candidates.Add((time, 100, new PassageBeatV1(time, found.Side, found.ParticipantId, PassageBeatKind.Event, found, found.Sequence)));
            }

            var kind = BuildUpKind(beat);

            if (kind is { } narrated && beat.Actor is Guid actor)
            {
                var priority = narrated switch
                {
                    PassageBeatKind.Chance => 80,
                    PassageBeatKind.Cross => 70,
                    PassageBeatKind.Header or PassageBeatKind.Save => 60,
                    PassageBeatKind.Interception => 50,
                    PassageBeatKind.GoalKick or PassageBeatKind.KeeperBall or PassageBeatKind.FreeKick => 45,
                    PassageBeatKind.Carry => 30,
                    _ => 20,
                };

                var who = narrated == PassageBeatKind.Interception && beat.Receiver is Guid interceptor ? interceptor : actor;

                candidates.Add((start, priority, new PassageBeatV1(start, beat.Side, who, narrated, null, seed + index)));
            }
        }

        var accepted = new List<(int Time, PassageBeatV1 Beat)>();

        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.Time))
        {
            var always = candidate.Priority >= 60;

            if (always || accepted.All(other => Math.Abs(other.Time - candidate.Time) >= options.CommentaryGapMilliseconds))
            {
                accepted.Add((candidate.Time, candidate.Beat));
            }
        }

        var ordered = accepted
            .OrderBy(line => line.Time)
            .Select(line => line.Beat with { TimeMilliseconds = int.Clamp(line.Time, 0, plan.DurationMs) })
            .ToList();

        return [.. CommentaryTokenBuilder.BuildPassageCommentary(_context.Input, ordered, Math.Max(1, plan.DurationMs))];
    }

    private static PassageBeatKind? BuildUpKind(FilmBeat beat) => beat.Kind switch
    {
        BeatKind.Pass or BeatKind.LoftedPass => PassageBeatKind.Pass,
        BeatKind.Carry => PassageBeatKind.Carry,
        BeatKind.Cross => PassageBeatKind.Cross,
        BeatKind.Header => PassageBeatKind.Header,
        BeatKind.Shot => PassageBeatKind.Chance,
        BeatKind.Save => PassageBeatKind.Save,
        BeatKind.Duel when beat.Foul => null,
        BeatKind.Duel when beat.Receiver is not null && beat.Receiver != beat.Actor => PassageBeatKind.Interception,
        BeatKind.Duel => PassageBeatKind.Carry,
        BeatKind.Hold when beat.Hold is HoldKind.GoalKick => PassageBeatKind.GoalKick,
        BeatKind.Hold when beat.Hold is HoldKind.KeeperBall => PassageBeatKind.KeeperBall,
        BeatKind.Hold when beat.Hold is HoldKind.FreeKick && beat.Formation == FormationMode.Open => PassageBeatKind.FreeKick,
        _ => null,
    };

    // ---- Tracks ------------------------------------------------------------------------------------------------

    /// <summary>Samples and compresses a passage's tracks at one rung of the payload ladder.</summary>
    /// <param name="plan">The passage.</param>
    /// <param name="playerIntervalMs">How often the players are sampled, in milliseconds of film.</param>
    /// <param name="playerTolerance">How far a dropped player keyframe may sit from its line.</param>
    public List<HighlightTrackV1> Tracks(FilmPassagePlan plan, int playerIntervalMs, int playerTolerance)
    {
        var options = _context.Options;
        var tracks = new List<HighlightTrackV1>(FilmRoster.Size + 1);
        var marks = Marks(plan);
        var firstRecord = _motion.Spans[plan.FirstBeat].FirstRecord;
        var lastRecord = _motion.Spans[plan.LastBeat].LastRecord;

        // Players: on a regular grid, and wherever an action is tagged.
        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!plan.Roster.IsOccupied(entity))
            {
                continue;
            }

            var times = GridTimes(plan.DurationMs, playerIntervalMs, marks.Where(mark => mark.Entity == entity).Select(mark => mark.Ms));
            var samples = new List<HighlightKeyframeV1>(times.Count);
            var cursor = firstRecord;

            foreach (var time in times)
            {
                var (x, y, _) = PlayerAt(entity, plan, time, firstRecord, lastRecord, ref cursor);
                var action = marks.FirstOrDefault(mark => mark.Entity == entity && mark.Ms == time).Action;

                samples.Add(new HighlightKeyframeV1(time, FilmSpace.NormalizeX(x), FilmSpace.NormalizeY(y), Action: action));
            }

            tracks.Add(new HighlightTrackV1(
                FilmRoster.EntityId(entity),
                KeyframeCompressor.Compress(samples, playerTolerance)));
        }

        // The ball: on the grid on the ground, finely while it is in the air, at every beat, and at every action.
        var ballTimes = BallTimes(plan, playerIntervalMs, options.BallAirSampleMilliseconds, marks);
        var ballSamples = new List<HighlightKeyframeV1>(ballTimes.Count);
        var ballCursor = firstRecord;

        foreach (var time in ballTimes)
        {
            var (x, y, z) = BallAtMs(plan, time, firstRecord, lastRecord, ref ballCursor);
            var action = marks.FirstOrDefault(mark => mark.Entity == -1 && mark.Ms == time).Action;

            ballSamples.Add(new HighlightKeyframeV1(
                time,
                FilmSpace.NormalizeX(x),
                FilmSpace.NormalizeY(y),
                int.Clamp((int)Math.Round(z), 0, 100),
                Action: action));
        }

        tracks.Add(new HighlightTrackV1("ball", KeyframeCompressor.Compress(ballSamples, options.BallTolerance)));
        tracks.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));

        return tracks;
    }

    private (double X, double Y, double Z) BallAtMs(FilmPassagePlan plan, int relativeMs, int firstRecord, int lastRecord, ref int cursor)
    {
        var (index, next, fraction) = Locate(plan, relativeMs, firstRecord, lastRecord, ref cursor);

        return (
            Lerp(_motion.BallX(index), _motion.BallX(next), fraction),
            Lerp(_motion.BallY(index), _motion.BallY(next), fraction),
            Lerp(_motion.BallZ(index), _motion.BallZ(next), fraction));
    }

    private (double X, double Y, double Z) PlayerAt(int entity, FilmPassagePlan plan, int relativeMs, int firstRecord, int lastRecord, ref int cursor)
    {
        var (index, next, fraction) = Locate(plan, relativeMs, firstRecord, lastRecord, ref cursor);

        return (
            Lerp(_motion.PlayerX(index, entity), _motion.PlayerX(next, entity), fraction),
            Lerp(_motion.PlayerY(index, entity), _motion.PlayerY(next, entity), fraction),
            0.0);
    }

    /// <summary>
    /// Finds the two records a passage-relative film time falls between. The passage's own first and last moments
    /// are its first and last records exactly, so that consecutive passages join on the same state rather than on
    /// two roundings of the same instant.
    /// </summary>
    private (int Index, int Next, double Fraction) Locate(FilmPassagePlan plan, int relativeMs, int firstRecord, int lastRecord, ref int cursor)
    {
        if (relativeMs <= 0)
        {
            cursor = firstRecord;

            return (firstRecord, firstRecord, 0.0);
        }

        if (relativeMs >= plan.DurationMs)
        {
            cursor = lastRecord;

            return (lastRecord, lastRecord, 0.0);
        }

        var seconds = ((plan.StartMs + relativeMs) * _pace) / 1000.0;

        cursor = Advance(cursor, seconds, firstRecord, lastRecord);

        return Bracket(cursor, seconds, lastRecord);
    }

    private int Advance(int cursor, double seconds, int firstRecord, int lastRecord)
    {
        if (cursor < firstRecord || seconds < _motion.TimeOf(cursor))
        {
            cursor = firstRecord;
        }

        while (cursor < lastRecord && _motion.TimeOf(cursor + 1) <= seconds)
        {
            cursor++;
        }

        return cursor;
    }

    private (int Index, int Next, double Fraction) Bracket(int cursor, double seconds, int lastRecord)
    {
        if (cursor >= lastRecord)
        {
            return (lastRecord, lastRecord, 0.0);
        }

        var from = _motion.TimeOf(cursor);
        var to = _motion.TimeOf(cursor + 1);
        var span = to - from;
        var fraction = span <= 1e-9 ? 0.0 : double.Clamp((seconds - from) / span, 0.0, 1.0);

        return (cursor, cursor + 1, fraction);
    }

    private static double Lerp(float from, float to, double fraction) => from + ((to - from) * fraction);

    private static List<int> GridTimes(int duration, int interval, IEnumerable<int> extra)
    {
        var times = new SortedSet<int>();

        for (var time = 0; time < duration; time += interval)
        {
            times.Add(time);
        }

        times.Add(duration);

        foreach (var time in extra)
        {
            times.Add(int.Clamp(time, 0, duration));
        }

        return [.. times];
    }

    private List<int> BallTimes(FilmPassagePlan plan, int groundInterval, int airInterval, List<(int Entity, int Ms, string Action)> marks)
    {
        var times = new SortedSet<int> { 0, plan.DurationMs };

        for (var index = plan.FirstBeat; index <= plan.LastBeat; index++)
        {
            var beat = _script.Beats[index];
            var start = int.Clamp(_startMs[index] - plan.StartMs, 0, plan.DurationMs);
            var end = int.Clamp(_endMs[index] - plan.StartMs, 0, plan.DurationMs);
            var airborne = !beat.IsHold && (beat.ZArc > 0 || beat.ZFrom > 0 || beat.ZTo > 0);
            var interval = airborne ? airInterval : groundInterval;

            times.Add(start);
            times.Add(end);

            for (var time = start + interval; time < end; time += interval)
            {
                times.Add(time);
            }
        }

        foreach (var mark in marks.Where(mark => mark.Entity == -1))
        {
            times.Add(int.Clamp(mark.Ms, 0, plan.DurationMs));
        }

        return [.. times];
    }

    /// <summary>The action tags a passage carries: the ball's beat kinds, and what each player does at them.</summary>
    private List<(int Entity, int Ms, string Action)> Marks(FilmPassagePlan plan)
    {
        var marks = new List<(int, int, string)>();

        for (var index = plan.FirstBeat; index <= plan.LastBeat; index++)
        {
            var beat = _script.Beats[index];
            var start = int.Clamp(_startMs[index] - plan.StartMs, 0, plan.DurationMs);
            var end = int.Clamp(_endMs[index] - plan.StartMs, 0, plan.DurationMs);

            if (beat.Possession < 0)
            {
                continue;
            }

            var roster = plan.Roster;
            var ballAction = BallAction(beat);

            if (ballAction is not null)
            {
                marks.Add((-1, start, ballAction));
            }

            AddMark(marks, roster, beat.Actor, start, ActorAction(beat));

            switch (beat.Kind)
            {
                case BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Cross:
                    AddMark(marks, roster, beat.Receiver, end, "receive");
                    break;

                case BeatKind.Duel:
                    AddMark(marks, roster, beat.Opponent, end, beat.Receiver == beat.Opponent ? "interception" : "tackle");
                    break;

                case BeatKind.Header:
                    AddMark(marks, roster, beat.Opponent, start, "header");
                    break;

                case BeatKind.Shot when beat.Strike is StrikeResult.Goal or StrikeResult.Woodwork:
                    AddMark(marks, roster, beat.Opponent, start, "dive");
                    break;

                default:
                    break;
            }

            if (beat.Hold == HoldKind.Goal)
            {
                AddMark(marks, roster, beat.Actor, start, "celebrate");
            }
        }

        return marks
            .GroupBy(mark => (mark.Item1, mark.Item2))
            .Select(group => group.First())
            .ToList();
    }

    private static void AddMark(List<(int, int, string)> marks, FilmRoster roster, Guid? participant, int ms, string? action)
    {
        if (action is null || participant is not Guid id)
        {
            return;
        }

        var entity = roster.EntityOf(id);

        if (entity >= 0)
        {
            marks.Add((entity, ms, action));
        }
    }

    private static string? BallAction(FilmBeat beat) => beat.Kind switch
    {
        BeatKind.Carry or BeatKind.Duel => "carry",
        BeatKind.Pass or BeatKind.LoftedPass => "pass",
        BeatKind.Cross => "cross",
        BeatKind.Shot => "shot",
        BeatKind.Clearance => "clearance",
        BeatKind.Placement => "restart",
        BeatKind.Header => "header",
        BeatKind.Save => "save",
        _ => null,
    };

    private static string? ActorAction(FilmBeat beat) => beat.Kind switch
    {
        BeatKind.Carry or BeatKind.Duel => "carry",
        BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Clearance => "pass",
        BeatKind.Cross => "cross",
        BeatKind.Shot => "shot",
        BeatKind.Header => "header",
        BeatKind.Save => "save",
        _ => null,
    };

    /// <summary>Estimates the serialized size of the film from its parts, the way the passages do.</summary>
    internal static string Describe(int minute, int stoppage) =>
        stoppage > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minute}+{stoppage}")
            : minute.ToString(CultureInfo.InvariantCulture);
}
