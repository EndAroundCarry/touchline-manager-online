using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Market;

/// <summary>
/// When a listing may resolve (`TRF-2`, `TRF-3`).
/// </summary>
/// <remarks>
/// <para>
/// Resolution happens at one fixed UTC window a day, never within six hours before a matchday kickoff. Both
/// halves are rules: the daily window is what makes every listing a timed auction with a predictable close,
/// and the blackout keeps a transfer from landing in the hours a manager finalises a team sheet for the round.
/// </para>
/// <para>
/// Pure and clock-free: every method takes the instant it reasons about, so the same call yields the same
/// answer in a test and in the worker (ADR-0003, ADR-0009).
/// </para>
/// </remarks>
public static class AuctionWindows
{
    /// <summary>Reports whether an instant falls in the pre-kickoff blackout (`TRF-3`).</summary>
    /// <param name="instant">The instant to test.</param>
    /// <returns>Whether resolution is barred at that instant.</returns>
    public static bool IsBlackout(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();

        if (!WorldRuleSet.KickoffWeekdays.Contains(utc.DayOfWeek))
        {
            return false;
        }

        var kickoff = new DateTimeOffset(
            utc.UtcDateTime.Date + WorldRuleSet.KickoffUtc.ToTimeSpan(),
            TimeSpan.Zero);
        var blackoutStart = kickoff.AddHours(-WorldRuleSet.AuctionBlackoutHoursBeforeKickoff);

        return utc >= blackoutStart && utc < kickoff;
    }

    /// <summary>Gets the first daily resolution window at or after an instant (`TRF-2`, `TRF-3`).</summary>
    /// <param name="instant">The earliest instant a window may fall on.</param>
    /// <returns>The resolution window, at or after <paramref name="instant"/> and outside the blackout.</returns>
    public static DateTimeOffset WindowOnOrAfter(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var candidate = new DateTimeOffset(
            utc.UtcDateTime.Date + WorldRuleSet.AuctionResolutionUtc.ToTimeSpan(),
            TimeSpan.Zero);

        if (candidate < utc)
        {
            candidate = candidate.AddDays(1);
        }

        // The window sits outside the blackout by construction, but the loop keeps the rule true rather than
        // merely likely if the two configured times are ever moved close together.
        while (IsBlackout(candidate))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    /// <summary>Gets when a listing opened now will resolve, honouring the minimum exposure (`TRF-2`).</summary>
    /// <param name="opensAt">When the listing opens.</param>
    /// <returns>The first resolution window at least the minimum exposure after the open.</returns>
    public static DateTimeOffset EndsAtFor(DateTimeOffset opensAt) =>
        WindowOnOrAfter(opensAt.ToUniversalTime().AddHours(WorldRuleSet.ListingMinimumExposureHours));
}
