namespace TouchlineManager.Domain.World;

/// <summary>
/// How full a country's pyramid is, measured at its lowest active tier (`WORLD-8`, `PYR-1`).
/// </summary>
/// <remarks>
/// <para>
/// New managers may only take over a club in the country's <em>lowest active tier</em>, so "is there
/// room in this country?" is a question about exactly one tier. Encoding it as a value object rather
/// than as endpoint logic means the capacity answer, the onboarding screen, and the expansion trigger
/// all read the same numbers, and the whole thing is testable without a database.
/// </para>
/// <para>
/// Occupancy counts tenures that are active <em>or</em> inactive (`OCC-8`). Counting only active
/// tenures would report a tier as having room while a returning manager still holds a club in it, and
/// the country would provision a tier it does not need.
/// </para>
/// </remarks>
/// <param name="CountryId">The country being measured.</param>
/// <param name="LowestActiveTier">The tier number new managers may join.</param>
/// <param name="LowestActiveTierDivisionId">The division row for that tier.</param>
/// <param name="ClubsInLowestTier">How many clubs the tier holds. Always <see cref="Rules.WorldRuleSet.ClubsPerDivision"/> for a complete tier.</param>
/// <param name="HumanOccupiedClubs">How many of those clubs hold an open human tenure.</param>
public sealed record CountryCapacity(
    Guid CountryId,
    int LowestActiveTier,
    Guid LowestActiveTierDivisionId,
    int ClubsInLowestTier,
    int HumanOccupiedClubs)
{
    /// <summary>Gets how many clubs a manager could take over right now.</summary>
    public int AvailableClubs => Math.Max(0, ClubsInLowestTier - HumanOccupiedClubs);

    /// <summary>Gets a value indicating whether any club is claimable.</summary>
    public bool HasAvailableClubs => AvailableClubs > 0;

    /// <summary>
    /// Gets a value indicating whether every club in the lowest tier is held by a human, which is the
    /// trigger for creating the next tier (`PYR-2`).
    /// </summary>
    public bool LowestTierIsFull => ClubsInLowestTier > 0 && HumanOccupiedClubs >= ClubsInLowestTier;

    /// <summary>Gets the tier that would be created if the pyramid expands (`PYR-11`).</summary>
    public int TargetTierForExpansion => LowestActiveTier + 1;

    /// <summary>Gets a value indicating whether this country's onboarding is out of capacity.</summary>
    public bool NeedsExpansion => LowestTierIsFull;

    /// <summary>Builds the capacity of a country from its lowest tier's numbers.</summary>
    /// <param name="countryId">The country.</param>
    /// <param name="lowestActiveTier">The lowest active tier number.</param>
    /// <param name="lowestActiveTierDivisionId">That tier's division row.</param>
    /// <param name="clubsInLowestTier">How many clubs it holds.</param>
    /// <param name="humanOccupiedClubs">How many hold an open human tenure.</param>
    public static CountryCapacity Measure(
        Guid countryId,
        int lowestActiveTier,
        Guid lowestActiveTierDivisionId,
        int clubsInLowestTier,
        int humanOccupiedClubs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lowestActiveTier, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(clubsInLowestTier);
        ArgumentOutOfRangeException.ThrowIfNegative(humanOccupiedClubs);

        return new CountryCapacity(
            countryId,
            lowestActiveTier,
            lowestActiveTierDivisionId,
            clubsInLowestTier,
            humanOccupiedClubs);
    }
}
