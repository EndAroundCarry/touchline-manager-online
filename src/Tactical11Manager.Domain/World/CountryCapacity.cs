namespace Tactical11Manager.Domain.World;

/// <summary>
/// How full a country's pyramid is, measured across every active league (`WORLD-8`, `PYR-1`).
/// </summary>
/// <remarks>
/// <para>
/// New managers may take over a club in any of the country's active leagues, and the next league is created
/// once the free (AI-run) clubs fall to <see cref="Rules.WorldRuleSet.ExpansionFreeClubsPercent"/> per cent of
/// them, so "is there room in this country?" and "should the pyramid grow?" are questions about the whole
/// pyramid. Encoding them as a value object rather than as endpoint logic means the capacity answer, the
/// onboarding screen, and the expansion trigger all read the same numbers, and the whole thing is testable
/// without a database.
/// </para>
/// <para>
/// Occupancy counts tenures that are active <em>or</em> inactive (`OCC-8`). Counting only active
/// tenures would report a league as having room while a returning manager still holds a club in it, and
/// the country would provision a league it does not need.
/// </para>
/// </remarks>
/// <param name="CountryId">The country being measured.</param>
/// <param name="LowestActiveTier">The country's lowest active tier number, which the pyramid grows below.</param>
/// <param name="LowestActiveTierDivisionId">The division row of that tier's first league.</param>
/// <param name="ClubsInCountry">How many clubs the active leagues hold: <see cref="Rules.WorldRuleSet.ClubsPerDivision"/> for each complete league.</param>
/// <param name="HumanOccupiedClubs">How many of those clubs hold an open human tenure.</param>
/// <param name="Leagues">The tier and league number of every division the country has, whatever its status.</param>
public sealed record CountryCapacity(
    Guid CountryId,
    int LowestActiveTier,
    Guid LowestActiveTierDivisionId,
    int ClubsInCountry,
    int HumanOccupiedClubs,
    IReadOnlyList<(int Tier, int League)> Leagues)
{
    /// <summary>Gets how many clubs a manager could take over right now.</summary>
    public int AvailableClubs => Math.Max(0, ClubsInCountry - HumanOccupiedClubs);

    /// <summary>Gets a value indicating whether any club is claimable.</summary>
    public bool HasAvailableClubs => AvailableClubs > 0;

    /// <summary>
    /// Gets a value indicating whether every club of every active league is held by a human, which is the only
    /// state that refuses a claim (`PYR-2`).
    /// </summary>
    public bool CountryIsFull => ClubsInCountry > 0 && HumanOccupiedClubs >= ClubsInCountry;

    /// <summary>Gets the league that is created if the pyramid expands (`PYR-11`, `PYR-15`).</summary>
    public PyramidLeague TargetLeague => PyramidShape.NextLeague(Leagues);

    /// <summary>
    /// Gets a value indicating whether the free clubs have fallen to the expansion share or below (`PYR-2`), in
    /// integer arithmetic so the boundary is exact: 9 of 32 triggers, 10 does not.
    /// </summary>
    public bool NeedsExpansion =>
        ClubsInCountry > 0 && AvailableClubs * 100 <= ClubsInCountry * Rules.WorldRuleSet.ExpansionFreeClubsPercent;

    /// <summary>Builds the capacity of a country from its active tiers' numbers.</summary>
    /// <param name="countryId">The country.</param>
    /// <param name="lowestActiveTier">The lowest active tier number.</param>
    /// <param name="lowestActiveTierDivisionId">That tier's first league's division row.</param>
    /// <param name="clubsInCountry">How many clubs its active tiers hold.</param>
    /// <param name="humanOccupiedClubs">How many hold an open human tenure.</param>
    /// <param name="leagues">The tier and league number of every division the country has.</param>
    public static CountryCapacity Measure(
        Guid countryId,
        int lowestActiveTier,
        Guid lowestActiveTierDivisionId,
        int clubsInCountry,
        int humanOccupiedClubs,
        IReadOnlyList<(int Tier, int League)> leagues)
    {
        ArgumentNullException.ThrowIfNull(leagues);
        ArgumentOutOfRangeException.ThrowIfLessThan(lowestActiveTier, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(clubsInCountry);
        ArgumentOutOfRangeException.ThrowIfNegative(humanOccupiedClubs);

        return new CountryCapacity(
            countryId,
            lowestActiveTier,
            lowestActiveTierDivisionId,
            clubsInCountry,
            humanOccupiedClubs,
            leagues);
    }
}
