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
/// Issues and validates the short-lived multi-factor login challenge (ADR-0042).
/// </summary>
/// <remarks>
/// The challenge is signed with the same key as an access token but carries a purpose claim, and the
/// access-token validator rejects any token that carries one. That keeps the two token types from being
/// interchangeable: a challenge is proof that a password was accepted and nothing more.
/// </remarks>
internal sealed class MfaChallengeIssuer : IMfaChallengeIssuer
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly AuthOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Initializes the issuer.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the signing key is absent or too short.</exception>
    public MfaChallengeIssuer(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey ?? string.Empty);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:SigningKey must be configured with at least 32 bytes of key material.");
        }

        _credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public MfaChallengeValue Issue(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = now.Add(_options.MfaChallengeLifetime);

        var identity = new ClaimsIdentity();

        identity.AddClaim(new Claim(AuthClaimNames.Subject, user.Id.ToString()));
        identity.AddClaim(new Claim(AuthClaimNames.SecurityStamp, user.SecurityStamp));
        identity.AddClaim(new Claim(AuthClaimNames.TokenId, Guid.CreateVersion7().ToString()));
        identity.AddClaim(new Claim(AuthClaimNames.Purpose, AuthClaimNames.MfaChallengePurpose));

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

        return new MfaChallengeValue(_handler.CreateToken(descriptor), expiresAt);
    }

    /// <inheritdoc />
    public async Task<MfaChallengeIdentity?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateLifetime = true,
            ClockSkew = ClockSkew,
        };

        var result = await _handler.ValidateTokenAsync(token, parameters);

        if (!result.IsValid)
        {
            return null;
        }

        var principal = result.ClaimsIdentity;

        if (!string.Equals(
                principal.FindFirst(AuthClaimNames.Purpose)?.Value,
                AuthClaimNames.MfaChallengePurpose,
                StringComparison.Ordinal))
        {
            return null;
        }

        var subject = principal.FindFirst(AuthClaimNames.Subject)?.Value;
        var stamp = principal.FindFirst(AuthClaimNames.SecurityStamp)?.Value;

        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrEmpty(stamp))
        {
            return null;
        }

        return new MfaChallengeIdentity(userId, stamp);
    }
}
