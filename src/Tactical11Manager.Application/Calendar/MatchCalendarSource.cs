using System.Globalization;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Contracts.Calendar;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Calendar;

/// <summary>
/// Puts the club's league matches on the calendar (`CAL-1`, `CAL-2`).
/// </summary>
/// <remarks>
/// Built on the competition module's own fixture list, so what the calendar says about a match is exactly
/// what the fixtures screen says: the same visibility rule for a score, the same outcome. A played match's
/// result sits in the detail and names its match, so the page can hold it back until the manager has seen it.
/// </remarks>
public sealed class MatchCalendarSource : ICalendarEventSource
{
    private readonly ICompetitionQueries _queries;

    /// <summary>Initializes the source.</summary>
    public MatchCalendarSource(ICompetitionQueries queries) => _queries = queries;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid clubId,
        CalendarWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var snapshot = await _queries.GetClubFixturesAsync(clubId, cancellationToken);

        if (snapshot is null)
        {
            return [];
        }

        return
        [
            .. snapshot.Fixtures
                .Where(fixture => fixture.Status != FixtureStatus.Void && window.Contains(fixture.KickoffAt))
                .Select(ToEvent),
        ];
    }

    private static CalendarEvent ToEvent(ClubFixtureRow fixture)
    {
        var response = fixture.ToResponse();
        var title = string.Create(
            CultureInfo.InvariantCulture,
            $"{(fixture.IsHome ? "Home" : "Away")} v {fixture.OpponentName}");
        // A promotion play-off match is named by its stage; the round number 31-33 is only where it is stored (PR-4).
        var round = response.StageLabel
            ?? string.Create(CultureInfo.InvariantCulture, $"Round {fixture.RoundNumber}");

        if (response.MatchId is { } matchId && response.HomeScore is { } home && response.AwayScore is { } away)
        {
            // The score reads home-first whichever side the club is on, as it does on the fixtures screen.
            var result = string.Create(CultureInfo.InvariantCulture, $"{home}–{away}");

            return new CalendarEvent(
                $"fixture:{fixture.Id}",
                CalendarEventKinds.Match,
                fixture.KickoffAt,
                title,
                $"{round} · {result} · {OutcomeWord(response.Outcome)}",
                $"/matches/{matchId}",
                ResultMatchId: matchId);
        }

        return new CalendarEvent(
            $"fixture:{fixture.Id}",
            CalendarEventKinds.Match,
            fixture.KickoffAt,
            title,
            round,
            fixture.Status == FixtureStatus.Scheduled ? $"/fixtures/{fixture.Id}/prepare" : "/fixtures");
    }

    private static string OutcomeWord(string? outcome) => outcome switch
    {
        "win" => "Win",
        "draw" => "Draw",
        "loss" => "Defeat",
        _ => "Played",
    };
}
