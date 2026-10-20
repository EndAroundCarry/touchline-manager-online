using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for the fans: fan bases, their history, and the expectations frozen at the lock (`FAN-10`).</summary>
internal sealed class FanRepository : IFanRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public FanRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(ClubFanBase fanBase) => _dbContext.ClubFanBases.Add(fanBase);

    /// <inheritdoc />
    public void AddEvent(ClubFanEvent fanEvent) => _dbContext.ClubFanEvents.Add(fanEvent);

    /// <inheritdoc />
    public void AddExpectation(FixtureFanExpectation expectation) => _dbContext.FixtureFanExpectations.Add(expectation);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubFanBase>> LoadAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken) =>
        await _dbContext.ClubFanBases
            .Where(fanBase => clubIds.Contains(fanBase.ClubId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
        IReadOnlyCollection<Guid> fixtureIds,
        CancellationToken cancellationToken) =>
        await _dbContext.FixtureFanExpectations
            .Where(expectation => fixtureIds.Contains(expectation.FixtureId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken) =>
        (await _dbContext.ClubFanEvents
            .Where(fanEvent => fanEvent.Kind == FanEventKind.Weekly && fanEvent.WeekOf == weekOf)
            .Select(fanEvent => fanEvent.ClubId)
            .ToListAsync(cancellationToken))
            .ToHashSet();
}

/// <summary>The reads the fan module is built from (`FAN-3`, `FAN-8`).</summary>
internal sealed class FanQueries : IFanQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public FanQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FanSeasonClubRow>> ListCurrentSeasonAsync(CancellationToken cancellationToken)
    {
        var seasonId = await CurrentSeasonIdAsync(cancellationToken);

        return seasonId is null ? [] : await ListSeasonAsync(seasonId.Value, divisionSeasonId: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FanSeasonClubRow>> ListDivisionOfClubAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var seasonId = await CurrentSeasonIdAsync(cancellationToken);

        if (seasonId is null)
        {
            return [];
        }

        var divisionSeasonId = await _dbContext.ClubSeasonEntries
            .Where(entry => entry.SeasonId == seasonId && entry.ClubId == clubId)
            .Select(entry => (Guid?)entry.DivisionSeasonId)
            .FirstOrDefaultAsync(cancellationToken);

        return divisionSeasonId is null ? [] : await ListSeasonAsync(seasonId.Value, divisionSeasonId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetSquadStrengthsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken)
    {
        var strengths = clubIds.Distinct().ToDictionary(clubId => clubId, _ => 0);

        if (strengths.Count == 0)
        {
            return strengths;
        }

        // A player is selectable when an active contract and an active registration agree on the club (SQ-6, SQ-7);
        // the same join the AI evaluation reads.
        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where strengths.Keys.Contains(contract.ClubId)
                && contract.Status == ContractStatus.Active
                && registration.ClubId == contract.ClubId
                && registration.Status == RegistrationStatus.Active
            select new { contract.ClubId, Player = player, Attributes = attributes, State = state })
            .ToListAsync(cancellationToken);

        var playerIds = rows.Select(row => row.Player.Id).ToList();
        var unavailable = playerIds.Count == 0
            ? []
            : (await _dbContext.PlayerUnavailabilities
                .Where(record => record.ResolvedAt == null && playerIds.Contains(record.PlayerId))
                .Select(record => record.PlayerId)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet();

        foreach (var squad in rows.GroupBy(row => row.ClubId))
        {
            strengths[squad.Key] = TeamStrength.OfSquad(
            [
                .. squad
                    .OrderBy(row => row.Player.Id)
                    .Select(row => new AiSquadPlayer(
                        row.Player.Id,
                        row.Player.PrimaryPosition,
                        row.Player.SecondaryPositions,
                        row.Attributes.ToSet().Values,
                        row.State.ConditionBp,
                        !unavailable.Contains(row.Player.Id))),
            ]);
        }

        return strengths;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, FanForm>> GetFormAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken) =>
        (await _dbContext.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .Select(standing => new { standing.ClubId, standing.Played, standing.Points })
            .ToListAsync(cancellationToken))
            .ToDictionary(standing => standing.ClubId, standing => new FanForm(standing.Played, standing.Points));

    /// <inheritdoc />
    public Task<int?> GetDivisionTierAsync(Guid divisionId, CancellationToken cancellationToken) =>
        _dbContext.Divisions
            .Where(division => division.Id == divisionId)
            .Select(division => (int?)division.TierNumber)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> GetFirstSeasonClubIdsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken) =>
        (await _dbContext.ClubSeasonEntries
            .Where(entry => clubIds.Contains(entry.ClubId))
            .GroupBy(entry => entry.ClubId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken))
            .ToHashSet();

    private async Task<Guid?> CurrentSeasonIdAsync(CancellationToken cancellationToken) =>
        await (
            from world in _dbContext.GameWorlds
            join season in _dbContext.Seasons on world.Id equals season.WorldId
            where season.SequenceNumber == world.CurrentSeasonNumber
            orderby world.CreatedAt
            select (Guid?)season.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<FanSeasonClubRow>> ListSeasonAsync(
        Guid seasonId,
        Guid? divisionSeasonId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from entry in _dbContext.ClubSeasonEntries
            where entry.SeasonId == seasonId && (divisionSeasonId == null || entry.DivisionSeasonId == divisionSeasonId)
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join standing in _dbContext.Standings
                on new { entry.DivisionSeasonId, entry.ClubId } equals new { standing.DivisionSeasonId, standing.ClubId }
                into standings
            from standing in standings.DefaultIfEmpty()
            orderby entry.ClubId
            select new
            {
                entry.ClubId,
                entry.DivisionSeasonId,
                entry.SeasonId,
                division.TierNumber,
                Played = (int?)standing.Played,
                Points = (int?)standing.Points,
                Rank = (int?)standing.Rank,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new FanSeasonClubRow(
                row.ClubId,
                row.DivisionSeasonId,
                row.SeasonId,
                row.TierNumber,
                row.Played ?? 0,
                row.Points ?? 0,
                row.Rank ?? 0)),
        ];
    }
}
