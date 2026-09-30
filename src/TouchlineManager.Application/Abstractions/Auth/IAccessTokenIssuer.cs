using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>A freshly issued access token and the instant it expires.</summary>
/// <param name="Token">The signed bearer token.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public sealed record AccessTokenValue(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues short-lived access tokens (ADR-0002).
/// </summary>
/// <remarks>
/// The <paramref name="securityStamp"/> is embedded so that a password reset, suspension, or
/// sign-out-everywhere can invalidate tokens that are still within their lifetime.
/// </remarks>
public interface IAccessTokenIssuer
{
    /// <summary>Issues an access token for an account.</summary>
    /// <param name="user">The authenticated account.</param>
    /// <param name="roles">The roles the account holds.</param>
    /// <param name="mfaCompleted">Whether the session completed a second factor (ADR-0042).</param>
    /// <param name="now">The current instant.</param>
    AccessTokenValue Issue(User user, IReadOnlyList<string> roles, bool mfaCompleted, DateTimeOffset now);
}
