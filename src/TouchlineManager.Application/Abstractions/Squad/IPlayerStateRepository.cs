using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>
/// Persistence for a player's condition, fatigue, and morale (`TRN-5`…`TRN-7`, `TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// A staging port like the rest of the module's repositories: it loads tracked states and never saves, so
/// the matchday publication applies a round's match load in the same transaction that publishes the results
/// that caused it. It sits beside the training repository rather than inside it because the two are written
/// by different workflows — training by its own daily job, match load by publication — even though they
/// share the one row.
/// </remarks>
public interface IPlayerStateRepository
{
    /// <summary>
    /// Loads the stored state of the given players, tracked, so a caller can apply a match load to it.
    /// </summary>
    /// <param name="playerIds">The players whose state is read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayerState>> LoadAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);
}
