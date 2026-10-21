using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
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

    /// <inheritdoc />
    public async Task<FanClubRow?> GetClubAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await _dbContext.Clubs
            .AsNoTracking()
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var season = await (
            from world in _dbContext.GameWorlds
            join candidate in _dbContext.Seasons on world.Id equals candidate.WorldId
            where candidate.SequenceNumber == world.CurrentSeasonNumber
            orderby world.CreatedAt
            select new { candidate.Id, candidate.SequenceNumber })
            .FirstOrDefaultAsync(cancellationToken);

        int? tier = season is null
            ? null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
                where entry.SeasonId == season.Id && entry.ClubId == clubId
                select (int?)division.TierNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var fanBase = await _dbContext.ClubFanBases
            .AsNoTracking()
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new
            {
                candidate.Fans,
                candidate.MoodBp,
                candidate.SeasonTargetRank,
                candidate.LastWeekGap,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // A club whose fans have not been opened yet reads as a new club of its tier, as the stadium page quotes it.
        var opening = FanRuleSet.OpeningFor(tier ?? 1);

        return new FanClubRow(
            clubId,
            club.Name,
            tier ?? 1,
            fanBase?.Fans ?? opening.Fans,
            fanBase?.MoodBp ?? opening.MoodBp,
            fanBase?.SeasonTargetRank,
            fanBase?.LastWeekGap,
            season?.Id,
            season?.SequenceNumber ?? 0);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FanEventRow>> ListEventsAsync(Guid clubId, CancellationToken cancellationToken) =>
        await (
            from fanEvent in _dbContext.ClubFanEvents.AsNoTracking()
            join season in _dbContext.Seasons on fanEvent.SeasonId equals season.Id
            where fanEvent.ClubId == clubId
            orderby fanEvent.OccurredAt, fanEvent.Id
            select new FanEventRow(
                fanEvent.Id,
                fanEvent.Kind,
                fanEvent.OccurredAt,
                fanEvent.SeasonId,
                season.SequenceNumber,
                fanEvent.FixtureId,
                fanEvent.FansBefore,
                fanEvent.FansAfter,
                fanEvent.MoodBeforeBp,
                fanEvent.MoodAfterBp,
                fanEvent.ExpectedPointsCentis,
                fanEvent.PointsWon,
                fanEvent.Gap))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, FanExpectation>> GetFrozenExpectationsAsync(
        Guid clubId,
        IReadOnlyCollection<Guid> fixtureIds,
        CancellationToken cancellationToken)
    {
        if (fixtureIds.Count == 0)
        {
            return new Dictionary<Guid, FanExpectation>();
        }

        var rows = await _dbContext.FixtureFanExpectations
            .AsNoTracking()
            .Where(expectation => expectation.ClubId == clubId && fixtureIds.Contains(expectation.FixtureId))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.FixtureId, row => row.Expectation);
    }

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
