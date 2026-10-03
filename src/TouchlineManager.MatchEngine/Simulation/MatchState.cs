using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// The whole mutable state of a match in progress: the clock, the two sides, and the event log.
/// </summary>
/// <remarks>
/// One object owns the clock and the event sequence so that every event is stamped from one place. A
/// simulation that let each subsystem decide its own minute is a simulation whose event times go backwards
/// at half-time, and "event times are ordered" is one of the invariants the property suite checks.
/// </remarks>
internal sealed class MatchState
{
    private int _sequence;
    private int _halfEventStoppageSeconds;
    private int _halfStoppageJitterSeconds;
    private int _metricMinute = -1;
    private int _ratingMinute = -1;
    private int _metricStart;

    /// <summary>Initializes the state for one match.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The seeded generator.</param>
    /// <param name="home">The home side's runtime state.</param>
    /// <param name="away">The away side's runtime state.</param>
    public MatchState(
        MatchInputV1 input,
        EngineRulesV2 rules,
        Pcg32 random,
        SideRuntime home,
        SideRuntime away)
    {
        Input = input;
        Rules = rules;
        Random = random;
        Home = home;
        Away = away;
    }

    /// <summary>
    /// Gets the sink the replay's live metrics are captured into, when one was supplied (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// Null for every ordinary simulation — the result does not depend on it, which is the point: the
    /// recorder is a by-product of the same run rather than a second mode of simulation.
    /// </remarks>
    internal PlayerLiveMetricsRecorder? LiveMetrics { get; init; }

    /// <summary>
    /// Gets the sink the replay's ball paths and touches are captured into, when one was supplied
    /// (`engine-v4`).
    /// </summary>
    /// <remarks>
    /// Null for every ordinary simulation — the result does not depend on it, which is the point: the
    /// recorder is a by-product of the same run rather than a second mode of simulation, and the geometry
    /// that fills it is drawn from a per-possession stream of its own so it can never move a play draw.
    /// </remarks>
    internal MatchPassageRecorder? Passages { get; init; }

    /// <summary>Gets or sets the 1-based ordinal of the possession being played (`engine-v4`).</summary>
    /// <remarks>
    /// The ordinal is what the per-possession geometry stream is derived from, so it must advance whether or
    /// not a recorder is attached: the same seed must produce the same ball path and the same outcome, with or
    /// without a replay reading it.
    /// </remarks>
    internal int PossessionOrdinal { get; set; }

    /// <summary>
    /// Gets or sets the dead ball the next possession must be played from, when there is one (`engine-v5`).
    /// </summary>
    /// <remarks>
    /// Set by the kick-offs and by every outcome that stops play — a goal, a save, a miss, a foul, an offside —
    /// and cleared by the very next possession, which takes the restart's side without drawing one. It replaces
    /// the two engine-v4 flags, which left a goal-area restart pending until that side next had the ball.
    /// </remarks>
    internal PendingRestart? NextRestart { get; set; }

    /// <summary>Sets the kick-off the conceding side takes from the centre spot after a goal (`engine-v5`).</summary>
    /// <param name="scorer">The side that scored.</param>
    public void RestartAfterGoal(MatchSide scorer) =>
        NextRestart = new PendingRestart(MatchInputV1.OpponentOf(scorer), PassageRestartKind.KickOff, SpatialPoint.Center);

    /// <summary>
    /// Sets the keeper's ball or goal kick the defending side takes from its own goal area after a save or a
    /// miss (`engine-v5`).
    /// </summary>
    /// <param name="defendingSide">The side restarting.</param>
    /// <param name="kind">Whether the keeper has the ball or it went out for a goal kick.</param>
    public void RestartFromGoalArea(MatchSide defendingSide, PassageRestartKind kind) =>
        NextRestart = new PendingRestart(defendingSide, kind, PassagePlanner.GoalAreaSpot(defendingSide, Rules));

    /// <summary>Sets the free kick a side takes where play was stopped for a foul or an offside (`engine-v5`).</summary>
    /// <param name="takingSide">The side taking the free kick.</param>
    /// <param name="spot">Where it is taken from.</param>
    public void RestartWithFreeKick(MatchSide takingSide, SpatialPoint spot) =>
        NextRestart = new PendingRestart(takingSide, PassageRestartKind.FreeKick, spot);

    private PassageAccumulator? _passage;

    /// <summary>Gets the frozen snapshot.</summary>
    public MatchInputV1 Input { get; }

    /// <summary>Gets the rules in force.</summary>
    public EngineRulesV2 Rules { get; }

