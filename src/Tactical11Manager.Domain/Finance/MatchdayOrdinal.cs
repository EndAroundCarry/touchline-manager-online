using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// Counts matchdays across seasons, so a state that lasts from one season into the next can be measured in matchdays (`DEBT-2`).
/// </summary>
public static class MatchdayOrdinal
{
    /// <summary>The ordinal of a round: the matchdays of the seasons before it, plus its own number.</summary>
    /// <param name="seasonSequenceNumber">The season's ordinal in the world, starting at 1.</param>
    /// <param name="roundNumber">The round within the season, 1 to <see cref="EconomyRuleSet.MatchdaysPerSeason"/>, or 0 before the first.</param>
    public static int Of(int seasonSequenceNumber, int roundNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonSequenceNumber, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(roundNumber);

        return ((seasonSequenceNumber - 1) * EconomyRuleSet.MatchdaysPerSeason) + roundNumber;
    }
}
