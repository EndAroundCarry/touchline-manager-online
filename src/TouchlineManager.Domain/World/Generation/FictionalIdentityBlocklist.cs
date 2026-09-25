namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// The curated list of real football identities a generated name must never resemble (`FIC-5`).
/// </summary>
/// <remarks>
/// <para>
/// This is a legal control, not a stylistic one: a generated name that reproduces a real club or a real
/// player would put licensed identity into the product, which is a release blocker (`LGL-9`). The
/// generator's tests fail if any pool entry or generated name matches an entry here, so a pool cannot
/// ship with a collision in it (`FIC-5`).
/// </para>
/// <para>
/// The comparison is token-based and case-insensitive: an entry matches when the candidate contains the
/// entry as a whole word. Substring matching would reject legitimate invented names that merely contain
/// a short entry, and would therefore be weakened until it stopped catching anything.
/// </para>
/// <para>
/// Entries are deliberately limited to identities famous enough that using one would actually be
/// noticed. A blocklist that tried to enumerate every real club in six countries would be unmaintainable
/// and would still miss the next one; the invented place pools in <see cref="ClubNamePools"/> are the
/// primary defence and this list is the backstop.
/// </para>
/// </remarks>
public static class FictionalIdentityBlocklist
{
    /// <summary>
    /// The blocklisted tokens. Real club names, real competition names, and surnames of the
    /// most widely recognised players.
    /// </summary>
    public static readonly IReadOnlyList<string> Entries =
    [
        // Real clubs, by the token that makes the name recognisable.
        "Arsenal",
        "Barcelona",
        "Bayern",
        "Chelsea",
        "Dortmund",
        "Juventus",
        "Liverpool",
        "Madrid",
        "Manchester",
        "Milan",
        "Napoli",
        "Paris",
        "Roma",
        "Tottenham",

        // Real national sides and competitions, which must not arrive through a generated name.
        "UEFA",
        "FIFA",
        "Champions",
        "Premier",

        // Surnames of globally recognised players.
        "Beckham",
        "Kane",
        "Mbappé",
        "Messi",
        "Modrić",
        "Neymar",
        "Ronaldo",
        "Salah",
        "Zidane",
    ];

    /// <summary>Checks whether a candidate name reproduces a blocklisted identity.</summary>
    /// <param name="candidate">The name to check, e.g. a pool place or a generated club name.</param>
    /// <returns><see langword="true"/> when the candidate contains a blocklisted token as a whole word.</returns>
    public static bool ContainsBlockedIdentity(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var words = candidate.Split(
            [' ', '-', '\'', '’', '.', ',', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            foreach (var blocked in Entries)
            {
                if (string.Equals(word, blocked, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
