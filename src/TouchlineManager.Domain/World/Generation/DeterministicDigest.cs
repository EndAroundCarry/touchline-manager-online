using System.Security.Cryptography;
using System.Text;

namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// Deterministic digests for generated content and generation provenance.
/// </summary>
/// <remarks>
/// A digest has to be stable across platforms and runtimes for the reproducibility rules to mean
/// anything (`FIC-7`, `PYR-14`), so the input is always UTF-8 bytes joined by a separator that cannot
/// appear in the parts, and the output is lowercase hexadecimal rather than base64.
/// </remarks>
public static class DeterministicDigest
{
    private const char Separator = '\u001f';

    /// <summary>Computes a stable digest over the given parts.</summary>
    /// <param name="parts">The parts to digest, in a caller-fixed order.</param>
    /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
    public static string Of(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var joined = string.Join(Separator, parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));

        return Convert.ToHexStringLower(bytes);
    }
}
