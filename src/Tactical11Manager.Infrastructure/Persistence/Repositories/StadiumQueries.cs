using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The stadium read projection (`STAD-1`).
/// </summary>
internal sealed class StadiumQueries : IStadiumQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public StadiumQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<ClubStadiumContext?> GetContextAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        // A club with no season yet prices as a tier-1 club, which is what the finance summary does too, so the
        // two screens never quote the same club in different scales.
        var tier = world is null
            ? (int?)null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
                join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
                where entry.ClubId == clubId
                    && season.WorldId == world.Id
                    && season.SequenceNumber == world.CurrentSeasonNumber
                select (int?)division.TierNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var colours = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.PrimaryColour, candidate.SecondaryColour })
            .FirstOrDefaultAsync(cancellationToken);

        var fanBase = await _dbContext.ClubFanBases
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.Fans, candidate.MoodBp })
            .FirstOrDefaultAsync(cancellationToken);

        // A club whose fans have not been opened yet is quoted as a new club of its tier.
        var opening = FanRuleSet.OpeningFor(tier ?? 1);

        return new ClubStadiumContext(
            tier ?? 1,
            account.CashMinor,
            account.ReservedMinor,
            fanBase?.Fans ?? opening.Fans,
            fanBase?.MoodBp ?? opening.MoodBp,
            colours?.PrimaryColour,
            colours?.SecondaryColour);
    }
}
