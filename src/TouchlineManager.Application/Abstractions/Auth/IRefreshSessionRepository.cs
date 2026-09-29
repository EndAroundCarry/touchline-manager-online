using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Persistence for refresh sessions (<c>auth.refresh_sessions</c>).
/// </summary>
/// <remarks>
/// Lookups are by token hash, because the raw token is never stored. Revoking a family is a single
/// set-based update rather than a read-modify-write loop, so a reuse incident cannot half-revoke a
/// family if the process dies midway.
/// </remarks>
public interface IRefreshSessionRepository
{
    /// <summary>Finds the session a token hash belongs to.</summary>
    Task<RefreshSession?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Lists the sessions that could still be used to refresh.</summary>
    Task<IReadOnlyList<RefreshSession>> FindActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Revokes every session in a token family.</summary>
    Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Revokes every active session for an account.</summary>
    Task RevokeAllForUserAsync(
        Guid userId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one session, but only when it belongs to the account, so a manager cannot revoke a
    /// session they do not own (`F-07`, §10.9).
    /// </summary>
    /// <remarks>
    /// A set-based conditional update rather than a read-modify-write, like the family and account
    /// revocations: the ownership check, the "still active" check, and the write are one statement, so
    /// two concurrent revocations of the same session cannot both report success.
    /// </remarks>
    /// <returns><see langword="true"/> when an active session owned by the account was revoked.</returns>
    Task<bool> RevokeByIdAsync(
        Guid sessionId,
        Guid userId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rotates a session in place, but only if its stored version still matches
    /// <paramref name="expectedVersion"/> and it is not already revoked.
    /// </summary>
    /// <remarks>
    /// This is the reuse check. A return of <see langword="false"/> means another refresh already
    /// consumed the token, which is the signature of a replayed token — the caller revokes the whole
    /// family (ADR-0002). Doing this as a conditional set-based update rather than a read-modify-write
    /// means two simultaneous refreshes cannot both win.
    /// </remarks>
    /// <returns><see langword="true"/> when this call performed the rotation.</returns>
    Task<bool> TryRotateAsync(
        Guid sessionId,
        long expectedVersion,
        Guid replacementSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Stages a new session.</summary>
    void Add(RefreshSession session);
}
