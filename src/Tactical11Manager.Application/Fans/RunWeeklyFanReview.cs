using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>What a week's fan review did.</summary>
/// <param name="Clubs">How many clubs are in the season being played.</param>
/// <param name="Reviewed">How many clubs' fans were reviewed.</param>
/// <param name="Skipped">How many clubs were left alone: already reviewed for the week, or with no game played yet.</param>
/// <param name="TargetsSet">How many clubs were given the place their fans hope for, because they had none.</param>
public sealed record RunWeeklyFanReviewResult(int Clubs, int Reviewed, int Skipped, int TargetsSet);

/// <summary>
/// Judges every club against the place its fans hoped for, once a week (`FAN-8`).
/// </summary>
/// <remarks>
/// <para>
/// A club's gap is its projected place minus its target. The projected place ranks the division by points a game, so it is fair to a
/// club that has played fewer games than another, with the published table's rank breaking a tie. The mood moves by the gap and by how
/// much it closed since last week, and counts for more as the season goes on (<c>FanRuleSet.AfterWeek</c>).
/// </para>
/// <para>
/// The target is the season objective once that feature exists; until then it is the club's rank by squad strength in its division when
/// the season began, set at the rollover. A club that has none (a world that was running before the fans existed) is given one here from
/// the squads as they stand, then stays with it for the season.
/// </para>
/// <para>
/// A club is reviewed once for a week: the week's review event is unique per club, and a club that already has it is skipped, so a retried
/// job or a second call for the same week changes nothing. A club that has not played a game has no projected place and is skipped. The
/// run is one transaction, so a week reviews every club or none.
/// </para>
/// </remarks>
public sealed partial class RunWeeklyFanReview
{
    private readonly IFanRepository _fans;
    private readonly IFanQueries _queries;
    private readonly FanBaseKeeper _keeper;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<RunWeeklyFanReview> _logger;

    /// <summary>Initializes the use case.</summary>
    public RunWeeklyFanReview(
        IFanRepository fans,
        IFanQueries queries,
        FanBaseKeeper keeper,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<RunWeeklyFanReview> logger)
    {
        _fans = fans;
        _queries = queries;
        _keeper = keeper;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Reviews every club's fans for one week.</summary>
    /// <param name="weekOf">The Sunday the review is for, which keys it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RunWeeklyFanReviewResult> ExecuteAsync(DateOnly weekOf, CancellationToken cancellationToken)
    {
        var rows = await _queries.ListCurrentSeasonAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new RunWeeklyFanReviewResult(0, 0, 0, 0);
        }

        var now = _clock.UtcNow;
        var reviewed = await _fans.FindClubsReviewedAsync(weekOf, cancellationToken);
        var tierOf = rows.ToDictionary(row => row.ClubId, row => row.TierNumber);
        var seasonOf = rows.ToDictionary(row => row.ClubId, row => row.SeasonId);

        var bases = await _keeper.LoadAsync(
            [.. tierOf.Keys],
            clubId => tierOf[clubId],
            rows[0].SeasonId,
            now,
            cancellationToken);

        var done = 0;
        var skipped = 0;
        var targetsSet = 0;

        foreach (var division in rows.GroupBy(row => row.DivisionSeasonId).OrderBy(group => group.Key))
        {
            var clubs = division.ToList();
            var ranks = await TargetRanksAsync(clubs, bases, cancellationToken);
            var seasonGames = 2 * Math.Max(1, clubs.Count - 1);
            var places = ProjectedPlaces(clubs);

            foreach (var row in clubs.OrderBy(club => club.ClubId))
            {
                var fanBase = bases[row.ClubId];

                if (reviewed.Contains(row.ClubId) || row.Played == 0)
                {
                    skipped++;

                    continue;
                }

                if (fanBase.SeasonTargetRank is null)
                {
                    fanBase.SetTarget(ranks[row.ClubId], now);
                    targetsSet++;
                }

                var gap = places[row.ClubId] - fanBase.SeasonTargetRank!.Value;
                var after = FanRuleSet.AfterWeek(fanBase.State, gap, row.Played, seasonGames);

                _keeper.Change(fanBase, after, FanEventKind.Weekly, seasonOf[row.ClubId], now, weekOf: weekOf, gap: gap);
                done++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogReviewed(weekOf, rows.Count, done, skipped, targetsSet);

        return new RunWeeklyFanReviewResult(rows.Count, done, skipped, targetsSet);
    }

    /// <summary>Ranks a division by points a game, with the published table breaking a tie.</summary>
    private static Dictionary<Guid, int> ProjectedPlaces(List<FanSeasonClubRow> clubs)
    {
        var places = new Dictionary<Guid, int>(clubs.Count);
        var place = 0;

        foreach (var club in clubs
            .OrderByDescending(club => club.Played == 0 ? 0 : club.Points * 1_000 / club.Played)
            .ThenBy(club => club.Rank == 0 ? int.MaxValue : club.Rank)
            .ThenBy(club => club.ClubId))
        {
            places[club.ClubId] = ++place;
        }

        return places;
    }

    /// <summary>
    /// Reads the squad ranks of a division, but only when one of its clubs still needs a target: a division whose clubs all have one costs
    /// nothing.
    /// </summary>
    private async Task<Dictionary<Guid, int>> TargetRanksAsync(
        IReadOnlyList<FanSeasonClubRow> clubs,
        Dictionary<Guid, ClubFanBase> bases,
        CancellationToken cancellationToken)
    {
        if (clubs.All(club => bases[club.ClubId].SeasonTargetRank is not null))
        {
            return [];
        }

        var strengths = await _queries.GetSquadStrengthsAsync([.. clubs.Select(club => club.ClubId)], cancellationToken);

        return FanBaseKeeper.RankByStrength(strengths);
    }

    [LoggerMessage(
        EventId = 3800,
        Level = LogLevel.Information,
        Message = "Reviewed the fans for the week of {WeekOf}: {ClubCount} clubs, {ReviewedCount} reviewed, {SkippedCount} skipped, {TargetCount} targets set (FAN-8).")]
    private partial void LogReviewed(DateOnly weekOf, int clubCount, int reviewedCount, int skippedCount, int targetCount);
}
