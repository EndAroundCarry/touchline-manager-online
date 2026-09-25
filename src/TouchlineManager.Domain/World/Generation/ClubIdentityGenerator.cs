using System.Globalization;
using System.Text;

namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// Generates fictional club identity deterministically from a seed (`FIC-4`, `FIC-7`, `PYR-14`).
/// </summary>
/// <remarks>
/// <para>
/// Every value is a pure function of the seed, the country's name pool, and the club's <em>ordinal
/// within its country</em>. Nothing reads the clock, a global random source, or the database, so the
/// same seed reproduces the same pyramid and a test can assert it without either.
/// </para>
/// <para>
/// Ordinals are per country and never restart per tier. Tier 1 of a country takes ordinals 0–17, tier 2
/// takes 18–35, and so on. That is what keeps a provisioned tier from colliding with the tier above it:
/// if each tier restarted at zero, every generated tier would propose the same 18 names and the unique
/// name index would reject the second one (`PYR-11`).
/// </para>
/// <para>
/// Names are unique by construction rather than by retry. Each ordinal maps to a distinct
/// <c>(cycle, place, suffix)</c> triple, and the cycle contributes a distinct qualifier or numeral, so
/// two ordinals can never produce the same name — including at the far end of a very deep pyramid, where
/// the combinations run out and the cycle numeral takes over.
/// </para>
/// </remarks>
public static class ClubIdentityGenerator
{
    /// <summary>
    /// The generator version stamped onto every generation run (`FIC-8`, `PYR-14`).
    /// </summary>
    /// <remarks>
    /// Bump this whenever the algorithm or a pool changes. The same seed and version must reproduce the
    /// same logical world; the same seed and a different version must not be expected to.
    /// </remarks>
    public const string Version = "world-gen-v1";

    /// <summary>How many hexadecimal characters of the badge digest a badge seed keeps.</summary>
    private const int BadgeSeedLength = 32;

    /// <summary>Gets the number of clubs a country names before the ordinal cycle repeats.</summary>
    /// <param name="namePoolKey">The country's name-pool key.</param>
    public static int TierCapacityBeforeCycleRepeat(string namePoolKey) =>
        ClubNamePools.For(namePoolKey).CombinationsPerCycle;

    /// <summary>Generates one division's worth of club identity.</summary>
    /// <param name="seed">The world's generation seed.</param>
    /// <param name="namePoolKey">The country's name-pool key.</param>
    /// <param name="countryCode">The country's stable code, mixed into each badge seed.</param>
    /// <param name="tierNumber">The tier being generated, starting at 1.</param>
    /// <param name="clubCount">How many clubs the tier holds.</param>
    /// <returns>The identities, in ordinal order.</returns>
    public static IReadOnlyList<ClubIdentity> GenerateDivision(
        string seed,
        string namePoolKey,
        string countryCode,
        int tierNumber,
        int clubCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(tierNumber, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clubCount);

        var pool = ClubNamePools.For(namePoolKey);
        var firstOrdinal = (tierNumber - 1) * clubCount;
        var identities = new List<ClubIdentity>(clubCount);

        for (var index = 0; index < clubCount; index++)
        {
            identities.Add(Generate(seed, pool, countryCode, firstOrdinal + index));
        }

        return identities;
    }

    /// <summary>Generates the identity for one club ordinal within a country.</summary>
    /// <param name="seed">The world's generation seed.</param>
    /// <param name="pool">The country's name pool.</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="countryOrdinal">
    /// The club's ordinal within the country, counted across every tier the country has ever generated.
    /// </param>
    public static ClubIdentity Generate(
        string seed,
        ClubNamePool pool,
        string countryCode,
        int countryOrdinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegative(countryOrdinal);

        // The offset is what makes the seed audible in the names: without it, two different seeds would
        // differ only in their badge colours and the pyramid would look identical every time.
        var cycleLength = pool.CombinationsPerCycle;
        var offset = new Pcg32(SeedFrom(seed, pool.Key)).NextInt(cycleLength);

        var combination = (countryOrdinal + offset) % cycleLength;
        var cycle = countryOrdinal / cycleLength;

        // Both indices walk the ordinal, so a division alternates its place and its suffix instead of
        // cycling one of them. The pair is injective over one cycle because the cycle is their least
        // common multiple, which is what makes the names unique without a retry loop.
        var place = pool.Places[combination % pool.Places.Count];
        var suffix = pool.ClubSuffixes[combination % pool.ClubSuffixes.Count];
        var placeName = QualifiedPlaceName(pool, place.Name, cycle);

        var name = string.Create(CultureInfo.InvariantCulture, $"{placeName} {suffix}");

        return new ClubIdentity(
            name,
            ShortNameFor(place.Name, suffix),
            place.Name,
            place.Region,
            BadgeSeedFor(seed, countryCode, countryOrdinal));
    }

    private static string QualifiedPlaceName(ClubNamePool pool, string place, int cycle)
    {
        if (cycle == 0)
        {
            return place;
        }

        if (pool.Qualifiers.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{place} {ToRoman(cycle + 1)}");
        }

        return cycle <= pool.Qualifiers.Count
            ? string.Create(CultureInfo.InvariantCulture, $"{pool.Qualifiers[cycle - 1]} {place}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{pool.Qualifiers[^1]} {place} {ToRoman(cycle - pool.Qualifiers.Count + 1)}");
    }

    /// <summary>
    /// Derives the abbreviated name shown in tables.
    /// </summary>
    /// <remarks>
    /// Diacritics are folded away, like the slug, because a three-letter column that renders "PEÑ" on one
    /// screen and "PEN" on another is a rendering bug waiting to be reported. The result is four
    /// characters, which fits every table in the product.
    /// </remarks>
    private static string ShortNameFor(string place, string suffix)
    {
        var folded = Fold(place);
        var letters = new StringBuilder(4);

        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                letters.Append(char.ToUpperInvariant(character));
            }

            if (letters.Length == 3)
            {
                break;
            }
        }

        if (letters.Length == 0)
        {
            letters.Append("CLB");
        }

        letters.Append(char.ToUpperInvariant(Fold(suffix)[0]));

        return letters.ToString();
    }

    private static string BadgeSeedFor(string seed, string countryCode, int countryOrdinal) =>
        DeterministicDigest
            .Of(seed, countryCode, countryOrdinal.ToString(CultureInfo.InvariantCulture))
            [..BadgeSeedLength];

    /// <summary>
    /// Folds a string to its unaccented base letters.
    /// </summary>
    /// <remarks>
    /// The same decomposition <see cref="Club.SlugFrom"/> uses, so a place name and the short name drawn
    /// from it agree about what it says.
    /// </remarks>
    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Builds the seed for a country's name choice from the world seed and the pool key.</summary>
    private static ulong SeedFrom(string seed, string poolKey) => DeterministicDigest.SeedOf(seed, poolKey);

    /// <summary>Renders a small positive integer as a roman numeral, for cycle disambiguation.</summary>
    private static string ToRoman(int value)
    {
        if (value is < 1 or > 3999)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<(int Value, string Symbol)> symbols =
        [
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I"),
        ];

        var remaining = value;
        var builder = new StringBuilder(8);

        foreach (var (amount, symbol) in symbols)
        {
            while (remaining >= amount)
            {
                builder.Append(symbol);
                remaining -= amount;
            }
        }

        return builder.ToString();
    }
}
