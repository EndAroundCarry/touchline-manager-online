using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when account deletion was requested.</summary>
public enum DeleteAccountOutcome
{
    /// <summary>Access was closed and the account is awaiting anonymization.</summary>
    Accepted = 0,

    /// <summary>The account no longer exists.</summary>
    AccountNotFound = 1,

    /// <summary>The confirming password was wrong.</summary>
    InvalidCredentials = 2,

    /// <summary>The account is already closing or anonymized. Idempotent.</summary>
    AlreadyClosed = 3,
}

/// <summary>
/// Requests account deletion.
/// </summary>
/// <remarks>
/// Deletion is not a <c>DELETE</c> of data. It closes access immediately, revokes every session, and
/// moves the account to <see cref="UserStatus.DeletionPending"/>; anonymization runs after the
/// cooling period and the historical competition records — matches, transfers, tables, finance and
/// audit rows — are retained, because removing them would rewrite results other managers already
/// played against (master plan §12.4). Anonymization itself belongs to the privacy workflow stage.
/// </remarks>
public sealed class DeleteAccount
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IRefreshSessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public DeleteAccount(
        IClock clock,
        IUserRepository users,
        IRefreshSessionRepository sessions,
        IPasswordHasher passwordHasher,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Requests deletion.</summary>
    public async Task<DeleteAccountOutcome> ExecuteAsync(
        Guid userId,
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return DeleteAccountOutcome.AccountNotFound;
        }

        if (user.Status is UserStatus.DeletionPending or UserStatus.Anonymized)
        {
            return DeleteAccountOutcome.AlreadyClosed;
        }

        if (_passwordHasher.Verify(user.PasswordHash, request.Password) == PasswordVerificationOutcome.Failed)
        {
            return DeleteAccountOutcome.InvalidCredentials;
        }

        await _sessions.RevokeAllForUserAsync(
            userId,
            RefreshSessionRevocationReasons.AccountDeletion,
            now,
            cancellationToken);

        user.RequestDeletion(_secureTokens.CreateSecurityStamp(), now);

        _audit.Record(new AuditEntry(
            AuthAuditActions.DeletionRequested,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            "manager requested account deletion"));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return DeleteAccountOutcome.Accepted;
    }
}
