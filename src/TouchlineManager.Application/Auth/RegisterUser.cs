using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a registration was attempted.</summary>
public enum RegisterUserOutcome
{
    /// <summary>The account was created.</summary>
    Registered = 0,

    /// <summary>The email address is already registered.</summary>
    EmailAlreadyRegistered = 1,

    /// <summary>The display name is already taken.</summary>
    DisplayNameTaken = 2,
}

/// <summary>The result of a registration attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The new account identity, or <see cref="Guid.Empty"/> when nothing was created.</param>
/// <param name="Email">The email address that was registered.</param>
/// <param name="VerificationEmailSent">Whether the verification email was dispatched.</param>
public sealed record RegisterUserResult(
    RegisterUserOutcome Outcome,
    Guid UserId,
    string Email,
    bool VerificationEmailSent);

/// <summary>
/// Creates an account in the <see cref="UserStatus.Pending"/> state and emails a verification link.
/// </summary>
/// <remarks>
/// The verification email is dispatched <em>after</em> the transaction commits. A mail outage must
/// not roll back a registration, and the account can always request another link, so a failed send
/// is reported rather than fatal.
/// </remarks>
public sealed partial class RegisterUser
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly EmailTokenIssuer _emailTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenService _secureTokens;
    private readonly IEmailSender _emailSender;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthOptions _options;
    private readonly ILogger<RegisterUser> _logger;

    /// <summary>Initializes the use case.</summary>
    public RegisterUser(
        IClock clock,
        IUserRepository users,
        EmailTokenIssuer emailTokens,
        IPasswordHasher passwordHasher,
        ISecureTokenService secureTokens,
        IEmailSender emailSender,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IOptions<AuthOptions> options,
        ILogger<RegisterUser> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _users = users;
        _emailTokens = emailTokens;
        _passwordHasher = passwordHasher;
        _secureTokens = secureTokens;
        _emailSender = emailSender;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Registers an account.</summary>
    public async Task<RegisterUserResult> ExecuteAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var normalizedEmail = User.NormalizeEmail(request.Email);
        var normalizedDisplayName = User.NormalizeDisplayName(request.DisplayName);

        if (await _users.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return new RegisterUserResult(RegisterUserOutcome.EmailAlreadyRegistered, Guid.Empty, request.Email, false);
        }

        if (await _users.DisplayNameExistsAsync(normalizedDisplayName, cancellationToken))
        {
            return new RegisterUserResult(RegisterUserOutcome.DisplayNameTaken, Guid.Empty, request.Email, false);
        }

        var userId = Guid.CreateVersion7();
        var ipHash = _secureTokens.HashClientValue(_requestContext.IpAddress);

        var user = User.Register(
            userId,
            request.Email,
            request.DisplayName,
            _passwordHasher.Hash(request.Password),
            _secureTokens.CreateSecurityStamp(),
            now);

        _users.Add(user);

        RecordConsents(userId, now, ipHash);

        var verificationToken = await _emailTokens.IssueAsync(
            userId,
            EmailTokenPurpose.VerifyEmail,
            _options.EmailVerificationLifetime,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.Registered,
            AuditActorTypes.Anonymous,
            ActorUserId: null,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            ipHash,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var sent = await TrySendVerificationEmailAsync(user, verificationToken, cancellationToken);

        return new RegisterUserResult(RegisterUserOutcome.Registered, userId, user.Email, sent);
    }

    private void RecordConsents(Guid userId, DateTimeOffset now, string? ipHash)
    {
        _users.AddConsent(UserConsent.Record(
            Guid.CreateVersion7(),
            userId,
            ConsentDocumentTypes.Terms,
            _options.TermsVersion,
            now,
            ipHash));

        _users.AddConsent(UserConsent.Record(
            Guid.CreateVersion7(),
            userId,
            ConsentDocumentTypes.Privacy,
            _options.PrivacyVersion,
            now,
            ipHash));
    }

    private async Task<bool> TrySendVerificationEmailAsync(
        User user,
        string verificationToken,
        CancellationToken cancellationToken)
    {
        var link = AuthLinks.VerifyEmail(_options.ClientBaseUrl, user.Id, verificationToken);

        try
        {
            await _emailSender.SendAsync(AuthEmails.Verification(user.Email, link), cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The account exists and can request another link, so a mail failure is reported, not
            // propagated. The correlation ID and account id are enough to investigate without
            // logging the recipient's address (data-classification §4).
            LogVerificationEmailFailed(user.Id, exception.Message);

            return false;
        }
    }

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Error,
        Message = "Failed to send the verification email for account {UserId}: {Reason}")]
    private partial void LogVerificationEmailFailed(Guid userId, string reason);
}
