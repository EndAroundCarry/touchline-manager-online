using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The world module's persistence: the world, its countries, and the competition shell it creates.
/// </summary>
internal sealed class WorldRepository : IWorldRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public WorldRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by creation so that a database holding an accidental second world still answers
    /// deterministically rather than by row order (the plan forbids row order as a tie-breaker anywhere,
    /// including here).
    /// </remarks>
    public Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken) =>
        _dbContext.GameWorlds
            .OrderBy(world => world.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void AddWorld(GameWorld world) => _dbContext.GameWorlds.Add(world);

    /// <inheritdoc />
    public Task<Season?> FindSeasonAsync(Guid worldId, int sequenceNumber, CancellationToken cancellationToken) =>
        _dbContext.Seasons.SingleOrDefaultAsync(
            season => season.WorldId == worldId && season.SequenceNumber == sequenceNumber,
            cancellationToken);

    /// <inheritdoc />
    public Task<Season?> FindSeasonByIdAsync(Guid seasonId, CancellationToken cancellationToken) =>
        _dbContext.Seasons.SingleOrDefaultAsync(season => season.Id == seasonId, cancellationToken);

    /// <inheritdoc />
    public void AddSeason(Season season) => _dbContext.Seasons.Add(season);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Country>> ListCountriesAsync(
        Guid worldId,
        CancellationToken cancellationToken) =>
        await _dbContext.Countries
            .Where(country => country.WorldId == worldId)
            .OrderBy(country => country.SortOrder)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Country?> FindCountryAsync(Guid countryId, CancellationToken cancellationToken) =>
        _dbContext.Countries.SingleOrDefaultAsync(country => country.Id == countryId, cancellationToken);

    /// <inheritdoc />
    public void AddCountry(Country country) => _dbContext.Countries.Add(country);

    /// <inheritdoc />
    public Task<Division?> FindLowestActiveDivisionAsync(
        Guid countryId,
        CancellationToken cancellationToken) =>
        _dbContext.Divisions
            .Where(division => division.CountryId == countryId && division.Status == DivisionStatus.Active)
            .OrderByDescending(division => division.TierNumber)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Division?> FindDivisionAsync(Guid divisionId, CancellationToken cancellationToken) =>
        _dbContext.Divisions.SingleOrDefaultAsync(division => division.Id == divisionId, cancellationToken);

    /// <inheritdoc />
    public void AddDivision(Division division) => _dbContext.Divisions.Add(division);

    /// <inheritdoc />
    public Task<DivisionSeason?> FindDivisionSeasonAsync(
        Guid divisionId,
        Guid seasonId,
        CancellationToken cancellationToken) =>
        _dbContext.DivisionSeasons.SingleOrDefaultAsync(
            divisionSeason => divisionSeason.DivisionId == divisionId && divisionSeason.SeasonId == seasonId,
            cancellationToken);

    /// <inheritdoc />
    public void AddDivisionSeason(DivisionSeason divisionSeason) => _dbContext.DivisionSeasons.Add(divisionSeason);

    /// <inheritdoc />
    public void AddClubSeasonEntry(ClubSeasonEntry entry) => _dbContext.ClubSeasonEntries.Add(entry);
}

/// <summary>Club and manager persistence.</summary>
internal sealed class ClubRepository : IClubRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ClubRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken) =>
        _dbContext.Clubs.SingleOrDefaultAsync(club => club.Id == clubId, cancellationToken);

    /// <inheritdoc />
    public void Add(Club club) => _dbContext.Clubs.Add(club);
}

/// <summary>Manager-profile persistence.</summary>
internal sealed class ManagerRepository : IManagerRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ManagerRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<Manager?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Managers.SingleOrDefaultAsync(manager => manager.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public Task<Manager?> FindByIdAsync(Guid managerId, CancellationToken cancellationToken) =>
        _dbContext.Managers.SingleOrDefaultAsync(manager => manager.Id == managerId, cancellationToken);

    /// <inheritdoc />
    public void Add(Manager manager) => _dbContext.Managers.Add(manager);
}

/// <summary>
/// Club-tenure persistence, the query side of the ownership model (`WORLD-7`).
/// </summary>
/// <remarks>
/// "Open" is expressed as <c>control_status &lt;&gt; 'closed'</c>, which is the same predicate the partial
/// unique indexes use. Any other reading would let this repository and the database disagree about whether
/// a club is taken (`OCC-8`, `OCC-9`).
/// </remarks>
internal sealed class ClubTenureRepository : IClubTenureRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ClubTenureRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<ClubTenure?> FindOpenByManagerAsync(Guid managerId, CancellationToken cancellationToken) =>
        _dbContext.ClubTenures
            .Where(tenure => tenure.ManagerId == managerId
                && tenure.ControlStatus != ClubTenureControlStatus.Closed)
            .OrderByDescending(tenure => tenure.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ClubTenure?> FindOpenByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
        _dbContext.ClubTenures
            .Where(tenure => tenure.ClubId == clubId
                && tenure.ControlStatus != ClubTenureControlStatus.Closed)
            .OrderByDescending(tenure => tenure.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ClubTenure?> FindByTakeoverKeyAsync(
        string takeoverIdempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.ClubTenures.SingleOrDefaultAsync(
            tenure => tenure.TakeoverIdempotencyKey == takeoverIdempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public void Add(ClubTenure tenure) => _dbContext.ClubTenures.Add(tenure);
}

/// <summary>Pyramid-expansion request persistence.</summary>
internal sealed class DivisionProvisioningRequestRepository : IDivisionProvisioningRequestRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public DivisionProvisioningRequestRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<DivisionProvisioningRequest?> FindAsync(
        Guid countryId,
        int targetTier,
        CancellationToken cancellationToken) =>
        _dbContext.DivisionProvisioningRequests.SingleOrDefaultAsync(
            request => request.CountryId == countryId && request.TargetTier == targetTier,
            cancellationToken);

    /// <inheritdoc />
    public void Add(DivisionProvisioningRequest request) => _dbContext.DivisionProvisioningRequests.Add(request);
}

/// <summary>Generation-run persistence.</summary>
internal sealed class GenerationRunRepository : IGenerationRunRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public GenerationRunRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(GenerationRun run) => _dbContext.GenerationRuns.Add(run);
}
