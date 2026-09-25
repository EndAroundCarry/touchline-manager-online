using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TouchlineManager.MatchEngine.Configuration;

/// <summary>
/// Identifies the exact rules configuration a match was simulated under.
/// </summary>
/// <remarks>
/// A match snapshot carries this hash, and the engine refuses to simulate when it does not match the rules
/// actually in force. Without that check a result could be produced with one configuration and stamped with
/// another's hash — and since a rules change is exactly the kind of change that alters scores, a wrong stamp
/// would make the result reproducible-looking and unreproducible in fact. It is the configuration's half of
/// `MAT-9`'s contract.
/// </remarks>
public static class EngineConfiguration
{
    /// <summary>Computes the canonical hash of a rules instance.</summary>
    /// <param name="rules">The rules.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static string HashOf(EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var canonical = string.Join('\n', rules.ToCanonicalParts());
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexStringLower(digest);
    }

    /// <summary>Describes a rules instance as text, for diagnosing a hash mismatch.</summary>
    /// <param name="rules">The rules.</param>
    public static string Describe(EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return string.Join(
            '\n',
            rules.ToCanonicalParts().Select(part => part.ToString(CultureInfo.InvariantCulture)));
    }
}
