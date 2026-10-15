namespace TouchlineManager.Domain.World;

/// <summary>
/// What a club's crest can be made of, as stable codes: a shape, and the stripes laid on it.
/// </summary>
/// <remarks>
/// <para>
/// The two are chosen independently, so any shape can wear any stripes. A shape is a silhouette such as the
/// shield, the roundel, the pointed or the Spanish-style rounded shield; the stripes are a division of the
/// field in the club's two colours: vertical or horizontal stripes, halves, quarters, a sash, a cross and so
/// on. They are the shapes and divisions real clubs' crests are built from, which is why a manager recognises
/// them, but each is drawn from scratch: no real mark, name, letter or animal is involved (`FIC-3`).
/// </para>
/// <para>
/// The server knows only the codes. Drawing them belongs to the web client, which keeps the same lists; the
/// server's job is to refuse a code no client could draw, and to give a club that never chose one a stable
/// shape and stripes of its own.
/// </para>
/// </remarks>
public static class CrestDesigns
{
    /// <summary>The longest a shape or stripes code may be, which is the width of its column.</summary>
    public const int MaxLength = 24;

    /// <summary>Gets every crest shape, in the order the dropdown shows them.</summary>
    public static IReadOnlyList<string> Shapes { get; } =
    [
        "shield",
        "round",
        "pointed",
        "rounded",
        "oval",
        "hexagon",
        "diamond",
        "octagon",
        "square",
        "arch",
        "pentagon",
        "teardrop",
        "flat-base",
        "pennant",
        "gothic",
        "waisted",
        "chamfered",
        "triangle",
        "scooped",
        "wide-oval",
    ];

    /// <summary>Gets every kind of stripes, in the order the dropdown shows them.</summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        "stripes",
        "hoops",
        "halves",
        "fess",
        "quarters",
        "sash",
        "diagonals",
        "diagonal",
        "cross",
        "saltire",
        "chevron",
        "pale",
        "band",
        "checks",
        "star",
        "plain",
    ];

    /// <summary>Checks that a code names a crest shape.</summary>
    /// <param name="code">The code to check.</param>
    public static bool IsKnownShape(string? code) => code is not null && Shapes.Contains(code, StringComparer.Ordinal);

    /// <summary>Checks that a code names a kind of stripes.</summary>
    /// <param name="code">The code to check.</param>
    public static bool IsKnownPattern(string? code) => code is not null && Patterns.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Gets the shape a club wears when its manager has not chosen one, derived from the club's badge seed.
    /// </summary>
    /// <remarks>
    /// A hash of the seed rather than anything stateful, so the same club wears the same crest on every run
    /// and on every platform, and a world seeded twice gives its clubs the same crests twice.
    /// </remarks>
    /// <param name="badgeSeed">The club's badge seed.</param>
    public static string GeneratedShape(string badgeSeed) => Pick(Shapes, badgeSeed, "shape");

    /// <summary>Gets the stripes a club wears when its manager has not chosen any, derived from the badge seed.</summary>
    /// <param name="badgeSeed">The club's badge seed.</param>
    public static string GeneratedPattern(string badgeSeed) => Pick(Patterns, badgeSeed, "pattern");

    /// <summary>Gets the shape a club wears: the one its manager chose, or the generated one.</summary>
    /// <param name="badgeSeed">The club's badge seed.</param>
    /// <param name="chosen">The manager's choice, if one was made.</param>
    public static string ResolveShape(string badgeSeed, string? chosen) =>
        IsKnownShape(chosen) ? chosen! : GeneratedShape(badgeSeed);

    /// <summary>Gets the stripes a club wears: the ones its manager chose, or the generated ones.</summary>
    /// <param name="badgeSeed">The club's badge seed.</param>
    /// <param name="chosen">The manager's choice, if one was made.</param>
    public static string ResolvePattern(string badgeSeed, string? chosen) =>
        IsKnownPattern(chosen) ? chosen! : GeneratedPattern(badgeSeed);

    /// <summary>Picks from a list by a hash of the seed and a salt, so the shape and the stripes vary independently.</summary>
    private static string Pick(IReadOnlyList<string> options, string badgeSeed, string salt)
    {
        ArgumentNullException.ThrowIfNull(badgeSeed);

        var hash = 2166136261u;

        foreach (var character in badgeSeed + "/" + salt)
        {
            hash = (hash ^ character) * 16777619u;
        }

        // Two strings that differ only in their last bytes leave linked low bits in a plain FNV hash, so the
        // shape would largely decide the stripes. A finalizer (murmur3's) spreads every input bit over the result.
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        hash *= 0xc2b2ae35u;
        hash ^= hash >> 16;

        return options[(int)(hash % (uint)options.Count)];
    }
}
