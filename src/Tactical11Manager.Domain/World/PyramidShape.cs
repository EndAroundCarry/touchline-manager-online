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
/// so the creation order is 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 4.1 and so on.
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

    /// <summary>How many leagues a tier holds when it is full: 2^(tier-1).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int LeaguesAllowed(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, MaximumTier);

        return 1 << (tier - 1);
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
    /// The league the pyramid creates next.
    /// </summary>
    /// <remarks>
    /// Every division counts, whatever its status: a league that is still provisioning already holds its slot, so two
    /// requests can never aim at the same league.
    /// </remarks>
    /// <param name="existing">The tier and league number of every division the country has.</param>
    public static PyramidLeague NextLeague(IReadOnlyList<(int Tier, int League)> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.Count == 0)
        {
            return new PyramidLeague(1, 1, null);
        }

        var lowestTier = existing.Max(division => division.Tier);
        var lastLeague = existing.Where(division => division.Tier == lowestTier).Max(division => division.League);

        return lastLeague < LeaguesAllowed(lowestTier)
            ? LeagueOf(lowestTier, lastLeague + 1)
            : LeagueOf(lowestTier + 1, 1);
    }

    private static PyramidLeague LeagueOf(int tier, int league) =>
        new(tier, league, tier == 1 ? null : ParentLeagueOf(tier, league));
}
