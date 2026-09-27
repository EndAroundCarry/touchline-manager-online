using TouchlineManager.MatchEngine.Configuration;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>One player's match, as the rating rule reads it.</summary>
/// <param name="MinutesPlayed">How long the player was on the pitch.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Assists">Goals set up.</param>
/// <param name="YellowCards">Bookings received, counting a second yellow as the booking it was.</param>
/// <param name="SentOff">Whether the player was sent off, counting a second yellow as the red it became.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="Won">Whether the player's side won.</param>
/// <param name="Drew">Whether the player's side drew.</param>
internal readonly record struct PlayerMatchFacts(
    int MinutesPlayed,
    int Goals,
    int Assists,
    int YellowCards,
    bool SentOff,
    int Saves,
    bool Won,
    bool Drew);

/// <summary>
/// Turns one player's match facts into a match rating in basis points (`engine-v2`).
/// </summary>
/// <remarks>
/// <para>
/// The engine's own summary of how a player played, produced with the result so a season's average rating
/// needs no second definition of it. It is arithmetic rather than chance: every term is a fact the match
/// already records — minutes, goals, assists, saves, cards, and the result — so it consumes no draw and
/// cannot move the simulation. A player who did not appear is given no rating at all rather than a low one,
/// because "how well did they play" is not a question a match has an answer to for the bench.
/// </para>
/// <para>
/// The result is weighted by playing time, so a late cameo carries it in proportion, and every other term
/// is absolute. The rating is clamped to the rules' own bounds, so no combination of a rout, a hat-trick,
/// and a clean sheet can produce a figure outside the scale.
/// </para>
/// </remarks>
internal static class PlayerRatingCalculator
{
    /// <summary>The minutes a full regulation match is played for, which scales the result's contribution.</summary>
    public const int FullMatchMinutes = 90;

    /// <summary>One whole of a basis-point measure: the divisor the weighting is scaled by.</summary>
    private const int BasisPointUnity = 10_000;

    /// <summary>Computes one player's match rating.</summary>
    /// <param name="rules">The rules in force.</param>
    /// <param name="facts">The player's match facts.</param>
    /// <returns>The rating in basis points, or zero when the player did not appear.</returns>
    public static int Calculate(EngineRulesV2 rules, PlayerMatchFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (facts.MinutesPlayed <= 0)
        {
            return 0;
        }

        var performance =
            ((long)facts.Goals * rules.RatingGoalBonusBasisPoints)
            + ((long)facts.Assists * rules.RatingAssistBonusBasisPoints)
            + Math.Min(
                facts.Saves * rules.RatingSaveBonusBasisPoints,
                rules.RatingMaxSaveBonusBasisPoints)
            - ((long)facts.YellowCards * rules.RatingYellowPenaltyBasisPoints)
            - (facts.SentOff ? rules.RatingRedPenaltyBasisPoints : 0);

        var resultBonus = facts.Won
            ? rules.RatingWinBonusBasisPoints
            : facts.Drew ? rules.RatingDrawBonusBasisPoints : -rules.RatingLossPenaltyBasisPoints;

        var weightBasisPoints = Math.Min(
            BasisPointUnity,
            facts.MinutesPlayed * BasisPointUnity / FullMatchMinutes);

        var rating = rules.RatingBaseBasisPoints
            + performance
            + ((long)resultBonus * weightBasisPoints / BasisPointUnity);

        return (int)Math.Clamp(rating, rules.RatingMinBasisPoints, rules.RatingMaxBasisPoints);
    }
}
