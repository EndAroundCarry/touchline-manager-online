using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// How much finding the right place is worth to a player at the finish (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// A multiplier in basis points, on the weight with which a player is picked to shoot or head the ball, that
/// rises linearly from <see cref="EngineRulesV2.PositioningFloorBasisPoints"/> at the lowest effective
/// Positioning to <see cref="EngineRulesV2.PositioningCeilingBasisPoints"/> at the highest. It reads the
/// effective skill, so a tired or out-of-position player finds less space than his sheet says, like everywhere
/// else (`engine-v6`).
/// </para>
/// <para>
/// A multiplier and not a flat bonus, so a striker who is both a good finisher and well placed is picked far more
/// often than one who is only one of the two. The edge is the same for a whole side that is uniformly good, so
/// it moves who takes the chance within a team and not how many chances a team has.
/// </para>
/// </remarks>
public static class PositioningEdge
{
    private const int LowestHundredths = MatchAttributeNames.Min * EffectiveSkill.Scale;
    private const int HighestHundredths = MatchAttributeNames.Max * EffectiveSkill.Scale;

    /// <summary>Gets the edge of a player's own Positioning, in basis points.</summary>
    /// <param name="slot">The player on the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static int Of(ActiveSlot slot, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        return FromHundredths(EffectiveSkill.Hundredths(slot, MatchAttributeName.Positioning, rules), rules);
    }

    /// <summary>
    /// Gets the edge of a defender reading the attacker's run: his Marking and his Positioning, averaged, in basis points.
    /// </summary>
    /// <param name="slot">The defender on the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static int OfDefender(ActiveSlot slot, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        var skill = (EffectiveSkill.Hundredths(slot, MatchAttributeName.Marking, rules)
            + EffectiveSkill.Hundredths(slot, MatchAttributeName.Positioning, rules)) / 2;

        return FromHundredths(skill, rules);
    }

    /// <summary>Gets the edge of an effective skill in hundredths of an attribute point, in basis points.</summary>
    /// <param name="hundredths">The skill, 100 to 2,000.</param>
    /// <param name="rules">The rules in force.</param>
    public static int FromHundredths(int hundredths, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var bounded = int.Clamp(hundredths, LowestHundredths, HighestHundredths) - LowestHundredths;
        var span = rules.PositioningCeilingBasisPoints - rules.PositioningFloorBasisPoints;

        return rules.PositioningFloorBasisPoints
            + (int)((long)span * bounded / (HighestHundredths - LowestHundredths));
    }

    /// <summary>Applies an edge to a selection weight.</summary>
    /// <param name="weight">The weight.</param>
    /// <param name="edgeBasisPoints">The edge, in basis points.</param>
    public static int Apply(int weight, int edgeBasisPoints) =>
        (int)((long)weight * edgeBasisPoints / EngineRulesV2.Certain);
}
