namespace Tactical11Manager.Domain.Squad.Generation;

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
/// Every pool holds 36 given names and 37 surnames. Being coprime, the walk visits all 1,332 combinations once, which names
/// 55 squads of 24: both seeded tiers (32 clubs) and a third (48 clubs) without a repeated full name.
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
    public const string Version = "player-name-pools-v2";

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
                    "Alden", "Bryce", "Calder", "Darcy", "Emrys", "Fergus", "Garrick", "Heath", "Ivor", "Jasper",
                    "Kit", "Leofric", "Marlow", "Osmond", "Percival", "Rufus",
                ],
                [
                    "Alderwick", "Brambleby", "Cawthorne", "Dunmoor", "Eastgate", "Fairbrook", "Garrowby",
                    "Inglewood", "Jarrow", "Kestrelby", "Loxley", "Marwood", "Netherby", "Oakendale",
                    "Pemberly", "Quillan", "Ravenscroft", "Stanbury", "Thorncroft", "Wexford", "Yarrowby",
                    "Ashdown", "Blackthorn", "Caldbeck", "Denholm", "Elmsworth", "Fallowfield", "Hartwell",
                    "Kingsley", "Lowther", "Mortlake", "Northcott", "Prestwick", "Redfern", "Shaftoe", "Thistlewood",
                    "Wetherby",
                ]),
            ["spain"] = new(
                "spain",
                [
                    "Adrián", "Bautista", "Ciro", "Damián", "Elio", "Fabián", "Gorka", "Hugo", "Iñaki",
                    "Jacinto", "Lucero", "Mateo", "Nando", "Óscar", "Pablo", "Quirino", "Ramón", "Salvador",
                    "Teodoro", "Ulises",
                    "Álvaro", "Benito", "César", "Domingo", "Emilio", "Fermín", "Gonzalo", "Hilario", "Isidro",
                    "Joaquín", "Leandro", "Mauro", "Nicanor", "Octavio", "Rafael", "Severo",
                ],
                [
                    "Alvarado", "Benavente", "Cifuentes", "Delgado", "Escalante", "Fuentes", "Garrido",
                    "Herrera", "Ibáñez", "Jaramillo", "Linares", "Montoya", "Núñez", "Olmedo", "Peralta",
                    "Quintana", "Rivas", "Sandoval", "Toledo", "Urrutia", "Vergara",
                    "Aguirre", "Bermejo", "Calderón", "Dorado", "Echevarría", "Fajardo", "Galindo", "Heredia",
                    "Iturralde", "Lara", "Mondragón", "Navarrete", "Ordóñez", "Palacios", "Robledo", "Zubiaga",
                ]),
            ["germany"] = new(
                "germany",
                [
                    "Anselm", "Bernd", "Corvin", "Dietmar", "Emil", "Falk", "Gunnar", "Hendrik", "Ivo",
                    "Jonas", "Klaus", "Lennart", "Moritz", "Nils", "Otmar", "Pascal", "Quintus", "Reimund",
                    "Sören", "Ulf",
                    "Armin", "Berthold", "Conrad", "Detlef", "Eckhard", "Friedhelm", "Gerrit", "Holger", "Ingo",
                    "Joachim", "Konrad", "Lutz", "Matthias", "Norbert", "Ottfried", "Rüdiger",
                ],
                [
                    "Adlerbach", "Bergmann", "Colditz", "Dornberg", "Ebersbach", "Falkenrath", "Grünwald",
                    "Hartmann", "Immelmann", "Josten", "Kellenbach", "Lindqvist", "Morgenroth", "Neuhaus",
                    "Ostermann", "Pfeiffer", "Quastler", "Richter", "Steinbach", "Thalberg", "Ulrichsen",
                    "Auerbach", "Blumenthal", "Dreyer", "Eisenhardt", "Frohnhagen", "Gerlach", "Hollenbeck",
                    "Kirchhoff", "Lehnert", "Mauerhofer", "Nordmann", "Pachmann", "Rothfuss", "Sattler",
                    "Tiefenbach", "Wendelin",
                ]),
            ["italy"] = new(
                "italy",
                [
                    "Aldo", "Bruno", "Cesare", "Dario", "Elio", "Fabio", "Gennaro", "Ivo", "Luca", "Marco",
                    "Nino", "Orazio", "Pietro", "Quirino", "Renzo", "Salvatore", "Tullio", "Umberto",
                    "Vittorio", "Zeno",
                    "Amedeo", "Carmine", "Domenico", "Enrico", "Franco", "Giorgio", "Ignazio", "Lorenzo", "Massimo",
                    "Nicola", "Osvaldo", "Paolo", "Raffaele", "Sandro", "Tommaso", "Ettore",
                ],
                [
                    "Albertazzi", "Bellandi", "Corsini", "Della Valle", "Esposito", "Ferraro", "Gallo",
                    "Iacobelli", "Lombardi", "Mancuso", "Marchetti", "Nardini", "Orsini", "Piras",
                    "Quattrini", "Ricci", "Santoro", "Trevisan", "Urbano", "Vitale", "Zampieri",
                    "Angeloni", "Bortolotti", "Cattaneo", "Dessì", "Fabbri", "Giordani", "Lazzarini", "Moretti",
                    "Neri", "Pellegrini", "Rinaldi", "Sartori", "Tedesco", "Vannucci", "Villani", "Zorzi",
                ]),
            ["france"] = new(
                "france",
                [
                    "Alain", "Bertrand", "Cédric", "Damien", "Étienne", "Fabien", "Gaël", "Hugo", "Ivan",
                    "Julien", "Kévin", "Laurent", "Mathis", "Noé", "Olivier", "Pascal", "Quentin", "Rémi",
                    "Sylvain", "Thibault",
                    "Armand", "Baptiste", "Clément", "Dorian", "Émile", "Florian", "Gilles", "Hervé", "Jérémy",
                    "Lucien", "Maxime", "Nicolas", "Octave", "Régis", "Tristan", "Yves",
                ],
                [
                    "Aubertin", "Bellanger", "Chevalier", "Dufresne", "Escoffier", "Fontaine", "Gascon",
                    "Hachette", "Imbert", "Joubert", "Lachapelle", "Mercier", "Nadeau", "Ollivier", "Perrin",
                    "Quémeneur", "Rousseau", "Sabatier", "Tessier", "Vasseur", "Wexler",
                    "Barbier", "Colbert", "Delmas", "Fabre", "Guérin", "Hamelin", "Lefort", "Marchand", "Noiret",
                    "Pelletier", "Rivière", "Soulier", "Tardieu", "Valette", "Verdier", "Brisset",
                ]),
            ["romania"] = new(
                "romania",
                [
                    "Alexandru", "Bogdan", "Cătălin", "Darius", "Emil", "Florin", "Gheorghe", "Horia",
                    "Ionuț", "Lucian", "Mihai", "Nicolae", "Octavian", "Petru", "Radu", "Sorin", "Tiberiu",
                    "Vasile", "Vlad", "Zoltan",
                    "Andrei", "Călin", "Dorin", "Eugen", "Fănel", "Gabriel", "Ilie", "Liviu", "Marius", "Narcis",
                    "Ovidiu", "Remus", "Silviu", "Teodor", "Viorel", "Costel",
                ],
                [
                    "Albu", "Bălan", "Cazacu", "Dobrescu", "Ene", "Florea", "Goga", "Hanganu", "Ionescu",
                    "Jianu", "Lupu", "Munteanu", "Nistor", "Oprea", "Popescu", "Radulescu", "Stanescu",
                    "Tudor", "Ungureanu", "Voicu", "Zamfir",
                    "Anghel", "Barbu", "Costache", "Dumitru", "Filip", "Grigore", "Iliescu", "Matei", "Neagu",
                    "Păun", "Rusu", "Sandu", "Toma", "Văduva", "Zaharia", "Dragomir",
                ]),

            ["belgium"] = new(
                "belgium",
                [
                    "Arne", "Bram", "Cedric", "Dries", "Egon", "Fons", "Gerrit", "Hannes", "Ivo", "Jens",
                    "Koen", "Lowie", "Maarten", "Niels", "Olivier", "Pieter", "Quinten", "Robbe", "Stijn",
                    "Thibe", "Ulrik", "Viktor", "Wouter", "Xander", "Yannick", "Zeno", "Joren", "Tijs",
                    "Sander", "Warre",
                    "Aron", "Bert", "Dimitri", "Emiel", "Gilles", "Hugo",
                ],
                [
                    "Aerts", "Baeten", "Claeys", "Desmet", "De Cock", "Dewaele", "Goossens", "Hendrickx",
                    "Heylen", "Jacobs", "Lambrecht", "Lenaerts", "Maes", "Nijs", "Opsomer", "Pauwels",
                    "Peeters", "Rombouts", "Segers", "Smeets", "Stevens", "Thijs", "Uyttenhove", "Van Acker",
                    "Vandamme", "Verbeke", "Vermeulen", "Willems", "Wouters", "Bosmans", "Cools",
                    "Declerck", "Gevers", "Hermans", "Maertens", "Vanhecke", "Cuyvers",
                ]),
            ["portugal"] = new(
                "portugal",
                [
                    "Afonso", "Bernardo", "Caetano", "Duarte", "Eduardo", "Filipe", "Gaspar", "Henrique",
                    "Ivo", "Jorge", "Leandro", "Martim", "Nuno", "Otávio", "Paulo", "Quim", "Rafael", "Simão",
                    "Tomé", "Ulisses", "Vasco", "Xavier", "Zacarias", "Diogo", "Rúben", "Gonçalo", "Matias",
                    "Salvador", "Rodrigo", "Tiago",
                    "Abel", "Baltasar", "Cláudio", "Délio", "Fábio", "Heitor",
                ],
                [
                    "Abrantes", "Barroso", "Cardoso", "Dantas", "Esteves", "Faria", "Gouveia", "Henriques",
                    "Lacerda", "Macedo", "Nogueira", "Pimentel", "Queirós", "Ribeiro", "Sampaio", "Teixeira",
                    "Ventura", "Amaral", "Borges", "Coutinho", "Furtado", "Loureiro", "Mourão", "Novais",
                    "Pestana", "Rebelo", "Salgado", "Tavares", "Vilar", "Albuquerque", "Brandão",
                    "Cabral", "Fontes", "Lopes", "Morais", "Prates", "Seixas",
                ]),
            ["netherlands"] = new(
                "netherlands",
                [
                    "Aart", "Bas", "Daan", "Evert", "Floris", "Gijs", "Hidde", "Ivar", "Jelle", "Kees",
                    "Luuk", "Mees", "Niek", "Olaf", "Pim", "Quirijn", "Rens", "Sjoerd", "Teun", "Ulbe",
                    "Wessel", "Yorick", "Zeger", "Bram", "Dirk", "Joost", "Lars", "Menno", "Thijmen",
                    "Willem",
                    "Anton", "Berend", "Cornelis", "Gerben", "Harm", "Pieter",
                ],
                [
                    "Akkerman", "Bakker", "Brouwer", "Dekker", "Evers", "Fokkema", "Gerritsen", "Hoekstra",
                    "IJzerman", "Jansma", "Kuipers", "Leenders", "Mulder", "Nieuwland", "Oosterhuis",
                    "Postma", "Quist", "Roelofs", "Schouten", "Timmer", "Uittenbogaard", "Veldkamp",
                    "Wolters", "Zwart", "Bosman", "De Wit", "Van Ommen", "Kramer", "Haverkamp", "Prins",
                    "Sluiter",
                    "Dijkman", "Groen", "Hermsen", "Lammers", "Smit", "Vink",
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
