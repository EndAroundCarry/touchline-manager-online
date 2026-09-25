using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// A player's condition, fatigue, morale, and match sharpness, in basis points (`TRN-5`…`TRN-7`).
/// </summary>
/// <remarks>
/// <para>
/// One row per player, keyed by <see cref="PlayerId"/> (`data-model.md` §3.2). The database is
/// authoritative in basis points and the API converts to a user-facing value (`TRN-8`), so no display
/// rounding ever reaches this table.
/// </para>
/// <para>
/// <see cref="DevelopmentRemainder"/> is the carry-forward of a partial training gain (`TRN-10`). It is
/// created at zero by the generator and is what stops a day's progress being lost to rounding: the
/// daily progression job (Stage 4's later milestone) accumulates fractional growth here.
/// </para>
/// </remarks>
public sealed class PlayerState
{
    /// <summary>The neutral opening morale and match-sharpness value, halfway up the scale.</summary>
    public const int NeutralBasisPoints = WorldRuleSet.StateBasisPointsMax / 2;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerState()
    {
    }

    /// <summary>Gets the owning player, which is also the primary key.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets condition: short-term freshness, 0–10,000.</summary>
    public int ConditionBp { get; private set; }

    /// <summary>Gets fatigue: accumulated load, 0–10,000. Higher is worse.</summary>
    public int FatigueBp { get; private set; }

    /// <summary>Gets morale, 0–10,000.</summary>
    public int MoraleBp { get; private set; }

    /// <summary>Gets match sharpness, 0–10,000.</summary>
    public int MatchSharpnessBp { get; private set; }

    /// <summary>Gets the partial development remainder carried across days (`TRN-10`).</summary>
    public int DevelopmentRemainder { get; private set; }

    /// <summary>Gets the last day the progression job ran for this player, if it has run.</summary>
    public DateOnly? LastProgressionDate { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Opens a player's state at the start of a world: fully rested, unloaded, and neutral, with no
    /// development carried yet.
    /// </summary>
    /// <param name="playerId">The owning player.</param>
    public static PlayerState Open(Guid playerId) => new()
    {
        PlayerId = playerId,
        ConditionBp = WorldRuleSet.StateBasisPointsMax,
        FatigueBp = WorldRuleSet.StateBasisPointsMin,
        MoraleBp = NeutralBasisPoints,
        MatchSharpnessBp = NeutralBasisPoints,
        DevelopmentRemainder = 0,
        LastProgressionDate = null,
        Version = 1,
    };

    /// <summary>Creates a state row with explicit values. Used where a fixture has already been played.</summary>
    /// <param name="playerId">The owning player.</param>
    /// <param name="conditionBp">Condition in basis points.</param>
    /// <param name="fatigueBp">Fatigue in basis points.</param>
    /// <param name="moraleBp">Morale in basis points.</param>
    /// <param name="matchSharpnessBp">Match sharpness in basis points.</param>
    /// <param name="developmentRemainder">The carried development remainder.</param>
    /// <param name="lastProgressionDate">The last progression day, if any.</param>
    public static PlayerState Create(
        Guid playerId,
        int conditionBp,
        int fatigueBp,
        int moraleBp,
        int matchSharpnessBp,
        int developmentRemainder,
        DateOnly? lastProgressionDate)
    {
        EnsureBasisPoints(conditionBp, nameof(conditionBp));
        EnsureBasisPoints(fatigueBp, nameof(fatigueBp));
        EnsureBasisPoints(moraleBp, nameof(moraleBp));
        EnsureBasisPoints(matchSharpnessBp, nameof(matchSharpnessBp));

        if (developmentRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(developmentRemainder),
                developmentRemainder,
                "A development remainder is never negative (TRN-10).");
        }

        return new PlayerState
        {
            PlayerId = playerId,
            ConditionBp = conditionBp,
            FatigueBp = fatigueBp,
            MoraleBp = moraleBp,
            MatchSharpnessBp = matchSharpnessBp,
            DevelopmentRemainder = developmentRemainder,
            LastProgressionDate = lastProgressionDate,
            Version = 1,
        };
    }

    /// <summary>
    /// Applies one day of training progression (`TRN-9`, `TRN-10`).
    /// </summary>
    /// <remarks>
    /// The values are the ones the pure <c>DailyProgression</c> calculator produced; this method only
    /// checks them and records the day, so the rules that decide development live in the calculator and
    /// the aggregate stays the sole place a stored value is written.
    /// </remarks>
    /// <param name="conditionBp">The new condition in basis points.</param>
    /// <param name="fatigueBp">The new fatigue in basis points.</param>
    /// <param name="moraleBp">The new morale in basis points.</param>
    /// <param name="matchSharpnessBp">The new match sharpness in basis points.</param>
    /// <param name="developmentRemainder">The partial development carried into the next day (`TRN-10`).</param>
    /// <param name="day">The day the progression was run for.</param>
    public void ApplyProgression(
        int conditionBp,
        int fatigueBp,
        int moraleBp,
        int matchSharpnessBp,
        int developmentRemainder,
        DateOnly day)
    {
        EnsureBasisPoints(conditionBp, nameof(conditionBp));
        EnsureBasisPoints(fatigueBp, nameof(fatigueBp));
        EnsureBasisPoints(moraleBp, nameof(moraleBp));
        EnsureBasisPoints(matchSharpnessBp, nameof(matchSharpnessBp));

        if (developmentRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(developmentRemainder),
                developmentRemainder,
                "A development remainder is never negative (TRN-10).");
        }

        ConditionBp = conditionBp;
        FatigueBp = fatigueBp;
        MoraleBp = moraleBp;
        MatchSharpnessBp = matchSharpnessBp;
        DevelopmentRemainder = developmentRemainder;
        LastProgressionDate = day;
        Version++;
    }

    private static void EnsureBasisPoints(int value, string parameterName)
    {
        if (value is < WorldRuleSet.StateBasisPointsMin or > WorldRuleSet.StateBasisPointsMax)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"A state value is between {WorldRuleSet.StateBasisPointsMin} and {WorldRuleSet.StateBasisPointsMax} basis points (TRN-5..TRN-7).");
        }
    }
}
