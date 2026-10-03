using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// Computes a side's six unit ratings from the players currently on the pitch (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// A rating is a weighted mean of the attributes the unit's table names, over the players the table says
/// do that job, with four things then applied to it: how well each player suits their slot (`INS-10`), how
/// fresh they are, what the manager has instructed (`INS-9`), and how many players the side still has.
/// </para>
/// <para>
/// Ratings are recomputed rather than cached, because the inputs change on every sending-off, substitution,
/// and minute of fatigue (the match refreshes both sides every minute), and a cached rating that was one event stale would be wrong in exactly the moments
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
        EngineRulesV2 rules)
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
        };
    }

    private static int Unit(
        IReadOnlyList<ActiveSlot> slots,
        MatchUnit unit,
        MatchInstructionsV1 instructions,
        bool isHome,
        EngineRulesV2 rules)
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

            // A tired player plays below his sheet, skill by skill (engine-v6): physical most, then technical,
            // then mental, and a goalkeeper not at all.
            foreach (var attribute in weighting.Attributes)
            {
                attributeMean += (long)attribute.Weight
                    * slot.Participant.Attributes.ValueOf(attribute.Attribute)
                    * rules.AttributeRatingFactor
                    * EffectiveSkill.TirednessFactor(slot, attribute.Attribute, rules)
                    / EngineRulesV2.Certain;
            }

            attributeMean /= attributeTotal;

            // Then how well they suit the slot, and their fatigue, morale, and sharpness.
            var effective = attributeMean * slot.FamiliarityBasisPoints / EngineRulesV2.Certain;
            effective = effective * EffectiveSkill.StateFactor(slot.Condition, rules) / EngineRulesV2.Certain;

            contributions += familyWeight * effective;
            familyTotal += familyWeight;
        }

        var rating = familyTotal == 0
            ? 0
            : (int)(contributions / familyTotal);

        rating = rating * TacticalModifiers.For(unit, instructions, rules) / EngineRulesV2.Certain;

        return int.Clamp(ApplySideContext(rating, slots.Count, isHome, rules), 0, rules.MaxUnitRating);
    }

    /// <summary>
    /// Applies the things that apply to a whole side rather than to one player: home advantage and being a
    /// player down.
    /// </summary>
    private static int ApplySideContext(int rating, int playersOnPitch, bool isHome, EngineRulesV2 rules)
    {
        if (isHome)
        {
            rating = rating * rules.HomeAdvantageBasisPoints / EngineRulesV2.Certain;
        }

        // A side with ten men is worse than the average of the ten men who remain: the shape is broken and
        // somebody has to cover a job nobody is left to do. One multiplicative step per missing player.
        for (var missing = MatchInputV1.StartersOnPitch - playersOnPitch; missing > 0; missing--)
        {
            rating = rating * rules.ShortHandedPenaltyBasisPoints / EngineRulesV2.Certain;
        }

        return rating;
    }
}
