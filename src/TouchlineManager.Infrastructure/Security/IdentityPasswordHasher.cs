using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Security;

/// <summary>
/// Password hashing via ASP.NET Core's supported password hasher (ADR-0002).
/// </summary>
/// <remarks>
/// <para>
/// The work factor is configured explicitly rather than left to a framework default, because raising
/// it later is the point: a successful login with an older-format hash reports
/// <see cref="PasswordVerificationOutcome.SuccessRehashNeeded"/>, and the login use case rehashes
/// transparently.
/// </para>
/// <para>
/// A throwaway hash is computed once so that verifying against it costs the same as verifying a real
/// account, which removes response time as an account-enumeration signal.
/// </para>
/// </remarks>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    /// <summary>The configured PBKDF2 iteration count.</summary>
    public const int WorkFactorIterations = 210_000;

    private readonly PasswordHasher<User> _hasher;
    private readonly string _timingEqualizationHash;

    /// <summary>Initializes the hasher.</summary>
    public IdentityPasswordHasher()
    {
        _hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = WorkFactorIterations,
        }));

        _timingEqualizationHash = _hasher.HashPassword(null!, "timing-equalisation-only");
    }

    /// <inheritdoc />
    public string Hash(string password) => _hasher.HashPassword(null!, password);

    /// <inheritdoc />
    public PasswordVerificationOutcome Verify(string passwordHash, string providedPassword) =>
        _hasher.VerifyHashedPassword(null!, passwordHash, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
            _ => PasswordVerificationOutcome.Failed,
        };

    /// <inheritdoc />
    public void PerformTimingEqualization(string providedPassword) =>
        _hasher.VerifyHashedPassword(null!, _timingEqualizationHash, providedPassword);
}
