using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection queries the squad, player, and contract screens read (master plan §10.3).
/// </summary>
/// <remarks>
/// <para>
/// Every query is one round trip shaped for one screen, or a small number of them, and none loads an
/// aggregate graph. The state rows come back in basis points and the positions as domain values, because
/// the conversion `TRN-8` requires belongs to the response mapping rather than to the query.
/// </para>
/// <para>
/// Secondary positions are a stored code list with a computed accessor on the entity, so the players are
/// projected as entities and read in memory rather than selected field by field. The alternative — a
/// translatable projection — would have to re-parse the list in SQL for no gain.
/// </para>
/// </remarks>
internal sealed class SquadQueries : ISquadQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public SquadQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<SquadSnapshot?> GetSquadAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var club = await (
            from candidate in _dbContext.Clubs
            join country in _dbContext.Countries on candidate.CountryId equals country.Id
            where candidate.Id == clubId
            select new { Club = candidate, Country = country })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where contract.ClubId == clubId && contract.Status == ContractStatus.Active
            orderby player.FullName
            select new { Player = player, Contract = contract, State = state })
            .Take(WorldRuleSet.SquadMaximumRegistered)
            .ToListAsync(cancellationToken);

        var playerIds = rows.Select(row => row.Player.Id).ToList();
        var availability = await OpenAvailabilityAsync(playerIds, cancellationToken);

        var players = rows
            .Select(row => new SquadPlayerRow(
                row.Player.Id,
                row.Player.FullName,
                row.Player.ShortName,
                row.Player.NationalityCode,
                row.Player.BirthGameYear,
                row.Player.PreferredFoot,
                row.Player.PrimaryPosition,
                row.Player.SecondaryPositions,
                new SquadStateRow(
                    row.State.ConditionBp,
                    row.State.FatigueBp,
                    row.State.MoraleBp,
                    row.State.MatchSharpnessBp),
                new SquadContractRow(
                    row.Contract.Id,
                    row.Contract.StartSeasonNumber,
                    row.Contract.EndSeasonNumber,
                    row.Contract.WeeklyWageMinor,
                    row.Contract.SquadStatus,
                    row.Contract.Status),
                availability.GetValueOrDefault(row.Player.Id, [])))
            // Goalkeepers first and then by position, which the stored code cannot express because it orders
            // alphabetically. Twenty-five rows at most, so sorting them here costs nothing.
            .OrderBy(player => (int)player.PrimaryPosition)
            .ThenBy(player => player.FullName, StringComparer.Ordinal)
            .ToList();

        return new SquadSnapshot(
            clubId,
            club.Club.Name,
            club.Club.ShortName,
            club.Country.Code,
            season.SequenceNumber,
            season.GameYear,
            players);
    }

    /// <inheritdoc />
    public async Task<PlayerSnapshot?> GetPlayerAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        // The active contract is joined rather than left-joined: a player with no contract is not in
        // anybody's squad (`SQ-6`), and there is no club to authorize a read against.
        var row = await (
            from player in _dbContext.Players
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            join contract in _dbContext.PlayerContracts on player.Id equals contract.PlayerId
            where player.Id == playerId && contract.Status == ContractStatus.Active
            select new
            {
                Player = player,
                Attributes = attributes,
                State = state,
                Contract = contract,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var registration = await _dbContext.PlayerRegistrations
            .Where(candidate => candidate.PlayerId == playerId
                && candidate.Status == RegistrationStatus.Active)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Select(candidate => new SquadRegistrationRow(
                candidate.Id,
                candidate.ClubId,
                candidate.Status,
                candidate.EffectiveFixtureBoundaryRound))
            .FirstOrDefaultAsync(cancellationToken);

        var availability = await OpenAvailabilityAsync([playerId], cancellationToken);

        return new PlayerSnapshot(
            row.Player.Id,
            row.Contract.ClubId,
            row.Player.FullName,
            row.Player.ShortName,
            row.Player.NationalityCode,
            row.Player.BirthGameYear,
            row.Player.BirthDayOfYear,
            row.Player.PreferredFoot,
            row.Player.HeightCm,
            row.Player.WeightKg,
            row.Player.PrimaryPosition,
            row.Player.SecondaryPositions,
            row.Player.Status,
            row.Attributes.ToSet(),
            new SquadStateRow(
                row.State.ConditionBp,
                row.State.FatigueBp,
                row.State.MoraleBp,
                row.State.MatchSharpnessBp),
            new SquadContractRow(
                row.Contract.Id,
                row.Contract.StartSeasonNumber,
                row.Contract.EndSeasonNumber,
                row.Contract.WeeklyWageMinor,
                row.Contract.SquadStatus,
                row.Contract.Status),
            registration,
            availability.GetValueOrDefault(playerId, []),
            season.SequenceNumber,
            season.GameYear);
    }

    /// <inheritdoc />
    public async Task<ContractsSnapshot?> GetContractsAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var club = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var contracts = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            where contract.ClubId == clubId && contract.Status == ContractStatus.Active
            // Closest to expiring first, which is the order a manager plans renewals in.
            orderby contract.EndSeasonNumber, player.FullName
            select new ContractRow(
                contract.Id,
                player.Id,
                player.FullName,
                player.ShortName,
                player.PrimaryPosition,
                player.BirthGameYear,
                contract.StartSeasonNumber,
                contract.EndSeasonNumber,
                contract.WeeklyWageMinor,
                contract.SquadStatus,
                contract.Status))
            .ToListAsync(cancellationToken);

        return new ContractsSnapshot(
            clubId,
            club.Name,
            season.SequenceNumber,
            season.GameYear,
            contracts);
    }

    /// <summary>Reads every open injury and suspension for the given players, keyed by player.</summary>
    /// <remarks>
    /// A player can hold more than one open record — an injury and a suspension overlap — so the result is
    /// a list per player rather than a single record, and the screen renders however many there are.
    /// </remarks>
    private async Task<Dictionary<Guid, IReadOnlyList<SquadAvailabilityRow>>> OpenAvailabilityAsync(
        List<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        var records = await _dbContext.PlayerUnavailabilities
            .Where(record => playerIds.Contains(record.PlayerId) && record.ResolvedAt == null)
            .OrderBy(record => record.RemainingFixtures)
            .Select(record => new
            {
                record.PlayerId,
                Row = new SquadAvailabilityRow(
                    record.Id,
                    record.Type,
                    record.Severity,
                    record.RemainingFixtures,
                    record.StartedAt),
            })
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(record => record.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SquadAvailabilityRow>)[.. group.Select(record => record.Row)]);
    }
}
