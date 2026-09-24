namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>The result of verifying a password against a stored hash.</summary>
public enum PasswordVerificationOutcome
{
    /// <summary>The password did not match.</summary>
    Failed = 0,

    /// <summary>The password matched.</summary>
    Success = 1,

    /// <summary>
    /// The password matched, but the stored hash uses a stale algorithm or work factor and must be
    /// replaced. Transparent rehash on login is required (ADR-0002).
    /// </summary>
    SuccessRehashNeeded = 2,
}

/// <summary>
/// Hashes and verifies passwords. Implemented by ASP.NET Core's supported password hasher
/// (ADR-0002), never by hand-written cryptography.
/// </summary>
/// <remarks>
/// The port exists so the application layer never depends on an identity package, and so tests can
/// substitute a fast hasher without weakening the production work factor.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a password with the configured work factor.</summary>
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash.</summary>
    PasswordVerificationOutcome Verify(string passwordHash, string providedPassword);

    /// <summary>
    /// Performs a verification that cannot succeed, so an unknown-account login costs the same as a
    /// known-account login. Without this, response time alone enumerates accounts (ADR-0002).
    /// </summary>
    /// <param name="providedPassword">The password the caller supplied.</param>
    void PerformTimingEqualization(string providedPassword);
}
