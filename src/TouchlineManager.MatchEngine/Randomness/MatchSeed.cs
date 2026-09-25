using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TouchlineManager.MatchEngine.Randomness;

/// <summary>
/// Derives the secret match seed and its publishable commitment.
/// </summary>
/// <remarks>
/// <para>
/// The seed is not chosen, it is derived — HMAC-SHA256, keyed by the world's secret, over the fixture, the
/// locked snapshot's content hash, and the engine version (master plan §8.2, ADR-0004). That is what stops
/// a manager from picking a favourable seed, and it binds the result to the exact frozen input: change one
/// attribute in the snapshot and the seed changes with it.
/// </para>
/// <para>
/// The content hash used here covers the snapshot <em>without</em> the seed. Hashing the seed into its own
/// derivation would be circular, so the derivation input is the snapshot's facts and the seed is attached
/// afterwards; the full input hash — facts plus seed — is what gets stored on the match (MAT-9).
/// </para>
/// <para>
/// The commitment is a separate, publishable digest of the seed and engine version. Publishing the
/// commitment lets anyone later verify that the seed used is the one that was committed to, while the raw
/// seed stays protected and is released only under the documented operations policy (MAT-10, MAT-11).
/// </para>
/// </remarks>
public static class MatchSeed
{
    /// <summary>The separator between derivation parts, chosen so it cannot appear in a UUID or a hash.</summary>
    private const char Separator = '\u001f';

    /// <summary>Derives the secret match seed from the world's secret and the fixture's frozen facts.</summary>
    /// <param name="worldSecret">The world's secret. Never leaves the server.</param>
    /// <param name="fixtureId">The fixture's identity.</param>
    /// <param name="snapshotContentHash">The snapshot's content hash, excluding the seed.</param>
    /// <param name="engineVersion">The engine version label.</param>
    /// <returns>The derived seed.</returns>
    public static ulong Derive(
        string worldSecret,
        Guid fixtureId,
        string snapshotContentHash,
        string engineVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotContentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);

        var key = Encoding.UTF8.GetBytes(worldSecret);

        var message = Encoding.UTF8.GetBytes(
            string.Join(
                Separator,
                fixtureId.ToString("D"),
                snapshotContentHash,
                engineVersion));

        var mac = HMACSHA256.HashData(key, message);

        // The first eight bytes are the seed. Taking the leading bytes, in a fixed byte order, is what
        // makes the value identical on a little-endian and a big-endian machine.
        return BinaryPrimitives.ReadUInt64BigEndian(mac);
    }

    /// <summary>Computes the publishable commitment to a seed.</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="engineVersion">The engine version label.</param>
    /// <returns>The lowercase hexadecimal commitment.</returns>
    public static string CommitmentOf(ulong seed, string engineVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);

        var material = Encoding.UTF8.GetBytes(
            string.Join(
                Separator,
                engineVersion,
                seed.ToString(CultureInfo.InvariantCulture)));

        return Convert.ToHexStringLower(SHA256.HashData(material));
    }
}
