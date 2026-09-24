using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Security;

/// <summary>
/// Issues short-lived HS256 access tokens (ADR-0002).
/// </summary>
/// <remarks>
/// The token carries the account id, its roles, and the security stamp. The stamp is what makes
/// revocation possible despite stateless tokens: the API compares it with the stored value on every
/// request, so a password reset or sign-out-everywhere invalidates tokens that have not yet expired.
/// </remarks>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly AuthOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Initializes the issuer.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the signing key is absent or too short.</exception>
    public JwtAccessTokenIssuer(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey ?? string.Empty);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:SigningKey must be configured with at least 32 bytes of key material.");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public AccessTokenValue Issue(User user, IReadOnlyList<string> roles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var expiresAt = now.Add(_options.AccessTokenLifetime);

        var identity = new ClaimsIdentity();

        identity.AddClaim(new Claim(AuthClaimNames.Subject, user.Id.ToString()));
        identity.AddClaim(new Claim(AuthClaimNames.SecurityStamp, user.SecurityStamp));
        identity.AddClaim(new Claim(AuthClaimNames.TokenId, Guid.CreateVersion7().ToString()));
        identity.AddClaim(new Claim(
            AuthClaimNames.EmailVerified,
            user.EmailVerifiedAt.HasValue ? "true" : "false"));

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(AuthClaimNames.Role, role));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = identity,
            SigningCredentials = _credentials,
        };

        return new AccessTokenValue(_handler.CreateToken(descriptor), expiresAt);
    }
}
