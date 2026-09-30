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

/// <summary>One durable job row, as the operator's queue read returns it (master plan §10.8, `F-46`).</summary>
/// <remarks>
/// The handler payload is deliberately absent: it is internal JSON a client cannot act on, and the
/// deterministic business key is the correlation an operator needs. The failure, attempts, and lease
/// fields are what tell a stuck job from a busy one.
/// </remarks>
/// <param name="Id">The job's identity.</param>
/// <param name="JobType">The registered job type, e.g. <c>competition.resolve-matchday</c>.</param>
/// <param name="BusinessKey">The deterministic key that makes the enqueue idempotent.</param>
/// <param name="Status">The lifecycle state, as a stable code.</param>
/// <param name="AttemptCount">How many attempts have been made.</param>
/// <param name="MaxAttempts">The attempt ceiling before dead-lettering.</param>
/// <param name="DueAt">The instant the job becomes claimable.</param>
/// <param name="CreatedAt">When the row was created.</param>
/// <param name="UpdatedAt">When the row was last modified.</param>
/// <param name="CompletedAt">When the job completed, when it did.</param>
/// <param name="LeaseOwner">The worker holding the lease, when one does.</param>
/// <param name="LeaseUntil">When the current lease expires, when one is held.</param>
/// <param name="LastError">The last recorded failure, when the job has failed.</param>
public sealed record AdminJobSummary(
    Guid Id,
    string JobType,
    string BusinessKey,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset DueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? LeaseOwner,
    DateTimeOffset? LeaseUntil,
    string? LastError);

/// <summary>Where a page of jobs starts: the last job the previous page returned.</summary>
/// <param name="CreatedAt">The instant the job row was created.</param>
/// <param name="Id">The job's identity, which breaks a same-instant tie.</param>
public sealed record AdminJobCursorPosition(DateTimeOffset CreatedAt, Guid Id);

/// <summary>What a page of the job queue asks for.</summary>
/// <param name="Status">Restrict to one lifecycle state, as a stable code, or null for every state.</param>
/// <param name="JobType">Restrict to one job type, or null for every type.</param>
/// <param name="Before">Where to continue from, or null for the first page.</param>
public sealed record AdminJobQuery(string? Status, string? JobType, AdminJobCursorPosition? Before);

/// <summary>One page of the job queue, newest first.</summary>
/// <param name="Items">The page.</param>
/// <param name="NextCursor">Where the next page begins, or null on the last page.</param>
public sealed record AdminJobPage(IReadOnlyList<AdminJobSummary> Items, string? NextCursor);

/// <summary>One latest simulation attempt for a fixture, the answer to "why is this round stuck?" (`MAT-7`).</summary>
/// <param name="AttemptNumber">The 1-based attempt number for the fixture.</param>
/// <param name="Status">How the attempt ended, as a stable code.</param>
/// <param name="ErrorCategory">The category a failure was classified under, when it failed.</param>
/// <param name="ErrorMessage">What went wrong, when it failed.</param>
/// <param name="CompletedAt">When the attempt finished.</param>
public sealed record AdminSimulationAttempt(
    int AttemptNumber,
    string Status,
    string? ErrorCategory,
    string? ErrorMessage,
    DateTimeOffset CompletedAt);

/// <summary>One fixture of a matchday, with its publication state and its latest simulation attempt.</summary>
/// <param name="Id">The fixture's identity.</param>
/// <param name="HomeClubId">The home club.</param>
/// <param name="HomeClubName">The home club's display name.</param>
/// <param name="AwayClubId">The away club.</param>
/// <param name="AwayClubName">The away club's display name.</param>
/// <param name="KickoffAt">The kickoff instant.</param>
/// <param name="Status">The fixture lifecycle state, as a stable code.</param>
/// <param name="HomeScore">The home score, present only once a result is staged or published.</param>
/// <param name="AwayScore">The away score, present only once a result is staged or published.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="LatestAttempt">The fixture's latest simulation attempt, when one exists.</param>
public sealed record AdminFixtureStatus(
    Guid Id,
    Guid HomeClubId,
    string HomeClubName,
    Guid AwayClubId,
    string AwayClubName,
    DateTimeOffset KickoffAt,
    string Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    AdminSimulationAttempt? LatestAttempt);

