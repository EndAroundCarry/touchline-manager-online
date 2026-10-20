using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Squad;

/// <summary>What releasing a player now costs (`CON-12`).</summary>
/// <param name="RemainingWeeks">The weeks of wages left on the contract, rounded up.</param>
/// <param name="PayoutMinor">The payout, in minor units: a share of the wages left, paid at once.</param>
public sealed record ReleaseQuote(int RemainingWeeks, long PayoutMinor);

/// <summary>
/// The payout a club owes to release a player now, half the wages left on his contract (`CON-12`).
/// </summary>
/// <remarks>
/// Pure, so the quote a manager sees before confirming is the amount charged. The weeks left are the rest of
/// the current season, rounded up to a whole week, plus the whole seasons the contract still has after it. A
/// season's length in weeks is read from the calendar (<see cref="WeeksPerSeason"/>) rather than assumed.
/// </remarks>
public static class ReleaseCost
{
    /// <summary>The whole weeks a season lasts, from the first matchday to the end of its rollover (`CAL-6`).</summary>
    /// <param name="window">The season's window.</param>
    public static int WeeksPerSeason(SeasonWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return WholeWeeksUp(window.RolloverEndsAt - window.StartsAt);
    }

    /// <summary>Calculates the payout for releasing a player now.</summary>
    /// <param name="weeklyWageMinor">The wage on the contract, per week, in minor units.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="currentSeasonEndsAt">When the current season, rollover included, ends.</param>
    /// <param name="seasonsAfterCurrent">The full seasons the contract runs after the current one.</param>
    /// <param name="weeksPerSeason">A season's length in weeks, from <see cref="WeeksPerSeason"/>.</param>
    public static ReleaseQuote Calculate(
        long weeklyWageMinor,
        DateTimeOffset now,
        DateTimeOffset currentSeasonEndsAt,
        int seasonsAfterCurrent,
        int weeksPerSeason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weeklyWageMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(seasonsAfterCurrent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeksPerSeason);

        var remainingWeeks = WholeWeeksUp(currentSeasonEndsAt - now)
            + (seasonsAfterCurrent * weeksPerSeason);

        return new ReleaseQuote(
            remainingWeeks,
            weeklyWageMinor * remainingWeeks * WorldRuleSet.ReleasePayoutBp / 10_000);
    }

    private static int WholeWeeksUp(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(span.TotalDays / 7d);
    }
}
