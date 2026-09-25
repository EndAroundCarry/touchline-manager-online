using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection query the prepare-match screen reads (master plan §10.4, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// One round trip per concern: the fixture with both clubs, the plan the sheet is prepared from, the saved
/// sheet with its entries already joined to the players they name, and the squad it may pick from. The
/// plan is the sheet's own when a sheet exists — so a stored selection is always described by the shape it
/// was prepared against — and the club's default otherwise.
/// </para>
/// <para>
/// The entries are joined to the player table rather than resolved against the selectable squad, because a
/// player who has since left the club still occupied the slot when the sheet was saved and a bench that
/// suddenly showed a nameless shirt would be a worse lie than naming him. The selectable list is what the
/// validator and the picker use.
/// </para>
/// </remarks>
internal sealed class TeamSheetQueries : ITeamSheetQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public TeamSheetQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<TeamSheetSnapshot?> GetTeamSheetAsync(
        Guid fixtureId,
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var fixtureRow = await (
            from fixture in _dbContext.Fixtures
            join matchday in _dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join home in _dbContext.Clubs on fixture.HomeClubId equals home.Id
            join away in _dbContext.Clubs on fixture.AwayClubId equals away.Id
            where fixture.Id == fixtureId
            select new
            {
                Fixture = fixture,
                Matchday = matchday,
                Division = division,
                Home = home,
                Away = away,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (fixtureRow is null)
        {
            return null;
        }

        var sheet = await _dbContext.FixtureTeamSheets
            .FirstOrDefaultAsync(
                candidate => candidate.FixtureId == fixtureId && candidate.ClubId == clubId,
                cancellationToken);

        var planRow = await ResolvePlanAsync(clubId, sheet, cancellationToken);

        var selectable = await SelectablePlayersAsync(clubId, cancellationToken);

        var sheetRow = sheet is null
            ? null
            : await ResolveSheetAsync(sheet, cancellationToken);

        return new TeamSheetSnapshot(
            new TeamSheetFixtureRow(
                fixtureRow.Fixture.Id,
                fixtureRow.Division.Id,
                fixtureRow.Division.DisplayName,
                fixtureRow.Matchday.RoundNumber,
                fixtureRow.Fixture.KickoffAt,
                fixtureRow.Matchday.LockAt,
                fixtureRow.Fixture.Status,
                fixtureRow.Home.Id,
                fixtureRow.Home.Name,
                fixtureRow.Home.ShortName,
                fixtureRow.Away.Id,
                fixtureRow.Away.Name,
                fixtureRow.Away.ShortName),
            planRow,
            sheetRow,
            selectable);
    }

    /// <summary>
    /// Resolves the plan the sheet is described by: the sheet's own when one exists, the club's default
    /// otherwise, and null when the club has neither.
    /// </summary>
    private async Task<TeamSheetPlanRow?> ResolvePlanAsync(
        Guid clubId,
        FixtureTeamSheet? sheet,
        CancellationToken cancellationToken)
    {
        var plan = sheet is not null
            ? await _dbContext.TacticalPlans
                .FirstOrDefaultAsync(candidate => candidate.Id == sheet.TacticalPlanId, cancellationToken)
            : null;

        plan ??= await _dbContext.TacticalPlans
            .FirstOrDefaultAsync(
                candidate => candidate.ClubId == clubId && candidate.IsDefault,
                cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var slots = await _dbContext.TacticalSlots
            .Where(slot => slot.PlanId == plan.Id)
            .OrderBy(slot => slot.SlotNumber)
            .Select(slot => new TeamSheetPlanSlotRow(slot.SlotNumber, slot.PositionFamily, slot.Role))
            .ToListAsync(cancellationToken);

        return new TeamSheetPlanRow(plan.Id, plan.Name, plan.FormationPreset, plan.Version, slots);
    }

    /// <summary>Reads the club's selectable players, goalkeepers first.</summary>
    private async Task<IReadOnlyList<TacticsSelectablePlayerRow>> SelectablePlayersAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        // A player is selectable when an active contract and an active registration agree on the club
        // (SQ-6, SQ-7). Both joins are required, so an inconsistent pair drops out rather than half-listing.
        var players = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && registration.ClubId == clubId
                && registration.Status == RegistrationStatus.Active
            select player)
            .ToListAsync(cancellationToken);

        var ordered = players
            .OrderBy(player => (int)player.PrimaryPosition)
            .ThenBy(player => player.FullName, StringComparer.Ordinal)
            .ToList();

        var unavailable = await OpenUnavailabilityAsync(
            ordered.Select(player => player.Id).ToList(),
            cancellationToken);

        return [.. ordered.Select(player => new TacticsSelectablePlayerRow(
            player.Id,
            player.FullName,
            player.ShortName,
            player.PrimaryPosition,
            player.SecondaryPositions,
            unavailable.Contains(player.Id)))];
    }

    /// <summary>Reads a saved sheet's entries, joined to the players they name.</summary>
    private async Task<TeamSheetRow> ResolveSheetAsync(
        FixtureTeamSheet sheet,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from entry in _dbContext.TeamSheetEntries
            join player in _dbContext.Players on entry.PlayerId equals player.Id
            where entry.TeamSheetId == sheet.Id
            orderby entry.SlotNumber
            select new { Entry = entry, Player = player })
            .ToListAsync(cancellationToken);

        var unavailable = await OpenUnavailabilityAsync(
            rows.Select(row => row.Player.Id).ToList(),
            cancellationToken);

        return new TeamSheetRow(
            sheet.Id,
            sheet.TacticalPlanId,
            sheet.TacticalPlanVersion,
            sheet.Status,
            sheet.Version,
            [.. rows.Select(row => new TeamSheetEntryRow(
                row.Entry.SlotNumber,
                row.Player.Id,
                row.Player.FullName,
                row.Player.ShortName,
                row.Player.PrimaryPosition,
                unavailable.Contains(row.Player.Id)))]);
    }

    /// <summary>Reads the open injury and suspension records for the given players.</summary>
    private async Task<HashSet<Guid>> OpenUnavailabilityAsync(
        List<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        var unavailable = await _dbContext.PlayerUnavailabilities
            .Where(record => playerIds.Contains(record.PlayerId) && record.ResolvedAt == null)
            .Select(record => record.PlayerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. unavailable];
    }
}
