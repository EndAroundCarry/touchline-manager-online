using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Competition;

/// <summary>
/// The real-time calendar every country shares (`CAL-2`, `WORLD-10`).
/// </summary>
/// <remarks>
/// <para>
/// A season is a double round-robin of 34 matchdays played on Tuesdays, Thursdays, and Sundays at
/// 19:00 UTC. The window is a pure function of the first matchday, so the seeder, the fixture
/// generator, and the season shell cannot disagree about when a season starts or ends — and a test can
/// assert the calendar without a database or a clock.
/// </para>
/// <para>
/// Matchday <em>instants</em> are computed in UTC and only ever rendered in local time (`CAL-4`). No
/// method here reads the system clock.
/// </para>
/// </remarks>
public static class SeasonCalendar
{
    /// <summary>Whether a date falls on a matchday weekday (`CAL-2`).</summary>
    public static bool IsMatchday(DateOnly date) =>
        Array.IndexOf(WorldRuleSet.KickoffWeekdays, date.DayOfWeek) >= 0;

    /// <summary>
    /// Finds the first matchday on or after a date.
    /// </summary>
    /// <remarks>
    /// This is what makes `CAL-5` implementable: the operator configures a desired start, and the
    /// calendar rounds it forward to a legal matchday rather than deriving one from deployment time.
    /// </remarks>
    public static DateOnly FirstMatchdayOnOrAfter(DateOnly date)
    {
        var candidate = date;

        while (!IsMatchday(candidate))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    /// <summary>Gets the kickoff instant for a matchday date, at the standard UTC time (`CAL-2`).</summary>
    public static DateTimeOffset KickoffAt(DateOnly date) =>
        new(date.ToDateTime(WorldRuleSet.KickoffUtc, DateTimeKind.Utc));

    /// <summary>
    /// Lists the dates of the matchdays of one season, starting from the first.
    /// </summary>
    /// <param name="firstMatchday">A date that is already a matchday weekday.</param>
    /// <param name="matchdays">How many matchdays the season has.</param>
    public static IReadOnlyList<DateOnly> MatchdayDates(DateOnly firstMatchday, int matchdays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchdays);

        if (!IsMatchday(firstMatchday))
        {
            throw new ArgumentException(
                $"{firstMatchday:yyyy-MM-dd} is not a matchday weekday (CAL-2).",
                nameof(firstMatchday));
        }

        var dates = new List<DateOnly>(matchdays);
        var current = firstMatchday;

        // Walk the weekday cycle rather than the calendar: Tue → Thu is two days, Thu → Sun is three,
        // Sun → Tue is two. Deriving the gap from the cycle keeps a four-matchday week impossible.
        var index = Array.IndexOf(WorldRuleSet.KickoffWeekdays, current.DayOfWeek);

        for (var matchday = 0; matchday < matchdays; matchday++)
        {
            dates.Add(current);

            var next = WorldRuleSet.KickoffWeekdays[(index + 1) % WorldRuleSet.KickoffWeekdays.Length];
            var gap = ((int)next - (int)current.DayOfWeek + 7) % 7;

            current = current.AddDays(gap == 0 ? 7 : gap);
            index = (index + 1) % WorldRuleSet.KickoffWeekdays.Length;
        }

        return dates;
    }

    /// <summary>
    /// Computes a season's window from its first matchday.
    /// </summary>
    /// <param name="firstMatchday">The first matchday, which must already be a matchday weekday.</param>
    /// <returns>The start, the end of the last matchday, and the end of the rollover period (`CAL-6`).</returns>
    public static SeasonWindow Window(DateOnly firstMatchday)
    {
        var dates = MatchdayDates(firstMatchday, WorldRuleSet.MatchdaysPerSeason);
        var startsAt = KickoffAt(dates[0]);
        var endsAt = KickoffAt(dates[^1]);

        return new SeasonWindow(startsAt, endsAt, endsAt.AddDays(WorldRuleSet.RolloverDays), dates);
    }
}

/// <summary>The real-time window of one season.</summary>
/// <param name="StartsAt">Kickoff of matchday 1.</param>
/// <param name="EndsAt">Kickoff of the final matchday.</param>
/// <param name="RolloverEndsAt">When the rollover period closes and the next season begins (`CAL-6`).</param>
/// <param name="MatchdayDates">The dates of every matchday, in order.</param>
public sealed record SeasonWindow(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset RolloverEndsAt,
    IReadOnlyList<DateOnly> MatchdayDates);
