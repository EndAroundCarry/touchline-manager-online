namespace TouchlineManager.Domain.Match;

/// <summary>
/// What happened, mirroring the engine's event vocabulary by value (`MAT-8`, master plan §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The engine may not depend on the domain (DEP-2, ADR-0004), so it defines its own <c>EngineEventType</c>
/// and this enum mirrors it: the numeric values are deliberately identical and a test asserts the two
/// agree member for member. The values are what a stored event's meaning rests on, so adding, removing,
/// or reordering a member is an engine-version change rather than a refactor.
/// </para>
/// <para>
/// The stable codes are the domain's, not the engine's: the engine has no wire vocabulary for event types
/// because nothing outside it needed one until events were stored. They are what a Stage 7 commentary or
/// highlight consumer dispatches on.
/// </para>
/// </remarks>
public enum MatchEventType
{
    /// <summary>The match began.</summary>
    KickOff = 0,

    /// <summary>The first half ended.</summary>
    HalfTime = 1,

    /// <summary>The second half began.</summary>
    SecondHalfStart = 2,

    /// <summary>The match ended.</summary>
    FullTime = 3,

    /// <summary>A goal from open play or a set piece.</summary>
    Goal = 4,

    /// <summary>A penalty was awarded.</summary>
    PenaltyAwarded = 5,

    /// <summary>A penalty was scored.</summary>
    PenaltyGoal = 6,

    /// <summary>A penalty was missed or saved.</summary>
    PenaltyMissed = 7,

    /// <summary>A shot was saved by the goalkeeper.</summary>
    ShotSaved = 8,

    /// <summary>A shot was blocked by a defender.</summary>
    ShotBlocked = 9,

    /// <summary>A shot was off target.</summary>
    ShotOffTarget = 10,

    /// <summary>A shot hit the woodwork.</summary>
    Woodwork = 11,

    /// <summary>A foul was committed.</summary>
    Foul = 12,

    /// <summary>A player was booked.</summary>
    YellowCard = 13,

    /// <summary>A player was booked a second time and sent off.</summary>
    SecondYellowCard = 14,

    /// <summary>A player was sent off.</summary>
    RedCard = 15,

    /// <summary>An offside was given.</summary>
    Offside = 16,

    /// <summary>A corner was won.</summary>
    Corner = 17,

    /// <summary>A player was injured.</summary>
    Injury = 18,

    /// <summary>A substitution was made.</summary>
    Substitution = 19,
}

/// <summary>Why a substitution was made.</summary>
public enum MatchSubstitutionCause
{
    /// <summary>The player could not continue.</summary>
    Injury = 0,

    /// <summary>The player was too tired to be worth keeping on.</summary>
    Fatigue = 1,

    /// <summary>The bench offered a better option at the same role.</summary>
    Tactical = 2,
}

/// <summary>Where on the pitch a shot was taken from.</summary>
public enum MatchShotZone
{
    /// <summary>Straight in front of goal.</summary>
    Central = 0,

    /// <summary>Left of centre, inside the width of the box.</summary>
    InsideLeft = 1,

    /// <summary>Right of centre, inside the width of the box.</summary>
    InsideRight = 2,

    /// <summary>Wide on the left.</summary>
    WideLeft = 3,

    /// <summary>Wide on the right.</summary>
    WideRight = 4,
}

/// <summary>Stable codes and relationships for the stored event vocabulary.</summary>
public static class MatchEventTypes
{
    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 18;

    /// <summary>Every event type, in declaration order.</summary>
    public static readonly IReadOnlyList<MatchEventType> All = [.. Enum.GetValues<MatchEventType>()];

    /// <summary>Converts an event type to its stable code.</summary>
    /// <param name="type">The event type.</param>
    public static string ToCode(this MatchEventType type) => type switch
    {
        MatchEventType.KickOff => "kick_off",
        MatchEventType.HalfTime => "half_time",
        MatchEventType.SecondHalfStart => "second_half_start",
        MatchEventType.FullTime => "full_time",
        MatchEventType.Goal => "goal",
        MatchEventType.PenaltyAwarded => "penalty_awarded",
        MatchEventType.PenaltyGoal => "penalty_goal",
        MatchEventType.PenaltyMissed => "penalty_missed",
        MatchEventType.ShotSaved => "shot_saved",
        MatchEventType.ShotBlocked => "shot_blocked",
        MatchEventType.ShotOffTarget => "shot_off_target",
        MatchEventType.Woodwork => "woodwork",
        MatchEventType.Foul => "foul",
        MatchEventType.YellowCard => "yellow_card",
        MatchEventType.SecondYellowCard => "second_yellow_card",
        MatchEventType.RedCard => "red_card",
        MatchEventType.Offside => "offside",
        MatchEventType.Corner => "corner",
        MatchEventType.Injury => "injury",
        MatchEventType.Substitution => "substitution",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown event type."),
    };

