using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Sends a fresh verification link to an unverified account.
/// </summary>
/// <remarks>
/// The caller always receives the same answer, whether or not the address exists. Returning anything
/// else would turn this endpoint into an account-existence oracle (ADR-0002).
/// </remarks>
public sealed partial class ResendVerificationEmail
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly EmailTokenIssuer _emailTokens;
    private readonly ISecureTokenService _secureTokens;
    private readonly IEmailSender _emailSender;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthOptions _options;
    private readonly ILogger<ResendVerificationEmail> _logger;

    /// <summary>Initializes the use case.</summary>
    public ResendVerificationEmail(
        IClock clock,
        IUserRepository users,
        EmailTokenIssuer emailTokens,
        ISecureTokenService secureTokens,
        IEmailSender emailSender,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IOptions<AuthOptions> options,
        ILogger<ResendVerificationEmail> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _users = users;
        _emailTokens = emailTokens;
        _secureTokens = secureTokens;
        _emailSender = emailSender;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Re-sends the link when the account exists and is still unverified.</summary>
    /// <returns><see langword="true"/> when an email was dispatched.</returns>
    public async Task<bool> ExecuteAsync(string email, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var user = await _users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken);

        if (user is null || user.EmailVerifiedAt.HasValue || !UserStatusRules.CanAuthenticate(user.Status))
        {
            return false;
        }

        var ipHash = _secureTokens.HashClientValue(_requestContext.IpAddress);

        var token = await _emailTokens.IssueAsync(
            user.Id,
            EmailTokenPurpose.VerifyEmail,
            _options.EmailVerificationLifetime,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.VerificationResent,
            AuditActorTypes.Anonymous,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            ipHash,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var link = AuthLinks.VerifyEmail(_options.ClientBaseUrl, user.Id, token);

        try
        {
            await _emailSender.SendAsync(AuthEmails.Verification(user.Email, link), cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogVerificationEmailFailed(user.Id, exception.Message);

            return false;
        }
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Error,
        Message = "Failed to re-send the verification email for account {UserId}: {Reason}")]
    private partial void LogVerificationEmailFailed(Guid userId, string reason);
}
