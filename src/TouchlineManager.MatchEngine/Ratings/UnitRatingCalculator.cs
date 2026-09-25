using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// Computes a side's nine unit ratings from the players currently on the pitch (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// A rating is a weighted mean of the attributes the unit's table names, over the players the table says
/// do that job, with four things then applied to it: how well each player suits their slot (`INS-10`), how
/// fresh they are, what the manager has instructed (`INS-9`), and how many players the side still has.
/// </para>
/// <para>
/// Ratings are recomputed rather than cached, because the inputs change on every sending-off, substitution,
/// and minute of fatigue, and a cached rating that was one event stale would be wrong in exactly the moments
/// that decide a match. The cost is a few hundred integer operations per possession, which is nothing
/// beside the clarity of every consumer reading the same number.
/// </para>
/// </remarks>
public static class UnitRatingCalculator
{
    /// <summary>Computes every unit rating for the players currently on the pitch.</summary>
    /// <param name="activeSlots">The players on the pitch, and where they are deployed.</param>
    /// <param name="instructions">The side's team instructions.</param>
    /// <param name="isHome">Whether the side is at home, which applies home advantage.</param>
    /// <param name="rules">The rules in force.</param>
    public static MatchUnitRatings Calculate(
        IReadOnlyList<ActiveSlot> activeSlots,
        MatchInstructionsV1 instructions,
        bool isHome,
        EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(activeSlots);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(rules);

        return new MatchUnitRatings
        {
            BuildUp = Unit(activeSlots, MatchUnit.BuildUp, instructions, isHome, rules),
            Creation = Unit(activeSlots, MatchUnit.Creation, instructions, isHome, rules),
            Finishing = Unit(activeSlots, MatchUnit.Finishing, instructions, isHome, rules),
            DefensivePressure = Unit(activeSlots, MatchUnit.DefensivePressure, instructions, isHome, rules),
            DefensiveShape = Unit(activeSlots, MatchUnit.DefensiveShape, instructions, isHome, rules),
            Goalkeeping = Unit(activeSlots, MatchUnit.Goalkeeping, instructions, isHome, rules),
            SetPieces = Unit(activeSlots, MatchUnit.SetPieces, instructions, isHome, rules),
            Fitness = Unit(activeSlots, MatchUnit.Fitness, instructions, isHome, rules),
            Cohesion = Cohesion(activeSlots, isHome, rules),
        };
    }

    /// <summary>
    /// Computes how well a side's eleven fit their jobs, as a rating on the same scale as the others.
    /// </summary>
    /// <remarks>
    /// Not an attribute mean. A side of excellent players all playing out of position is a bad side, and a
    /// mean over their attributes would call it a good one. This reads the familiarity already resolved per
    /// slot instead, so it measures fit rather than quality, and it takes no tactical modifier because no
    /// instruction changes how well a player suits a job.
    /// </remarks>
    private static int Cohesion(IReadOnlyList<ActiveSlot> slots, bool isHome, EngineRulesV1 rules)
    {
        if (slots.Count == 0)
        {
            return 0;
        }

        long familiarityTotal = 0;
        var makeshift = 0;

        foreach (var slot in slots)
        {
            familiarityTotal += slot.FamiliarityBasisPoints;

            if (slot.FamiliarityBasisPoints < rules.UnfamiliarRolePenaltyBasisPoints)
            {
                makeshift++;
            }
        }

        var average = (int)(familiarityTotal / slots.Count);

        average -= makeshift * rules.OutOfPositionCohesionPenaltyBasisPoints;
        average = int.Clamp(average, 0, EngineRulesV1.Certain);

        // Put onto the rating scale so cohesion can be compared with the other units: a side where everyone
        // fits is worth a maximum-attribute player, and a side of misfits is worth correspondingly less.
        var rating = average * (rules.AttributeRatingFactor * MatchAttributeNames.Max) / EngineRulesV1.Certain;

        return int.Clamp(ApplySideContext(rating, slots.Count, isHome, rules), 0, rules.MaxUnitRating);
    }

