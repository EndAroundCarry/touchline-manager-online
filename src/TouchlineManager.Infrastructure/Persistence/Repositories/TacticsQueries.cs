using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection query the tactics screen reads (master plan §10.4, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// One round trip per concern and none loading an untracked graph for its own sake: the plans, their
/// slots, the squad the slots are picked from, and the open unavailability that decides who can be
/// fielded. Positions and state come back as domain values, because the conversion the response needs
/// belongs to the application mapper.
/// </para>
/// <para>
/// A slot's occupant is resolved against the whole player table rather than only the selectable squad: a
/// player who has since left the club still occupied the slot when the plan was saved, and a plan that
/// suddenly showed an empty shirt would be a worse lie than naming him. The selectable list is what the
/// validator and the picker use.
/// </para>
/// </remarks>
internal sealed class TacticsQueries : ITacticsQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public TacticsQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<TacticsSnapshot?> GetTacticsAsync(Guid clubId, CancellationToken cancellationToken)
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

        var plans = await _dbContext.TacticalPlans
            .Where(plan => plan.ClubId == clubId)
            // The default first, then by name, which is the order the screen lists them in.
            .OrderByDescending(plan => plan.IsDefault)
            .ThenBy(plan => plan.Name)
            .ToListAsync(cancellationToken);

        var planIds = plans.Select(plan => plan.Id).ToList();

        var slots = await _dbContext.TacticalSlots
            .Where(slot => planIds.Contains(slot.PlanId))
            .OrderBy(slot => slot.PlanId)
            .ThenBy(slot => slot.SlotNumber)
            .ToListAsync(cancellationToken);

        // A player is selectable when an active contract and an active registration agree on the club
        // (SQ-6, SQ-7). Both joins are required, so an inconsistent pair drops out rather than half-listing.
        var selectable = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && registration.ClubId == clubId
                && registration.Status == RegistrationStatus.Active
            select player)
            .ToListAsync(cancellationToken);

        // Goalkeepers first and then by name, the order a manager reads a squad in. The stored position is
        // a code, which sorts alphabetically, so the ordering is done after materialization.
        selectable =
        [
            .. selectable
                .OrderBy(player => (int)player.PrimaryPosition)
                .ThenBy(player => player.FullName, StringComparer.Ordinal),
        ];

        var assignedIds = slots
            .Where(slot => slot.AssignedPlayerId is not null)
            .Select(slot => slot.AssignedPlayerId!.Value)
            .Distinct()
            .ToList();

        var selectableIds = selectable.Select(player => player.Id).ToHashSet();
        var departedIds = assignedIds.Where(id => !selectableIds.Contains(id)).ToList();

        var departed = departedIds.Count == 0
            ? new List<Player>()
            : await _dbContext.Players
                .Where(player => departedIds.Contains(player.Id))
                .ToListAsync(cancellationToken);

        var playersById = selectable
            .Concat(departed)
            .ToDictionary(player => player.Id);

        var unavailable = await OpenUnavailabilityAsync(playersById.Keys.ToList(), cancellationToken);

        var planRows = plans
            .Select(plan => new TacticsPlanRow(
                plan.Id,
                plan.Name,
                plan.FormationPreset,
                plan.Instructions,
                plan.IsDefault,
                plan.Version,
                [.. slots
                    .Where(slot => slot.PlanId == plan.Id)
                    .OrderBy(slot => slot.SlotNumber)
                    .Select(slot => new TacticsSlotRow(
                        slot.SlotNumber,
                        slot.PositionFamily,
                        slot.Role,
                        slot.NormalizedX,
                        slot.NormalizedY,
                        Describe(slot.AssignedPlayerId, playersById, unavailable)))]))
            .ToList();

        var selectableRows = selectable
            .Select(player => new TacticsSelectablePlayerRow(
                player.Id,
                player.FullName,
                player.ShortName,
                player.PrimaryPosition,
                player.SecondaryPositions,
                unavailable.Contains(player.Id)))
            .ToList();

        return new TacticsSnapshot(
            clubId,
            club.Club.Name,
            club.Club.ShortName,
            club.Country.Code,
            season.SequenceNumber,
            planRows,
            selectableRows);
    }

    private static TacticsAssignedPlayerRow? Describe(
        Guid? playerId,
        Dictionary<Guid, Player> playersById,
        HashSet<Guid> unavailable) =>
        playerId is { } id && playersById.TryGetValue(id, out var player)
            ? new TacticsAssignedPlayerRow(
                player.Id,
                player.FullName,
                player.ShortName,
                player.PrimaryPosition,
                player.SecondaryPositions,
                unavailable.Contains(player.Id))
            : null;

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
