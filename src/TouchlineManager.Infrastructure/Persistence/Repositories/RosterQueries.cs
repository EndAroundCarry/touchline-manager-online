using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad facts the market prices and revalidates a transfer from (`MOD-3`, `TRF-1`, `TRF-9`).
/// </summary>
/// <remarks>
/// A narrow read port rather than a widening of the squad reads: the market needs the ability, potential, age,
/// tier, and squad composition behind a listing, and it needs them the same way a renewal quote does. Nothing
/// here mutates and nothing returns an aggregate graph.
/// </remarks>
internal sealed class RosterQueries : IRosterQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public RosterQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<ListingEligibilityContext?> GetListingEligibilityAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var row = await (
            from player in _dbContext.Players
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join contract in _dbContext.PlayerContracts on player.Id equals contract.PlayerId
            where player.Id == playerId && contract.Status == ContractStatus.Active
            select new
            {
                Player = player,
                Attributes = attributes,
                Contract = contract,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var moraleBp = await _dbContext.PlayerStates
            .Where(state => state.PlayerId == playerId)
            .Select(state => (int?)state.MoraleBp)
            .FirstOrDefaultAsync(cancellationToken);

        var placement = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where entry.ClubId == row.Contract.ClubId && divisionSeason.SeasonId == season.SeasonId
            select new { divisionSeason.Id, division.TierNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (placement is null)
        {
            return null;
        }

        var hasRegistration = await _dbContext.PlayerRegistrations
            .AnyAsync(
                registration => registration.PlayerId == playerId
                    && registration.Status == RegistrationStatus.Active,
                cancellationToken);

        var appearances = await _dbContext.PlayerSeasonStats
            .Where(stat => stat.DivisionSeasonId == placement.Id
                && stat.PlayerId == playerId
                && stat.ClubId == row.Contract.ClubId)
            .Select(stat => (int?)stat.Appearances)
            .FirstOrDefaultAsync(cancellationToken);

        var composition = await GetCompositionAsync(row.Contract.ClubId, cancellationToken);

        var isListed = await _dbContext.TransferListings
            .AnyAsync(
                listing => listing.PlayerId == playerId && listing.Status == ListingStatus.Open,
                cancellationToken);

        var values = row.Attributes.ToSet().Values;
        var ability = values.Count == 0
            ? WorldRuleSet.AttributeMin
            : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);

        return new ListingEligibilityContext(
            playerId,
            row.Contract.ClubId,
            row.Player.PrimaryPosition,
            ability,
            row.Player.Potential,
            season.GameYear - row.Player.BirthGameYear,
            moraleBp ?? 0,
            appearances ?? 0,
            placement.TierNumber,
            season.SequenceNumber,
            row.Contract.Id,
            row.Contract.EndSeasonNumber,
            hasRegistration,
            composition?.RegisteredCount ?? 0,
            composition?.GoalkeeperCount ?? 0,
            isListed);
    }

    /// <inheritdoc />
    public async Task<RosterComposition?> GetCompositionAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var clubExists = await _dbContext.Clubs
            .AnyAsync(club => club.Id == clubId, cancellationToken);

        if (!clubExists)
        {
            return null;
        }

        var registered = await _dbContext.PlayerContracts
            .CountAsync(
                contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active,
                cancellationToken);

        var goalkeepers = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && player.PrimaryPosition == PlayerPosition.Goalkeeper
            select contract.Id)
            .CountAsync(cancellationToken);

        return new RosterComposition(clubId, registered, goalkeepers);
    }
}