    private static int Unit(
        IReadOnlyList<ActiveSlot> slots,
        MatchUnit unit,
        MatchInstructionsV1 instructions,
        bool isHome,
        EngineRulesV1 rules)
    {
        var weighting = UnitRatingWeights.Of(unit);
        var attributeTotal = weighting.TotalAttributeWeight;

        long contributions = 0;
        long familyTotal = 0;

        foreach (var slot in slots)
        {
            var familyWeight = weighting.FamilyWeightOf(slot.Slot.Family);

            if (familyWeight == 0)
            {
                continue;
            }

            // The player's own rating for this unit, before anything about the situation.
            long attributeMean = 0;

            foreach (var attribute in weighting.Attributes)
            {
                attributeMean += (long)attribute.Weight
                    * slot.Participant.Attributes.ValueOf(attribute.Attribute)
                    * rules.AttributeRatingFactor;
            }

            attributeMean /= attributeTotal;

            // Then how well they suit the slot, and how fresh they are.
            var effective = attributeMean * slot.FamiliarityBasisPoints / EngineRulesV1.Certain;
            effective = effective * StateMultiplier(slot.Condition, rules) / EngineRulesV1.Certain;

            contributions += familyWeight * effective;
            familyTotal += familyWeight;
        }

        var rating = familyTotal == 0
            ? 0
            : (int)(contributions / familyTotal);

        rating = rating * TacticalModifiers.For(unit, instructions, rules) / EngineRulesV1.Certain;

        return int.Clamp(ApplySideContext(rating, slots.Count, isHome, rules), 0, rules.MaxUnitRating);
    }

    /// <summary>
    /// Applies the things that apply to a whole side rather than to one player: home advantage and being a
    /// player down.
    /// </summary>
    private static int ApplySideContext(int rating, int playersOnPitch, bool isHome, EngineRulesV1 rules)
    {
        if (isHome)
        {
            rating = rating * rules.HomeAdvantageBasisPoints / EngineRulesV1.Certain;
        }

        // A side with ten men is worse than the average of the ten men who remain: the shape is broken and
        // somebody has to cover a job nobody is left to do. One multiplicative step per missing player.
        for (var missing = MatchInputV1.StartersOnPitch - playersOnPitch; missing > 0; missing--)
        {
            rating = rating * rules.ShortHandedPenaltyBasisPoints / EngineRulesV1.Certain;
        }

        return rating;
    }

    /// <summary>
    /// Combines condition, fatigue, morale, and sharpness into one multiplier on a player's contribution.
    /// </summary>
    /// <remarks>
    /// Each is a linear interpolation between the rules' floor and ceiling, and they compose
    /// multiplicatively, so a tired, unfit, unhappy player is worse than any one of those alone. The bounds
    /// keep the effect real but never decisive: at the worst possible state a player is worth about a
    /// quarter less, which is roughly five attribute points — enough to prefer a fresh substitute, not
    /// enough to make a good player bad.
    /// </remarks>
    private static int StateMultiplier(PlayerCondition condition, EngineRulesV1 rules)
    {
        var multiplier = Interpolate(
            rules.ConditionFactorFloorBasisPoints,
            rules.ConditionFactorCeilingBasisPoints,
            condition.ConditionBasisPoints);

        multiplier = multiplier * Interpolate(
            rules.FatigueFactorFloorBasisPoints,
            rules.FatigueFactorCeilingBasisPoints,
            EngineRulesV1.Certain - condition.FatigueBasisPoints) / EngineRulesV1.Certain;

        multiplier = multiplier * Interpolate(
            rules.MoraleFactorFloorBasisPoints,
            rules.MoraleFactorCeilingBasisPoints,
            condition.MoraleBasisPoints) / EngineRulesV1.Certain;

        multiplier = multiplier * Interpolate(
            rules.SharpnessFactorFloorBasisPoints,
            rules.SharpnessFactorCeilingBasisPoints,
            condition.SharpnessBasisPoints) / EngineRulesV1.Certain;

        return multiplier;
    }

    private static int Interpolate(int floor, int ceiling, int valueBasisPoints)
    {
        var bounded = int.Clamp(valueBasisPoints, 0, EngineRulesV1.Certain);

        return floor + (int)(((long)(ceiling - floor) * bounded) / EngineRulesV1.Certain);
    }
}
