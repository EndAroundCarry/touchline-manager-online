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
    AccessTokenValue Issue(User user, IReadOnlyList<string> roles, DateTimeOffset now);
}
