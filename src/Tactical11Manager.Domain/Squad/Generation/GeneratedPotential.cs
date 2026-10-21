using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Squad.Generation;

/// <summary>
/// Draws a generated player's hidden potential before his skills exist (`TRN-18`).
/// </summary>
/// <remarks>
/// <para>
/// Potential is a ceiling drawn on its own, around the tier's mean ability. The generator then derives the
/// player's current skills from it and his age, so a young player starts below a ceiling that is already
/// fixed. Deriving potential from skills did the opposite: the years a young player had left to develop were
/// taken off his skills and added back to his potential, the two cancelled, and every ceiling in a tier landed
/// in the same narrow band.
/// </para>
/// <para>
/// The draw is a band, then an offset from the tier mean. Most players are normal; a few are talents
/// and fewer are gems. The bands do not overlap, so a talent is always above every normal player of his tier.
/// </para>
/// <list type="table">
///   <listheader><term>Band</term><description>Share, offset</description></listheader>
///   <item><term>Normal</term><description>80%, two four-faced dice less four: −4..+2, most often −1</description></item>
///   <item><term>Talent</term><description>15%, +3..+5</description></item>
///   <item><term>Gem</term><description>5%, +6..+7</description></item>
/// </list>
/// <para>
/// The mean offset is about +0.1, so a squad's overall strength per tier is kept. The normal band sits one
/// point low to pay for the tail.
/// </para>
/// </remarks>
public static class GeneratedPotential
{
    /// <summary>Draws one player's potential, on the 1–20 scale.</summary>
    /// <param name="tier">The tier the club plays in, which fixes the mean the offset is taken from.</param>
    /// <param name="rng">The generator's random stream. One draw for the band and one or two for the offset.</param>
    public static int Draw(int tier, Pcg32 rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        return Around(WorldRuleSet.GeneratedAbilityMeanForTier(tier), rng);
    }

    /// <summary>
    /// Draws a youth-intake player's potential, on the 1–20 scale. The same bands as <see cref="Draw"/>, around
    /// the youth mean for the tier, which moves on its own so reseeding the starting world cannot cap a gem.
    /// </summary>
    /// <param name="tier">The tier the club plays in, which fixes the mean the offset is taken from.</param>
    /// <param name="rng">The generator's random stream. One draw for the band and one or two for the offset.</param>
    public static int DrawYouth(int tier, Pcg32 rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        return Around(WorldRuleSet.YouthPotentialMeanForTier(tier), rng);
    }

    private static int Around(int mean, Pcg32 rng) =>
        Math.Clamp(mean + OffsetFor(rng), WorldRuleSet.AttributeMin, WorldRuleSet.AttributeMax);

    private static int OffsetFor(Pcg32 rng)
    {
        var roll = rng.NextInt(100);

        if (roll < WorldRuleSet.GeneratedPotentialGemPercent)
        {
            return WorldRuleSet.GeneratedPotentialGemOffsetMin
                + rng.NextInt(WorldRuleSet.GeneratedPotentialGemOffsetCount);
        }

        if (roll < WorldRuleSet.GeneratedPotentialGemPercent + WorldRuleSet.GeneratedPotentialTalentPercent)
        {
            return WorldRuleSet.GeneratedPotentialTalentOffsetMin
                + rng.NextInt(WorldRuleSet.GeneratedPotentialTalentOffsetCount);
        }

        return rng.NextInt(WorldRuleSet.GeneratedPotentialNormalDieFaces)
            + rng.NextInt(WorldRuleSet.GeneratedPotentialNormalDieFaces)
            + WorldRuleSet.GeneratedPotentialNormalFloor;
    }
}
