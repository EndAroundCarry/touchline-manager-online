using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// One passage of a tick film: the run of recorded frames it shows, and the presentation passage they are
/// described as (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// The tick range is the synthesizer's own working state rather than anything the wire carries: the keyframe
/// tracks of the next step sample every tick between <see cref="FirstTick"/> and <see cref="LastTick"/>, and the
/// tests read the ranges to prove the film tiles, joins and jumps exactly where it says it does.
/// </remarks>
/// <param name="FirstTick">The tick the passage's first frame is recorded at, inclusive.</param>
/// <param name="LastTick">
/// The tick the passage's last frame is recorded at, inclusive. Two passages without a cut between them share
/// the frame, so their boundary is one moment of the match rather than two roundings of one.
/// </param>
/// <param name="Passage">The passage as the presentation carries it.</param>
internal sealed record TickFilmPassage(int FirstTick, int LastTick, PassageV1 Passage);

/// <summary>
/// A whole tick match as one film (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// The recording is continuous, so the film is too: every passage picks up at the frame the one before it ends
/// on, played at the one pace <see cref="PaceMilli"/> describes, except where an explicit jump — a kick-off
/// after a goal, the interval — is covered by a cut. Both halves, stoppage and every dead ball are shown, which
/// is what makes the whole match about ten minutes of film at the ten-to-one divisor the plan's viewer is built
/// around (`tick-engine-v1`, Milestone 8).
/// </remarks>
/// <param name="Passages">The passages, in match order.</param>
/// <param name="PaceMilli">The one pace the whole film is played at, in thousandths of real time.</param>
internal sealed record TickReplayFilm(IReadOnlyList<TickFilmPassage> Passages, int PaceMilli)
{
    /// <summary>Gets how long the film runs for, in milliseconds.</summary>
    public int TotalMilliseconds => Passages.Sum(passage => passage.Passage.DurationMilliseconds);
}

/// <summary>
/// Slices the tick engine's continuous 10 Hz recording into the presentation's passages (`tick-engine-v1`,
/// Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// The tick engine needs no reconstructed motion: every player and the ball have a real position at every tick,
/// so a passage is a window of the recording played at a constant pace — ten match seconds to one film second,
/// the divisor that turns a whole match into the roughly ten minutes the 2D viewer is built to show. A
/// passage's entities stand where the recording puts the twenty-three at its first frame, its clock is the match
/// clock the recording stamped on that frame, and its events are the ones the engine emitted between the
/// passage's ticks.
/// </para>
/// <para>
/// The film only jumps where a restart cannot be reached at a speed anybody could run — the walk into a
/// kick-off after a goal, and the interval's restart. Those setups are dropped from the film and the passage
/// that resumes at the kick carries the cut that covers the jump (`replay-v4`). Everything else is shown:
/// corners, goal kicks, throw-ins, free kicks and penalties are part of the match and stay in it.
/// </para>
/// <para>
/// Passages run from a minimum to a maximum length of film, splitting at the natural boundaries the dead-ball
/// stamps give so a passage starts where play is set up again rather than mid-move, and falling back on the
/// maximum when no boundary comes before it. The seam between the halves is always a boundary, because a
/// passage never carries two clocks, and the interval's hold is a passage of its own marked with the
/// <c>half_time</c> outcome the viewer's interval card reads.
/// </para>
/// </remarks>
internal sealed class TickReplaySynthesizer
{
    private readonly MatchState _state;
    private readonly TickMatchRecording _recording;
    private readonly HighlightOptionsV1 _options;
    private readonly int _rung;
    private readonly int _filmMsPerTick;
    private readonly Dictionary<Guid, string> _names;
    private readonly Dictionary<int, int> _dismissals;
    private readonly Dictionary<(int Tick, int Entity), PassageAction> _touches;
    private readonly Dictionary<int, PassageAction> _ballTouches;
    private readonly string _homeColour;
    private readonly string _awayColour;