    /// <summary>Gets the seeded generator, the only source of randomness in the engine.</summary>
    public Pcg32 Random { get; }

    /// <summary>Gets the home side's runtime state.</summary>
    public SideRuntime Home { get; }

    /// <summary>Gets the away side's runtime state.</summary>
    public SideRuntime Away { get; }

    /// <summary>Gets every event emitted so far, in sequence order.</summary>
    public List<EngineEventV1> Events { get; } = [];

    /// <summary>Gets or sets the elapsed match time, in seconds.</summary>
    public int ClockSeconds { get; set; }

    /// <summary>Gets whether the first half is in progress.</summary>
    public bool InFirstHalf { get; set; } = true;

    /// <summary>
    /// Gets the minute the substitution planner last looked at.
    /// </summary>
    /// <remarks>
    /// Possessions run for thirty-odd seconds, so a minute can be skipped entirely and a planner that only
    /// asked "is it minute 58?" would sometimes never make the change it wanted to. Asking whether a window
    /// fell anywhere in the minutes that just elapsed makes the planner's windows robust to how long a
    /// possession happened to take.
    /// </remarks>
    public int LastPlannerMinute { get; set; }

    /// <summary>
    /// Where the ball is on the normalized pitch, and how high it is (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// The simulation is not a physics engine — the ball moves with the play rather than the play moving
    /// the ball — but its position is a fact the play produces, and the highlight director consumes the
    /// same coordinates. Ground level is the rule: only a strike or a cross lifts the ball, which keeps
    /// the spatial model honest about what it is for.
    /// </remarks>
    public BallState Ball { get; private set; } = BallState.Center;

    /// <summary>Moves the ball to a new ground position.</summary>
    /// <param name="position">The position, in pitch coordinates.</param>
    public void MoveBall(SpatialPoint position) => Ball = new BallState(position.X, position.Y);

    /// <summary>Moves the ball to a new position at an altitude, for a strike or a cross.</summary>
    /// <param name="position">The position, in pitch coordinates.</param>
    /// <param name="altitude">The altitude, 0..100.</param>
    public void MoveBall(SpatialPoint position, int altitude) =>
        Ball = new BallState(position.X, position.Y, int.Clamp(altitude, 0, 100));

    /// <summary>
    /// Moves the ball along the passage being played and records the point it reached (`engine-v4`).
    /// </summary>
    /// <param name="position">The position, in pitch coordinates.</param>
    /// <param name="kind">How the ball travelled to this point.</param>
    /// <param name="altitude">The altitude, 0..100; zero for a ground touch.</param>
    public void MoveBallAndRecord(SpatialPoint position, PassageWaypointKind kind, int altitude = 0)
    {
        MoveBall(position, altitude);
        _passage?.AddWaypoint(Ball.X, Ball.Y, Ball.Z, kind);
    }

    /// <summary>
    /// Records a player's touch of the ball at its current position (`engine-v4`).
    /// </summary>
    /// <param name="participantId">The player who touched it.</param>
    /// <param name="action">What they did with it.</param>
    public void RecordTouch(Guid participantId, PassageAction action) =>
        _passage?.AddTouch(participantId, action, Ball.X, Ball.Y, Ball.Z);

    /// <summary>Opens a new passage for the possession that is about to be played (`engine-v4`).</summary>
    /// <param name="side">The side in possession.</param>
    /// <param name="startClockSeconds">
    /// The match second the possession started at, which the caller reads before the clock advances so that
    /// the possessions tile each half (`engine-v5`).
    /// </param>
    /// <param name="restart">The dead-ball restart the possession began with, or none.</param>
    public void BeginPassage(MatchSide side, int startClockSeconds, PassageRestartKind restart)
    {
        _passage = new PassageAccumulator(
            PossessionOrdinal,
            side,
            InFirstHalf ? 1 : 2,
            startClockSeconds,
            restart);

        // The possession's pass ledger starts empty whether or not a recorder is attached (`engine-v7`).
        Passing = new PossessionPassing(side);
    }

    /// <summary>Gets the passes the possession being played has made so far (`engine-v7`).</summary>
    /// <remarks>
    /// A tally the possession's phases add to and <see cref="PassTally"/> settles into players when it ends.
    /// It is bookkeeping only: it reads no draw and writes nothing the match depends on.
    /// </remarks>
    internal PossessionPassing Passing { get; private set; } = new(MatchSide.Home);

