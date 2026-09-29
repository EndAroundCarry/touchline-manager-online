namespace TouchlineManager.Contracts.Auth;

/// <summary>
/// One of the account's active sessions, as the settings screen lists it (`F-07`).
/// </summary>
/// <remarks>
/// Deliberately carries no token hash, IP hash, or user-agent hash. A manager needs to recognise and
/// revoke a session, not to inspect the client fingerprint the server keeps for support analysis
/// (`INT-3`, `VOI-4`), and those hashes are class C3 that must not leave the server.
/// </remarks>
/// <param name="Id">The session identity, used to revoke it.</param>
/// <param name="IssuedAt">When the session was started.</param>
/// <param name="ExpiresAt">When it would expire if never used again.</param>
/// <param name="LastUsedAt">When it was last used to refresh, or null if it never has been.</param>
/// <param name="IsCurrent">Whether the request that produced this list belongs to this session.</param>
public sealed record ActiveSessionResponse(
    Guid Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsCurrent);

/// <summary>The account's active sessions, and which one is asking (`F-07`).</summary>
/// <param name="Sessions">The active sessions, oldest first.</param>
/// <param name="ServerTime">The server's current instant, so client clock drift is visible.</param>
public sealed record SessionsResponse(
    IReadOnlyList<ActiveSessionResponse> Sessions,
    DateTimeOffset ServerTime);
