using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

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
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public OnboardingQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

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

        var division = await _dbContext.Divisions
            .Where(candidate => candidate.CountryId == countryId
                && candidate.Status == DivisionStatus.Active)
            .OrderByDescending(candidate => candidate.TierNumber)
            .Select(candidate => new { candidate.Id, candidate.TierNumber, candidate.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (division is null)
        {
            return null;
        }

        var divisionSeasonId = await _dbContext.DivisionSeasons
            .Where(candidate => candidate.DivisionId == division.Id && candidate.SeasonId == season.Value.SeasonId)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (divisionSeasonId is null)
        {
            return null;
        }

        var clubsInTier = await _dbContext.ClubSeasonEntries
            .CountAsync(entry => entry.DivisionSeasonId == divisionSeasonId.Value, cancellationToken);

        var occupied = await OccupiedClubsAsync(divisionSeasonId.Value, cancellationToken);

        return new CountryCapacitySnapshot(
            countryId,
            division.TierNumber,
            division.Id,
            divisionSeasonId.Value,
            division.DisplayName,
            clubsInTier,
            occupied);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableClubRow>> GetAvailableClubsAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken) =>
        await (
            from entry in _dbContext.ClubSeasonEntries
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
                club.StadiumBaseline,
                !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed)))
            .ToListAsync(cancellationToken);

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
    /// Counts the clubs in a tier that hold an open human tenure whose account is not suspended
    /// (`OCC-8`, decision D-1 of the Stage 0 review).
    /// </summary>
    private async Task<int> OccupiedClubsAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
        await (
            from tenure in _dbContext.ClubTenures
            join entry in _dbContext.ClubSeasonEntries on tenure.ClubId equals entry.ClubId
            join manager in _dbContext.Managers on tenure.ManagerId equals manager.Id
            join user in _dbContext.Users on manager.UserId equals user.Id
            where entry.DivisionSeasonId == divisionSeasonId
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
