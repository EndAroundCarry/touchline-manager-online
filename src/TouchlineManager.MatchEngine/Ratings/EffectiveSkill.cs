using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// What a player on the pitch can actually do with a skill right now, as opposed to what his sheet says
/// (`engine-v6`).
/// </summary>
/// <remarks>
/// <para>
/// One definition, read by the team ratings and by every duel, so a tired or misplaced player is worse in the
/// same way wherever he is asked to do something. Four things move a skill: how well the player suits his slot,
/// how tired he is, and his fatigue, morale, and sharpness.
/// </para>
/// <para>
/// Tiredness is a drop that grows linearly with the condition lost, by skill family: physical skills fall
/// furthest, technical next, mental least. A goalkeeper is exempt, as are his goalkeeping skills, because the
/// engine does not model his legs.
/// </para>
/// </remarks>
public static class EffectiveSkill
{
    /// <summary>The scale <see cref="Hundredths"/> returns on: a sheet value of 13 is 1,300.</summary>
    public const int Scale = 100;

    /// <summary>
    /// Gets a player's effective skill in hundredths of an attribute point, so a drop of a few per cent is not
    /// lost to rounding.
    /// </summary>
    /// <param name="slot">The player on the pitch.</param>
    /// <param name="attribute">The skill.</param>
    /// <param name="rules">The rules in force.</param>
    public static int Hundredths(ActiveSlot slot, MatchAttributeName attribute, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        var sheet = (long)slot.Participant.Attributes.ValueOf(attribute) * Scale;

        return (int)(sheet * Multiplier(slot, attribute, rules) / EngineRulesV2.Certain);
    }

    /// <summary>
    /// Gets the multiplier, in basis points, that position fit, tiredness, fatigue, morale, and sharpness put
    /// on one of a player's skills.
    /// </summary>
    /// <param name="slot">The player on the pitch.</param>
    /// <param name="attribute">The skill.</param>
    /// <param name="rules">The rules in force.</param>
    public static int Multiplier(ActiveSlot slot, MatchAttributeName attribute, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        var fit = (long)slot.FamiliarityBasisPoints * TirednessFactor(slot, attribute, rules) / EngineRulesV2.Certain;

        return (int)(fit * StateFactor(slot.Condition, rules) / EngineRulesV2.Certain);
    }

    /// <summary>
    /// Gets what tiredness multiplies a skill by, in basis points: 10,000 when fresh, falling with the
    /// condition lost to the family's drop at empty.
    /// </summary>
    /// <param name="slot">The player on the pitch.</param>
    /// <param name="attribute">The skill.</param>
    /// <param name="rules">The rules in force.</param>
    public static int TirednessFactor(ActiveSlot slot, MatchAttributeName attribute, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        if (slot.Slot.Family == MatchPositionFamily.Goalkeeper)
        {
            return EngineRulesV2.Certain;
        }

        var dropAtEmpty = MatchAttributeNames.FamilyOf(attribute) switch
        {
            MatchAttributeFamily.Physical => rules.TiredPhysicalDropBasisPoints,
            MatchAttributeFamily.Mental => rules.TiredMentalDropBasisPoints,
            _ => rules.TiredTechnicalDropBasisPoints,
        };

        var conditionLost = EngineRulesV2.Certain - slot.Condition.ConditionBasisPoints;

        return EngineRulesV2.Certain - (int)((long)dropAtEmpty * conditionLost / EngineRulesV2.Certain);
    }

    /// <summary>
    /// Combines fatigue, morale, and sharpness into one multiplier on a player's skills, in basis points.
    /// </summary>
    /// <remarks>
    /// Each is a linear interpolation between the rules' floor and ceiling, and they compose multiplicatively.
    /// Condition is not here: since `engine-v6` it is the per-skill tiredness drop instead of one all-round
    /// factor, so tiredness is never counted twice.
    /// </remarks>
    /// <param name="condition">The player's live condition.</param>
    /// <param name="rules">The rules in force.</param>
    public static int StateFactor(PlayerCondition condition, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var multiplier = Interpolate(
            rules.FatigueFactorFloorBasisPoints,
            rules.FatigueFactorCeilingBasisPoints,
            EngineRulesV2.Certain - condition.FatigueBasisPoints);

        multiplier = multiplier * Interpolate(
            rules.MoraleFactorFloorBasisPoints,
            rules.MoraleFactorCeilingBasisPoints,
            condition.MoraleBasisPoints) / EngineRulesV2.Certain;

        multiplier = multiplier * Interpolate(
            rules.SharpnessFactorFloorBasisPoints,
            rules.SharpnessFactorCeilingBasisPoints,
            condition.SharpnessBasisPoints) / EngineRulesV2.Certain;

        return multiplier;
    }

    private static int Interpolate(int floor, int ceiling, int valueBasisPoints)
    {
        var bounded = int.Clamp(valueBasisPoints, 0, EngineRulesV2.Certain);

        return floor + (int)(((long)(ceiling - floor) * bounded) / EngineRulesV2.Certain);
    }
}
