using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Infrastructure.Security;

/// <summary>
/// Protects the stored TOTP secret with AES-256-GCM under <c>Auth:EncryptionKey</c> (ADR-0042).
/// </summary>
/// <remarks>
/// <para>
/// A TOTP secret is recoverable by design, so it cannot be hashed the way a refresh token is; it is
/// encrypted instead. AES-GCM is authenticated, so a value that has been tampered with is rejected rather
/// than silently decrypted to a wrong secret — the difference between "a verification fails" and "a
/// verification succeeds against a secret an attacker chose".
/// </para>
/// <para>
/// The stored form is <c>v1.{base64(nonce || ciphertext || tag)}</c>. The version prefix is what will let
/// the scheme change without a data migration, and the nonce is unique per protection so the same secret
/// never encrypts to the same bytes twice.
/// </para>
/// </remarks>
internal sealed class MfaSecretProtector : IMfaSecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    /// <summary>Initializes the protector.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the encryption key is absent or too short.</exception>
    public MfaSecretProtector(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var material = options.Value.EncryptionKey;

        if (string.IsNullOrWhiteSpace(material) || Encoding.UTF8.GetByteCount(material) < 32)
        {
            throw new InvalidOperationException(
                "Auth:EncryptionKey must be configured with at least 32 bytes to protect multi-factor secrets.");
        }

        // SHA-256 folds any key material of 32 bytes or more into exactly one AES-256 key.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    /// <inheritdoc />
    public string Protect(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, secret, ciphertext, tag);
        }

        var combined = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(combined, 0);
        ciphertext.CopyTo(combined, NonceSize);
        tag.CopyTo(combined, NonceSize + ciphertext.Length);

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Version}.{Convert.ToBase64String(combined)}");
    }

    /// <inheritdoc />
    public byte[] Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);

        var separator = protectedSecret.IndexOf('.', StringComparison.Ordinal);

        if (separator < 0 || !string.Equals(protectedSecret[..separator], Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The protected multi-factor secret is not in a recognised format.");
        }

        byte[] combined;

        try
        {
            combined = Convert.FromBase64String(protectedSecret[(separator + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "The protected multi-factor secret is not valid base64.",
                exception);
        }

        if (combined.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("The protected multi-factor secret is too short to be valid.");
        }

        var nonce = combined.AsSpan(0, NonceSize);
        var ciphertext = combined.AsSpan(NonceSize, combined.Length - NonceSize - TagSize);
        var tag = combined.AsSpan(combined.Length - TagSize, TagSize);
        var secret = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, secret);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The protected multi-factor secret failed authentication.", exception);
        }

        return secret;
    }
}
