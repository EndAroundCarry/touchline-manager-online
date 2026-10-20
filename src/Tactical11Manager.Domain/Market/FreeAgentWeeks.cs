using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Market;

/// <summary>
/// When a free-agent week begins and ends (`FA-5`).
/// </summary>
/// <remarks>
/// A week runs from Monday at the daily auction window to the next Monday at the same instant, so the listings a
/// batch opens close at the instant the next batch opens. Pure and clock-free: every method takes the instant
/// it reasons about.
/// </remarks>
public static class FreeAgentWeeks
{
    /// <summary>Gets the instant a week opens on its Monday.</summary>
    /// <param name="weekStart">The Monday.</param>
    public static DateTimeOffset OpensAt(DateOnly weekStart) =>
        new(
            weekStart.ToDateTime(WorldRuleSet.AuctionResolutionUtc, DateTimeKind.Utc),
            TimeSpan.Zero);

    /// <summary>Gets the instant a week's listings resolve, which is when the next week opens.</summary>
    /// <param name="weekStart">The Monday.</param>
    public static DateTimeOffset EndsAt(DateOnly weekStart) => OpensAt(weekStart).AddDays(7);

    /// <summary>Gets the Monday of the most recent week that has opened at or before an instant.</summary>
    /// <param name="instant">The instant.</param>
    public static DateOnly WeekStartAtOrBefore(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var day = DateOnly.FromDateTime(utc.UtcDateTime);

        if (utc < OpensAt(day))
        {
            day = day.AddDays(-1);
        }

        while (day.DayOfWeek != WorldRuleSet.FreeAgentWeekStartDay)
        {
            day = day.AddDays(-1);
        }

        return day;
    }
}