    /// <summary>Parses a stable code back to its event type.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchEventType FromCode(string code) => code switch
    {
        "kick_off" => MatchEventType.KickOff,
        "half_time" => MatchEventType.HalfTime,
        "second_half_start" => MatchEventType.SecondHalfStart,
        "full_time" => MatchEventType.FullTime,
        "goal" => MatchEventType.Goal,
        "penalty_awarded" => MatchEventType.PenaltyAwarded,
        "penalty_goal" => MatchEventType.PenaltyGoal,
        "penalty_missed" => MatchEventType.PenaltyMissed,
        "shot_saved" => MatchEventType.ShotSaved,
        "shot_blocked" => MatchEventType.ShotBlocked,
        "shot_off_target" => MatchEventType.ShotOffTarget,
        "woodwork" => MatchEventType.Woodwork,
        "foul" => MatchEventType.Foul,
        "yellow_card" => MatchEventType.YellowCard,
        "second_yellow_card" => MatchEventType.SecondYellowCard,
        "red_card" => MatchEventType.RedCard,
        "offside" => MatchEventType.Offside,
        "corner" => MatchEventType.Corner,
        "injury" => MatchEventType.Injury,
        "substitution" => MatchEventType.Substitution,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown event type code."),
    };

    /// <summary>Gets whether the event is a goal of any kind, which the score reconciles against (`MAT-5`).</summary>
    /// <param name="type">The event type.</param>
    public static bool IsGoal(this MatchEventType type) =>
        type is MatchEventType.Goal or MatchEventType.PenaltyGoal;

    /// <summary>Gets whether the event is a booking, which the standings count per club (`TBL-8`, `TBL-9`).</summary>
    /// <param name="type">The event type.</param>
    /// <remarks>
    /// A second yellow is counted as a red and not as a second yellow: the card that ends a player's match
    /// is the sending-off, and counting it twice would make the discipline columns disagree with the
    /// sending-off count.
    /// </remarks>
    public static bool IsRedCard(this MatchEventType type) =>
        type is MatchEventType.RedCard or MatchEventType.SecondYellowCard;

    /// <summary>Converts a shot zone to its stable code.</summary>
    /// <param name="zone">The shot zone.</param>
    public static string ToCode(this MatchShotZone zone) => zone switch
    {
        MatchShotZone.Central => "central",
        MatchShotZone.InsideLeft => "inside_left",
        MatchShotZone.InsideRight => "inside_right",
        MatchShotZone.WideLeft => "wide_left",
        MatchShotZone.WideRight => "wide_right",
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown shot zone."),
    };

    /// <summary>Parses a stable code back to its shot zone.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchShotZone ZoneFromCode(string code) => code switch
    {
        "central" => MatchShotZone.Central,
        "inside_left" => MatchShotZone.InsideLeft,
        "inside_right" => MatchShotZone.InsideRight,
        "wide_left" => MatchShotZone.WideLeft,
        "wide_right" => MatchShotZone.WideRight,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown shot zone code."),
    };

    /// <summary>Converts a substitution cause to its stable code.</summary>
    /// <param name="cause">The cause.</param>
    public static string ToCode(this MatchSubstitutionCause cause) => cause switch
    {
        MatchSubstitutionCause.Injury => "injury",
        MatchSubstitutionCause.Fatigue => "fatigue",
        MatchSubstitutionCause.Tactical => "tactical",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown substitution cause."),
    };

    /// <summary>Parses a stable code back to its substitution cause.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchSubstitutionCause CauseFromCode(string code) => code switch
    {
        "injury" => MatchSubstitutionCause.Injury,
        "fatigue" => MatchSubstitutionCause.Fatigue,
        "tactical" => MatchSubstitutionCause.Tactical,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown substitution cause code."),
    };
}