    private TickReplaySynthesizer(MatchState state, TickMatchRecording recording, HighlightOptionsV1 options, int rung)
    {
        _state = state;
        _recording = recording;
        _options = options;
        _rung = Math.Clamp(rung, 0, Math.Max(0, options.TickPlayerTolerances.Count - 1));
        _filmMsPerTick = Math.Max(1, TickSpatialUnits.TickDeltaMs / Math.Max(1, options.FilmMatchSecondsPerFilmSecond));
        _names = FilmLabels.Names(state.Input);
        _dismissals = Dismissed(state, recording);
        _touches = Touches(recording);
        _ballTouches = BallTouches(recording);
        (_homeColour, _awayColour) = FilmLabels.Colours(state.Input);
    }

    /// <summary>Slices a finished match's recording into the film the presentation carries.</summary>
    /// <param name="state">The match state the recording was made from, for its events, rules and kit colours.</param>
    /// <param name="recording">The continuous trace the loop recorded.</param>
    /// <param name="options">How long a passage may run and how the film is paced.</param>
    /// <param name="rung">
    /// Which rung of the payload ladder the tracks are compressed at. The assembly retries at widening
    /// tolerances until the film fits the presentation's payload budget, deterministically
    /// (`tick-engine-v1`, Milestone 8).
    /// </param>
    public static TickReplayFilm Synthesize(
        MatchState state,
        TickMatchRecording recording,
        HighlightOptionsV1? options = null,
        int rung = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(recording);

        return new TickReplaySynthesizer(state, recording, options ?? new HighlightOptionsV1(), rung).Slice();
    }

    private TickReplayFilm Slice()
    {
        var halfTime = HalfTimeTick();
        var (skips, cuts) = Jumps(halfTime);
        var slices = Chunk(skips);
        var events = OwnedEvents(slices);
        var tolerance = _options.TickPlayerTolerances[_rung];
        var interval = _options.TickPlayerSampleIntervals[Math.Min(_rung, _options.TickPlayerSampleIntervals.Count - 1)];
        var passages = new List<TickFilmPassage>(slices.Count);

        for (var index = 0; index < slices.Count; index++)
        {
            passages.Add(new TickFilmPassage(
                slices[index].FirstTick,
                slices[index].LastTick,
                Describe(slices[index], events[index], cuts, halfTime, interval, tolerance)));
        }

        return new TickReplayFilm(passages, _options.FilmMatchSecondsPerFilmSecond * 1_000);
    }

