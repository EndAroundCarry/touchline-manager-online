using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Issues a single-use email token, invalidating any older one for the same account and purpose.
/// </summary>
/// <remarks>
/// Shared by registration, resend-verification, and forgot-password because the rule is identical in
/// all three: only the newest active link works (ADR-0002). Putting it in one place is what stops
/// that rule from being reimplemented slightly differently three times.
/// </remarks>
public sealed class EmailTokenIssuer
{
    private readonly IEmailTokenRepository _emailTokens;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the issuer.</summary>
    public EmailTokenIssuer(IEmailTokenRepository emailTokens, ISecureTokenService secureTokens)
    {
        _emailTokens = emailTokens;
        _secureTokens = secureTokens;
    }

    /// <summary>Stages a new token and returns the raw value to place in the email.</summary>
    /// <param name="userId">The account the token belongs to.</param>
    /// <param name="purpose">What the token authorizes.</param>
    /// <param name="lifetime">How long the token remains usable.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> IssueAsync(
        Guid userId,
        EmailTokenPurpose purpose,
        TimeSpan lifetime,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _emailTokens.SupersedeActiveAsync(userId, purpose, now, cancellationToken);

        var rawToken = _secureTokens.CreateToken();

        _emailTokens.Add(EmailToken.Issue(
            Guid.CreateVersion7(),
            userId,
            purpose,
            _secureTokens.HashToken(rawToken),
            now,
            lifetime));

        return rawToken;
    }
}
