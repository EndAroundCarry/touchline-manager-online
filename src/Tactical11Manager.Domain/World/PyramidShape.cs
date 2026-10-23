namespace Tactical11Manager.Domain.World;

/// <summary>
/// One league of a country's pyramid, named by its tier and its number within the tier (the "2.1" of "Division 2.1").
/// </summary>
/// <param name="Tier">The tier number, starting at 1.</param>
/// <param name="League">The league's number within its tier, starting at 1.</param>
/// <param name="ParentLeague">The league number in the tier above that this league feeds, or <see langword="null"/> for the top division.</param>
public sealed record PyramidLeague(int Tier, int League, int? ParentLeague);

/// <summary>
/// The shape of a country's pyramid: how many leagues each tier holds, which league is created next, and which league
/// each one feeds (`PYR-15`).
/// </summary>
/// <remarks>
/// <para>
/// Tier 1 holds one league and every tier below holds twice as many as the one above: 1, 2, 4, 8 and so on. A new league
/// goes into the lowest tier while that tier has room, and a tier only opens once the tier above holds all its leagues,
/// so the creation order is 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 4.1 and so on. Until the promotion play-off covers a league with two
/// feeders, <see cref="OneLeaguePerTier"/> holds the second league of every tier back, and the order is 2.1, 3.1, 4.1.
/// </para>
/// <para>
/// League <c>k.i</c> feeds league <c>(k-1).(((i-1) mod 2^(k-2)) + 1)</c>, which gives every league in a tier its first
/// feeder before any league gets a second one. Both rules are pure arithmetic, so the capacity answer, the provisioning
/// worker and the rollover all read the same shape without a database.
/// </para>
/// </remarks>
public static class PyramidShape
{
    /// <summary>The deepest tier the arithmetic supports, so <see cref="LeaguesAllowed"/> fits an <see cref="int"/>.</summary>
    public const int MaximumTier = 30;

    /// <summary>
    /// Temporary hold: a tier never gets a second league until the promotion play-off covers a league with two feeders.
    /// A country that needs room opens the next tier down (3.1) instead of 2.2. Growing-pyramid milestone 4 removes this
    /// and then fills the highest tier that has room first, so 2.2 comes before 3.2.
    /// </summary>
    public const bool OneLeaguePerTier = true;

    /// <summary>How many leagues a tier holds when it is full: 2^(tier-1).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int LeaguesAllowed(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, MaximumTier);

        return 1 << (tier - 1);
    }

    /// <summary>
    /// The league's position among every league of its country, counted from the top division: 0 for the top division, 1 and
    /// 2 for 2.1 and 2.2, 3 to 6 for 3.1 to 3.4 (`PYR-15`).
    /// </summary>
    /// <remarks>
    /// Club identities and squads are numbered by this, times the clubs per league, so no two leagues of a country share a
    /// club name. For the top division and 2.1 it equals the old per-tier ordinal, so their names and squads do not change.
    /// </remarks>
    /// <param name="tier">The league's tier.</param>
    /// <param name="league">The league's number within its tier.</param>
    public static int LeagueOrdinal(int tier, int league)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(league, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(league, LeaguesAllowed(tier));

        return (LeaguesAllowed(tier) - 1) + (league - 1);
    }

    /// <summary>The league of the tier above that a league feeds.</summary>
    /// <param name="tier">The league's tier, at least 2.</param>
    /// <param name="league">The league's number within its tier.</param>
    public static int ParentLeagueOf(int tier, int league)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(league, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(league, LeaguesAllowed(tier));

        return ((league - 1) % LeaguesAllowed(tier - 1)) + 1;
    }

    /// <summary>
    /// The league the pyramid creates next, while <see cref="OneLeaguePerTier"/> holds.
    /// </summary>
    /// <remarks>
    /// Every division counts, whatever its status: a league that is still provisioning already holds its slot, so two
    /// requests can never aim at the same league.
    /// </remarks>
    /// <param name="existing">The tier and league number of every division the country has.</param>
    public static PyramidLeague NextLeague(IReadOnlyList<(int Tier, int League)> existing) =>
        NextLeague(existing, OneLeaguePerTier);

    /// <summary>
    /// The league the pyramid creates next, with the one-league-per-tier hold given explicitly.
    /// </summary>
    /// <remarks>
    /// With the hold on, a country that has a league in its lowest tier opens the tier below instead of a second league
    /// beside it, so the creation order is 2.1, 3.1, 4.1. League numbers still come from the full shape, so a 2.2 made
    /// once the hold is lifted keeps the names and squads <see cref="LeagueOrdinal"/> gives it.
    /// </remarks>
    /// <param name="existing">The tier and league number of every division the country has.</param>
    /// <param name="oneLeaguePerTier">Whether a tier never gets a second league.</param>
    public static PyramidLeague NextLeague(IReadOnlyList<(int Tier, int League)> existing, bool oneLeaguePerTier)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.Count == 0)
        {
            return new PyramidLeague(1, 1, null);
        }

        var lowestTier = existing.Max(division => division.Tier);
        var lastLeague = existing.Where(division => division.Tier == lowestTier).Max(division => division.League);

        return !oneLeaguePerTier && lastLeague < LeaguesAllowed(lowestTier)
            ? LeagueOf(lowestTier, lastLeague + 1)
            : LeagueOf(lowestTier + 1, 1);
    }

    private static PyramidLeague LeagueOf(int tier, int league) =>
        new(tier, league, tier == 1 ? null : ParentLeagueOf(tier, league));
}
