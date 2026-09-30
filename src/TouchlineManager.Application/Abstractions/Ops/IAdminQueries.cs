namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// The operator's at-a-glance view of the live game: world and season state, the next deadline, and the
/// state of the durable queue (master plan §13, `F-46`).
/// </summary>
/// <param name="WorldId">The world's identity.</param>
/// <param name="WorldStatus">The world lifecycle state, as a stable code.</param>
/// <param name="CurrentSeasonNumber">The season currently running.</param>
/// <param name="SeasonLabel">The current season's display label, when a season exists.</param>
/// <param name="SeasonStatus">The current season's state, as a stable code.</param>
/// <param name="NextMatchdayKickoffAt">The next kickoff anywhere in the world, when one is scheduled.</param>
/// <param name="PendingJobs">Jobs that are queued or currently leased.</param>
/// <param name="DeadLetterJobs">Jobs that exhausted their attempts and need an operator.</param>
/// <param name="OldestOverdueJobDueAt">The due instant of the longest-overdue ready job, when one is late.</param>
/// <param name="GeneratedAt">When the snapshot was taken.</param>
public sealed record AdminGameHealth(
    Guid WorldId,
    string WorldStatus,
    int CurrentSeasonNumber,
    string? SeasonLabel,
    string? SeasonStatus,
    DateTimeOffset? NextMatchdayKickoffAt,
    int PendingJobs,
    int DeadLetterJobs,
    DateTimeOffset? OldestOverdueJobDueAt,
    DateTimeOffset GeneratedAt);

/// <summary>Reads an operator the state of the live game (master plan §13, `F-46`).</summary>
public interface IAdminQueries
{
    /// <summary>Reads the game-health snapshot, or <see langword="null"/> when no world has been seeded.</summary>
    Task<AdminGameHealth?> GetGameHealthAsync(CancellationToken cancellationToken);
}
