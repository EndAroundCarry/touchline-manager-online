namespace Tactical11Manager.Domain.World;

/// <summary>
/// How full a country's pyramid is, measured across every active tier (`WORLD-8`, `PYR-1`).
/// </summary>
/// <remarks>
/// <para>
/// New managers may take over a club in any of the country's active tiers, and the next tier is only
/// created when all of them are full, so "is there room in this country?" is one question about the
/// whole pyramid. Encoding it as a value object rather than as endpoint logic means the capacity
/// answer, the onboarding screen, and the expansion trigger all read the same numbers, and the whole
/// thing is testable without a database.
/// </para>
/// <para>
/// Occupancy counts tenures that are active <em>or</em> inactive (`OCC-8`). Counting only active
/// tenures would report a tier as having room while a returning manager still holds a club in it, and
/// the country would provision a tier it does not need.
/// </para>
/// </remarks>
/// <param name="CountryId">The country being measured.</param>
/// <param name="LowestActiveTier">The country's lowest active tier number, which the pyramid grows below.</param>
/// <param name="LowestActiveTierDivisionId">The division row for that tier.</param>
/// <param name="ClubsInCountry">How many clubs the active tiers hold: <see cref="Rules.WorldRuleSet.ClubsPerDivision"/> for each complete tier.</param>
/// <param name="HumanOccupiedClubs">How many of those clubs hold an open human tenure.</param>
public sealed record CountryCapacity(
    Guid CountryId,
    int LowestActiveTier,
    Guid LowestActiveTierDivisionId,
    int ClubsInCountry,
    int HumanOccupiedClubs)
{
    /// <summary>Gets how many clubs a manager could take over right now.</summary>
    public int AvailableClubs => Math.Max(0, ClubsInCountry - HumanOccupiedClubs);

    /// <summary>Gets a value indicating whether any club is claimable.</summary>
    public bool HasAvailableClubs => AvailableClubs > 0;

    /// <summary>
    /// Gets a value indicating whether every club of every active tier is held by a human, which is the
    /// trigger for creating the next tier (`PYR-2`).
    /// </summary>
    public bool CountryIsFull => ClubsInCountry > 0 && HumanOccupiedClubs >= ClubsInCountry;

    /// <summary>Gets the tier that would be created if the pyramid expands (`PYR-11`).</summary>
    public int TargetTierForExpansion => LowestActiveTier + 1;

    /// <summary>Gets a value indicating whether this country's onboarding is out of capacity.</summary>
    public bool NeedsExpansion => CountryIsFull;

    /// <summary>Builds the capacity of a country from its active tiers' numbers.</summary>
    /// <param name="countryId">The country.</param>
    /// <param name="lowestActiveTier">The lowest active tier number.</param>
    /// <param name="lowestActiveTierDivisionId">That tier's division row.</param>
    /// <param name="clubsInCountry">How many clubs its active tiers hold.</param>
    /// <param name="humanOccupiedClubs">How many hold an open human tenure.</param>
    public static CountryCapacity Measure(
        Guid countryId,
        int lowestActiveTier,
        Guid lowestActiveTierDivisionId,
        int clubsInCountry,
        int humanOccupiedClubs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lowestActiveTier, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(clubsInCountry);
        ArgumentOutOfRangeException.ThrowIfNegative(humanOccupiedClubs);

        return new CountryCapacity(
            countryId,
            lowestActiveTier,
            lowestActiveTierDivisionId,
            clubsInCountry,
            humanOccupiedClubs);
    }
}
