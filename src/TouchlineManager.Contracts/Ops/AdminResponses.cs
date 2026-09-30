namespace TouchlineManager.Contracts.Ops;

/// <summary>
/// The operator's at-a-glance view of the live game (master plan §13, `F-46`).
/// </summary>
/// <remarks>
/// Every field is a status, an instant, or a count over rows the game already holds. It carries no
/// manager, club, or account data, so it cannot become a disclosure path (`F-46`, §13).
/// </remarks>
/// <param name="WorldId">The world's identity.</param>
/// <param name="WorldStatus">The world lifecycle state, as a stable code.</param>
/// <param name="CurrentSeasonNumber">The season currently running.</param>
/// <param name="SeasonLabel">The current season's display label, when a season exists.</param>
/// <param name="SeasonStatus">The current season's state, as a stable code.</param>
/// <param name="NextMatchdayKickoffAt">The next kickoff anywhere in the world, when one is scheduled.</param>
/// <param name="PendingJobs">Jobs queued or currently leased.</param>
/// <param name="DeadLetterJobs">Jobs that exhausted their attempts and need an operator.</param>
/// <param name="OldestOverdueJobDueAt">The due instant of the longest-overdue ready job, when one is late.</param>
/// <param name="GeneratedAt">When the snapshot was taken.</param>
public sealed record AdminGameHealthResponse(
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

/// <summary>Request to suspend or restore an account. The reason is required and audited.</summary>
public sealed record AccountStatusRequest
{
    /// <summary>Gets why the operator is changing the account's status.</summary>
    public required string Reason { get; init; }
}

/// <summary>The result of an account-status change.</summary>
/// <param name="UserId">The account that was addressed.</param>
/// <param name="Status">The resulting state, as a stable code.</param>
public sealed record AccountStatusResponse(Guid UserId, string Status);