    /// <summary>Gets the tick the interval began at, or -1 when the match never reached half-time.</summary>
    private int HalfTimeTick()
    {
        foreach (var stamp in _recording.Restarts)
        {
            if (stamp.Phase == TickMatchPhase.HalfTime)
            {
                return stamp.Tick;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the setups the film jumps rather than plays: the walk into a kick-off after a goal and the interval
    /// restart are dropped, and the passage that resumes at the kick carries the cut that covers the jump.
    /// </summary>
    /// <param name="halfTime">The tick the interval began at, or -1.</param>
    private (List<(int Start, int End)> Skips, Dictionary<int, string> Cuts) Jumps(int halfTime)
    {
        var skips = new List<(int Start, int End)>();
        var cuts = new Dictionary<int, string>();
        var restarts = _recording.Restarts;

        for (var index = 1; index < restarts.Count; index++)
        {
            var stamp = restarts[index];

            if (stamp.Phase != TickMatchPhase.KickOffPending)
            {
                continue;
            }

            var previous = restarts[index - 1];

            if (previous.Phase is not (TickMatchPhase.GoalCelebration or TickMatchPhase.HalfTime))
            {
                continue;
            }

            var kick = stamp.Tick + TickMatchStateMachine.HoldTicksFor(stamp.Phase) - 1;

            if (kick >= _recording.TickCount)
            {
                // The whistle went before the restart was taken, so there is nothing to jump to.
                continue;
            }

            if (previous.Phase == TickMatchPhase.HalfTime)
            {
                skips.Add((stamp.Tick, kick));
                cuts[kick] = "half_time";

                continue;
            }

            if (halfTime > stamp.Tick && halfTime <= kick)
            {
                // The half ended during the setup, so the kick-off never came and the film runs into the interval.
                skips.Add((stamp.Tick, halfTime));

                continue;
            }

            skips.Add((stamp.Tick, kick));
            cuts[kick] = "kick_off";
        }

        skips.Sort((left, right) => left.Start.CompareTo(right.Start));

        return (skips, cuts);
    }

    /// <summary>Groups the kept ticks into passages of about the target length, at the payload's cap on how many there may be.</summary>
    /// <param name="skips">The spans the film jumps.</param>
    private List<TickFilmSlice> Chunk(List<(int Start, int End)> skips)
    {
        var stretches = Stretches(skips);
        var breaks = PeriodBreaks();
        var candidates = new HashSet<int>(_recording.Restarts.Select(stamp => stamp.Tick));
        var minimum = _options.MinPassageMilliseconds;
        var slices = new List<TickFilmSlice>();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var maximum = minimum + (_options.MaxPassageMilliseconds - _options.MinPassageMilliseconds);

            slices = Group(stretches, breaks, candidates, minimum, maximum);

            if (slices.Count <= _options.MaxPassages)
            {
                break;
            }

            minimum += 1_500;
        }

        return slices;
    }

    /// <summary>Gets the runs of ticks the film keeps: everything the jumps leave.</summary>
    /// <param name="skips">The spans the film jumps, in order.</param>
    private List<(int Start, int End)> Stretches(List<(int Start, int End)> skips)
    {
        var stretches = new List<(int Start, int End)>();
        var start = 0;

        foreach (var (skipStart, skipEnd) in skips)
        {
            if (skipStart > start)
            {
                stretches.Add((start, skipStart - 1));
            }

            start = Math.Max(start, skipEnd);
        }

        if (start < _recording.TickCount)
        {
            stretches.Add((start, _recording.TickCount - 1));
        }

        // A stretch of a single tick would be a passage that shows no movement at all; it is dropped rather than filmed.
        stretches.RemoveAll(stretch => stretch.End == stretch.Start);

        return stretches;
    }

    /// <summary>Gets the ticks a new half begins at, where the clock jumps back to 45:00.</summary>
    private HashSet<int> PeriodBreaks()
    {
        var breaks = new HashSet<int>();

        for (var tick = 1; tick < _recording.TickCount; tick++)
        {
            if (_recording.PeriodAt(tick) != _recording.PeriodAt(tick - 1))
            {
                breaks.Add(tick);
            }
        }

        return breaks;
    }

    /// <summary>
    /// Splits one run of ticks into passages: a boundary is forced at the half's seam, taken at a dead ball once
    /// the passage has run its minimum, and taken regardless once it has reached its maximum.
    /// </summary>
    /// <param name="stretches">The runs of kept ticks.</param>
    /// <param name="breaks">The ticks a passage may not span.</param>
    /// <param name="candidates">The ticks a dead ball is set up at.</param>
    /// <param name="minimumMs">The shortest a passage runs before a natural boundary is taken.</param>
    /// <param name="maximumMs">The longest a passage may grow to.</param>
    private List<TickFilmSlice> Group(
        List<(int Start, int End)> stretches,
        HashSet<int> breaks,
        HashSet<int> candidates,
        int minimumMs,
        int maximumMs)
    {
        var slices = new List<TickFilmSlice>();

        foreach (var (start, end) in stretches)
        {
            var first = start;

            for (var tick = start + 1; tick <= end; tick++)
            {
                var elapsed = (tick - first) * _filmMsPerTick;

                if (breaks.Contains(tick))
                {
                    // The half's seam is not shared: the frame before it closes one clock and the frame at it opens the other.
                    slices.Add(new TickFilmSlice(first, tick - 1));
                    first = tick;

                    continue;
                }

                if ((candidates.Contains(tick) && elapsed >= minimumMs) || elapsed >= maximumMs)
                {
                    slices.Add(new TickFilmSlice(first, tick));
                    first = tick;
                }
            }

            slices.Add(new TickFilmSlice(first, end));
        }

        return slices;
    }

    /// <summary>Gives every recorded event its passage: the frame two passages share belongs to the one that ends on it.</summary>
    /// <param name="slices">The passages, in film order.</param>
    private List<List<EngineEventV1>> OwnedEvents(List<TickFilmSlice> slices)
    {
        var owned = new List<List<EngineEventV1>>(slices.Count);

        for (var index = 0; index < slices.Count; index++)
        {
            owned.Add([]);
        }

        foreach (var stamp in _recording.Events)
        {
            owned[Owner(slices, stamp.Tick)].Add(_state.Events[stamp.Sequence - 1]);
        }

        return owned;
    }

    /// <summary>Gets the index of the passage a tick's events belong to.</summary>
    /// <param name="slices">The passages, in film order.</param>
    /// <param name="tick">The tick.</param>
    private static int Owner(List<TickFilmSlice> slices, int tick)
    {
        // The whistle after the last frame belongs to the passage that was playing when it went.
        var owner = slices.Count - 1;

        for (var index = 0; index < slices.Count; index++)
        {
            if (tick <= slices[index].LastTick)
            {
                owner = tick >= slices[index].FirstTick ? index : Math.Max(0, index - 1);

                break;
            }
        }

        return owner;
    }

    /// <summary>Describes one window of the recording as the presentation's passage.</summary>
    /// <param name="slice">The window.</param>
    /// <param name="events">The events the window owns.</param>
    /// <param name="cuts">The jump each tick that resumes the film after one carries.</param>
    /// <param name="halfTime">The tick the interval began at, or -1.</param>
    /// <param name="intervalMs">How often the players are sampled, in milliseconds of film.</param>
    /// <param name="tolerance">How far a dropped player keyframe may sit from the line that replaces it.</param>
    private PassageV1 Describe(
        TickFilmSlice slice,
        List<EngineEventV1> events,
        Dictionary<int, string> cuts,
        int halfTime,
        int intervalMs,
        int tolerance)
    {
        var first = slice.FirstTick;
        var last = slice.LastTick;
        var duration = (last - first) * _filmMsPerTick;
        var period = _recording.PeriodAt(first);
        var startSecond = _recording.SecondAt(first);
        var endSecond = Math.Max(startSecond, _recording.SecondAt(last));

        var inPlay = events.Where(matchEvent => !matchEvent.IsPeriodBoundary && matchEvent.Type != EngineEventType.Substitution).ToList();
        var principal = inPlay.FirstOrDefault(matchEvent => matchEvent.IsGoal)
            ?? inPlay.FirstOrDefault(matchEvent => FilmLabels.IsShot(matchEvent.Type))
            ?? inPlay.FirstOrDefault();

        var isHalfTime = first == halfTime;
        var (minute, stoppage) = principal is null
            ? FilmLabels.ClockOf(startSecond, period, _state.Rules)
            : (principal.Minute, principal.StoppageMinute);

        IReadOnlyList<PassageCutV1> passageCuts = cuts.TryGetValue(first, out var kind)
            ? [new PassageCutV1(0, _options.CutMilliseconds, kind)]
            : [];

        return new PassageV1
        {
            PresentationVersion = ReplayDirector.Version,
            SourceEventSequence = principal?.Sequence ?? 0,
            Minute = minute,
            StoppageMinute = stoppage,
            Period = period,
            StartMatchSecond = startSecond,
            EndMatchSecond = endSecond,
            Clock = ClockOf(duration, startSecond, endSecond),
            Cuts = passageCuts,
            DurationMilliseconds = duration,
            OutcomeCode = isHalfTime ? "half_time" : principal is null ? "play" : FilmLabels.OutcomeCode(principal.Type),
            Narration = isHalfTime ? "Half-time." : FilmLabels.Narration(principal, _names, minute, stoppage),
            HomeColour = _homeColour,
            AwayColour = _awayColour,
            EventSequences = [.. events.Select(matchEvent => matchEvent.Sequence)],
            Entities = Entities(first),
            Tracks = Tracks(first, last, intervalMs, tolerance),
        };
    }

    /// <summary>Gets the passage's match clock: two points, because a tick is always a tenth of a second of it.</summary>
    /// <param name="duration">The passage's film length, in milliseconds.</param>
    /// <param name="startSecond">The match second at the passage's first frame.</param>
    /// <param name="endSecond">The match second at the passage's last frame.</param>
    private static IReadOnlyList<ClockKeyframeV1> ClockOf(int duration, int startSecond, int endSecond) =>
        duration > 0
            ? [new ClockKeyframeV1(0, startSecond), new ClockKeyframeV1(duration, endSecond)]
            : [new ClockKeyframeV1(0, startSecond)];

    /// <summary>Builds the passage's entities: the twenty-two players and the ball, where the recording's first frame puts them.</summary>
    /// <param name="tick">The passage's first tick.</param>
    private List<HighlightEntityV1> Entities(int tick)
    {
        var entities = new List<HighlightEntityV1>(TickMatchRecording.EntityCount);

        foreach (var entity in _recording.Entities)
        {
            if (entity.IsBall)
            {
                continue;
            }

            // A sent-off player is not on the pitch to be drawn; the trace keeps writing his slot's last position
            // because the loop packs the eleven, and that ghost is left out of the film.
            if (_dismissals.TryGetValue(entity.Index, out var dismissed) && tick >= dismissed)
            {
                continue;
            }

            var point = FilmSpace.FromEngine(_recording.XAt(tick, entity.Index), _recording.YAt(tick, entity.Index));

            entities.Add(new HighlightEntityV1
            {
                EntityId = FilmRoster.EntityId(entity.Index),
                IsBall = false,
                Side = entity.Side,
                ParticipantId = entity.ParticipantId,
                ShirtNumber = entity.ShirtNumber,
                Family = entity.Family,
                X = FilmSpace.NormalizeX(point.X),
                Y = FilmSpace.NormalizeY(point.Y),
                Name = entity.DisplayName,
                Position = PositionCode(entity.Family),
            });
        }

        var ball = FilmSpace.FromEngine(
            _recording.XAt(tick, TickMatchRecording.BallEntityIndex),
            _recording.YAt(tick, TickMatchRecording.BallEntityIndex));

        entities.Add(new HighlightEntityV1
        {
            EntityId = "ball",
            IsBall = true,
            X = FilmSpace.NormalizeX(ball.X),
            Y = FilmSpace.NormalizeY(ball.Y),
        });

        entities.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));

        return entities;
    }

