using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Abstractions.Competition;

/// <summary>
/// Persistence for the season-rollover state machine row (`PR-4`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// The row is the checkpoint. A worker that dies between phases reads it, sees the phase it reached, and
/// resumes at the next one; a second rollover for the same closing season cannot exist because the table's
/// unique index refuses it. This is the competition module's own table, so it lives on a port of its own
/// rather than being folded into the world repository.
/// </remarks>
public interface ISeasonRolloverRepository
{
    /// <summary>Finds a season's rollover, tracked, so the worker can advance it.</summary>
    /// <param name="worldId">The world.</param>
    /// <param name="seasonId">The closing season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SeasonRollover?> FindBySeasonAsync(Guid worldId, Guid seasonId, CancellationToken cancellationToken);

    /// <summary>Finds a rollover by identity, tracked, so a job that names one can advance it.</summary>
    /// <param name="rolloverId">The rollover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SeasonRollover?> FindByIdAsync(Guid rolloverId, CancellationToken cancellationToken);

    /// <summary>Stages a new rollover.</summary>
    /// <param name="rollover">The rollover.</param>
    void Add(SeasonRollover rollover);
}
