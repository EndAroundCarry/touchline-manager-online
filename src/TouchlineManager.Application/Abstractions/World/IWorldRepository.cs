using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Persistence for the world module's own aggregates and the competition shell it creates them in.
/// </summary>
/// <remarks>
/// <para>
/// The world and the competition shell share one port because they are written together: seeding a world
/// creates its countries, its first season, one division per country, that division's season instance,
/// and the club entries that place clubs in it. Split across five ports, those five writes would still
/// have to happen in one unit of work, and the composition root would be the only place where that was
/// visible.
/// </para>
/// <para>
/// Reads that feed a screen rather than a decision live on <c>IOnboardingQueries</c> instead, so a query
/// shape can change without widening what a command can reach.
/// </para>
/// </remarks>
public interface IWorldRepository
{
    /// <summary>
    /// Finds the persistent world.
    /// </summary>
    /// <remarks>
    /// Production runs exactly one world (`WORLD-1`), so this is a read of the only row rather than a
    /// lookup by identity.
    /// </remarks>
    Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken);

    /// <summary>Stages a new world.</summary>
    void AddWorld(GameWorld world);

    /// <summary>Finds a season by its ordinal within a world.</summary>
    Task<Season?> FindSeasonAsync(Guid worldId, int sequenceNumber, CancellationToken cancellationToken);

    /// <summary>Finds a season by identity.</summary>
    Task<Season?> FindSeasonByIdAsync(Guid seasonId, CancellationToken cancellationToken);

    /// <summary>Stages a new season.</summary>
    void AddSeason(Season season);

    /// <summary>Lists a world's countries in presentation order.</summary>
    Task<IReadOnlyList<Country>> ListCountriesAsync(Guid worldId, CancellationToken cancellationToken);

    /// <summary>Finds a country by identity.</summary>
    Task<Country?> FindCountryAsync(Guid countryId, CancellationToken cancellationToken);

    /// <summary>Stages a new country.</summary>
    void AddCountry(Country country);

    /// <summary>
    /// Finds the country's lowest active tier, which is the only tier a new manager may join (`WORLD-8`).
    /// </summary>
    Task<Division?> FindLowestActiveDivisionAsync(Guid countryId, CancellationToken cancellationToken);

    /// <summary>Finds a division by identity.</summary>
    Task<Division?> FindDivisionAsync(Guid divisionId, CancellationToken cancellationToken);

    /// <summary>Stages a new division.</summary>
    void AddDivision(Division division);

    /// <summary>Finds a division's instance in a season.</summary>
    Task<DivisionSeason?> FindDivisionSeasonAsync(
        Guid divisionId,
        Guid seasonId,
        CancellationToken cancellationToken);

    /// <summary>Stages a division-season.</summary>
    void AddDivisionSeason(DivisionSeason divisionSeason);

    /// <summary>Stages a club's membership of a division-season. Immutable history (`PR-6`).</summary>
    void AddClubSeasonEntry(ClubSeasonEntry entry);
}
