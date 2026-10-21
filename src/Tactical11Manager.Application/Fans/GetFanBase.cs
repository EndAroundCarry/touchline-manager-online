using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Fans;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>The result of reading the manager's Fan Base page.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="InvalidHistory">Whether the requested history was refused as not one the page serves.</param>
/// <param name="FanBase">The page, when the read succeeded.</param>
public sealed record GetFanBaseResult(SquadReadOutcome Outcome, bool InvalidHistory, FanBaseResponse? FanBase);

/// <summary>
/// Reads the fans of the club the caller manages: how many there are, how they feel, what they expect from the next match, and the
/// history the Fan Base page draws its charts from (`FAN-1`…`FAN-10`).
/// </summary>
/// <remarks>
/// <para>
/// Which club that is comes from the tenure, never from the request (`WORLD-9`). The fans' mood is the club's own business, so only the
/// manager who holds the club reads this; everyone else gets <see cref="GetClubFanSummary"/>.
/// </para>
/// <para>
/// The expectation for the next match is the one frozen at the team-sheet lock when there is one, because that is the one the result will
/// be judged against. Before the lock it is worked out now from the squads and the table, exactly as the lock will work it out, and it is
/// marked as not frozen so the page can say it may still move.
/// </para>
/// </remarks>
public sealed class GetFanBase
{
    /// <summary>How many of the season's published matches the page lists.</summary>
    public const int RecentMatchCount = 10;

    private readonly ResolveOwnedClub _access;
    private readonly IFanQueries _fans;
    private readonly ICompetitionQueries _competition;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetFanBase(ResolveOwnedClub access, IFanQueries fans, ICompetitionQueries competition, IClock clock)
    {
        _access = access;
        _fans = fans;
        _competition = competition;
        _clock = clock;
    }