/// <summary>
/// One structured, immutable thing that happened in a match (`MAT-8`, master plan §6.6).
/// </summary>
/// <remarks>
/// <para>
/// Events are the durable narrative of a match: commentary and highlights are built from them and cannot
/// change an outcome, which is what lets the same match be narrated in another language, or replayed
/// years later, without re-simulating it. They carry facts and never prose.
/// </para>
/// <para>
/// <see cref="Sequence"/> is a total order within the match and is unique per match, so the event stream
/// is readable in exactly the order it happened rather than by timestamp. <see cref="ClubId"/> carries
/// which end an event belongs to — the side is derivable from it against the fixture, and storing both
/// would be two answers to one question.
/// </para>
/// <para>
/// <see cref="QualityBasisPoints"/> is a fact about a shot — the goal probability it was resolved
/// against — which is what makes "the best chances" expressible in Stage 7. It is derived from attributes
/// a manager can already see, but it is engine detail rather than player-facing data, so no response
/// carries it (`MAT-11`).
/// </para>
/// </remarks>
public sealed class MatchEvent
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private MatchEvent()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the match the event belongs to.</summary>
    public Guid MatchId { get; private set; }

    /// <summary>Gets the 1-based position in the match's event sequence, which is a total order.</summary>
    public int Sequence { get; private set; }

    /// <summary>Gets the minute the event happened in.</summary>
    public int Minute { get; private set; }

    /// <summary>Gets the stoppage minute within that minute, or zero in regulation time.</summary>
    public int StoppageMinute { get; private set; }

    /// <summary>Gets the club the event belongs to.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets what happened.</summary>
    public MatchEventType Type { get; private set; }

    /// <summary>Gets the principal participant: the scorer, the fouler, the player coming off.</summary>
    public Guid? ParticipantId { get; private set; }

    /// <summary>Gets the second participant: the goalkeeper beaten, the player coming on.</summary>
    public Guid? SecondaryParticipantId { get; private set; }

    /// <summary>Gets where on the pitch the event happened, on shots and set pieces.</summary>
    public MatchShotZone? Zone { get; private set; }

    /// <summary>Gets how good the chance was, in basis points, on shot events.</summary>
    public int? QualityBasisPoints { get; private set; }

    /// <summary>Gets how many fixtures an injury rules the player out for (`DIS-1`).</summary>
    public int? AbsenceFixtures { get; private set; }

    /// <summary>Gets why a substitution was made.</summary>
    public MatchSubstitutionCause? SubstitutionCause { get; private set; }

    /// <summary>Records an event.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="matchId">The match the event belongs to.</param>
    /// <param name="sequence">The 1-based position in the match's sequence.</param>
    /// <param name="minute">The minute the event happened in.</param>
    /// <param name="stoppageMinute">The stoppage minute within that minute, or zero.</param>
    /// <param name="clubId">The club the event belongs to.</param>
    /// <param name="type">What happened.</param>
    /// <param name="participantId">The principal participant, when the event has one.</param>
    /// <param name="secondaryParticipantId">The second participant, when the event has one.</param>
    /// <param name="zone">Where on the pitch, on shots and set pieces.</param>
    /// <param name="qualityBasisPoints">How good the chance was, on shot events.</param>
    /// <param name="absenceFixtures">How many fixtures an injury rules the player out for.</param>
    /// <param name="substitutionCause">Why a substitution was made.</param>
    public static MatchEvent Record(
        Guid id,
        Guid matchId,
        int sequence,
        int minute,
        int stoppageMinute,
        Guid clubId,
        MatchEventType type,
        Guid? participantId = null,
        Guid? secondaryParticipantId = null,
        MatchShotZone? zone = null,
        int? qualityBasisPoints = null,
        int? absenceFixtures = null,
        MatchSubstitutionCause? substitutionCause = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(minute);
        ArgumentOutOfRangeException.ThrowIfNegative(stoppageMinute);

        if (matchId == Guid.Empty)
        {
            throw new ArgumentException("An event belongs to a match.", nameof(matchId));
        }

        if (clubId == Guid.Empty)
        {
            throw new ArgumentException("An event belongs to a club.", nameof(clubId));
        }

        if (qualityBasisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(qualityBasisPoints),
                qualityBasisPoints,
                "A shot quality is a probability in basis points.");
        }

        if (absenceFixtures is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absenceFixtures),
                absenceFixtures,
                "An absence is never negative.");
        }

        return new MatchEvent
        {
            Id = id,
            MatchId = matchId,
            Sequence = sequence,
            Minute = minute,
            StoppageMinute = stoppageMinute,
            ClubId = clubId,
            Type = type,
            ParticipantId = participantId,
            SecondaryParticipantId = secondaryParticipantId,
            Zone = zone,
            QualityBasisPoints = qualityBasisPoints,
            AbsenceFixtures = absenceFixtures,
            SubstitutionCause = substitutionCause,
        };
    }
}
