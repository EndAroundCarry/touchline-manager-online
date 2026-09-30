using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TouchlineManager.Domain.Auth;

/// <summary>
/// A time-based one-time password, per RFC 6238 with the RFC 4226 HMAC-SHA1 dynamics (ADR-0042).
/// </summary>
/// <remarks>
/// <para>
/// Twenty years of authenticator applications default to SHA-1 with six digits and a thirty-second step,
/// so this is deliberately the least surprising configuration rather than the most modern one: the value
/// is only as strong as the app a manager already has, and SHA-1 is not the weak point of a six-digit
/// code valid for thirty seconds.
/// </para>
/// <para>
/// The class is pure and takes the instant it should evaluate, so it is tested against the RFC 6238
/// Appendix B vectors instead of against the clock.
/// </para>
/// </remarks>
public static class Totp
{
    /// <summary>The time step, in seconds.</summary>
    public const int StepSeconds = 30;

    /// <summary>The number of digits in a code.</summary>
    public const int Digits = 6;

    /// <summary>The default number of steps either side of the current one that is accepted.</summary>
    public const int DefaultWindowSteps = 1;

    /// <summary>Computes the code for one time step.</summary>
    /// <param name="key">The shared secret.</param>
    /// <param name="timeStep">The time step, i.e. Unix time divided by <see cref="StepSeconds"/>.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 4226 fixes HMAC-SHA1, and authenticator applications default to it, so this is "
            + "interoperability rather than a choice. The code is a six-digit value valid for thirty seconds.")]
    public static string Compute(ReadOnlySpan<byte> key, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        WriteBigEndian(counter, timeStep);

        using var hmac = new HMACSHA1(key.ToArray());
        Span<byte> hash = stackalloc byte[20];
        hmac.TryComputeHash(counter, hash, out _);

        // RFC 4226 dynamic truncation: the low nibble of the last byte selects a four-byte window.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var modulus = (int)Math.Pow(10, Digits);
        var value = binary % modulus;

        return value.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    /// <summary>The time step an instant falls in.</summary>
    /// <param name="now">The current instant.</param>
    public static long CurrentStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>
    /// Verifies a code against the current step and a window of neighbouring steps, so a code entered just
    /// before or after its step still works.
    /// </summary>
    /// <param name="key">The shared secret.</param>
    /// <param name="code">The code presented by the manager.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="windowSteps">How many steps either side of the current one are accepted.</param>
    public static bool Verify(ReadOnlySpan<byte> key, string code, DateTimeOffset now, int windowSteps = DefaultWindowSteps)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var candidate = code.Trim();

        if (candidate.Length != Digits || !candidate.All(char.IsAsciiDigit))
        {
            return false;
        }

        var step = CurrentStep(now);

        for (var offset = -windowSteps; offset <= windowSteps; offset++)
        {
            if (ConstantTimeEquals(Compute(key, step + offset), candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the <c>otpauth</c> provisioning URI an authenticator scans.</summary>
    /// <param name="issuer">The product name shown beside the account.</param>
    /// <param name="account">The account label, typically the email address.</param>
    /// <param name="secret">The shared secret.</param>
    public static string BuildOtpAuthUri(string issuer, string account, ReadOnlySpan<byte> secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        var label = Uri.EscapeDataString($"{issuer}:{account}");

        return $"otpauth://totp/{label}?secret={Base32.Encode(secret)}"
            + $"&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    private static bool ConstantTimeEquals(string expected, string candidate) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(candidate));

    private static void WriteBigEndian(Span<byte> destination, long value)
    {
        for (var index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }
}
