namespace TouchlineManager.Domain.World;

/// <summary>
/// A country the world launches with, as data (`WORLD-2`).
/// </summary>
/// <remarks>
/// Holding this as a table rather than as branches in the seeder is what keeps "add a country" a data
/// change: the generator, the name pools, and the onboarding screens all key off these five values.
/// </remarks>
/// <param name="Code">The stable three-letter code.</param>
/// <param name="DisplayName">The name shown to managers.</param>
/// <param name="Locale">The locale for formatting and localization, e.g. <c>en-GB</c>.</param>
/// <param name="NamePoolKey">The key selecting the fictional club name dictionary.</param>
/// <param name="SortOrder">The presentation order.</param>
public sealed record LaunchCountry(
    string Code,
    string DisplayName,
    string Locale,
    string NamePoolKey,
    int SortOrder);

/// <summary>The six countries a production world begins with (`WORLD-2`).</summary>
public static class LaunchCountries
{
    /// <summary>England.</summary>
    public const string EnglandCode = "ENG";

    /// <summary>Spain.</summary>
    public const string SpainCode = "ESP";

    /// <summary>Germany.</summary>
    public const string GermanyCode = "GER";

    /// <summary>Italy.</summary>
    public const string ItalyCode = "ITA";

    /// <summary>France.</summary>
    public const string FranceCode = "FRA";

    /// <summary>Romania.</summary>
    public const string RomaniaCode = "ROU";

    /// <summary>The launch set, in presentation order.</summary>
    public static readonly IReadOnlyList<LaunchCountry> All =
    [
        new(EnglandCode, "England", "en-GB", "england", 1),
        new(SpainCode, "Spain", "es-ES", "spain", 2),
        new(GermanyCode, "Germany", "de-DE", "germany", 3),
        new(ItalyCode, "Italy", "it-IT", "italy", 4),
        new(FranceCode, "France", "fr-FR", "france", 5),
        new(RomaniaCode, "Romania", "ro-RO", "romania", 6),
    ];
}
