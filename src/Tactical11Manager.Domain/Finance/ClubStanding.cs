using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// How a club's standing moves at the end of a season (`STD-*`, plan toasty-scott section 4).
/// </summary>
/// <remarks>
/// <para>
/// Standing is the slow lever behind how long it takes a club to become rich: it multiplies what a sponsor pays, and it moves by the club's
/// final place and by what it did against its objective, minus a pull back toward what its tier would give a club that had done nothing. A
/// club that wins every season reaches about 95 after about ten; a club that is regularly in the top three reaches about 85; a mid-table club
/// stays near its tier's opening standing. Nothing is capped beyond the scale, so the elite is what repeated success produces and not a rule.
/// </para>
/// <para>
/// It is kept in hundredths of a point, so a pull of a tenth of a small distance still moves it. A manager sees whole points.
/// </para>
/// </remarks>
public static class ClubStanding
{
    /// <summary>The hundredths in one point of standing.</summary>
    public const int CentisPerPoint = 100;

    /// <summary>A tier's opening standing in hundredths of a point.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int OpeningCentis(int tier) => EconomyRuleSet.OpeningStandingFor(tier) * CentisPerPoint;

    /// <summary>The whole points a manager reads for a standing in hundredths.</summary>
    /// <param name="centis">The standing in hundredths of a point.</param>
    public static int PointsOf(int centis) =>
        Math.Clamp((centis + (CentisPerPoint / 2)) / CentisPerPoint, EconomyRuleSet.StandingMin, EconomyRuleSet.StandingMax);

    /// <summary>The standing after a season, in hundredths of a point.</summary>
    /// <param name="standingCentis">The standing the season began with.</param>
    /// <param name="finishedTier">The tier the club played the season in.</param>
    /// <param name="place">The final place, 1 to 18.</param>
    /// <param name="outcome">How the season's objective turned out, or null when the club had none.</param>
    /// <param name="nextTier">The tier the club plays in next: higher than <paramref name="finishedTier"/> for a relegated club.</param>
    public static int Step(int standingCentis, int finishedTier, int place, ObjectiveOutcome? outcome, int nextTier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finishedTier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextTier, 1);

        var relegated = nextTier > finishedTier;
        var points = EconomyRuleSet.StandingFinishDelta(place, relegated)
            + (outcome is { } result ? EconomyRuleSet.StandingObjectiveDelta(result) : 0);

        var moved = standingCentis + (points * CentisPerPoint);
        var pull = (long)(OpeningCentis(nextTier) - moved) * EconomyRuleSet.StandingPullBp / EconomyRuleSet.OneHundredPercentBp;

        return Math.Clamp(
            (int)(moved + pull),
            EconomyRuleSet.StandingMin * CentisPerPoint,
            EconomyRuleSet.StandingMax * CentisPerPoint);
    }
}
