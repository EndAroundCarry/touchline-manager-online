using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Notification-preferences persistence (`COM-4`, master plan §6.9).
/// </summary>
/// <remarks>
/// A module of its own with a port of its own (`MOD-1`). It stages and never saves, so a preference change
/// commits in the transaction that audited it.
/// </remarks>
internal sealed class NotificationPreferencesRepository : INotificationPreferencesRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public NotificationPreferencesRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<NotificationPreferences?> FindByManagerAsync(
        Guid managerId,
        CancellationToken cancellationToken) =>
        _dbContext.NotificationPreferences.SingleOrDefaultAsync(
            preferences => preferences.ManagerId == managerId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, NotificationPreferences>> FindByManagersAsync(
        IReadOnlyCollection<Guid> managerIds,
        CancellationToken cancellationToken)
    {
        if (managerIds.Count == 0)
        {
            return new Dictionary<Guid, NotificationPreferences>();
        }

        return await _dbContext.NotificationPreferences
            .Where(preferences => managerIds.Contains(preferences.ManagerId))
            .ToDictionaryAsync(preferences => preferences.ManagerId, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(NotificationPreferences preferences) => _dbContext.NotificationPreferences.Add(preferences);
}
