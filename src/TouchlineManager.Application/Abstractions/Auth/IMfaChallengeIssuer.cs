using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>A freshly issued multi-factor challenge and the instant it expires.</summary>
/// <param name="Token">The signed challenge token, presented back with the code.</param>
/// <param name="ExpiresAt">When the challenge stops being accepted.</param>
public sealed record MfaChallengeValue(string Token, DateTimeOffset ExpiresAt);

/// <summary>The account a valid challenge names, and the stamp it was issued against.</summary>
/// <param name="UserId">The account that must complete the second factor.</param>
/// <param name="SecurityStamp">The security stamp at issue time, re-checked on completion.</param>
public sealed record MfaChallengeIdentity(Guid UserId, string SecurityStamp);

/// <summary>
/// Issues and validates the short-lived token that carries a half-authenticated login between the
/// password step and the second-factor step (ADR-0042).
/// </summary>
/// <remarks>
/// The challenge is a signed token with its own purpose rather than a stored row: it has nothing to
/// revoke, it expires in minutes, and it cannot be used as an access token — the validator rejects any
/// token that carries a purpose.
/// </remarks>
public interface IMfaChallengeIssuer
{
    /// <summary>Issues a challenge for an account whose password was just accepted.</summary>
    /// <param name="user">The account.</param>
    /// <param name="now">The current instant.</param>
    MfaChallengeValue Issue(User user, DateTimeOffset now);

    /// <summary>Validates a challenge token, returning its identity or <see langword="null"/> when invalid.</summary>
    /// <param name="token">The challenge token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MfaChallengeIdentity?> ValidateAsync(string token, CancellationToken cancellationToken);
}
