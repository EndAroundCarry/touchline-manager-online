using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>
/// Persistence for a manager's notification preferences (`COM-4`, ADR-0029).
/// </summary>
/// <remarks>
/// A row may be absent for a manager who has never changed a preference, so absence means "the defaults"
/// rather than "nothing set" — the readers treat a missing row as all-on, which is what makes a fresh manager
/// reachable without a row having to exist first. It stages and never saves.
/// </remarks>
public interface INotificationPreferencesRepository
{
    /// <summary>Finds a manager's preferences, or null when they have never been set.</summary>
    /// <param name="managerId">The manager.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NotificationPreferences?> FindByManagerAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads several managers' preferences at once, so a batch notification checks opt-outs in one query
    /// rather than one per recipient.
    /// </summary>
    /// <param name="managerIds">The managers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, NotificationPreferences>> FindByManagersAsync(
        IReadOnlyCollection<Guid> managerIds,
        CancellationToken cancellationToken);

    /// <summary>Stages a manager's preferences.</summary>
    /// <param name="preferences">The preferences.</param>
    void Add(NotificationPreferences preferences);
}
