using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// A player's live condition, fatigue, morale, and sharpness during the match, in basis points.
/// </summary>
/// <remarks>
/// Separate from <see cref="PlayerMatchStateV1"/> because the snapshot's values are frozen and these move:
/// a match spends condition, accumulates fatigue, drifts morale with the scoreline, and sharpens a player
/// who is on the pitch. Every value is clamped to the scale, so a sequence of bounded decrements cannot
/// walk a value out of range however long the match runs.
/// </remarks>
public readonly record struct PlayerCondition
{
    /// <summary>Gets the least and greatest a state value can be.</summary>
    public const int Minimum = 0;

    /// <summary>Gets the greatest a state value can be.</summary>
    public const int Maximum = 10_000;

    /// <summary>Initializes a condition.</summary>
    /// <param name="condition">Condition.</param>
    /// <param name="fatigue">Fatigue.</param>
    /// <param name="morale">Morale.</param>
    /// <param name="sharpness">Match sharpness.</param>
    public PlayerCondition(int condition, int fatigue, int morale, int sharpness)
    {
        ConditionBasisPoints = Clamp(condition);
        FatigueBasisPoints = Clamp(fatigue);
        MoraleBasisPoints = Clamp(morale);
        SharpnessBasisPoints = Clamp(sharpness);
    }

    /// <summary>Gets condition.</summary>
    public int ConditionBasisPoints { get; }

    /// <summary>Gets accumulated fatigue.</summary>
    public int FatigueBasisPoints { get; }

    /// <summary>Gets morale.</summary>
    public int MoraleBasisPoints { get; }

    /// <summary>Gets match sharpness.</summary>
    public int SharpnessBasisPoints { get; }

    /// <summary>Takes the frozen state into the match.</summary>
    /// <param name="state">The snapshot's state.</param>
    public static PlayerCondition From(PlayerMatchStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new PlayerCondition(
            state.ConditionBasisPoints,
            state.FatigueBasisPoints,
            state.MoraleBasisPoints,
            state.SharpnessBasisPoints);
    }

    /// <summary>Returns a copy with the condition changed by a signed amount, clamped to the scale.</summary>
    /// <param name="delta">The change, which may be negative.</param>
    public PlayerCondition WithConditionDelta(int delta) =>
        new(Clamp(ConditionBasisPoints + delta), FatigueBasisPoints, MoraleBasisPoints, SharpnessBasisPoints);

    /// <summary>Returns a copy with the fatigue changed by a signed amount, clamped to the scale.</summary>
    /// <param name="delta">The change, which may be negative.</param>
    public PlayerCondition WithFatigueDelta(int delta) =>
        new(ConditionBasisPoints, Clamp(FatigueBasisPoints + delta), MoraleBasisPoints, SharpnessBasisPoints);

    /// <summary>Returns a copy with the morale changed by a signed amount, clamped to the scale.</summary>
    /// <param name="delta">The change, which may be negative.</param>
    public PlayerCondition WithMoraleDelta(int delta) =>
        new(ConditionBasisPoints, FatigueBasisPoints, Clamp(MoraleBasisPoints + delta), SharpnessBasisPoints);

    /// <summary>Returns a copy with the sharpness changed by a signed amount, clamped to the scale.</summary>
    /// <param name="delta">The change, which may be negative.</param>
    public PlayerCondition WithSharpnessDelta(int delta) =>
        new(ConditionBasisPoints, FatigueBasisPoints, MoraleBasisPoints, Clamp(SharpnessBasisPoints + delta));

    private static int Clamp(int value) => int.Clamp(value, Minimum, Maximum);
}

/// <summary>
/// One player currently on the pitch, with the live condition a rating is computed against.
/// </summary>
/// <remarks>
/// The unit that everything in the simulation iterates over. It keeps the slot, the occupant, their
/// familiarity, and their live condition together, so a sending-off or a substitution removes one element
/// and every rating, every possession, and every foul immediately reflects a side with ten players — rather
/// than each consumer deciding for itself who is still on.
/// </remarks>
public sealed record ActiveSlot
{
    /// <summary>Gets the slot the player occupies.</summary>
    public required MatchSlotV1 Slot { get; init; }

    /// <summary>Gets the player.</summary>
    public required MatchParticipantV1 Participant { get; init; }

    /// <summary>Gets how well the player suits the slot (`INS-10`).</summary>
    public required int FamiliarityBasisPoints { get; init; }

    /// <summary>Gets the player's live condition.</summary>
    public required PlayerCondition Condition { get; init; }

    /// <summary>Resolves a starting lineup slot into an active slot.</summary>
    /// <param name="slot">The resolved starting slot.</param>
    public static ActiveSlot From(ResolvedSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new ActiveSlot
        {
            Slot = slot.Slot,
            Participant = slot.Participant,
            FamiliarityBasisPoints = slot.FamiliarityBasisPoints,
            Condition = PlayerCondition.From(slot.Participant.State),
        };
    }
}

/// <summary>
/// A side's nine unit ratings (master plan §8.4).
/// </summary>
/// <remarks>
/// On the rating scale, where an average attribute of 13 is about 650. The simulation never compares a
/// rating against an absolute threshold; it compares one side's against the other's, which is what keeps a
/// balance change to the generator's ability mean from silently changing how many goals a match has.
/// </remarks>
public sealed record MatchUnitRatings
{
    /// <summary>Gets build-up and control.</summary>
    public required int BuildUp { get; init; }

    /// <summary>Gets creation.</summary>
    public required int Creation { get; init; }

    /// <summary>Gets finishing.</summary>
    public required int Finishing { get; init; }

    /// <summary>Gets defensive pressure.</summary>
    public required int DefensivePressure { get; init; }

    /// <summary>Gets defensive shape.</summary>
    public required int DefensiveShape { get; init; }

    /// <summary>Gets goalkeeping.</summary>
    public required int Goalkeeping { get; init; }

    /// <summary>Gets set pieces.</summary>
    public required int SetPieces { get; init; }

    /// <summary>Gets fitness.</summary>
    public required int Fitness { get; init; }

    /// <summary>Gets cohesion.</summary>
    public required int Cohesion { get; init; }

    /// <summary>
    /// Gets the ratings a side has before any player has been read, which is where a side starts before
    /// its first calculation rather than a rating any side is expected to play at.
    /// </summary>
    public static MatchUnitRatings Neutral { get; } = new()
    {
        BuildUp = 0,
        Creation = 0,
        Finishing = 0,
        DefensivePressure = 0,
        DefensiveShape = 0,
        Goalkeeping = 0,
        SetPieces = 0,
        Fitness = 0,
        Cohesion = 0,
    };

    /// <summary>Gets one unit's rating.</summary>
    /// <param name="unit">The unit.</param>
    public int Of(MatchUnit unit) => unit switch
    {
        MatchUnit.BuildUp => BuildUp,
        MatchUnit.Creation => Creation,
        MatchUnit.Finishing => Finishing,
        MatchUnit.DefensivePressure => DefensivePressure,
        MatchUnit.DefensiveShape => DefensiveShape,
        MatchUnit.Goalkeeping => Goalkeeping,
        MatchUnit.SetPieces => SetPieces,
        MatchUnit.Fitness => Fitness,
        MatchUnit.Cohesion => Cohesion,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown unit."),
    };
}