    /// <summary>
    /// Closes the current passage and hands it to the recorder, when one is attached (`engine-v4`).
    /// </summary>
    /// <remarks>
    /// Called once per possession, after the ball has reached the point the possession ends at, so the next
    /// passage can begin where this one left it.
    /// </remarks>
    /// <param name="outcome">How the possession ended (`engine-v5`).</param>
    public void EndPassage(PassageOutcome outcome)
    {
        if (_passage is null)
        {
            return;
        }

        Passages?.Add(_passage.Build(ClockSeconds, outcome));
        _passage = null;
    }

    /// <summary>Gets how much stoppage the first half was given, once it has ended.</summary>
    public int FirstHalfStoppageSeconds { get; private set; }

    /// <summary>Gets how much stoppage the second half was given, once it has ended.</summary>
    public int SecondHalfStoppageSeconds { get; private set; }

    /// <summary>Gets the currently displayed match minute.</summary>
    public int Minute
    {
        get
        {
            var played = ClockSeconds / Rules.SecondsPerMinute;

            if (InFirstHalf)
            {
                return played < Rules.HalfTimeMinute ? played + 1 : Rules.HalfTimeMinute;
            }

            return played < Rules.RegulationMinutes ? played + 1 : Rules.RegulationMinutes;
        }
    }

    /// <summary>Gets the minute of stoppage time, or zero while the half is inside its regulation time.</summary>
    public int StoppageMinute
    {
        get
        {
            var played = ClockSeconds / Rules.SecondsPerMinute;

            if (InFirstHalf)
            {
                return played < Rules.HalfTimeMinute ? 0 : played + 1 - Rules.HalfTimeMinute;
            }

            return played < Rules.RegulationMinutes ? 0 : played + 1 - Rules.RegulationMinutes;
        }
    }

    /// <summary>Gets the second the current half's regulation time ends.</summary>
    public int HalfRegulationEndSeconds => InFirstHalf
        ? Rules.HalfTimeMinute * Rules.SecondsPerMinute
        : Rules.RegulationMinutes * Rules.SecondsPerMinute;

    /// <summary>Gets how much stoppage the current half has accumulated, clamped to the rules' bounds.</summary>
    public int HalfStoppageSeconds => int.Clamp(
        Rules.StoppageBaseSeconds + _halfStoppageJitterSeconds + _halfEventStoppageSeconds,
        Rules.MinStoppageMinutes * Rules.SecondsPerMinute,
        Rules.MaxStoppageMinutes * Rules.SecondsPerMinute);

    /// <summary>Gets whether the current half has played itself out, stoppage included.</summary>
    public bool HalfIsOver => ClockSeconds >= HalfRegulationEndSeconds + HalfStoppageSeconds;

    /// <summary>Gets the home or away side.</summary>
    /// <param name="side">Which end.</param>
    public SideRuntime SideOf(MatchSide side) => side == MatchSide.Home ? Home : Away;

    /// <summary>Gets the side that is not the given one.</summary>
    /// <param name="side">Which end.</param>
    public SideRuntime OpponentOf(MatchSide side) => side == MatchSide.Home ? Away : Home;

    /// <summary>
    /// Begins a half, drawing the stoppage jitter that makes two halves end differently.
    /// </summary>
    /// <remarks>
    /// The second half starts from the half-time minute, not from wherever the first half's stoppage left the
    /// clock (`engine-v5`, `MAT-3`). Each half has its own stoppage on top of its own regulation time, so the
    /// second half runs 46'…90' and then its own added time; carrying the clock across instead started the
    /// half at about 48' and ended its regulation a few minutes early.
    /// </remarks>
    /// <param name="firstHalf">Whether the half beginning is the first.</param>
    public void BeginHalf(bool firstHalf)
    {
        InFirstHalf = firstHalf;
        _halfEventStoppageSeconds = 0;
        _halfStoppageJitterSeconds = Random.NextInt(Rules.StoppageJitterSeconds + 1);

        if (!firstHalf)
        {
            ClockSeconds = Rules.HalfTimeMinute * Rules.SecondsPerMinute;
        }
    }

    /// <summary>
    /// Ends the current half, recording the stoppage it was given so the result can report the total.
    /// </summary>
    public void EndHalf()
    {
        if (InFirstHalf)
        {
            FirstHalfStoppageSeconds = HalfStoppageSeconds;
        }
        else
        {
            SecondHalfStoppageSeconds = HalfStoppageSeconds;
        }
    }

    /// <summary>Adds the stoppage an event is worth, which is how the clock is topped up realistically.</summary>
    /// <param name="seconds">The seconds to add.</param>
    public void AddStoppage(int seconds) => _halfEventStoppageSeconds += Math.Max(0, seconds);

