using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// When a season's sponsor and objective must be chosen by, and how long a new manager may think again (`SPN-4`, `SPN-6`).
/// </summary>
/// <remarks>
/// The deadline is the team-sheet lock of the fourth matchday, which is the end of the season's first week. It is a pure function of the
/// season's first matchday, so the offers' message, the job that signs for the board and the calendar all name the same instant without
/// reading the fixture list.
/// </remarks>
public static class SponsorDeadline
{
    /// <summary>The matchday whose team-sheet lock is the deadline.</summary>
    public const int Round = 4;

    /// <summary>How many matchdays a manager who takes a club over may re-pick the offer and the objective for.</summary>
    public const int TakeoverWindowMatchdays = 3;

    /// <summary>The instant the board signs for a club that has not signed.</summary>
    /// <param name="firstMatchday">The date of the season's first matchday.</param>
    public static DateTimeOffset LockAtFor(DateOnly firstMatchday)
    {
        var dates = SeasonCalendar.MatchdayDates(firstMatchday, WorldRuleSet.MatchdaysPerSeason);

        return SeasonCalendar.KickoffAt(dates[Round - 1]).AddMinutes(-WorldRuleSet.TeamSheetLockMinutes);
    }
}
