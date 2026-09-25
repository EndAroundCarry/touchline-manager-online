namespace TouchlineManager.Domain.World.Generation;

/// <summary>A place a generated club is named after.</summary>
/// <param name="Name">The place name. Invented fiction, never a real club's home town (`FIC-1`).</param>
/// <param name="Region">The region the place sits in, shown on the club page.</param>
public sealed record PoolPlace(string Name, string Region);

/// <summary>
/// The curated dictionaries one locale generates club identity from (`FIC-4`, `FIC-8`).
/// </summary>
/// <remarks>
/// <para>
/// Pools are keyed by <see cref="Country.NamePoolKey"/> rather than by display name, so adding a country
/// is a data change and the generator never guesses a locale from a string. A pool is a versioned
/// artifact reviewed like code: every entry is invented, none is scraped from a real squad or a real
/// club registry, and the whole set is checked against
/// <see cref="FictionalIdentityBlocklist"/> by the generator's tests (`FIC-5`).
/// </para>
/// <para>
/// <see cref="Places"/> and <see cref="ClubSuffixes"/> are combined into names, so
/// <c>places × suffixes × qualifiers</c> is how many clubs a country can name before the generator
/// falls back to a numeral. That product is deliberately large: a pyramid has no maximum depth
/// (`PYR-11`), and running out of names would otherwise become a hidden ceiling.
/// </para>
/// </remarks>
/// <param name="Key">The pool key, matching a launch country's <see cref="Country.NamePoolKey"/>.</param>
/// <param name="Places">The invented places clubs are named after.</param>
/// <param name="ClubSuffixes">The club-name patterns, e.g. <c>United</c>, <c>Deportivo</c>.</param>
/// <param name="Qualifiers">
/// District words that prefix a place when the base combinations are exhausted, e.g. <c>North Ashvale</c>.
/// </param>
public sealed record ClubNamePool(
    string Key,
    IReadOnlyList<PoolPlace> Places,
    IReadOnlyList<string> ClubSuffixes,
    IReadOnlyList<string> Qualifiers)
{
    /// <summary>
    /// Gets how many distinct base names one qualifier cycle can produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator picks a place with <c>ordinal % places</c> and a suffix with
    /// <c>ordinal % suffixes</c>, so the cycle is the least common multiple of the two counts rather
    /// than their product: over one cycle every <c>(place, suffix)</c> pair it can reach is visited
    /// exactly once, which the Chinese remainder theorem guarantees and a test asserts.
    /// </para>
    /// <para>
    /// That matters for how the names read. Ordering the combinations place-major would name a whole
    /// division "Ashvale United", "Ashvale City", "Ashvale Athletic" and so on; interleaving alternates
    /// both dimensions, so a division reads like a set of clubs rather than like one club's reserve sides.
    /// </para>
    /// </remarks>
    public int CombinationsPerCycle => LeastCommonMultiple(Places.Count, ClubSuffixes.Count);

    /// <summary>Gets how many clubs the pool can name before it must append a numeral.</summary>
    public int NamedCapacity => CombinationsPerCycle * (Qualifiers.Count + 1);

    private static int LeastCommonMultiple(int left, int right)
    {
        if (left <= 0 || right <= 0)
        {
            return 0;
        }

        return left / GreatestCommonDivisor(left, right) * right;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}

/// <summary>
/// The versioned name pools the world generator draws from (`FIC-4`, `FIC-8`).
/// </summary>
/// <remarks>
/// <para>
/// All six launch countries are covered with invented places in the country's own orthography, because a
/// Romanian pyramid named from an English pool would read as a placeholder rather than as a place. The
/// accents matter to the generator: <see cref="Club.SlugFrom"/> folds them, so "Corvalán" and "Corvalan"
/// cannot become two clubs sharing one URL.
/// </para>
/// <para>
/// Bump <see cref="Version"/> whenever a pool changes. A generated world records the pool version it
/// was built from, so a name that changed later can still be explained (`FIC-8`, `FIC-10`).
/// </para>
/// </remarks>
public static class ClubNamePools
{
    /// <summary>The version stamped onto generation runs that used these pools.</summary>
    public const string Version = "name-pools-v1";

    private static readonly IReadOnlyDictionary<string, ClubNamePool> Pools =
        new Dictionary<string, ClubNamePool>(StringComparer.Ordinal)
        {
            ["england"] = new ClubNamePool(
                "england",
                [
                    new("Ashvale", "Northern Counties"),
                    new("Bramford", "Northern Counties"),
                    new("Coldharbour", "Northern Counties"),
                    new("Dunmere", "Midlands"),
                    new("Eastgate", "Midlands"),
                    new("Fenwick", "Midlands"),
                    new("Grimsdale", "Midlands"),
                    new("Harrowbrook", "Home Counties"),
                    new("Ironbridge", "Midlands"),
                    new("Kestrelmoor", "Northern Counties"),
                    new("Langmere", "Eastern Counties"),
                    new("Millbrook", "Home Counties"),
                    new("Northaven", "Eastern Counties"),
                    new("Oakhurst", "Southern Counties"),
                    new("Pellham", "Southern Counties"),
                    new("Quarrydale", "Northern Counties"),
                    new("Redmarsh", "Eastern Counties"),
                    new("Stonebury", "Southern Counties"),
                    new("Thornwick", "Home Counties"),
                    new("Westbourne", "Southern Counties"),
                ],
                [
                    "United",
                    "City",
                    "Athletic",
                    "Rovers",
                    "Wanderers",
                    "Town",
                    "County",
                    "Rangers",
                    "Harriers",
                    "Park",
                    "Vale",
                ],
                ["North", "South", "East", "West", "Central", "Upper", "Lower", "Old"]),

            ["spain"] = new ClubNamePool(
                "spain",
                [
                    new("Alvedra", "Costa Verde"),
                    new("Brazales", "Meseta Alta"),
                    new("Corvalán", "Vega Sur"),
                    new("Durazno", "Meseta Alta"),
                    new("Esparza", "Costa Este"),
                    new("Fontanar", "Vega Sur"),
                    new("Granela", "Tierras Altas"),
                    new("Hontanar", "Costa Verde"),
                    new("Izarbe", "Tierras Altas"),
                    new("Jarales", "Vega Sur"),
                    new("Lamedo", "Costa Este"),
                    new("Maroña", "Costa Verde"),
                    new("Navalvillar", "Meseta Alta"),
                    new("Olmeda", "Tierras Altas"),
                    new("Peñalta", "Tierras Altas"),
                    new("Quintanar", "Meseta Alta"),
                    new("Ribalta", "Costa Este"),
                    new("Salcedo", "Vega Sur"),
                    new("Tordeza", "Costa Verde"),
                    new("Valdoro", "Costa Este"),
                ],
                [
                    "Club",
                    "Atlético",
                    "Unión",
                    "Deportivo",
                    "Sporting",
                    "Cultural",
                    "Gimnástico",
                    "Olímpico",
                    "Alianza",
                    "Balompié",
                    "Aurora",
                ],
                ["Norte", "Sur", "Este", "Oeste", "Central", "Alto", "Bajo", "Viejo"]),

            ["germany"] = new ClubNamePool(
                "germany",
                [
                    new("Aldenmoor", "Nordmark"),
                    new("Brachfeld", "Nordmark"),
                    new("Dornstett", "Südmark"),
                    new("Elbermark", "Westmark"),
                    new("Falkenried", "Ostmark"),
                    new("Grünhag", "Südmark"),
                    new("Hohensteig", "Alpenvorland"),
                    new("Jägerstett", "Alpenvorland"),
                    new("Kronmoor", "Nordmark"),
                    new("Lindenmark", "Westmark"),
                    new("Möwenfeld", "Ostmark"),
                    new("Nordhag", "Nordmark"),
                    new("Osterstett", "Ostmark"),
                    new("Priemwald", "Westmark"),
                    new("Quellstett", "Südmark"),
                    new("Rotenmoor", "Nordmark"),
                    new("Silberhag", "Alpenvorland"),
                    new("Tannstett", "Alpenvorland"),
                    new("Ufermark", "Westmark"),
                    new("Waldstett", "Ostmark"),
                ],
                [
                    "Sport",
                    "Turn",
                    "Wacker",
                    "Sturm",
                    "Blau-Weiß",
                    "Grün-Weiß",
                    "Nordstern",
                    "Stern",
                    "Adler",
                    "Falke",
                    "Löwe",
                ],
                ["Nord", "Süd", "Ost", "West", "Mittel", "Ober", "Unter", "Alt"]),

            ["italy"] = new ClubNamePool(
                "italy",
                [
                    new("Alviano", "Norditalia"),
                    new("Brenzano", "Norditalia"),
                    new("Cortale", "Centroitalia"),
                    new("Dolmara", "Alpi"),
                    new("Ervetta", "Centroitalia"),
                    new("Fioralta", "Toscana Interna"),
                    new("Gorgonza", "Norditalia"),
                    new("Ischiara", "Mezzogiorno"),
                    new("Lucento", "Mezzogiorno"),
                    new("Marzante", "Alpi"),
                    new("Norcetta", "Centroitalia"),
                    new("Orvietta", "Toscana Interna"),
                    new("Palmara", "Mezzogiorno"),
                    new("Querciano", "Toscana Interna"),
                    new("Rivalba", "Alpi"),
                    new("Sestara", "Norditalia"),
                    new("Torreno", "Mezzogiorno"),
                    new("Udento", "Norditalia"),
                    new("Valserra", "Alpi"),
                    new("Vermenta", "Toscana Interna"),
                ],
                [
                    "Calcio",
                    "Unione",
                    "Sportiva",
                    "Pro",
                    "Virtus",
                    "Libertas",
                    "Audace",
                    "Fortitudo",
                    "Concordia",
                    "Rinascita",
                    "Speranza",
                ],
                ["Nord", "Sud", "Est", "Ovest", "Centro", "Alto", "Basso", "Vecchia"]),

            ["france"] = new ClubNamePool(
                "france",
                [
                    new("Aubrelac", "Haute Vallée"),
                    new("Belleroche", "Côte Sud"),
                    new("Casterine", "Plaine Centrale"),
                    new("Durenval", "Haute Vallée"),
                    new("Essandres", "Côte Ouest"),
                    new("Fontarèche", "Côte Sud"),
                    new("Grandval", "Plaine Centrale"),
                    new("Hauteroche", "Haute Vallée"),
                    new("Isleverte", "Côte Ouest"),
                    new("Jonqueres", "Côte Sud"),
                    new("Lansac", "Plaine Centrale"),
                    new("Montserre", "Haute Vallée"),
                    new("Noyarelle", "Plaine Centrale"),
                    new("Ombreval", "Côte Ouest"),
                    new("Pierrelac", "Côte Sud"),
                    new("Ronceval", "Haute Vallée"),
                    new("Savernelle", "Plaine Centrale"),
                    new("Toulverne", "Côte Ouest"),
                    new("Valcreuse", "Côte Sud"),
                    new("Verchamp", "Haute Vallée"),
                ],
                [
                    "Union",
                    "Olympique",
                    "Étoile",
                    "Entente",
                    "Jeunesse",
                    "Avenir",
                    "Élan",
                    "Vaillante",
                    "Espérance",
                    "Cercle",
                    "Alerte",
                ],
                ["Nord", "Sud", "Est", "Ouest", "Centre", "Haut", "Bas", "Vieux"]),

            ["romania"] = new ClubNamePool(
                "romania",
                [
                    new("Ardelu", "Transilvania"),
                    new("Bălceana", "Muntenia"),
                    new("Corunești", "Oltenia"),
                    new("Drăgănei", "Muntenia"),
                    new("Eforina", "Dobrogea"),
                    new("Făgărel", "Transilvania"),
                    new("Guravei", "Moldova"),
                    new("Hârlești", "Moldova"),
                    new("Iazmina", "Dobrogea"),
                    new("Jurileni", "Dobrogea"),
                    new("Lăcuste", "Oltenia"),
                    new("Măguricea", "Muntenia"),
                    new("Neajlovia", "Muntenia"),
                    new("Oltișor", "Oltenia"),
                    new("Petroșeni", "Transilvania"),
                    new("Răzvanii", "Moldova"),
                    new("Sălcioara", "Oltenia"),
                    new("Tismănești", "Moldova"),
                    new("Urzicenița", "Muntenia"),
                    new("Vâlcelele", "Dobrogea"),
                ],
                [
                    "Sportiv",
                    "Vulturul",
                    "Stejarul",
                    "Speranța",
                    "Avântul",
                    "Flacăra",
                    "Zorii",
                    "Prietenii",
                    "Talentul",
                    "Bravura",
                    "Voința",
                ],
                ["Nord", "Sud", "Est", "Vest", "Central", "Mare", "Mic", "Vechi"]),
        };

    /// <summary>Gets every pool key, in a stable order.</summary>
    public static IReadOnlyCollection<string> Keys => Pools.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Finds the pool for a country's name-pool key.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when no pool exists for the key.</exception>
    public static ClubNamePool For(string namePoolKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePoolKey);

        return Pools.TryGetValue(namePoolKey, out var pool)
            ? pool
            : throw new ArgumentOutOfRangeException(
                nameof(namePoolKey),
                namePoolKey,
                $"No fictional name pool is registered for '{namePoolKey}' (FIC-4).");
    }
}
