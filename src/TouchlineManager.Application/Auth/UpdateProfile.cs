using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a profile change was attempted.</summary>
public enum UpdateProfileOutcome
{
    /// <summary>The display name was changed.</summary>
    Updated = 0,

    /// <summary>The account no longer exists.</summary>
    AccountNotFound = 1,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 2,

    /// <summary>Another account already uses the requested display name.</summary>
    DisplayNameTaken = 3,

    /// <summary>The account is not email-verified, which a display-name change requires.</summary>
    NotVerified = 4,
}

/// <summary>The result of a profile change.</summary>
public sealed record UpdateProfileResult(UpdateProfileOutcome Outcome, ProfileView? Profile);

/// <summary>
/// Changes the public display name under an optimistic concurrency check (ADR-0009, CONC-1).
/// </summary>
/// <remarks>
/// The <c>If-Match</c> version is mandatory: two devices editing the profile must not silently
/// overwrite each other, and the loser is told to reapply rather than discovering the loss later.
/// </remarks>
public sealed class UpdateProfile
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public UpdateProfile(
        IClock clock,
        IUserRepository users,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Applies the change.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c> header.</param>
    /// <param name="request">The requested change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpdateProfileResult> ExecuteAsync(
        Guid userId,
        long expectedVersion,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return new UpdateProfileResult(UpdateProfileOutcome.AccountNotFound, Profile: null);
        }

        if (user.Version != expectedVersion)
        {
            return new UpdateProfileResult(
                UpdateProfileOutcome.PreconditionFailed,
                new ProfileView(UserProfileMapper.ToProfile(user), user.Version));
        }

        if (!UserStatusRules.CanWrite(user.Status))
        {
            return new UpdateProfileResult(UpdateProfileOutcome.NotVerified, Profile: null);
        }

        var normalizedDisplayName = User.NormalizeDisplayName(request.DisplayName);
        var takenBy = await _users.FindByNormalizedDisplayNameAsync(normalizedDisplayName, cancellationToken);

        if (takenBy is not null && takenBy.Id != user.Id)
        {
            return new UpdateProfileResult(UpdateProfileOutcome.DisplayNameTaken, Profile: null);
        }

        user.ChangeDisplayName(request.DisplayName, now);

        _audit.Record(new AuditEntry(
            AuthAuditActions.ProfileUpdated,
            AuditActorTypes.User,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The row moved between the read and the write. Report a stale precondition and let the
            // manager decide, rather than overwriting a concurrent change.
            return new UpdateProfileResult(UpdateProfileOutcome.PreconditionFailed, Profile: null);
        }

        return new UpdateProfileResult(
            UpdateProfileOutcome.Updated,
            new ProfileView(UserProfileMapper.ToProfile(user), user.Version));
    }
}
