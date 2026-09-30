namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Encrypts and decrypts the stored TOTP secret (ADR-0042).
/// </summary>
/// <remarks>
/// A TOTP secret cannot be hashed, because it has to be recovered to compute a code, so it is protected
/// at rest instead. The application layer never handles the key; this port is the boundary, exactly as
/// <see cref="ISecureTokenService"/> keeps the hashing decision out of the use cases.
/// </remarks>
public interface IMfaSecretProtector
{
    /// <summary>Protects a raw secret for storage. The returned value is opaque and safe to persist.</summary>
    /// <param name="secret">The raw secret bytes.</param>
    string Protect(byte[] secret);

    /// <summary>Recovers a raw secret from its protected form.</summary>
    /// <param name="protectedSecret">The value <see cref="Protect"/> produced.</param>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a valid protected secret.</exception>
    byte[] Unprotect(string protectedSecret);
}
