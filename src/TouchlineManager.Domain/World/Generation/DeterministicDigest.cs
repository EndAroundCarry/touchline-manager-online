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

    /// <summary>
    /// Derives a 64-bit pseudo-random seed from the given parts, for a <see cref="Pcg32"/> stream.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than SHA-256 because this feeds a PRNG rather than proving provenance: it is fast,
    /// has no allocation, and is exactly reproducible. Parts are folded in order with no separator, so
    /// callers must pass them in a fixed order and must not depend on where one part ends and the next
    /// begins.
    /// </remarks>
    /// <param name="parts">The parts to fold, in a caller-fixed order.</param>
    /// <returns>The derived seed.</returns>
    public static ulong SeedOf(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        var hash = Offset;

        foreach (var part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);

            foreach (var character in part)
            {
                unchecked
                {
                    hash ^= character;
                    hash *= Prime;
                }
            }
        }

        return hash;
    }
}
