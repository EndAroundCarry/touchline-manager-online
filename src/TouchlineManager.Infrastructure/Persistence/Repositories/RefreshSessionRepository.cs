using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence for refresh sessions.
/// </summary>
/// <remarks>
/// Revocation and rotation are set-based updates rather than read-modify-write loops, so they are
/// single statements that cannot half-apply, and <see cref="TryRotateAsync"/> can compare-and-set the
/// version in its <c>WHERE</c> clause to make concurrent refreshes mutually exclusive (ADR-0002).
/// </remarks>
internal sealed class RefreshSessionRepository : IRefreshSessionRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public RefreshSessionRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<RefreshSession?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _dbContext.RefreshSessions.SingleOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshSession>> FindActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _dbContext.RefreshSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .OrderBy(session => session.IssuedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _dbContext.RefreshSessions
            .Where(session => session.FamilyId == familyId && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, now)
                    .SetProperty(session => session.RevocationReason, reason)
                    .SetProperty(session => session.Version, session => session.Version + 1),
                cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAllForUserAsync(
        Guid userId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _dbContext.RefreshSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, now)
                    .SetProperty(session => session.RevocationReason, reason)
                    .SetProperty(session => session.Version, session => session.Version + 1),
                cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryRotateAsync(
        Guid sessionId,
        long expectedVersion,
        Guid replacementSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.RefreshSessions
            .Where(session => session.Id == sessionId
                && session.Version == expectedVersion
                && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, now)
                    .SetProperty(session => session.RevocationReason, RefreshSessionRevocationReasons.Rotated)
                    .SetProperty(session => session.ReplacedBySessionId, replacementSessionId)
                    .SetProperty(session => session.LastUsedAt, now)
                    .SetProperty(session => session.Version, session => session.Version + 1),
                cancellationToken);

        return affected == 1;
    }

    /// <inheritdoc />
    public void Add(RefreshSession session) => _dbContext.RefreshSessions.Add(session);
}
