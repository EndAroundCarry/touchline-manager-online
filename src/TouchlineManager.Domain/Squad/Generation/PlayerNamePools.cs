namespace TouchlineManager.Domain.Squad.Generation;

/// <summary>
/// The curated given names and surnames one locale generates players from (`FIC-4`).
/// </summary>
/// <remarks>
/// <para>
/// Pool sizes are chosen to be coprime and larger than a squad, so a club's consecutive player ordinals
/// visit distinct <c>(given, surname)</c> pairs and a twenty-two-man squad has no duplicate full name by
/// construction — the same argument <c>ClubNamePool.CombinationsPerCycle</c> makes for club names, and
/// the reason neither pool needs a retry loop.
/// </para>
/// <para>
/// A pool is a versioned artifact reviewed like code, exactly as the club name pools are: every entry is
/// invented, none is scraped from a real squad or registry, and the whole set is checked against
/// <c>FictionalIdentityBlocklist</c> by the generator's tests (`FIC-5`).
/// </para>
/// </remarks>
/// <param name="Key">The pool key, matching a launch country's <c>NamePoolKey</c>.</param>
/// <param name="GivenNames">The invented given names.</param>
/// <param name="Surnames">The invented surnames.</param>
public sealed record PlayerNamePool(
    string Key,
    IReadOnlyList<string> GivenNames,
    IReadOnlyList<string> Surnames);

/// <summary>
/// The versioned per-locale player-name dictionaries (`FIC-4`, `FIC-8`).
/// </summary>
public static class PlayerNamePools
{
    /// <summary>
    /// The name-pool version. Bump it whenever a pool changes, exactly as the club pools require
    /// (`FIC-8`); it is folded into every generation run's input hash.
    /// </summary>
    public const string Version = "player-name-pools-v1";

    /// <summary>Every pool, keyed by the locale key shared with the club name pools.</summary>
    public static readonly IReadOnlyDictionary<string, PlayerNamePool> Pools =
        new Dictionary<string, PlayerNamePool>(StringComparer.Ordinal)
        {
            ["england"] = new(
                "england",
                [
                    "Alaric", "Bramwell", "Corin", "Dexter", "Ewan", "Finlay", "Grady", "Hollis", "Idris",
                    "Jarvis", "Kellan", "Lorcan", "Merrick", "Nolan", "Orson", "Piers", "Quentin", "Rowan",
                    "Silas", "Tobin",
                ],
                [
                    "Alderwick", "Brambleby", "Cawthorne", "Dunmoor", "Eastgate", "Fairbrook", "Garrowby",
                    "Inglewood", "Jarrow", "Kestrelby", "Loxley", "Marwood", "Netherby", "Oakendale",
                    "Pemberly", "Quillan", "Ravenscroft", "Stanbury", "Thorncroft", "Wexford", "Yarrowby",
                ]),
            ["spain"] = new(
                "spain",
                [
                    "Adrián", "Bautista", "Ciro", "Damián", "Elio", "Fabián", "Gorka", "Hugo", "Iñaki",
                    "Jacinto", "Lucero", "Mateo", "Nando", "Óscar", "Pablo", "Quirino", "Ramón", "Salvador",
                    "Teodoro", "Ulises",
                ],
                [
                    "Alvarado", "Benavente", "Cifuentes", "Delgado", "Escalante", "Fuentes", "Garrido",
                    "Herrera", "Ibáñez", "Jaramillo", "Linares", "Montoya", "Núñez", "Olmedo", "Peralta",
                    "Quintana", "Rivas", "Sandoval", "Toledo", "Urrutia", "Vergara",
                ]),
            ["germany"] = new(
                "germany",
                [
                    "Anselm", "Bernd", "Corvin", "Dietmar", "Emil", "Falk", "Gunnar", "Hendrik", "Ivo",
                    "Jonas", "Klaus", "Lennart", "Moritz", "Nils", "Otmar", "Pascal", "Quintus", "Reimund",
                    "Sören", "Ulf",
                ],
                [
                    "Adlerbach", "Bergmann", "Colditz", "Dornberg", "Ebersbach", "Falkenrath", "Grünwald",
                    "Hartmann", "Immelmann", "Josten", "Kellenbach", "Lindqvist", "Morgenroth", "Neuhaus",
                    "Ostermann", "Pfeiffer", "Quastler", "Richter", "Steinbach", "Thalberg", "Ulrichsen",
                ]),
            ["italy"] = new(
                "italy",
                [
                    "Aldo", "Bruno", "Cesare", "Dario", "Elio", "Fabio", "Gennaro", "Ivo", "Luca", "Marco",
                    "Nino", "Orazio", "Pietro", "Quirino", "Renzo", "Salvatore", "Tullio", "Umberto",
                    "Vittorio", "Zeno",
                ],
                [
                    "Albertazzi", "Bellandi", "Corsini", "Della Valle", "Esposito", "Ferraro", "Gallo",
                    "Iacobelli", "Lombardi", "Mancuso", "Marchetti", "Nardini", "Orsini", "Piras",
                    "Quattrini", "Ricci", "Santoro", "Trevisan", "Urbano", "Vitale", "Zampieri",
                ]),
            ["france"] = new(
                "france",
                [
                    "Alain", "Bertrand", "Cédric", "Damien", "Étienne", "Fabien", "Gaël", "Hugo", "Ivan",
                    "Julien", "Kévin", "Laurent", "Mathis", "Noé", "Olivier", "Pascal", "Quentin", "Rémi",
                    "Sylvain", "Thibault",
                ],
                [
                    "Aubertin", "Bellanger", "Chevalier", "Dufresne", "Escoffier", "Fontaine", "Gascon",
                    "Hachette", "Imbert", "Joubert", "Lachapelle", "Mercier", "Nadeau", "Ollivier", "Perrin",
                    "Quémeneur", "Rousseau", "Sabatier", "Thibault", "Vasseur", "Wexler",
                ]),
            ["romania"] = new(
                "romania",
                [
                    "Alexandru", "Bogdan", "Cătălin", "Darius", "Emil", "Florin", "Gheorghe", "Horia",
                    "Ionuț", "Lucian", "Mihai", "Nicolae", "Octavian", "Petru", "Radu", "Sorin", "Tiberiu",
                    "Vasile", "Vlad", "Zoltan",
                ],
                [
                    "Albu", "Bălan", "Cazacu", "Dobrescu", "Ene", "Florea", "Goga", "Hanganu", "Ionescu",
                    "Jianu", "Lupu", "Munteanu", "Nistor", "Oprea", "Popescu", "Radulescu", "Stanescu",
                    "Tudor", "Ungureanu", "Voicu", "Zamfir",
                ]),
        };

    /// <summary>Gets every pool key, in a stable order.</summary>
    public static IReadOnlyCollection<string> Keys => [.. Pools.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Gets whether a pool exists for a locale key.</summary>
    /// <param name="key">The locale key.</param>
    public static bool Contains(string key) => Pools.ContainsKey(key);

    /// <summary>Gets the pool for a locale key.</summary>
    /// <param name="key">The locale key.</param>
    public static PlayerNamePool For(string key) =>
        Pools.TryGetValue(key, out var pool)
            ? pool
            : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown player name pool.");
}
