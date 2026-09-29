using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection the rollover's contract-continuity step reads (`CON-6`, `CON-8`).
/// </summary>
/// <remarks>
/// <para>
/// A handful of queries for the whole world, because the step touches every club at once and a query per
/// club would be a thousand round trips. The attribute rows are read as entities rather than projected,
/// because the ability signal is a mean over the row's twenty-eight columns and re-parsing that in SQL would
/// buy nothing.
/// </para>
/// <para>
/// Nothing here is tracked: the entities that are then closed and re-signed are loaded through
/// <see cref="ISquadRepository"/> so the read never widens what a write can reach.
/// </para>
/// </remarks>
internal sealed class ContractContinuityQueries : IContractContinuityQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public ContractContinuityQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContractContinuityClub>> LoadAsync(
        Guid worldId,
        Guid seasonId,
        int gameYear,
        CancellationToken cancellationToken)
    {
        var clubs = await (
            from club in _dbContext.Clubs
            join country in _dbContext.Countries on club.CountryId equals country.Id
            where club.WorldId == worldId
            select new ClubHeader(club.Id, club.Name, country.Code, country.NamePoolKey))
            .ToListAsync(cancellationToken);

        if (clubs.Count == 0)
        {
            return [];
        }

        var clubIds = clubs.Select(club => club.ClubId).ToList();

        var tiers = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where divisionSeason.SeasonId == seasonId && clubIds.Contains(entry.ClubId)
            select new { entry.ClubId, division.TierNumber })
            .ToListAsync(cancellationToken);

        var tierByClub = tiers
            .GroupBy(tier => tier.ClubId)
            .ToDictionary(group => group.Key, group => group.First().TierNumber);

        var attentive = (await _dbContext.ClubTenures
                .Where(tenure => tenure.ControlStatus == ClubTenureControlStatus.Active
                    && clubIds.Contains(tenure.ClubId))
                .Select(tenure => tenure.ClubId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var contracts = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where contract.Status == ContractStatus.Active && clubIds.Contains(contract.ClubId)
            select new { Contract = contract, Player = player, State = state })
            .ToListAsync(cancellationToken);

        var playerIds = contracts.Select(row => row.Player.Id).Distinct().ToList();

        var ability = await AbilityByPlayerAsync(playerIds, cancellationToken);

        var appearances = await (
            from stat in _dbContext.PlayerSeasonStats
            join divisionSeason in _dbContext.DivisionSeasons on stat.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.SeasonId == seasonId && playerIds.Contains(stat.PlayerId)
            group stat by stat.PlayerId
            into grouped
            select new { PlayerId = grouped.Key, Appearances = grouped.Sum(stat => stat.Appearances) })
            .ToListAsync(cancellationToken);

        var appearancesByPlayer = appearances.ToDictionary(row => row.PlayerId, row => row.Appearances);

        var byClub = contracts
            .GroupBy(row => row.Contract.ClubId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new List<ContractContinuityClub>(clubs.Count);

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var rows = byClub.TryGetValue(club.ClubId, out var clubRows) ? clubRows : [];

            var players = rows
                .OrderBy(row => row.Player.Id)
                .Select(row => new ContractContinuityPlayer(
                    row.Player.Id,
                    row.Contract.Id,
                    row.Contract.EndSeasonNumber,
                    row.Contract.WeeklyWageMinor,
                    row.Contract.SquadStatus,
                    PlayerPositions.FamilyOf(row.Player.PrimaryPosition),
                    row.Player.PrimaryPosition == PlayerPosition.Goalkeeper,
                    gameYear - row.Player.BirthGameYear,
                    ability.TryGetValue(row.Player.Id, out var value) ? value : WorldRuleSet.AttributeMin,
                    row.Player.Potential,
                    row.State.MoraleBp,
                    row.State.ConditionBp,
                    appearancesByPlayer.GetValueOrDefault(row.Player.Id),
                    row.Player.IsRetirementAnnounced))
                .ToList();

            result.Add(new ContractContinuityClub(
                club.ClubId,
                club.Name,
                club.Code,
                club.NamePoolKey,
                tierByClub.GetValueOrDefault(club.ClubId, 1),
                attentive.Contains(club.ClubId),
                players));
        }

        return result;
    }

    /// <summary>Reads each player's ability, the mean of their twenty-eight attributes.</summary>
    private async Task<Dictionary<Guid, int>> AbilityByPlayerAsync(
        List<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        var attributes = await _dbContext.PlayerAttributes
            .Where(row => playerIds.Contains(row.PlayerId))
            .ToListAsync(cancellationToken);

        return attributes.ToDictionary(
            row => row.PlayerId,
            row =>
            {
                var values = row.ToSet().Values;

                return values.Count == 0
                    ? WorldRuleSet.AttributeMin
                    : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
            });
    }

    private sealed record ClubHeader(Guid ClubId, string Name, string Code, string NamePoolKey);
}