    /// <summary>Records stoppage for a goal.</summary>
    public void AddGoalStoppage() => AddStoppage(Rules.StoppageSecondsPerGoal);

    /// <summary>Records stoppage for a card.</summary>
    public void AddCardStoppage() => AddStoppage(Rules.StoppageSecondsPerCard);

    /// <summary>Records stoppage for a substitution.</summary>
    public void AddSubstitutionStoppage() => AddStoppage(Rules.StoppageSecondsPerSubstitution);

    /// <summary>Records stoppage for an injury.</summary>
    public void AddInjuryStoppage() => AddStoppage(Rules.StoppageSecondsPerInjury);

    /// <summary>
    /// Emits an event at the current clock position, stamping it with the next sequence number and the
    /// ball's current location on the pitch (`engine-v3`).
    /// </summary>
    /// <param name="side">Which side it belongs to.</param>
    /// <param name="type">What happened.</param>
    /// <param name="participantId">The principal participant, if any.</param>
    /// <param name="secondaryParticipantId">The second participant, if any.</param>
    /// <param name="zone">Where it happened, if it was a shot or set piece.</param>
    /// <param name="qualityBasisPoints">How good a chance it was, on shot events.</param>
    /// <param name="absenceFixtures">How long an injury rules a player out, if it was an injury.</param>
    /// <param name="substitutionReason">Why a substitution was made, if it was one.</param>
    /// <param name="at">
    /// Where the event happened when that is not where the ball is now (`engine-v5`): a shot event is stamped
    /// where the shot was struck from, although by the time it is emitted the ball has travelled to its target.
    /// </param>
    /// <returns>The event, so a caller can count it without re-reading the log.</returns>
    public EngineEventV1 Emit(
        MatchSide side,
        EngineEventType type,
        Guid? participantId = null,
        Guid? secondaryParticipantId = null,
        ShotZone? zone = null,
        int? qualityBasisPoints = null,
        int? absenceFixtures = null,
        MatchSubstitutionReason? substitutionReason = null,
        SpatialPoint? at = null)
    {
        var matchEvent = new EngineEventV1
        {
            Sequence = ++_sequence,
            Minute = Minute,
            StoppageMinute = StoppageMinute,
            Side = side,
            ClubId = SideOf(side).ClubId,
            Type = type,
            ParticipantId = participantId,
            SecondaryParticipantId = secondaryParticipantId,
            Zone = zone,
            QualityBasisPoints = qualityBasisPoints,
            AbsenceFixtures = absenceFixtures,
            SubstitutionReason = substitutionReason,
            X = at?.X ?? Ball.X,
            Y = at?.Y ?? Ball.Y,
        };

        Events.Add(matchEvent);

        // An event that happens during a passage is a fact of that passage; a period boundary is not, because
        // no passage is open when it is emitted.
        _passage?.AddEvent(matchEvent.Sequence);

        return matchEvent;
    }

    /// <summary>
    /// Captures every player on the pitch's condition and live rating when the clock has reached a new
    /// minute (`engine-v3`, §9.5).
    /// </summary>
    /// <remarks>
    /// Called after each possession, because a possession is longer than a minute and there is no finer
    /// moment to sample: each minute the play passed through ends holding the condition and rating the
    /// players had when it did. A minute captured again later in the same run replaces its earlier rows
    /// rather than adding to them, so a minute carries one bar per player and it is the state that minute
    /// ended in. A player who came on during the possession is captured from it; a player who left is not
    /// captured by it, so a substitution's minute is the first minute the substitute's bar is drawn from
    /// and the last minute the departing player's is.
    /// </remarks>
    public void CaptureLiveMetrics()
    {
        if (LiveMetrics is null)
        {
            return;
        }

        var minute = Minute;

        if (minute == _metricMinute)
        {
            LiveMetrics.Truncate(_metricStart);
        }
        else
        {
            _metricMinute = minute;
            _metricStart = LiveMetrics.Count;
        }

        foreach (var side in new[] { Home, Away })
        {
            foreach (var slot in side.Active)
            {
                LiveMetrics.Record(
                    slot.Participant.ParticipantId,
                    minute,
                    slot.Condition.ConditionBasisPoints,
                    slot.LiveRatingBasisPoints);
            }
        }
    }