    /// <summary>Builds the passage's tracks: one per drawn entity, sampled, action-tagged and compressed.</summary>
    /// <remarks>
    /// The recording is ten samples a second, which is finer than any display needs: the tracks are sampled at
    /// the payload ladder's interval, kept wherever a player touched the ball, and compressed to the points
    /// where the movement actually changes. That is the delta compression the plan asks for (`tick-engine-v1`,
    /// Milestone 8, ADR-0006), and a semantic touch is never dropped because the compressor keeps every
    /// keyframe that carries an action.
    /// </remarks>
    /// <param name="first">The passage's first tick.</param>
    /// <param name="last">The passage's last tick.</param>
    /// <param name="intervalMs">How often a player is sampled, in film milliseconds.</param>
    /// <param name="tolerance">How far a dropped player keyframe may sit from the line that replaces it.</param>
    private List<HighlightTrackV1> Tracks(int first, int last, int intervalMs, int tolerance)
    {
        var tracks = new List<HighlightTrackV1>(TickMatchRecording.EntityCount);

        foreach (var entity in _recording.Entities)
        {
            if (entity.IsBall)
            {
                continue;
            }

            // The tracks describe the entities the passage draws, so a sent-off player the entities leave out
            // is left out of the tracks too.
            if (_dismissals.TryGetValue(entity.Index, out var dismissed) && first >= dismissed)
            {
                continue;
            }

            tracks.Add(new HighlightTrackV1(
                FilmRoster.EntityId(entity.Index),
                TrackOf(entity.Index, first, last, intervalMs, tolerance)));
        }

        tracks.Add(new HighlightTrackV1(
            "ball",
            TrackOf(TickMatchRecording.BallEntityIndex, first, last, intervalMs, _options.BallTolerance)));
        tracks.Sort((left, right) => string.CompareOrdinal(left.EntityId, right.EntityId));

        return tracks;
    }