/// <summary>
/// The operator's view of one round: the publication state, the nine fixtures with their simulation
/// evidence, and the three jobs that drive it (master plan §13, `F-46`).
/// </summary>
/// <param name="Id">The matchday's identity.</param>
/// <param name="DivisionSeasonId">The division-season the round belongs to.</param>
/// <param name="RoundNumber">The round number, 1-based within the season.</param>
/// <param name="LockAt">When team sheets lock for the round.</param>
/// <param name="KickoffAt">When the round kicks off.</param>
/// <param name="PublicationStatus">The round's publication state, as a stable code.</param>
/// <param name="DivisionId">The division the round belongs to.</param>
/// <param name="DivisionName">The division's display name.</param>
/// <param name="TierNumber">The division's tier.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season's ordinal.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Fixtures">The round's fixtures, in kickoff order.</param>
/// <param name="Jobs">The lock, resolution, and publication jobs for the round, when they exist.</param>
public sealed record AdminMatchdayDetail(
    Guid Id,
    Guid DivisionSeasonId,
    int RoundNumber,
    DateTimeOffset LockAt,
    DateTimeOffset KickoffAt,
    string PublicationStatus,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<AdminFixtureStatus> Fixtures,
    IReadOnlyList<AdminJobSummary> Jobs);

/// <summary>One append-only audit row, as the operator's search returns it (master plan §13, `F-47`).</summary>
/// <remarks>
/// The hashed client IP and the before/after metadata columns are deliberately absent: the search exists
/// to answer who did what, to which target, and why, and an operator does not need a pseudonymous address
/// to do that.
/// </remarks>
/// <param name="Id">The audit row's identity.</param>
/// <param name="ActorType">What kind of actor acted: <c>user</c>, <c>service</c>, or <c>anonymous</c>.</param>
/// <param name="ActorUserId">The acting account, when one is authenticated.</param>
/// <param name="Action">The action performed, e.g. <c>admin.account.suspended</c>.</param>
/// <param name="TargetType">The kind of entity acted upon.</param>
/// <param name="TargetId">The identity of the entity acted upon.</param>
/// <param name="CorrelationId">The correlation ID that ties the row to logs and jobs.</param>
/// <param name="OccurredAt">The real instant the action occurred.</param>
/// <param name="Reason">Why the action was taken, for operator actions.</param>
public sealed record AdminAuditEntry(
    Guid Id,
    string ActorType,
    Guid? ActorUserId,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    string? Reason);

/// <summary>Where a page of audit rows starts: the last row the previous page returned.</summary>
/// <param name="OccurredAt">The instant the action occurred.</param>
/// <param name="Id">The row's identity, which breaks a same-instant tie.</param>
public sealed record AdminAuditCursorPosition(DateTimeOffset OccurredAt, Guid Id);

/// <summary>What a page of the audit search asks for. Every filter is optional.</summary>
/// <param name="ActionPrefix">Restrict to actions beginning with this text, e.g. <c>admin.</c>.</param>
/// <param name="ActorUserId">Restrict to rows acted by one account.</param>
/// <param name="TargetType">Restrict to one target kind.</param>
/// <param name="TargetId">Restrict to one target identity.</param>
/// <param name="Before">Where to continue from, or null for the first page.</param>
public sealed record AdminAuditQuery(
    string? ActionPrefix,
    Guid? ActorUserId,
    string? TargetType,
    Guid? TargetId,
    AdminAuditCursorPosition? Before);

/// <summary>One page of the audit search, newest first.</summary>
/// <param name="Items">The page.</param>
/// <param name="NextCursor">Where the next page begins, or null on the last page.</param>
public sealed record AdminAuditPage(IReadOnlyList<AdminAuditEntry> Items, string? NextCursor);

/// <summary>
/// Reads the operator's surfaces: the game-health snapshot and the read console over the job queue,
/// matchdays, and the audit trail (master plan §10.8, §13, `F-46`, `F-47`).
/// </summary>
/// <remarks>
/// Every read is a projection over rows the game already holds, so the console adds no table and exposes
/// no manager's private data. The pages are keyset-paginated because the queue and the audit trail grow
/// from the top, exactly like the inbox (`§10`).
/// </remarks>
public interface IAdminQueries
{
    /// <summary>The number of rows a page holds.</summary>
    int PageSize { get; }

    /// <summary>Reads the game-health snapshot, or <see langword="null"/> when no world has been seeded.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminGameHealth?> GetGameHealthAsync(CancellationToken cancellationToken);

    /// <summary>Reads one page of the durable job queue, newest first.</summary>
    /// <param name="query">What the page asks for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminJobPage> ListJobsAsync(AdminJobQuery query, CancellationToken cancellationToken);

    /// <summary>Reads one round's publication state, fixtures, and driving jobs, or null when unknown.</summary>
    /// <param name="matchdayId">The matchday to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminMatchdayDetail?> GetMatchdayAsync(Guid matchdayId, CancellationToken cancellationToken);

    /// <summary>Reads one page of the append-only audit trail, newest first.</summary>
    /// <param name="query">What the page asks for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminAuditPage> ListAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken);
}
