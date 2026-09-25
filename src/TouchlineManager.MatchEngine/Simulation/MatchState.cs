using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;

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

    /// <summary>Initializes the state for one match.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The seeded generator.</param>
    /// <param name="home">The home side's runtime state.</param>
    /// <param name="away">The away side's runtime state.</param>
    public MatchState(
        MatchInputV1 input,
        EngineRulesV1 rules,
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

    /// <summary>Gets the frozen snapshot.</summary>
    public MatchInputV1 Input { get; }

    /// <summary>Gets the rules in force.</summary>
    public EngineRulesV1 Rules { get; }

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
    /// <param name="firstHalf">Whether the half beginning is the first.</param>
    public void BeginHalf(bool firstHalf)
    {
        InFirstHalf = firstHalf;
        _halfEventStoppageSeconds = 0;
        _halfStoppageJitterSeconds = Random.NextInt(Rules.StoppageJitterSeconds + 1);
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
    /// Emits an event at the current clock position, stamping it with the next sequence number.
    /// </summary>
    /// <param name="side">Which side it belongs to.</param>
    /// <param name="type">What happened.</param>
    /// <param name="participantId">The principal participant, if any.</param>
    /// <param name="secondaryParticipantId">The second participant, if any.</param>
    /// <param name="zone">Where it happened, if it was a shot or set piece.</param>
    /// <param name="qualityBasisPoints">How good a chance it was, on shot events.</param>
    /// <param name="absenceFixtures">How long an injury rules a player out, if it was an injury.</param>
    /// <param name="substitutionReason">Why a substitution was made, if it was one.</param>
    /// <returns>The event, so a caller can count it without re-reading the log.</returns>
    public EngineEventV1 Emit(
        MatchSide side,
        EngineEventType type,
        Guid? participantId = null,
        Guid? secondaryParticipantId = null,
        ShotZone? zone = null,
        int? qualityBasisPoints = null,
        int? absenceFixtures = null,
        MatchSubstitutionReason? substitutionReason = null)
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
        };

        Events.Add(matchEvent);

        return matchEvent;
    }

    /// <summary>Gets the total minutes played, regulation plus both halves' stoppage.</summary>
    public int TotalMinutesPlayed =>
        Rules.RegulationMinutes
        + ((FirstHalfStoppageSeconds + SecondHalfStoppageSeconds) / Rules.SecondsPerMinute);

    /// <summary>Gets the goals scored by a side, read from the events (MAT-5).</summary>
    /// <param name="side">Which end.</param>
    public int GoalsOf(MatchSide side) => Events.Count(matchEvent => matchEvent.Side == side && matchEvent.IsGoal);
}