    /// <summary>Builds one entity's compressed track through a passage.</summary>
    /// <param name="entity">The entity's index in the recording.</param>
    /// <param name="first">The passage's first tick.</param>
    /// <param name="last">The passage's last tick.</param>
    /// <param name="intervalMs">How often the entity is sampled on the ground, in film milliseconds.</param>
    /// <param name="tolerance">How far a dropped keyframe may sit from the line that replaces it.</param>
    private List<HighlightKeyframeV1> TrackOf(int entity, int first, int last, int intervalMs, int tolerance)
    {
        var ball = entity == TickMatchRecording.BallEntityIndex;
        var step = Math.Max(1, intervalMs / _filmMsPerTick);
        var airStep = Math.Max(1, _options.BallAirSampleMilliseconds / _filmMsPerTick);
        var samples = new List<HighlightKeyframeV1>(((last - first) / step) + 2);

        for (var tick = first; tick <= last; tick++)
        {
            var offset = tick - first;
            var wanted = offset == 0
                || tick == last
                || offset % step == 0
                || (ball && offset % airStep == 0 && _recording.ZAt(tick, entity) > 0)
                || _touches.ContainsKey((tick, entity));

            if (!wanted)
            {
                continue;
            }

            var point = FilmSpace.FromEngine(_recording.XAt(tick, entity), _recording.YAt(tick, entity));

            samples.Add(new HighlightKeyframeV1(
                offset * _filmMsPerTick,
                FilmSpace.NormalizeX(point.X),
                FilmSpace.NormalizeY(point.Y),
                ball ? _recording.ZAt(tick, entity) : 0,
                Action: ActionOf(tick, entity)));
        }

        return KeyframeCompressor.Compress(samples, tolerance);
    }

