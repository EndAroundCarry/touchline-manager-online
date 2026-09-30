using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a role was granted or revoked.</summary>
public enum RoleChangeOutcome
{
    /// <summary>The account's roles changed.</summary>
    Applied = 0,

    /// <summary>The account already held (or did not hold) the role; nothing changed.</summary>
    Unchanged = 1,

    /// <summary>No account exists for that identity.</summary>
    UserNotFound = 2,

    /// <summary>The role name is not one the product recognises.</summary>
    InvalidRole = 3,

    /// <summary>The base <c>player</c> role cannot be revoked.</summary>
    BaseRoleNotRevocable = 4,
}

/// <summary>The result of a role change.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The account whose roles were addressed.</param>
/// <param name="Role">The role that was named.</param>
public sealed record RoleChangeResult(RoleChangeOutcome Outcome, Guid UserId, string Role);

/// <summary>Shared rules for granting and revoking roles (master plan §6.2, §10.8).</summary>
internal static class RoleAdministration
{
    /// <summary>The longest operator reason the audit trail stores.</summary>
    public const int ReasonMaxLength = 200;

    /// <summary>Validates an operator reason, which every role change must carry.</summary>
    public static void ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                $"The reason must be at most {ReasonMaxLength} characters.",
                nameof(reason));
        }
    }

    /// <summary>
    /// Chooses the audit actor. A request acting on another account is a <c>user</c>; the access
    /// administration tool has no request, so its changes are a <c>service</c> action.
    /// </summary>
    public static string ActorType(IRequestContext requestContext) =>
        requestContext.ActorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.Service;
}

/// <summary>
/// Grants a role to an account: how an <c>operator</c>, <c>support</c>, or <c>admin</c> account comes to
/// exist (master plan §6.2, §10.8, ADR-0042).
/// </summary>
/// <remarks>
/// Idempotent: granting a role the account already holds succeeds without writing. The change and its
/// audit entry commit together, so a grant with no audit trail is impossible.
/// </remarks>
public sealed class GrantRole
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public GrantRole(
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

    /// <summary>Grants the role.</summary>
    /// <param name="userId">The account to grant to.</param>
    /// <param name="role">The role name.</param>
    /// <param name="reason">Why the role is being granted. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleChangeResult> ExecuteAsync(
        Guid userId,
        string role,
        string reason,
        CancellationToken cancellationToken)
    {
        RoleAdministration.ValidateReason(reason);

        if (!UserRoles.IsKnown(role))
        {
            return new RoleChangeResult(RoleChangeOutcome.InvalidRole, userId, role);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return new RoleChangeResult(RoleChangeOutcome.UserNotFound, userId, role);
        }

        if (user.HasRole(role))
        {
            return new RoleChangeResult(RoleChangeOutcome.Unchanged, userId, role);
        }

        user.GrantRole(role, _clock.UtcNow);

        _audit.Record(new AuditEntry(
            AdminAuditActions.RoleGranted,
            RoleAdministration.ActorType(_requestContext),
            _requestContext.ActorUserId,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleChangeResult(RoleChangeOutcome.Applied, userId, role);
    }
}

/// <summary>
/// Revokes a role from an account (master plan §6.2, §10.8, ADR-0042).
/// </summary>
/// <remarks>
/// Idempotent: revoking a role the account does not hold succeeds without writing. The base
/// <c>player</c> role is refused, because every account is a manager.
/// </remarks>
public sealed class RevokeRole
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RevokeRole(
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

    /// <summary>Revokes the role.</summary>
    /// <param name="userId">The account to revoke from.</param>
    /// <param name="role">The role name.</param>
    /// <param name="reason">Why the role is being revoked. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleChangeResult> ExecuteAsync(
        Guid userId,
        string role,
        string reason,
        CancellationToken cancellationToken)
    {
        RoleAdministration.ValidateReason(reason);

        if (!UserRoles.IsKnown(role))
        {
            return new RoleChangeResult(RoleChangeOutcome.InvalidRole, userId, role);
        }

        if (string.Equals(role, UserRoles.Player, StringComparison.Ordinal))
        {
            return new RoleChangeResult(RoleChangeOutcome.BaseRoleNotRevocable, userId, role);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return new RoleChangeResult(RoleChangeOutcome.UserNotFound, userId, role);
        }

        if (!user.HasRole(role))
        {
            return new RoleChangeResult(RoleChangeOutcome.Unchanged, userId, role);
        }

        user.RevokeRole(role, _clock.UtcNow);

        _audit.Record(new AuditEntry(
            AdminAuditActions.RoleRevoked,
            RoleAdministration.ActorType(_requestContext),
            _requestContext.ActorUserId,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleChangeResult(RoleChangeOutcome.Applied, userId, role);
    }
}
