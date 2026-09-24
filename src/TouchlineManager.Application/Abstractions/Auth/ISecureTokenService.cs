namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Generates and hashes the opaque secrets the auth module stores: refresh tokens, email tokens,
/// security stamps, and client fingerprints.
/// </summary>
/// <remarks>
/// One port for all of them keeps the "how long is the value, how is it hashed" decisions in exactly
/// one place, and keeps <c>RandomNumberGenerator</c> out of the application and domain layers.
/// </remarks>
public interface ISecureTokenService
{
    /// <summary>Creates a fresh, URL-safe opaque token. The value is shown once and never stored raw.</summary>
    string CreateToken();

    /// <summary>Creates a fresh security stamp used to invalidate issued access tokens.</summary>
    string CreateSecurityStamp();

    /// <summary>Hashes a token for storage and lookup. Deterministic, so lookups by hash work.</summary>
    string HashToken(string token);

    /// <summary>
    /// Hashes a client value such as an IP prefix or user agent for abuse analysis
    /// (INT-3, data-classification §4). Returns <see langword="null"/> for a null or empty input.
    /// </summary>
    string? HashClientValue(string? value);
}
