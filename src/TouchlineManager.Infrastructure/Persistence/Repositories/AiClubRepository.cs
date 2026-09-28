using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The AI evaluation's read: every club no human holds, with the facts the policy decides from
/// (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// A handful of flat queries rather than one graph, the same shape the training roster load uses: the
/// clubs, the two plan flags, the players, and their absences come back in a fixed number of round trips
/// however deep the pyramid gets.
/// </para>
/// <para>
/// A club is the AI's to set up when it has no *active* tenure: an AI club, or one whose manager is away and
/// whose tenure the inactivity ladder has marked inactive (`OCC-2`). The AI only fills gaps, so it can never
/// overwrite a present manager's choices (`INS-12`); an inactive tenure is exactly the case `OCC-2` names,
/// where the AI steps in while the manager may still return.
/// </para>
/// </remarks>
internal sealed class AiClubRepository : IAiClubRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public AiClubRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiClubRecord>> LoadAiClubsAsync(CancellationToken cancellationToken)
    {
        var clubIds = await _dbContext.Clubs
            .Where(club => club.Status == ClubStatus.Active
                && !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus == ClubTenureControlStatus.Active))
            .OrderBy(club => club.Id)
            .Select(club => club.Id)
            .ToListAsync(cancellationToken);

        if (clubIds.Count == 0)
        {
            return [];
        }

        var withDefaultPlan = (await _dbContext.TacticalPlans
            .Where(plan => plan.IsDefault && clubIds.Contains(plan.ClubId))
            .Select(plan => plan.ClubId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var withTrainingPlan = (await _dbContext.TrainingPlans
            .Where(plan => clubIds.Contains(plan.ClubId))
            .Select(plan => plan.ClubId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // A player is selectable when an active contract and an active registration agree on the club
        // (SQ-6, SQ-7). The whole entities are read rather than a projection because the player's secondary
        // positions and the attributes' canonical set are computed, not columns.
        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where clubIds.Contains(contract.ClubId)
                && contract.Status == ContractStatus.Active
                && registration.ClubId == contract.ClubId
                && registration.Status == RegistrationStatus.Active
            select new { ClubId = contract.ClubId, Player = player, Attributes = attributes, State = state })
            .ToListAsync(cancellationToken);

        var playerIds = rows.Select(row => row.Player.Id).ToList();

        var unavailable = playerIds.Count == 0
            ? []
            : await _dbContext.PlayerUnavailabilities
                .Where(record => record.ResolvedAt == null && playerIds.Contains(record.PlayerId))
                .Select(record => record.PlayerId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var unavailableIds = unavailable.ToHashSet();
        var byClub = rows.GroupBy(row => row.ClubId).ToDictionary(group => group.Key, group => group.ToList());

        var clubs = new List<AiClubRecord>(clubIds.Count);

        foreach (var clubId in clubIds)
        {
            var squad = byClub.TryGetValue(clubId, out var members) ? members : [];

            clubs.Add(new AiClubRecord(
                clubId,
                withDefaultPlan.Contains(clubId),
                withTrainingPlan.Contains(clubId),
                [.. squad
                    .OrderBy(row => row.Player.Id)
                    .Select(row => new AiClubPlayerRow(
                        row.Player.Id,
                        row.Player.PrimaryPosition,
                        row.Player.SecondaryPositions,
                        row.Attributes.ToSet().Values,
                        row.State.ConditionBp,
                        !unavailableIds.Contains(row.Player.Id)))]));
        }

        return clubs;
    }
}
