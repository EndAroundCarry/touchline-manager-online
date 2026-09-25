using TouchlineManager.Domain.Match;

namespace TouchlineManager.Application.Abstractions.Match;

/// <summary>
/// The match module's port: the snapshots, results, events, and attempts a matchday writes
/// (`MAT-1`, `MAT-9`, master plan §6.6).
/// </summary>
/// <remarks>
/// A port of its own rather than another competition repository, because the match module owns its tables
/// (`MOD-1`). The workflow that writes them spans both modules, which master plan §5.2 says is an
/// application use case's job: the use case loads the competition side and stages the match side, and the
/// unit of work commits both or neither.
/// </remarks>
public interface IMatchRepository
{
    /// <summary>Stages the frozen input a fixture is simulated from.</summary>
    /// <param name="snapshot">The snapshot.</param>
    void AddSnapshot(InputSnapshot snapshot);

    /// <summary>Stages a simulated result.</summary>
    /// <param name="match">The match.</param>
    void AddMatch(SimulatedMatch match);

    /// <summary>Stages a match's event stream.</summary>
    /// <param name="events">The events, in sequence order.</param>
    void AddEvents(IEnumerable<MatchEvent> events);

    /// <summary>Stages a simulation attempt, successful or not.</summary>
    /// <param name="attempt">The attempt.</param>
    void AddAttempt(SimulationAttempt attempt);

    /// <summary>Reads a fixture's snapshot, or null when it has not been taken.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InputSnapshot?> FindSnapshotAsync(Guid fixtureId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the attempts already made for a fixture, which numbers the next one (`MAT-7`).
    /// </summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountAttemptsAsync(Guid fixtureId, CancellationToken cancellationToken);
}
