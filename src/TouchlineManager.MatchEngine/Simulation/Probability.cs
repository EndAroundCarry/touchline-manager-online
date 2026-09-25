using TouchlineManager.MatchEngine.Configuration;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// The engine's probability arithmetic.
/// </summary>
/// <remarks>
/// Kept in one place because every probability in the simulation has the same shape: a baseline, moved by
/// a rating differential, clamped into the band its own formula allows. Writing that shape once is what
/// makes the constants in <c>EngineRulesV1</c> mean the same thing wherever they are used, and it is
/// integer arithmetic throughout so a decision is identical on every platform.
/// </remarks>
internal static class Probability
{
    /// <summary>
    /// Converts a rating differential into a movement on a baseline probability.
    /// </summary>
    /// <param name="differential">The rating difference, positive favouring the acting side.</param>
    /// <param name="swingBasisPoints">How far the probability moves at a full reference difference.</param>
    /// <param name="reference">The differential at which the swing is applied in full.</param>
    /// <returns>The signed movement in basis points.</returns>
    public static int Swing(int differential, int swingBasisPoints, int reference)
    {
        if (reference <= 0)
        {
            return 0;
        }

        var bounded = int.Clamp(differential, -reference, reference);

        return (int)(((long)swingBasisPoints * bounded) / reference);
    }

    /// <summary>Applies a basis-point multiplier to a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="multiplierBasisPoints">The multiplier, where 10_000 is unchanged.</param>
    public static int Apply(int value, int multiplierBasisPoints) =>
        (int)(((long)value * multiplierBasisPoints) / EngineRulesV1.Certain);

    /// <summary>Clamps a probability into a band.</summary>
    /// <param name="value">The probability.</param>
    /// <param name="minimum">The least it may be.</param>
    /// <param name="maximum">The most it may be.</param>
    public static int Band(int value, int minimum, int maximum) =>
        int.Clamp(value, minimum, maximum);

    /// <summary>
    /// Converts two sides' ratings for the same job into the difference that drives a formula.
    /// </summary>
    /// <param name="actor">The acting side's rating.</param>
    /// <param name="opponent">The opposing side's rating for the job that opposes it.</param>
    public static int Differential(int actor, int opponent) => actor - opponent;
}
