using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>
/// Persistence for the absences that keep a player out of a side (`TRN-12`, `DIS-1`, `DIS-5`).
/// </summary>
/// <remarks>
/// A staging port like the rest of the module's repositories: it loads tracked records, mutates them, and
/// never saves, so the matchday publication commits the served and newly opened absences with the results
/// that produced them. It exists beside the competition module's matchday port rather than inside it
/// because availability is the squad module's to write, and the publication is the use case that crosses
/// the boundary (§5.2).
/// </remarks>
public interface IAvailabilityRepository
{
    /// <summary>
    /// Loads every open absence of the given clubs, tracked, so a matchday can serve one fixture against
    /// each (`DIS-5`).
    /// </summary>
    /// <param name="clubIds">The clubs whose absences are read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayerUnavailability>> LoadOpenAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Stages a newly opened absence.</summary>
    /// <param name="record">The absence.</param>
    void Add(PlayerUnavailability record);
}
