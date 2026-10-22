using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The sponsor's reads: a club's season, who has not signed, and how long a manager has held a club (`SPN-1`, `SPN-4`, `SPN-6`).
/// </summary>
internal sealed class SponsorQueries : ISponsorQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public SponsorQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<SponsorSeasonContext?> GetClubContextAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var row = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            where entry.ClubId == clubId && divisionSeason.SeasonId == season.SeasonId
            select new
            {
                club.Name,
                seasonRow.WorldId,
                seasonRow.StartsAt,
                DivisionSeasonId = divisionSeason.Id,
                division.TierNumber,
                club.Reputation,
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var fans = await _dbContext.ClubFanBases
            .AsNoTracking()
            .Where(fanBase => fanBase.ClubId == clubId)
            .Select(fanBase => (int?)fanBase.Fans)
            .FirstOrDefaultAsync(cancellationToken);

        var published = await _dbContext.Matchdays
            .CountAsync(
                matchday => matchday.DivisionSeasonId == row.DivisionSeasonId
                    && matchday.PublicationStatus == MatchdayPublicationStatus.Published,
                cancellationToken);

        return new SponsorSeasonContext(
            clubId,
            row.Name,
            row.WorldId,
            season.SeasonId,
            season.SequenceNumber,
            DateOnly.FromDateTime(row.StartsAt.UtcDateTime),
            row.DivisionSeasonId,
            row.TierNumber,
            fans ?? FanRuleSet.OpeningFor(row.TierNumber).Fans,
            row.Reputation,
            published);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListClubsWithoutDealAsync(Guid divisionSeasonId, CancellationToken cancellationToken)
    {
        var rows = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            where entry.DivisionSeasonId == divisionSeasonId
                && _dbContext.SponsorOffers.Any(offer => offer.ClubId == entry.ClubId && offer.SeasonId == divisionSeason.SeasonId)
                && !_dbContext.SponsorDeals.Any(deal => deal.ClubId == entry.ClubId && deal.SeasonId == divisionSeason.SeasonId)
            select entry.ClubId)
            .ToListAsync(cancellationToken);

        return [.. rows.Order()];
    }

    /// <inheritdoc />
    public Task<int> CountMatchdaysPublishedSinceAsync(Guid divisionSeasonId, DateTimeOffset since, CancellationToken cancellationToken) =>
        _dbContext.Matchdays.CountAsync(
            matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Published
                && matchday.UpdatedAt > since,
            cancellationToken);

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetTenureStartAsync(Guid clubId, CancellationToken cancellationToken) =>
        await _dbContext.ClubTenures
            .AsNoTracking()
            .Where(tenure => tenure.ClubId == clubId && tenure.EndedAt == null)
            .Select(tenure => (DateTimeOffset?)tenure.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
