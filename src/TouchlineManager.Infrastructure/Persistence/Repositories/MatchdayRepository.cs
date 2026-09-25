using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The matchday workflow's persistence: what a round is locked, resolved, and published from
/// (master plan §7.3, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// The loads return tracked aggregates because the workflow mutates them: a fixture locks, stages, and
/// publishes through its own transitions, and a matchday marks itself staged and published. Reading rows
/// and writing them back would move those rules into a use case that has no business knowing them.
/// </para>
/// <para>
/// The sides read is the expensive one, and deliberately so: it gathers everything a snapshot is built
/// from — the plan, the prepared sheet, and every player who may be picked with their attributes, state,
/// and availability — in a handful of queries per club, once, at the moment the fixture is frozen. After
/// that nothing about a match is read from live tables again (`MAT-1`).
/// </para>
/// </remarks>
internal sealed class MatchdayRepository : IMatchdayRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public MatchdayRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<MatchdayWorkload?> LoadMatchdayAsync(Guid matchdayId, CancellationToken cancellationToken)
    {
        var matchday = await _dbContext.Matchdays
            .FirstOrDefaultAsync(candidate => candidate.Id == matchdayId, cancellationToken);

        if (matchday is null)
        {
            return null;
        }

        var divisionSeason = await _dbContext.DivisionSeasons
            .FirstOrDefaultAsync(
                candidate => candidate.Id == matchday.DivisionSeasonId,
                cancellationToken);

        var season = divisionSeason is null
            ? null
            : await _dbContext.Seasons
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == divisionSeason.SeasonId,
                    cancellationToken);

        if (season is null)
        {
            return null;
        }

        // Ordered by identity, which for a UUIDv7 is generation order: the fixture list is stable between
        // reads without depending on the database's row order (TBL-12's principle, applied to the calendar).
        var fixtures = await _dbContext.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId)
            .OrderBy(fixture => fixture.Id)
            .ToListAsync(cancellationToken);

        return new MatchdayWorkload(matchday, fixtures, season.Id, season.WorldId, divisionSeason!.TieDrawSeed);
    }

    /// <inheritdoc />
    public async Task<FixtureSidesSnapshot?> LoadFixtureSidesAsync(
        Guid fixtureId,
        CancellationToken cancellationToken)
    {
        var fixture = await (
            from candidate in _dbContext.Fixtures
            join matchday in _dbContext.Matchdays on candidate.MatchdayId equals matchday.Id
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
            where candidate.Id == fixtureId
            select new
            {
                candidate.HomeClubId,
                candidate.AwayClubId,
                SeasonId = season.Id,
                season.WorldId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (fixture is null)
        {
            return null;
        }

        var sheets = await _dbContext.FixtureTeamSheets
            .Where(sheet => sheet.FixtureId == fixtureId)
            .ToListAsync(cancellationToken);

        var home = await LoadSideAsync(fixture.HomeClubId, fixtureId, sheets, cancellationToken);
        var away = await LoadSideAsync(fixture.AwayClubId, fixtureId, sheets, cancellationToken);

        return new FixtureSidesSnapshot(
            fixtureId,
            fixture.SeasonId,
            fixture.WorldId,
            home,
            away,
            sheets);
    }

    /// <inheritdoc />
    public async Task<DivisionTableSource?> LoadTableSourceAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken)
    {
        var divisionSeason = await _dbContext.DivisionSeasons
            .FirstOrDefaultAsync(candidate => candidate.Id == divisionSeasonId, cancellationToken);

        if (divisionSeason is null)
        {
            return null;
        }

        var clubIds = await _dbContext.ClubSeasonEntries
            .Where(entry => entry.DivisionSeasonId == divisionSeasonId)
            .OrderBy(entry => entry.ClubId)
            .Select(entry => entry.ClubId)
            .ToListAsync(cancellationToken);

        var results = await (
            from fixture in _dbContext.Fixtures
            join matchday in _dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            where matchday.DivisionSeasonId == divisionSeasonId
                && fixture.Status == FixtureStatus.Published
                && fixture.HomeScore != null
                && fixture.AwayScore != null
            orderby fixture.KickoffAt, fixture.Id
            select new
            {
                fixture.MatchId,
                fixture.HomeClubId,
                fixture.AwayClubId,
                HomeScore = fixture.HomeScore!.Value,
                AwayScore = fixture.AwayScore!.Value,
            })
            .ToListAsync(cancellationToken);

        var matchIds = results
            .Select(result => result.MatchId!.Value)
            .ToList();

        // Cards are counted from the match's events rather than from its statistics document: events are the
        // source the engine reconciles the statistics against (MAT-5), they are relational, and a stored
        // payload is not something a projection should have to parse.
        var cards = matchIds.Count == 0
            ? []
            : await _dbContext.MatchEvents
                .Where(matchEvent => matchIds.Contains(matchEvent.MatchId))
                .GroupBy(matchEvent => new { matchEvent.MatchId, matchEvent.ClubId })
                .Select(group => new
                {
                    group.Key.MatchId,
                    group.Key.ClubId,
                    Yellow = group.Count(matchEvent => matchEvent.Type == MatchEventType.YellowCard),
                    Red = group.Count(matchEvent => matchEvent.Type == MatchEventType.RedCard
                        || matchEvent.Type == MatchEventType.SecondYellowCard),
                })
                .ToListAsync(cancellationToken);

        var discipline = cards.ToDictionary(
            card => (card.MatchId, card.ClubId),
            card => new DisciplineCounts(card.Yellow, card.Red));

        var outcomes = new List<MatchOutcome>(results.Count);

        foreach (var result in results)
        {
            var matchId = result.MatchId!.Value;

            outcomes.Add(new MatchOutcome(
                result.HomeClubId,
                result.AwayClubId,
                result.HomeScore,
                result.AwayScore,
                CountsFor(discipline, matchId, result.HomeClubId),
                CountsFor(discipline, matchId, result.AwayClubId)));
        }

        return new DivisionTableSource(
            divisionSeasonId,
            divisionSeason.TieDrawSeed,
            clubIds,
            outcomes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Standing>> LoadStandingsAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken) =>
        await _dbContext.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddStanding(Standing standing) => _dbContext.Standings.Add(standing);

    private static DisciplineCounts CountsFor(
        Dictionary<(Guid MatchId, Guid ClubId), DisciplineCounts> discipline,
        Guid matchId,
        Guid clubId) =>
        discipline.TryGetValue((matchId, clubId), out var counts) ? counts : new DisciplineCounts(0, 0);

    /// <summary>Reads one club's side: its plan, its prepared selection, and everyone it may pick.</summary>
    private async Task<ClubSideSource> LoadSideAsync(
        Guid clubId,
        Guid fixtureId,
        IReadOnlyList<FixtureTeamSheet> sheets,
        CancellationToken cancellationToken)
    {
        var clubName = await _dbContext.Clubs
            .Where(club => club.Id == clubId)
            .Select(club => club.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Club {clubId:D} is named by fixture {fixtureId:D} but does not exist.");

        var plan = await _dbContext.TacticalPlans
            .FirstOrDefaultAsync(
                candidate => candidate.ClubId == clubId && candidate.IsDefault,
                cancellationToken);

        var slots = plan is null
            ? []
            : await _dbContext.TacticalSlots
                .Where(slot => slot.PlanId == plan.Id)
                .OrderBy(slot => slot.SlotNumber)
                .Select(slot => new SnapshotSlotRow(
                    slot.SlotNumber,
                    slot.PositionFamily,
                    slot.Role,
                    slot.NormalizedX,
                    slot.NormalizedY))
                .ToListAsync(cancellationToken);

        var sheet = sheets.FirstOrDefault(candidate => candidate.ClubId == clubId);

        var selection = sheet is null
            ? []
            : await _dbContext.TeamSheetEntries
                .Where(entry => entry.TeamSheetId == sheet.Id)
                .OrderBy(entry => entry.SlotNumber)
                .Select(entry => new SnapshotSelectionRow(
                    entry.SlotNumber,
                    entry.PlayerId,
                    entry.RoleOverride))
                .ToListAsync(cancellationToken);

        // A player is selectable when an active contract and an active registration agree on the club
        // (SQ-6, SQ-7). Both joins are required, so an inconsistent pair drops out rather than half-listing.
        var players = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && registration.ClubId == clubId
                && registration.Status == RegistrationStatus.Active
            select new { player, attributes, state })
            .ToListAsync(cancellationToken);

        var playerIds = players.Select(row => row.player.Id).ToList();

        var unavailable = playerIds.Count == 0
            ? []
            : await _dbContext.PlayerUnavailabilities
                .Where(record => record.ResolvedAt == null && playerIds.Contains(record.PlayerId))
                .Select(record => record.PlayerId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var unavailableIds = unavailable.ToHashSet();

        return new ClubSideSource(
            clubId,
            clubName,
            plan?.Instructions,
            slots,
            selection,
            [.. players.Select(row => new SnapshotPlayerRow(
                row.player.Id,
                row.player.FullName,
                row.player.ShortName,
                row.player.PrimaryPosition,
                row.player.SecondaryPositions,
                row.attributes.ToSet().Values,
                row.state.ConditionBp,
                row.state.FatigueBp,
                row.state.MoraleBp,
                row.state.MatchSharpnessBp,
                !unavailableIds.Contains(row.player.Id)))]);
    }
}
