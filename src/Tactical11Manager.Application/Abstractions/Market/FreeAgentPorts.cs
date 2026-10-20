using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Application.Abstractions.Market;

/// <summary>One country as the weekly free-agent batch reads it (`FA-1`, `FA-2`, `FA-6`).</summary>
/// <param name="CountryCode">The country code, which is also a generated player's nationality.</param>
/// <param name="NamePoolKey">The country's name pool key.</param>
/// <param name="ContractedPlayers">The players under active contract in the country's clubs.</param>
/// <param name="ReferenceTier">The country's lowest active tier, which prices its free agents (`FA-6`).</param>
public sealed record FreeAgentCountryFacts(
    string CountryCode,
    string NamePoolKey,
    int ContractedPlayers,
    int ReferenceTier);

/// <summary>The world facts a weekly batch is generated from.</summary>
/// <param name="WorldId">The world.</param>
/// <param name="WorldSeed">The world's generation seed (`PYR-14`).</param>
/// <param name="GameYear">The current season's game year, which fixes ages.</param>
/// <param name="ReleasedSince">
/// The earliest contract close the first batch picks up (`FA-3`): when the previous season ended, so the
/// rollover that opened the current season counts and older leftovers do not, or when the world began.
/// </param>
/// <param name="Countries">The countries that have players under contract, in presentation order.</param>
public sealed record FreeAgentWorldFacts(
    Guid WorldId,
    string WorldSeed,
    int GameYear,
    DateTimeOffset ReleasedSince,
    IReadOnlyList<FreeAgentCountryFacts> Countries);

/// <summary>A released player waiting to join a batch (`FA-3`).</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Ability">The ability he shows: the rounded mean of his attributes.</param>
/// <param name="Potential">His hidden potential. It prices his fee, as a club listing's is, and is never shown.</param>
/// <param name="Age">His age in game years.</param>
/// <param name="NationalityCode">His nationality, which picks the reference tier he is priced at.</param>
public sealed record ReleasedFreeAgentRow(Guid PlayerId, int Ability, int Potential, int Age, string NationalityCode);

/// <summary>Persistence for the weekly free-agent batch record (`FA-5`).</summary>
public interface IFreeAgentBatchRepository
{
    /// <summary>Stages a new batch.</summary>
    /// <param name="batch">The batch.</param>
    void Add(FreeAgentBatch batch);

    /// <summary>Finds the batch opened for a week, or null.</summary>
    /// <param name="weekStart">The Monday the batch opens on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FreeAgentBatch?> FindByWeekAsync(DateOnly weekStart, CancellationToken cancellationToken);

    /// <summary>Finds the most recently created batch before a week, or null when there is none.</summary>
    /// <param name="weekStart">The week being opened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FreeAgentBatch?> FindPreviousAsync(DateOnly weekStart, CancellationToken cancellationToken);
}

/// <summary>The reads the weekly free-agent batch is built from (`FA-1`, `FA-3`).</summary>
public interface IFreeAgentQueries
{
    /// <summary>Reads the world facts the batch is sized and generated from, or null when no world exists.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FreeAgentWorldFacts?> GetWorldFactsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the free agents whose last contract closed as expired or released at or after an instant, who have
    /// no open listing and are not already in a batch (`FA-3`).
    /// </summary>
    /// <param name="since">The earliest contract close that counts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ReleasedFreeAgentRow>> ListReleasedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);
}
