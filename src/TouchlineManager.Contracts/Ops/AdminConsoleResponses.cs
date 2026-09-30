namespace TouchlineManager.Contracts.Ops;

/// <summary>One durable job as the operator's queue read returns it (master plan §10.8, `F-46`).</summary>
/// <remarks>
/// The handler payload is not serialized: it is internal JSON a client cannot act on, and the business key
/// is the correlation an operator needs.
/// </remarks>
/// <param name="Id">The job's identity.</param>
/// <param name="JobType">The registered job type.</param>
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
public sealed record AdminJobSummaryResponse(
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

/// <summary>One page of the job queue, newest first.</summary>
/// <param name="Items">The page.</param>
/// <param name="NextCursor">Where the next page begins, or null on the last page.</param>
/// <param name="ServerTime">The server's instant, so a client can show relative times without trusting its clock.</param>
public sealed record AdminJobPageResponse(
    IReadOnlyList<AdminJobSummaryResponse> Items,
    string? NextCursor,
    DateTimeOffset ServerTime);

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
public sealed record AdminFixtureStatusResponse(
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
    AdminSimulationAttemptResponse? LatestAttempt);

/// <summary>One simulation attempt, the evidence a round is stuck on (`MAT-7`).</summary>
/// <param name="AttemptNumber">The 1-based attempt number for the fixture.</param>
/// <param name="Status">How the attempt ended, as a stable code.</param>
/// <param name="ErrorCategory">The category a failure was classified under, when it failed.</param>
/// <param name="ErrorMessage">What went wrong, when it failed.</param>
/// <param name="CompletedAt">When the attempt finished.</param>
public sealed record AdminSimulationAttemptResponse(
    int AttemptNumber,
    string Status,
    string? ErrorCategory,
    string? ErrorMessage,
    DateTimeOffset CompletedAt);

/// <summary>
/// The operator's view of one round: publication state, the fixtures with their simulation evidence, and the
/// three jobs that drive it (master plan §13, `F-46`).
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
public sealed record AdminMatchdayDetailResponse(
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
    IReadOnlyList<AdminFixtureStatusResponse> Fixtures,
    IReadOnlyList<AdminJobSummaryResponse> Jobs);

/// <summary>One append-only audit row as the operator's search returns it (master plan §13, `F-47`).</summary>
/// <remarks>
/// The hashed client IP and the before/after metadata columns are not serialized.
/// </remarks>
/// <param name="Id">The audit row's identity.</param>
/// <param name="ActorType">What kind of actor acted: <c>user</c>, <c>service</c>, or <c>anonymous</c>.</param>
/// <param name="ActorUserId">The acting account, when one is authenticated.</param>
/// <param name="Action">The action performed.</param>
/// <param name="TargetType">The kind of entity acted upon.</param>
/// <param name="TargetId">The identity of the entity acted upon.</param>
/// <param name="CorrelationId">The correlation ID that ties the row to logs and jobs.</param>
/// <param name="OccurredAt">The real instant the action occurred.</param>
/// <param name="Reason">Why the action was taken, for operator actions.</param>
public sealed record AdminAuditEntryResponse(
    Guid Id,
    string ActorType,
    Guid? ActorUserId,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    string? Reason);

/// <summary>One page of the audit search, newest first.</summary>
/// <param name="Items">The page.</param>
/// <param name="NextCursor">Where the next page begins, or null on the last page.</param>
/// <param name="ServerTime">The server's instant, so a client can show relative times without trusting its clock.</param>
public sealed record AdminAuditPageResponse(
    IReadOnlyList<AdminAuditEntryResponse> Items,
    string? NextCursor,
    DateTimeOffset ServerTime);
