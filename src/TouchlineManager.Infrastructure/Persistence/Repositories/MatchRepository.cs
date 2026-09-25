using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Domain.Match;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The match module's persistence: frozen inputs, results, events, and attempts (master plan §6.6).
/// </summary>
/// <remarks>
/// Staging only, like the competition repository: the write path adds rows and the unit of work commits
/// them, so a fixture's result, its events, its attempt, and the fixture's own transition are one
/// transaction and a half-written match is impossible (`MAT-7`, `MAT-9`).
/// </remarks>
internal sealed class MatchRepository : IMatchRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public MatchRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void AddSnapshot(InputSnapshot snapshot) => _dbContext.InputSnapshots.Add(snapshot);

    /// <inheritdoc />
    public void AddMatch(SimulatedMatch match) => _dbContext.Matches.Add(match);

    /// <inheritdoc />
    public void AddEvents(IEnumerable<MatchEvent> events) => _dbContext.MatchEvents.AddRange(events);

    /// <inheritdoc />
    public void AddAttempt(SimulationAttempt attempt) => _dbContext.SimulationAttempts.Add(attempt);

    /// <inheritdoc />
    public Task<InputSnapshot?> FindSnapshotAsync(Guid fixtureId, CancellationToken cancellationToken) =>
        _dbContext.InputSnapshots.FirstOrDefaultAsync(
            snapshot => snapshot.FixtureId == fixtureId,
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAttemptsAsync(Guid fixtureId, CancellationToken cancellationToken) =>
        _dbContext.SimulationAttempts.CountAsync(
            attempt => attempt.FixtureId == fixtureId,
            cancellationToken);
}