    /// <summary>Gets the total minutes played, regulation plus both halves' stoppage.</summary>
    public int TotalMinutesPlayed =>
        Rules.RegulationMinutes
        + ((FirstHalfStoppageSeconds + SecondHalfStoppageSeconds) / Rules.SecondsPerMinute);

    /// <summary>
    /// Refreshes both sides' unit ratings, once per minute of play (`engine-v6`).
    /// </summary>
    /// <remarks>
    /// Ratings used to be calculated at kick-off and again only at a substitution or a sending-off, so the cost
    /// of tiredness, morale, and being a man down mostly never reached them, and a side's first change made
    /// all its ratings jump to their tired state while its opponent's stayed frozen. Both sides are now
    /// refreshed together, and the score is read at the same moment so a situational time-wasting
    /// instruction follows the scoreline.
    /// </remarks>
    public void RefreshRatings()
    {
        var played = ClockSeconds / Rules.SecondsPerMinute;

        if (played == _ratingMinute)
        {
            return;
        }

        _ratingMinute = played;

        var homeGoals = GoalsOf(MatchSide.Home);
        var awayGoals = GoalsOf(MatchSide.Away);

        Home.Leading = homeGoals > awayGoals;
        Away.Leading = awayGoals > homeGoals;

        Home.RecalculateRatings(Rules);
        Away.RecalculateRatings(Rules);
    }

    /// <summary>Gets the goals scored by a side, read from the events (MAT-5).</summary>
    /// <param name="side">Which end.</param>
    public int GoalsOf(MatchSide side) => Events.Count(matchEvent => matchEvent.Side == side && matchEvent.IsGoal);

    /// <summary>
    /// Assembles one possession's record as it is played and hands it to the recorder when it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Waypoints, touches and events are stamped with a monotonic tick rather than a clock reading, because a
    /// possession's seconds are drawn up front and the clock does not move again until it is over. The tick
    /// is the order the ball reached each fact in, and the finished passage maps it evenly onto the 0…10,000
    /// fraction a replay measures playback against. An event takes the tick of the item recorded last, so it
    /// sits where the ball was when it happened.
    /// </para>
    /// <para>
    /// The accumulator never reads the random stream and never writes to the match, so a recorder attached to
    /// a simulation cannot move its outcome.
    /// </para>
    /// </remarks>
    private sealed class PassageAccumulator
    {
        private readonly List<(int Sequence, int Tick)> _events = [];
        private readonly List<(int Tick, PassageWaypointV1 Waypoint)> _waypoints = [];
        private readonly List<(int Tick, PassageTouchV1 Touch)> _touches = [];
        private readonly int _ordinal;
        private readonly MatchSide _side;
        private readonly int _period;
        private readonly int _startClockSeconds;
        private readonly PassageRestartKind _restart;
        private int _nextTick;

        public PassageAccumulator(
            int ordinal,
            MatchSide side,
            int period,
            int startClockSeconds,
            PassageRestartKind restart)
        {
            _ordinal = ordinal;
            _side = side;
            _period = period;
            _startClockSeconds = startClockSeconds;
            _restart = restart;
        }

        public void AddWaypoint(int x, int y, int z, PassageWaypointKind kind) =>
            _waypoints.Add((_nextTick++, new PassageWaypointV1(0, x, y, z, kind)));

        public void AddTouch(Guid participantId, PassageAction action, int x, int y, int z) =>
            _touches.Add((_nextTick++, new PassageTouchV1(0, participantId, action, x, y, z)));

        public void AddEvent(int sequence) => _events.Add((sequence, Math.Max(0, _nextTick - 1)));

        public MatchPassageV1 Build(int endClockSeconds, PassageOutcome outcome)
        {
            var span = Math.Max(1, _nextTick - 1);

            return new MatchPassageV1
            {
                Ordinal = _ordinal,
                Side = _side,
                Period = _period,
                StartClockSeconds = _startClockSeconds,
                EndClockSeconds = endClockSeconds,
                Outcome = outcome,
                Restart = _restart,
                Events =
                [
                    .. _events.Select(pair => new PassageEventV1(
                        pair.Sequence,
                        pair.Tick * EngineRulesV2.Certain / span)),
                ],
                Waypoints =
                [
                    .. _waypoints.Select(pair => pair.Waypoint with
                    {
                        FractionBasisPoints = pair.Tick * EngineRulesV2.Certain / span,
                    }),
                ],
                Touches =
                [
                    .. _touches.Select(pair => pair.Touch with
                    {
                        FractionBasisPoints = pair.Tick * EngineRulesV2.Certain / span,
                    }),
                ],
            };
        }
    }
}
