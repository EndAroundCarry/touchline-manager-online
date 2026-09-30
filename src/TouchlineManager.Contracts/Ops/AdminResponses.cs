using System.Text.Json;

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
/// <param name="ReadOnly">Whether the game is in read-only mode (`F-51`).</param>
/// <param name="ReadOnlyMessage">The operator's stated reason while read-only, otherwise null.</param>
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
    DateTimeOffset GeneratedAt,
    bool ReadOnly,
    string? ReadOnlyMessage);

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

/// <summary>
/// Request to act on a durable job or a matchday. The reason is required and audited (master plan §10.8).
/// </summary>
public sealed record AdminActionRequest
{
    /// <summary>Gets why the operator is taking the action.</summary>
    public required string Reason { get; init; }
}

/// <summary>The result of an operator action on a job.</summary>
/// <param name="JobId">The job that was addressed.</param>
/// <param name="Status">The job's resulting state, as a stable code.</param>
public sealed record AdminJobActionResponse(Guid JobId, string Status);

/// <summary>The result of resuming a matchday.</summary>
/// <param name="MatchdayId">The round that was addressed.</param>
/// <param name="Step">The workflow step that was requeued: <c>resolve</c> or <c>publish</c>.</param>
/// <param name="Requeued">
/// <see langword="true"/> when a dead-lettered job row was reset; <see langword="false"/> when a fresh row
/// was enqueued under the same business key.
/// </param>
public sealed record AdminMatchdayResumeResponse(Guid MatchdayId, string Step, bool Requeued);

/// <summary>The result of handing a club to the AI.</summary>
/// <param name="ClubId">The club that was addressed.</param>
/// <param name="ControlStatus">The club's control state afterwards, as a stable code (<c>ai</c>).</param>
/// <param name="ManagerId">The manager whose tenure was closed, when one was.</param>
public sealed record AdminClubAssignAiResponse(Guid ClubId, string ControlStatus, Guid? ManagerId);

/// <summary>
/// Request to post a compensating finance entry. The reason is required and audited, and the idempotency key
/// becomes the entry's correlation key (master plan §10.8, §13, `FIN-12`).
/// </summary>
public sealed record CompensatingEntryRequest
{
    /// <summary>Gets the club whose ledger is corrected.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets the signed correction to the club's cash, in minor units.</summary>
    public required long CashDeltaMinor { get; init; }

    /// <summary>Gets the entry this correction names, when the operator knows it.</summary>
    public Guid? ReversesEntryId { get; init; }

    /// <summary>Gets why the operator is posting the correction.</summary>
    public required string Reason { get; init; }
}

/// <summary>The result of a compensating finance entry.</summary>
/// <param name="EntryId">The compensating entry that was posted.</param>
/// <param name="ClubId">The club whose ledger was corrected.</param>
/// <param name="CashDeltaMinor">The signed correction that was applied, in minor units.</param>
/// <param name="ResultingCashMinor">The club's cash after the correction, in minor units.</param>
/// <param name="ReversesEntryId">The entry that was corrected, when the operator named one.</param>
public sealed record AdminCompensationResponse(
    Guid EntryId,
    Guid ClubId,
    long CashDeltaMinor,
    long ResultingCashMinor,
    Guid? ReversesEntryId);

/// <summary>
/// Request to publish an operator announcement. The title and body are the notice; the reason is required and
/// audited (master plan §10.8, §13, `F-46`).
/// </summary>
public sealed record AnnouncementRequest
{
    /// <summary>Gets the headline.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the notice.</summary>
    public required string Body { get; init; }

    /// <summary>Gets the country to scope it to, or null for the whole world.</summary>
    public Guid? CountryId { get; init; }

    /// <summary>Gets the division to scope it to, or null for a broader scope.</summary>
    public Guid? DivisionId { get; init; }

    /// <summary>Gets when it stops being shown, or null when it does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets why the operator is publishing it.</summary>
    public required string Reason { get; init; }
}

/// <summary>The result of publishing an announcement.</summary>
/// <param name="NewsItemId">The news item that was published.</param>
/// <param name="Category">The feed category, as a stable code.</param>
/// <param name="PublishedAt">When it became public.</param>
/// <param name="ExpiresAt">When it stops being shown, when it does.</param>
public sealed record AdminAnnouncementResponse(
    Guid NewsItemId,
    string Category,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Request to set a feature flag. The value is an opaque JSON document; the reason is required and audited
/// (master plan §10.8, §13, `F-46`).
/// </summary>
public sealed record SetFeatureFlagRequest
{
    /// <summary>Gets the flag's value, as a JSON document.</summary>
    public JsonElement Value { get; init; }

    /// <summary>Gets optional rollout metadata, as a JSON document.</summary>
    public JsonElement? RolloutMetadata { get; init; }

    /// <summary>Gets why the operator is setting the flag.</summary>
    public required string Reason { get; init; }
}

/// <summary>The result of setting a feature flag.</summary>
/// <param name="Key">The flag's key.</param>
/// <param name="Scope">The scope it belongs to.</param>
/// <param name="Value">The stored value, as a JSON document.</param>
/// <param name="Version">The stored row's version.</param>
/// <param name="Created"><see langword="true"/> when the flag was created; <see langword="false"/> when it was updated.</param>
public sealed record AdminFeatureFlagResponse(
    string Key,
    string Scope,
    JsonElement Value,
    long Version,
    bool Created);
