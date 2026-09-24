using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Infrastructure.Security;

/// <summary>
/// Generates and hashes the opaque secrets the auth module stores.
/// </summary>
/// <remarks>
/// <para>
/// Tokens and stamps come from <see cref="RandomNumberGenerator"/>; token hashes use SHA-256, which
/// is right for a high-entropy value that only needs a fast, deterministic lookup form.
/// </para>
/// <para>
/// Client fingerprints (IP prefix, user agent) are keyed with HMAC-SHA256 using the token signing key
/// rather than a bare hash. An unsalted hash of an IPv4 address is reversible by brute force over the
/// address space, which would defeat the point of storing a hash at all
/// (data-classification §4).
/// </para>
/// </remarks>
internal sealed class SecureTokenService : ISecureTokenService
{
    private const int TokenByteLength = 32;
    private const int StampByteLength = 16;

    private readonly byte[] _fingerprintKey;

    /// <summary>Initializes the service.</summary>
    public SecureTokenService(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var signingKey = options.Value.SigningKey;

        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be configured with at least 32 bytes.");
        }

        _fingerprintKey = Encoding.UTF8.GetBytes(signingKey);
    }

    /// <inheritdoc />
    public string CreateToken() => Base64Url(RandomNumberGenerator.GetBytes(TokenByteLength));

    /// <inheritdoc />
    public string CreateSecurityStamp() => Base64Url(RandomNumberGenerator.GetBytes(StampByteLength));

    /// <inheritdoc />
    public string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <inheritdoc />
    public string? HashClientValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        using var hmac = new HMACSHA256(_fingerprintKey);

        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
