using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Ops;

/// <summary>What happened when an operator changed an account's status.</summary>
public enum AccountAdministrationOutcome
{
    /// <summary>The account's status changed.</summary>
    Applied = 0,

    /// <summary>No live account exists for that identity.</summary>
    NotFound = 1,

    /// <summary>The account is already suspended.</summary>
    AlreadySuspended = 2,

    /// <summary>The account is not suspended, so there is nothing to restore.</summary>
    NotSuspended = 3,

    /// <summary>An operator may not suspend itself; that is how an outage becomes a lockout.</summary>
    SelfNotAllowed = 4,
}

/// <summary>The result of an account-status change.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The account that was addressed.</param>
/// <param name="Status">The resulting state as a stable code, when the change was applied.</param>
public sealed record AccountAdministrationResult(
    AccountAdministrationOutcome Outcome,
    Guid UserId,
    string? Status = null);

/// <summary>Shared rules for suspending and restoring accounts (master plan §13, `F-46`).</summary>
internal static class AccountAdministration
{
    /// <summary>The longest operator reason the audit trail stores.</summary>
    public const int ReasonMaxLength = 200;

    /// <summary>Validates an operator reason, which every status change must carry.</summary>
    public static void ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                $"The operator reason must be at most {ReasonMaxLength} characters.",
                nameof(reason));
        }
    }
}

/// <summary>
/// Suspends an account on an operator's authority and closes its sessions (master plan §13, `F-46`).
/// </summary>
/// <remarks>
/// Suspension is immediate: rotating the security stamp invalidates every already-issued access token, and
/// revoking the refresh sessions stops the account coming back (ADR-0002). The change and its audit entry
/// commit together.
/// </remarks>
public sealed class SuspendAccount
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SuspendAccount(
        IClock clock,
        IUserRepository users,
        IRefreshSessionRepository sessions,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _sessions = sessions;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Suspends the account.</summary>
    /// <param name="userId">The account to suspend.</param>
    /// <param name="reason">Why it is being suspended. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountAdministrationResult> ExecuteAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);

        if (_requestContext.ActorUserId == userId)
        {
            return new AccountAdministrationResult(AccountAdministrationOutcome.SelfNotAllowed, userId);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        // An anonymized account is a retained history, not a live identity; it is never re-suspended.
        if (user is null || UserStatusRules.IsTerminal(user.Status))
        {
            return new AccountAdministrationResult(AccountAdministrationOutcome.NotFound, userId);
        }

        if (user.Status == UserStatus.Suspended)
        {
            return new AccountAdministrationResult(AccountAdministrationOutcome.AlreadySuspended, userId);
        }

        var now = _clock.UtcNow;

        user.Suspend(_secureTokens.CreateSecurityStamp(), now);

        await _sessions.RevokeAllForUserAsync(
            userId,
            RefreshSessionRevocationReasons.AccountSuspended,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AdminAuditActions.AccountSuspended,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountAdministrationResult(
            AccountAdministrationOutcome.Applied,
            userId,
            user.Status.ToCode());
    }
}

/// <summary>
/// Restores a suspended account on an operator's authority (master plan §13, `F-46`).
/// </summary>
public sealed class RestoreAccount
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RestoreAccount(
        IClock clock,
        IUserRepository users,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Restores the account.</summary>
    /// <param name="userId">The account to restore.</param>
    /// <param name="reason">Why it is being restored. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountAdministrationResult> ExecuteAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || UserStatusRules.IsTerminal(user.Status))
        {
            return new AccountAdministrationResult(AccountAdministrationOutcome.NotFound, userId);
        }

        if (user.Status != UserStatus.Suspended)
        {
            return new AccountAdministrationResult(AccountAdministrationOutcome.NotSuspended, userId);
        }

        user.Restore(_clock.UtcNow);

        _audit.Record(new AuditEntry(
            AdminAuditActions.AccountRestored,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            IpHash: null,
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountAdministrationResult(
            AccountAdministrationOutcome.Applied,
            userId,
            user.Status.ToCode());
    }
}
