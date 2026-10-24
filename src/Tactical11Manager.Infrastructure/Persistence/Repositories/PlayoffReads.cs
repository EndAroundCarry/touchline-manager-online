using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Competition.Playoffs;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The reads that tell a fixture list or a match page which promotion play-off tie a fixture belongs to (`PR-4`, `PR-6`).
/// </summary>
/// <remarks>
/// Shared by the competition and match projections so a play-off match is described the same way wherever it is read.
/// </remarks>
internal static class PlayoffReads
{
    private const string UnknownPlayer = "Unknown player";

    /// <summary>Gives the stage of each fixture in <paramref name="fixtureIds"/> that is a play-off match.</summary>
    /// <param name="dbContext">The context.</param>
    /// <param name="fixtureIds">The fixtures to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Dictionary<Guid, PlayoffStage>> StagesByFixtureAsync(
        Tactical11ManagerDbContext dbContext,
        IReadOnlyCollection<Guid> fixtureIds,
        CancellationToken cancellationToken)
    {
        if (fixtureIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.PlayoffTies
            .Where(tie => tie.FixtureId != null && fixtureIds.Contains(tie.FixtureId.Value))
            .Select(tie => new { FixtureId = tie.FixtureId!.Value, tie.Stage })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.FixtureId, row => row.Stage);
    }

    /// <summary>Reads the tie a fixture is the match of, with its shoot-out's players named, or null for a league match.</summary>
    /// <param name="dbContext">The context.</param>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<PlayoffMatchRow?> ForFixtureAsync(
        Tactical11ManagerDbContext dbContext,
        Guid fixtureId,
        CancellationToken cancellationToken)
    {
        var tie = await dbContext.PlayoffTies
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.FixtureId == fixtureId, cancellationToken);

        if (tie is null)
        {
            return null;
        }

        if (tie.ShootoutKicks is null)
        {
            return new PlayoffMatchRow(
                tie.Stage,
                tie.DecidedBy,
                tie.WinnerClubId,
                tie.ShootoutHomeScore,
                tie.ShootoutAwayScore,
                null,
                []);
        }

        var stored = PlayoffShootoutDocument.Read(tie.ShootoutKicks);

        var playerIds = stored.Kicks
            .SelectMany(kick => new[] { (Guid?)kick.TakerId, kick.GoalkeeperId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var names = await dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .Select(player => new { player.Id, player.FullName })
            .ToDictionaryAsync(player => player.Id, player => player.FullName, cancellationToken);

        return new PlayoffMatchRow(
            tie.Stage,
            tie.DecidedBy,
            tie.WinnerClubId,
            tie.ShootoutHomeScore,
            tie.ShootoutAwayScore,
            stored.FirstSide,
            [
                .. stored.Kicks.Select(kick => new PlayoffKickRow(
                    kick.Number,
                    kick.Round,
                    kick.Side,
                    names.GetValueOrDefault(kick.TakerId, UnknownPlayer),
                    kick.GoalkeeperId is { } keeper ? names.GetValueOrDefault(keeper, UnknownPlayer) : null,
                    kick.Outcome)),
            ]);
    }
}
