using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when enrolment was started.</summary>
public enum MfaEnrolmentOutcome
{
    /// <summary>A fresh secret and recovery codes were issued.</summary>
    Enrolled = 0,

    /// <summary>The account already has a confirmed credential, so it must be reset first.</summary>
    AlreadyEnrolled = 1,

    /// <summary>No account exists for that identity.</summary>
    AccountNotFound = 2,
}

/// <summary>The material a manager needs to finish enrolling, returned exactly once.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Secret">The base32 secret, when one was issued.</param>
/// <param name="OtpAuthUri">The authenticator provisioning URI, when a secret was issued.</param>
/// <param name="RecoveryCodes">The one-time recovery codes, when they were issued.</param>
public sealed record MfaEnrolmentResult(
    MfaEnrolmentOutcome Outcome,
    string? Secret,
    string? OtpAuthUri,
    IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Starts multi-factor enrolment: a new unconfirmed credential and its recovery codes (ADR-0042).
/// </summary>
/// <remarks>
/// Enrolment is two steps on purpose. The secret is issued here but the credential is not confirmed until
/// a code derived from it is presented, so a manager who mistypes the secret into their authenticator is
/// told so immediately rather than locking themselves out at the next sign-in.
/// </remarks>
public sealed class EnrolMfa
{
    /// <summary>The length of a TOTP secret, in bytes (RFC 4226 recommends 20).</summary>
    public const int SecretByteLength = 20;

    private const string IssuerName = "Touchline Manager";

    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IMfaCredentialRepository _credentials;
    private readonly IMfaSecretProtector _protector;
    private readonly ISecureTokenService _secureTokens;
    private readonly MfaRecoveryCodeIssuer _recoveryCodes;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public EnrolMfa(
        IClock clock,
        IUserRepository users,
        IMfaCredentialRepository credentials,
        IMfaSecretProtector protector,
        ISecureTokenService secureTokens,
        MfaRecoveryCodeIssuer recoveryCodes,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _credentials = credentials;
        _protector = protector;
        _secureTokens = secureTokens;
        _recoveryCodes = recoveryCodes;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Starts enrolment for the account.</summary>
    /// <param name="userId">The account enrolling.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MfaEnrolmentResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Refused(MfaEnrolmentOutcome.AccountNotFound);
        }

        var existing = await _credentials.FindByUserIdAsync(userId, cancellationToken);

        if (existing is { IsConfirmed: true })
        {
            return Refused(MfaEnrolmentOutcome.AlreadyEnrolled);
        }

        var now = _clock.UtcNow;
        var secret = _secureTokens.CreateRandomBytes(SecretByteLength);
        var protectedSecret = _protector.Protect(secret);
        var codes = _recoveryCodes.Issue();

        var credential = existing ?? MfaCredential.StartEnrolment(userId, protectedSecret, now);

        if (existing is null)
        {
            _credentials.Add(credential);
        }
        else
        {
            credential.ReplaceSecret(protectedSecret, now);
        }

        credential.ReplaceRecoveryCodes(codes.Select(_secureTokens.HashToken), now);

        _audit.Record(new AuditEntry(
            AdminAuditActions.MfaEnrolmentStarted,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.MfaCredential,
            userId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MfaEnrolmentResult(
            MfaEnrolmentOutcome.Enrolled,
            Base32.Encode(secret),
            Totp.BuildOtpAuthUri(IssuerName, user.Email, secret),
            codes);
    }

    private static MfaEnrolmentResult Refused(MfaEnrolmentOutcome outcome) =>
        new(outcome, Secret: null, OtpAuthUri: null, RecoveryCodes: []);
}