    /// <summary>Reads the manager's club's fans, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="history">Which history to return, <see cref="FanHistoryScopes"/>, or null for the current manager's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetFanBaseResult> ExecuteAsync(Guid userId, string? history, CancellationToken cancellationToken)
    {
        var scope = history ?? FanHistoryScopes.Tenure;

        if (scope is not (FanHistoryScopes.Tenure or FanHistoryScopes.Club))
        {
            return new GetFanBaseResult(SquadReadOutcome.Found, true, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetFanBaseResult(access.Outcome.ToReadOutcome(), false, null);
        }

        var club = await _fans.GetClubAsync(access.ClubId, cancellationToken);

        if (club is null)
        {
            return new GetFanBaseResult(SquadReadOutcome.ClubNotFound, false, null);
        }

        var allEvents = await _fans.ListEventsAsync(club.ClubId, cancellationToken);
        var lastTakeover = allEvents.ToList().FindLastIndex(fanEvent => fanEvent.Kind == FanEventKind.Takeover);
        var tenureEvents = lastTakeover < 0 ? allEvents : [.. allEvents.Skip(lastTakeover)];
        var shown = scope == FanHistoryScopes.Club ? allEvents : tenureEvents;

        var snapshot = await _competition.GetClubFixturesAsync(club.ClubId, cancellationToken);
        var next = snapshot?.Fixtures.FirstOrDefault(
            fixture => fixture.Status is not (FixtureStatus.Published or FixtureStatus.Void));
        var recent = snapshot is null
            ? []
            : snapshot.Fixtures
                .Where(fixture => fixture.Status == FixtureStatus.Published)
                .OrderByDescending(fixture => fixture.RoundNumber)
                .Take(RecentMatchCount)
                .ToList();

        var fixtureIds = recent.Select(fixture => fixture.Id).ToList();

        if (next is not null)
        {
            fixtureIds.Add(next.Id);
        }

        var frozen = await _fans.GetFrozenExpectationsAsync(club.ClubId, fixtureIds, cancellationToken);

        var nextFixture = next is null ? null : await NextFixtureAsync(club, next, frozen, cancellationToken);
        var matchEvents = allEvents
            .Where(fanEvent => fanEvent.Kind == FanEventKind.Match && fanEvent.FixtureId is not null)
            .GroupBy(fanEvent => fanEvent.FixtureId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var recentMatches = recent
            .Select(fixture =>
            {
                matchEvents.TryGetValue(fixture.Id, out var fanEvent);

                return new FanRecentMatchResponse(
                    fixture.ToResponse(),
                    frozen.TryGetValue(fixture.Id, out var expectation) ? expectation.ToResponse() : null,
                    fanEvent?.PointsWon,
                    fanEvent?.FansBefore,
                    fanEvent?.FansAfter,
                    fanEvent?.MoodBeforeBp,
                    fanEvent?.MoodAfterBp);
            })
            .ToList();

        var tier = club.TierNumber;

        return new GetFanBaseResult(
            SquadReadOutcome.Found,
            false,
            new FanBaseResponse(
                club.ClubId,
                club.Name,
                tier,
                club.Fans,
                FanRuleSet.WantToComeFor(club.Fans),
                StadiumRuleSet.MatchdayDemandFor(club.Fans, club.MoodBp),
                club.MoodBp,
                FanMoods.Code(FanMoods.WordFor(club.MoodBp)),
                FanMoods.Phrase(FanMoods.WordFor(club.MoodBp)),
                FanRuleSet.FloorFansFor(tier),
                FanRuleSet.SoftCapFansFor(tier),
                club.SeasonTargetRank,
                club.LastWeekGap,
                club.Fans - FansAtSeasonStart(club, tenureEvents),
                scope,
                lastTakeover < 0 ? null : allEvents[lastTakeover].OccurredAt,
                nextFixture,
                recentMatches,
                [.. shown.Select(ToEventResponse)],
                _clock.UtcNow));
    }

    /// <summary>How many fans the club had when the season began, or when the manager took over if that was later.</summary>
    private static int FansAtSeasonStart(FanClubRow club, IReadOnlyList<FanEventRow> tenureEvents)
    {
        var first = tenureEvents.FirstOrDefault(fanEvent => fanEvent.SeasonId == club.CurrentSeasonId);

        if (first is null)
        {
            return club.Fans;
        }

        // A takeover is the first thing that happens to a manager's fans, and what they start from is what it left them with.
        return first.Kind == FanEventKind.Takeover ? first.FansAfter : first.FansBefore;
    }

    private async Task<FanNextFixtureResponse> NextFixtureAsync(
        FanClubRow club,
        ClubFixtureRow next,
        IReadOnlyDictionary<Guid, FanExpectation> frozen,
        CancellationToken cancellationToken)
    {
        if (frozen.TryGetValue(next.Id, out var held))
        {
            return new FanNextFixtureResponse(next.ToResponse(), held.ToResponse(), Frozen: true);
        }

        // Not locked yet: size the two squads up now, the way the lock will (`FAN-3`, `FAN-4`).
        var division = await _fans.ListDivisionOfClubAsync(club.ClubId, cancellationToken);
        var form = division.ToDictionary(row => row.ClubId, row => new FanForm(row.Played, row.Points));
        var strengths = await _fans.GetSquadStrengthsAsync([club.ClubId, next.OpponentClubId], cancellationToken);

        var live = FanRuleSet.ExpectationFor(
            strengths.GetValueOrDefault(club.ClubId),
            form.GetValueOrDefault(club.ClubId, FanForm.None),
            strengths.GetValueOrDefault(next.OpponentClubId),
            form.GetValueOrDefault(next.OpponentClubId, FanForm.None),
            next.IsHome);

        return new FanNextFixtureResponse(next.ToResponse(), live.ToResponse(), Frozen: false);
    }

    private static FanEventResponse ToEventResponse(FanEventRow fanEvent) =>
        new(
            fanEvent.Id,
            FanEventKinds.Code(fanEvent.Kind),
            fanEvent.OccurredAt,
            fanEvent.SeasonNumber,
            fanEvent.FansAfter,
            fanEvent.MoodAfterBp,
            fanEvent.FixtureId,
            fanEvent.ExpectedPointsCentis,
            fanEvent.PointsWon,
            fanEvent.Gap);
}
