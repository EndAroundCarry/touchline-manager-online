using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for the weekly free-agent batch record (`FA-5`).</summary>
internal sealed class FreeAgentBatchRepository : IFreeAgentBatchRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public FreeAgentBatchRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(FreeAgentBatch batch) => _dbContext.FreeAgentBatches.Add(batch);

    /// <inheritdoc />
    public Task<FreeAgentBatch?> FindByWeekAsync(DateOnly weekStart, CancellationToken cancellationToken) =>
        _dbContext.FreeAgentBatches.SingleOrDefaultAsync(batch => batch.WeekStart == weekStart, cancellationToken);

    /// <inheritdoc />
    public Task<FreeAgentBatch?> FindPreviousAsync(DateOnly weekStart, CancellationToken cancellationToken) =>
        _dbContext.FreeAgentBatches
            .Where(batch => batch.WeekStart < weekStart)
            .OrderByDescending(batch => batch.WeekStart)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The reads the weekly free-agent batch is built from (`FA-1`, `FA-3`, `FA-6`).</summary>
internal sealed class FreeAgentQueries : IFreeAgentQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public FreeAgentQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<FreeAgentWorldFacts?> GetWorldFactsAsync(CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber, candidate.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var season = await _dbContext.Seasons
            .Where(candidate => candidate.WorldId == world.Id && candidate.SequenceNumber == world.CurrentSeasonNumber)
            .Select(candidate => new { candidate.GameYear })
            .FirstOrDefaultAsync(cancellationToken);

        // The first batch picks up what the rollover into this season released, and nothing older.
        var previousSeasonEnd = await _dbContext.Seasons
            .Where(candidate => candidate.WorldId == world.Id
                && candidate.SequenceNumber == world.CurrentSeasonNumber - 1)
            .Select(candidate => (DateTimeOffset?)candidate.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        // The seed is the world bootstrap's, as the provisioning worker reads it (ADR-0033). Without it the
        // batch cannot be reproduced, so there is nothing to open.
        var seed = await _dbContext.GenerationRuns
            .Where(run => run.Kind == GenerationRunKind.WorldBootstrap)
            .OrderBy(run => run.StartedAt)
            .Select(run => run.Seed)
            .FirstOrDefaultAsync(cancellationToken);

        if (season is null || seed is null)
        {
            return null;
        }

        var contracted = await (
            from contract in _dbContext.PlayerContracts
            join club in _dbContext.Clubs on contract.ClubId equals club.Id
            where contract.Status == ContractStatus.Active
            group contract by club.CountryId into byCountry
            select new { CountryId = byCountry.Key, Players = byCountry.Count() })
            .ToListAsync(cancellationToken);

        var countries = await _dbContext.Countries
            .Where(country => country.WorldId == world.Id && country.IsActive)
            .OrderBy(country => country.SortOrder)
            .ThenBy(country => country.Code)
            .Select(country => new { country.Id, country.Code, country.NamePoolKey })
            .ToListAsync(cancellationToken);

        var lowestTiers = await _dbContext.Divisions
            .Where(division => division.Status == DivisionStatus.Active)
            .GroupBy(division => division.CountryId)
            .Select(group => new { CountryId = group.Key, Tier = group.Max(division => division.TierNumber) })
            .ToListAsync(cancellationToken);

        var playersByCountry = contracted.ToDictionary(row => row.CountryId, row => row.Players);
        var tierByCountry = lowestTiers.ToDictionary(row => row.CountryId, row => row.Tier);

        var facts = countries
            .Where(country => playersByCountry.GetValueOrDefault(country.Id) > 0)
            .Select(country => new FreeAgentCountryFacts(
                country.Code,
                country.NamePoolKey,
                playersByCountry[country.Id],
                tierByCountry.GetValueOrDefault(country.Id, 1)))
            .ToList();

        return new FreeAgentWorldFacts(world.Id, seed, season.GameYear, previousSeasonEnd ?? world.CreatedAt, facts);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleasedFreeAgentRow>> ListReleasedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        // A free agent whose last contract ended by expiry or release at or after the bound and who no listing
        // has been opened for since. A generated free agent has no contract, so he is never picked up here.
        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            where contract.Status == ContractStatus.Closed
                && contract.ClosedAt >= since
                && (contract.ClosedReason == PlayerContractCloseReasons.Expired
                    || contract.ClosedReason == PlayerContractCloseReasons.Released)
                && player.Status == PlayerStatus.FreeAgent
                && !_dbContext.TransferListings.Any(listing => listing.PlayerId == player.Id
                    && listing.CreatedAt >= contract.ClosedAt)
            orderby player.Id
            select new { player.Id, player.BirthGameYear, player.NationalityCode, Attributes = attributes })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var gameYear = (await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken))?.GameYear ?? 0;

        return [.. rows
            .DistinctBy(row => row.Id)
            .Select(row => new ReleasedFreeAgentRow(
                row.Id,
                Ability(row.Attributes),
                gameYear - row.BirthGameYear,
                row.NationalityCode))];
    }

    private static int Ability(PlayerAttributes attributes)
    {
        var values = attributes.ToSet().Values;

        return values.Count == 0
            ? WorldRuleSet.AttributeMin
            : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }
}
