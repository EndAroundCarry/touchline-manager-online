namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// The kind of thing that happened, which is also what commentary and highlights dispatch on.
/// </summary>
/// <remarks>
/// Events are the engine's interface to everything downstream (MAT-8): commentary and the highlight
/// director consume them and cannot change an outcome. Adding a member, renaming one, or changing what
/// an existing one means is an engine-version change (ADR-0004), because a stored event's meaning is
/// part of what a replay reproduces.
/// </remarks>
public enum EngineEventType
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

/// <summary>Why a substitution was made, which the determinant planner records.</summary>
public enum MatchSubstitutionReason
{
    /// <summary>The player could not continue.</summary>
    Injury = 0,

    /// <summary>The player was too tired to be worth keeping on.</summary>
    Fatigue = 1,

    /// <summary>The bench offered a better option at the same role.</summary>
    Tactical = 2,
}

/// <summary>
/// One structured, immutable match event.
/// </summary>
/// <remarks>
/// <para>
/// An event carries facts, never prose. Commentary and highlights are built from these fields by downstream
/// consumers, which is what lets the same match be narrated in another language later without re-simulating
/// it and what keeps hidden attributes out of player-facing text (MAT-8, MAT-11, master plan §8.6).
/// </para>
/// <para>
/// A field is nullable where it does not apply to the event's type — a kickoff has no scorer — rather than
/// the type hierarchy being modelled as a class per event, because the canonical output hash is taken over a
/// flat, ordered sequence and a discriminated union would make the serialization rules the interesting part
/// of the engine rather than the simulation.
/// </para>
/// </remarks>
public sealed record EngineEventV1
{
    /// <summary>Gets the 1-based position in the match's event sequence, which is a total order.</summary>
    public required int Sequence { get; init; }

    /// <summary>Gets the match minute the event happened in.</summary>
    public required int Minute { get; init; }

    /// <summary>Gets the stoppage minute within that minute, or zero in regulation time.</summary>
    public int StoppageMinute { get; init; }

    /// <summary>Gets which side the event belongs to.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets the club the event belongs to.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets what happened.</summary>
    public required EngineEventType Type { get; init; }

    /// <summary>Gets the event's principal participant: the scorer, the fouler, the player coming off.</summary>
    public Guid? ParticipantId { get; init; }

    /// <summary>Gets the event's second participant: the goalkeeper beaten, the player coming on.</summary>
    public Guid? SecondaryParticipantId { get; init; }

    /// <summary>Gets where on the pitch the event happened, for shots and set pieces.</summary>
    public ShotZone? Zone { get; init; }

    /// <summary>
    /// Gets how good the chance was, in basis points, on shot events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The goal probability the shot was resolved against, kept so that highlight selection can pick out the
    /// chances worth replaying rather than the ones that merely happened. Without it "notable saves above a
    /// configured threshold" is not expressible, because a save from three yards and a save from thirty look
    /// identical in the event stream.
    /// </para>
    /// <para>
    /// This is a fact about a shot, derived from attributes that are already visible to the manager who owns
    /// the club. It is emphatically <em>not</em> a hidden player value, and it is not engine diagnostics
    /// either — but it is also not something a player-facing response should carry (`MAT-11`), so the
    /// transport mapping and the data-classification test are what keep it out of a DTO. It is present here
    /// because the alternative was a highlight director that could not tell a good chance from a bad one.
    /// </para>
    /// </remarks>
    public int? QualityBasisPoints { get; init; }

    /// <summary>Gets how many fixtures an injury rules the player out for (DIS-1).</summary>
    public int? AbsenceFixtures { get; init; }

    /// <summary>Gets why a substitution was made.</summary>
    public MatchSubstitutionReason? SubstitutionReason { get; init; }

    /// <summary>Gets whether the event is one of the two halves' boundaries.</summary>
    public bool IsPeriodBoundary =>
        Type is EngineEventType.KickOff
            or EngineEventType.HalfTime
            or EngineEventType.SecondHalfStart
            or EngineEventType.FullTime;

    /// <summary>Gets whether the event is a goal of any kind, which the score is reconciled against (MAT-5).</summary>
    public bool IsGoal => Type is EngineEventType.Goal or EngineEventType.PenaltyGoal;
}