    /// <summary>
    /// Gets the action a keyframe carries: the touch the entity made at that tick, or for the ball the touch
    /// that struck it, so a strike is visible on the ball's own track as well as the striker's.
    /// </summary>
    /// <param name="tick">The tick.</param>
    /// <param name="entity">The entity.</param>
    private string? ActionOf(int tick, int entity)
    {
        if (entity != TickMatchRecording.BallEntityIndex && _touches.TryGetValue((tick, entity), out var own))
        {
            return own.Code();
        }

        return _ballTouches.TryGetValue(tick, out var striking) ? striking.Code() : null;
    }

    /// <summary>Indexes every touch by tick and entity, so a keyframe can carry the action it belongs to.</summary>
    /// <param name="recording">The recording.</param>
    private static Dictionary<(int Tick, int Entity), PassageAction> Touches(TickMatchRecording recording)
    {
        var touches = new Dictionary<(int Tick, int Entity), PassageAction>(recording.Touches.Count);

        foreach (var touch in recording.Touches)
        {
            touches.TryAdd((touch.Tick, touch.EntityIndex), touch.Action);
        }

        return touches;
    }

    /// <summary>Indexes the first touch at each tick, which is the action the ball's own keyframe carries.</summary>
    /// <param name="recording">The recording.</param>
    private static Dictionary<int, PassageAction> BallTouches(TickMatchRecording recording)
    {
        var touches = new Dictionary<int, PassageAction>(recording.Touches.Count);

        foreach (var touch in recording.Touches)
        {
            touches.TryAdd(touch.Tick, touch.Action);
        }

        return touches;
    }

    /// <summary>Gets the abbreviated position a family is labelled with, as the film's entities are.</summary>
    /// <param name="family">The position family.</param>
    private static string PositionCode(MatchPositionFamily family) => family switch
    {
        MatchPositionFamily.Goalkeeper => "GK",
        MatchPositionFamily.Defence => "DF",
        MatchPositionFamily.Midfield => "MF",
        MatchPositionFamily.Attack => "FW",
        _ => string.Empty,
    };

    /// <summary>Gets the tick each sent-off player left the pitch at, by entity.</summary>
    /// <param name="state">The match state, for the events.</param>
    /// <param name="recording">The recording, for the stamps.</param>
    private static Dictionary<int, int> Dismissed(MatchState state, TickMatchRecording recording)
    {
        var entities = new Dictionary<Guid, int>();

        foreach (var entity in recording.Entities)
        {
            if (!entity.IsBall)
            {
                entities[entity.ParticipantId] = entity.Index;
            }
        }

        var dismissals = new Dictionary<int, int>();

        foreach (var stamp in recording.Events)
        {
            var matchEvent = state.Events[stamp.Sequence - 1];

            if (matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard
                && matchEvent.ParticipantId is Guid participant
                && entities.TryGetValue(participant, out var index))
            {
                dismissals[index] = stamp.Tick;
            }
        }

        return dismissals;
    }

    /// <summary>One window of the recorded ticks, before it is described as a passage.</summary>
    /// <param name="FirstTick">The window's first tick, inclusive.</param>
    /// <param name="LastTick">The window's last tick, inclusive.</param>
    private readonly record struct TickFilmSlice(int FirstTick, int LastTick);
}
