using System.Text;

namespace TouchlineManager.Domain.Auth;

/// <summary>
/// The RFC 4648 base32 encoding the authenticator provisioning URI and the one-time recovery codes use.
/// </summary>
/// <remarks>
/// Base32 rather than base64 because the value is typed by a human into an authenticator or read aloud
/// from recovery codes, and the 32-character alphabet avoids the case- and look-alike ambiguity of
/// base64. Padding is omitted, matching the <c>otpauth</c> convention.
/// </remarks>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes as unpadded upper-case base32.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;

        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return output.ToString();
    }

    /// <summary>Decodes unpadded upper-case base32, tolerating a padded or lower-case input.</summary>
    /// <param name="text">The base32 text.</param>
    public static byte[] Decode(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var cleaned = text.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in cleaned)
        {
            var value = Alphabet.IndexOf(character, StringComparison.Ordinal);

            if (value < 0)
            {
                throw new ArgumentException($"'{character}' is not a base32 character.", nameof(text));
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
