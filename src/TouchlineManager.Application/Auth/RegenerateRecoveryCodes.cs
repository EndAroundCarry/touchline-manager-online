using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when recovery codes were regenerated.</summary>
public enum RecoveryCodeOutcome
{
    /// <summary>A fresh set was issued.</summary>
    Regenerated = 0,

    /// <summary>The code did not match.</summary>
    InvalidCode = 1,

    /// <summary>The account has no confirmed credential.</summary>
    NotEnrolled = 2,

    /// <summary>No account exists for that identity.</summary>
    AccountNotFound = 3,
}

/// <summary>The replacement codes, returned exactly once.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Codes">The plaintext codes, when a set was issued.</param>
public sealed record RecoveryCodeResult(RecoveryCodeOutcome Outcome, IReadOnlyList<string> Codes);

/// <summary>
/// Issues a fresh set of recovery codes, invalidating the old ones (ADR-0042).
/// </summary>
public sealed class RegenerateRecoveryCodes
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IMfaCredentialRepository _credentials;
    private readonly MfaAuthenticator _authenticator;
    private readonly MfaRecoveryCodeIssuer _codes;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RegenerateRecoveryCodes(
        IClock clock,
        IUserRepository users,
        IMfaCredentialRepository credentials,
        MfaAuthenticator authenticator,
        MfaRecoveryCodeIssuer codes,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _credentials = credentials;
        _authenticator = authenticator;
        _codes = codes;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Regenerates the account's recovery codes.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="code">A current time-based code, proving possession.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecoveryCodeResult> ExecuteAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return new RecoveryCodeResult(RecoveryCodeOutcome.AccountNotFound, []);
        }

        var credential = await _credentials.FindByUserIdAsync(userId, cancellationToken);

        if (credential is null || !credential.IsConfirmed)
        {
            return new RecoveryCodeResult(RecoveryCodeOutcome.NotEnrolled, []);
        }

        var now = _clock.UtcNow;

        if (!_authenticator.VerifyCode(credential, code, now))
        {
            return new RecoveryCodeResult(RecoveryCodeOutcome.InvalidCode, []);
        }

        var codes = _codes.Issue();

        credential.ReplaceRecoveryCodes(codes.Select(_secureTokens.HashToken), now);

        _audit.Record(new AuditEntry(
            AdminAuditActions.RecoveryCodesRegenerated,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.MfaCredential,
            userId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RecoveryCodeResult(RecoveryCodeOutcome.Regenerated, codes);
    }
}
