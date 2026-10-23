using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection queries onboarding reads (`WORLD-8`, `PYR-1`, `PYR-10`).
/// </summary>
/// <remarks>
/// <para>
/// Every query here is one round trip shaped for one screen, and none of them loads an aggregate graph.
/// Onboarding is the busiest unauthenticated-ish surface in the product — every new manager walks it — so
/// it is the one place where a query per table would be visible.
/// </para>
/// <para>
/// Occupancy is counted by joining tenures to accounts, which reads the auth module's table. It is a read,
/// so it is permitted (`MOD-3`); the count is what tells a full tier from an empty one, and a suspended
/// account must not hold a place in it (`OCC-8`).
/// </para>
/// </remarks>
internal sealed class OnboardingQueries : IOnboardingQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public OnboardingQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<CountryCapacitySnapshot?> GetCountryCapacityAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var season = await ResolveCurrentSeasonAsync(cancellationToken);

        if (season is null)
        {
            return null;
        }

        // Every active tier counts: a manager may take any of them, so the country is full only when all are
        // (WORLD-8, PYR-2). The lowest one is still named, because the pyramid grows below it.
        var divisions = await (
            from division in _dbContext.Divisions
            join divisionSeason in _dbContext.DivisionSeasons on division.Id equals divisionSeason.DivisionId
            where division.CountryId == countryId
                && division.Status == DivisionStatus.Active
                && divisionSeason.SeasonId == season.Value.SeasonId
            orderby division.TierNumber descending, division.LeagueNumber descending
            select new
            {
                division.Id,
                division.TierNumber,
                division.DisplayName,
                DivisionSeasonId = divisionSeason.Id,
            })
            .ToListAsync(cancellationToken);

        if (divisions.Count == 0)
        {
            return null;
        }

        var lowest = divisions[0];
        var divisionSeasonIds = divisions.Select(division => division.DivisionSeasonId).ToList();

        var clubsInCountry = await _dbContext.ClubSeasonEntries
            .CountAsync(entry => divisionSeasonIds.Contains(entry.DivisionSeasonId), cancellationToken);

        var occupied = await OccupiedClubsAsync(divisionSeasonIds, cancellationToken);

        // A league still being provisioned has no season-instance in the count above, but it already holds its slot in the
        // pyramid, so the shape is read from every division in any status.
        var leagues = (await _dbContext.Divisions
            .Where(division => division.CountryId == countryId)
            .Select(division => new { division.TierNumber, division.LeagueNumber })
            .ToListAsync(cancellationToken))
            .Select(division => (division.TierNumber, division.LeagueNumber))
            .ToList();

        return new CountryCapacitySnapshot(
            countryId,
            lowest.TierNumber,
            lowest.Id,
            lowest.DivisionSeasonId,
            lowest.DisplayName,
            clubsInCountry,
            occupied,
            leagues);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableClubRow>> GetAvailableClubsAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken) =>
        await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            where entry.DivisionSeasonId == divisionSeasonId
            orderby club.Name
            select new AvailableClubRow(
                club.Id,
                club.Name,
                club.ShortName,
                club.City,
                club.Region,
                club.BadgeSeed,
                club.Reputation,
                division.TierNumber,
                division.DisplayName,
                !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed)))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableClubRow>> GetCountryClubsAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var season = await ResolveCurrentSeasonAsync(cancellationToken);

        if (season is null)
        {
            return [];
        }

        return await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            where division.CountryId == countryId
                && division.Status == DivisionStatus.Active
                && divisionSeason.SeasonId == season.Value.SeasonId
            orderby division.TierNumber, club.Name
            select new AvailableClubRow(
                club.Id,
                club.Name,
                club.ShortName,
                club.City,
                club.Region,
                club.BadgeSeed,
                club.Reputation,
                division.TierNumber,
                division.DisplayName,
                !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClubDashboardSnapshot?> GetClubDashboardAsync(
        Guid clubId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var placement = await (
            from club in _dbContext.Clubs
            join country in _dbContext.Countries on club.CountryId equals country.Id
            join entry in _dbContext.ClubSeasonEntries on club.Id equals entry.ClubId
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
            where club.Id == clubId && season.SequenceNumber == seasonNumber
            select new
            {
                Club = club,
                Country = country,
                Division = division,
                Season = season,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (placement is null)
        {
            return null;
        }

        var tenure = await _dbContext.ClubTenures
            .Where(candidate => candidate.ClubId == clubId
                && candidate.ControlStatus != ClubTenureControlStatus.Closed)
            .OrderByDescending(candidate => candidate.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var account = await _dbContext.ClubAccounts
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        return new ClubDashboardSnapshot(
            placement.Club,
            placement.Country,
            placement.Division,
            placement.Season,
            tenure,
            account?.CashMinor,
            account?.ReservedMinor);
    }

    /// <inheritdoc />
    public async Task<ClubTenureSnapshot?> GetCurrentTenureAsync(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var season = await ResolveCurrentSeasonAsync(cancellationToken);

        if (season is null)
        {
            return null;
        }

        var row = await (
            from tenure in _dbContext.ClubTenures
            join club in _dbContext.Clubs on tenure.ClubId equals club.Id
            join country in _dbContext.Countries on club.CountryId equals country.Id
            join entry in _dbContext.ClubSeasonEntries on club.Id equals entry.ClubId
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where tenure.ManagerId == managerId
                && tenure.ControlStatus != ClubTenureControlStatus.Closed
                && divisionSeason.SeasonId == season.Value.SeasonId
            select new
            {
                Tenure = tenure,
                Club = club,
                Country = country,
                Division = division,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ClubTenureSnapshot(row.Tenure, row.Club, row.Country, row.Division);
    }

    /// <summary>
    /// Counts the clubs in these tiers that hold an open human tenure whose account is not suspended
    /// (`OCC-8`, decision D-1 of the Stage 0 review).
    /// </summary>
    private async Task<int> OccupiedClubsAsync(
        IReadOnlyCollection<Guid> divisionSeasonIds,
        CancellationToken cancellationToken) =>
        await (
            from tenure in _dbContext.ClubTenures
            join entry in _dbContext.ClubSeasonEntries on tenure.ClubId equals entry.ClubId
            join manager in _dbContext.Managers on tenure.ManagerId equals manager.Id
            join user in _dbContext.Users on manager.UserId equals user.Id
            where divisionSeasonIds.Contains(entry.DivisionSeasonId)
                && tenure.ControlStatus != ClubTenureControlStatus.Closed
                && user.Status != Domain.Auth.UserStatus.Suspended
            select tenure.Id)
            .Distinct()
            .CountAsync(cancellationToken);

    /// <summary>
    /// Resolves the season in progress, so membership is measured against the season actually being played.
    /// </summary>
    /// <remarks>
    /// A club appears once per season it has played, so "which clubs are in this tier" is a question about
    /// one season. Filtering on the world's current season number rather than on the latest one keeps the
    /// answer stable during the rollover window, when next season's entries already exist
    /// (`PR-6`, `CAL-6`).
    /// </remarks>
    private async Task<(Guid SeasonId, int SequenceNumber)?> ResolveCurrentSeasonAsync(
        CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var seasonId = await _dbContext.Seasons
            .Where(season => season.WorldId == world.Id && season.SequenceNumber == world.CurrentSeasonNumber)
            .Select(season => (Guid?)season.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return seasonId is null ? null : (seasonId.Value, world.CurrentSeasonNumber);
    }
}
