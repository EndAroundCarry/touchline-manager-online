using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad module's write-side persistence for training plans, individual focuses, and the daily
/// progression run (`TRN-1`, `TRN-2`, `TRN-9`).
/// </summary>
/// <remarks>
/// <para>
/// The plan and a player's focus are loaded as tracked aggregates because the commands that write them
/// revise in place, and the row is kept rather than replaced so its version advances through the same
/// concurrency token the entity tag is built from.
/// </para>
/// <para>
/// The progression roster is loaded as tracked aggregates too — the job mutates every player — but it is
/// loaded in a fixed set of queries rather than one graph, so the whole world's players, attributes, and
/// state come back in a handful of round trips instead of thousands.
/// </para>
/// </remarks>
internal sealed class TrainingRepository : ITrainingRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public TrainingRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<TrainingPlan?> FindPlanAsync(Guid clubId, CancellationToken cancellationToken) =>
        _dbContext.TrainingPlans.FirstOrDefaultAsync(plan => plan.ClubId == clubId, cancellationToken);

    /// <inheritdoc />
    public void AddTrainingPlan(TrainingPlan plan) => _dbContext.TrainingPlans.Add(plan);

    /// <inheritdoc />
    public Task<PlayerTrainingFocus?> FindFocusAsync(Guid playerId, CancellationToken cancellationToken) =>
        _dbContext.PlayerTrainingFocuses.FirstOrDefaultAsync(
            focus => focus.PlayerId == playerId,
            cancellationToken);

    /// <inheritdoc />
    public void AddPlayerFocus(PlayerTrainingFocus focus) =>
        _dbContext.PlayerTrainingFocuses.Add(focus);

    /// <inheritdoc />
    public void RemovePlayerFocus(PlayerTrainingFocus focus) =>
        _dbContext.PlayerTrainingFocuses.Remove(focus);

    /// <inheritdoc />
    public async Task<Guid?> FindPlayerClubAsync(Guid playerId, CancellationToken cancellationToken) =>
        await _dbContext.PlayerContracts
            .Where(contract => contract.PlayerId == playerId && contract.Status == ContractStatus.Active)
            .Select(contract => (Guid?)contract.ClubId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubTrainingRoster>> LoadRostersAsync(CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return [];
        }

        var contracts = await _dbContext.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .ToListAsync(cancellationToken);

        var playerIds = contracts.Select(contract => contract.PlayerId).ToList();

        if (playerIds.Count == 0)
        {
            return [];
        }

        // Four flat queries rather than one graph: every row is a tracked aggregate the job will write, and
        // joining them would either multiply the result or force an untracked projection.
        var players = await _dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .ToListAsync(cancellationToken);

        var attributes = await _dbContext.PlayerAttributes
            .Where(row => playerIds.Contains(row.PlayerId))
            .ToListAsync(cancellationToken);

        var states = await _dbContext.PlayerStates
            .Where(row => playerIds.Contains(row.PlayerId))
            .ToListAsync(cancellationToken);

        var focuses = await _dbContext.PlayerTrainingFocuses
            .Where(focus => playerIds.Contains(focus.PlayerId))
            .ToListAsync(cancellationToken);

        var plans = await _dbContext.TrainingPlans.ToListAsync(cancellationToken);

        var attributesByPlayer = attributes.ToDictionary(row => row.PlayerId);
        var statesByPlayer = states.ToDictionary(row => row.PlayerId);
        var focusesByPlayer = focuses.ToDictionary(focus => focus.PlayerId);
        var plansByClub = plans.ToDictionary(plan => plan.ClubId);
        var playersById = players.ToDictionary(player => player.Id);

        var rosters = new List<ClubTrainingRoster>();

        foreach (var squad in contracts.GroupBy(contract => contract.ClubId))
        {
            var plan = plansByClub.GetValueOrDefault(squad.Key);
            var members = new List<ProgressablePlayer>();

            foreach (var contract in squad.OrderBy(contract => contract.PlayerId))
            {
                if (!playersById.TryGetValue(contract.PlayerId, out var player)
                    || !attributesByPlayer.TryGetValue(contract.PlayerId, out var playerAttributes)
                    || !statesByPlayer.TryGetValue(contract.PlayerId, out var state))
                {
                    continue;
                }

                members.Add(new ProgressablePlayer(
                    player,
                    playerAttributes,
                    state,
                    player.Potential,
                    focusesByPlayer.GetValueOrDefault(contract.PlayerId)?.FocusFamily));
            }

            rosters.Add(new ClubTrainingRoster(
                squad.Key,
                season.GameYear,
                plan?.TeamFocus ?? TrainingMapping.DefaultTeamFocus,
                plan?.Intensity ?? TrainingMapping.DefaultIntensity,
                members));
        }

        return rosters;
    }
}
