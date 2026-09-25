using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad module's write-side persistence for tactical plans.
/// </summary>
/// <remarks>
/// Plans and slots load together because neither is meaningful alone, and both are tracked: an update
/// reshapes the loaded slots in place rather than replacing them, so a save that changes a formation is
/// one <c>SaveChanges</c> with no delete-and-reinsert churn against the plan's unique indexes.
/// </remarks>
internal sealed class TacticsRepository : ITacticsRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public TacticsRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void AddPlan(TacticalPlan plan) => _dbContext.TacticalPlans.Add(plan);

    /// <inheritdoc />
    public void AddSlot(TacticalSlot slot) => _dbContext.TacticalSlots.Add(slot);

    /// <inheritdoc />
    public Task<TacticalPlanRecord?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
        LoadAsync(_dbContext.TacticalPlans.Where(plan => plan.Id == planId), cancellationToken);

    /// <inheritdoc />
    public Task<TacticalPlanRecord?> FindDefaultAsync(Guid clubId, CancellationToken cancellationToken) =>
        LoadAsync(
            _dbContext.TacticalPlans.Where(plan => plan.ClubId == clubId && plan.IsDefault),
            cancellationToken);

    private async Task<TacticalPlanRecord?> LoadAsync(
        IQueryable<TacticalPlan> plans,
        CancellationToken cancellationToken)
    {
        var plan = await plans.FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var slots = await _dbContext.TacticalSlots
            .Where(slot => slot.PlanId == plan.Id)
            .OrderBy(slot => slot.SlotNumber)
            .ToListAsync(cancellationToken);

        return new TacticalPlanRecord(plan, slots);
    }
}
