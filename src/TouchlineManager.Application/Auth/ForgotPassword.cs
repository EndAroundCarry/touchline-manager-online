using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Starts a password reset by emailing a single-use link.
/// </summary>
/// <remarks>
/// The endpoint always reports success, whether or not the address exists, and takes comparable time
/// either way. Anything else would make this the easiest account-enumeration oracle in the product
/// (ADR-0002).
/// </remarks>
public sealed partial class ForgotPassword
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
    private readonly ILogger<ForgotPassword> _logger;

    /// <summary>Initializes the use case.</summary>
    public ForgotPassword(
        IClock clock,
        IUserRepository users,
        EmailTokenIssuer emailTokens,
        ISecureTokenService secureTokens,
        IEmailSender emailSender,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IOptions<AuthOptions> options,
        ILogger<ForgotPassword> logger)
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

    /// <summary>Issues a reset link when the account exists and may authenticate.</summary>
    /// <returns><see langword="true"/> when an email was dispatched.</returns>
    public async Task<bool> ExecuteAsync(string email, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var user = await _users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken);

        if (user is null || !UserStatusRules.CanAuthenticate(user.Status))
        {
            return false;
        }

        var token = await _emailTokens.IssueAsync(
            user.Id,
            EmailTokenPurpose.ResetPassword,
            _options.PasswordResetLifetime,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.PasswordResetRequested,
            AuditActorTypes.Anonymous,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var link = AuthLinks.ResetPassword(_options.ClientBaseUrl, user.Id, token);

        try
        {
            await _emailSender.SendAsync(AuthEmails.PasswordReset(user.Email, link), cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPasswordResetEmailFailed(user.Id, exception.Message);

            return false;
        }
    }

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Error,
        Message = "Failed to send the password-reset email for account {UserId}: {Reason}")]
    private partial void LogPasswordResetEmailFailed(Guid userId, string reason);
}
