using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Abstractions.Finance;

/// <summary>
/// Persistence for the sponsor deals clubs sign and the standing steps a season's end writes (`SPN-2`, `STD-3`).
/// </summary>
/// <remarks>
/// A staging port like the rest of finance: it adds rows to the current unit of work and never saves, so a deal's settlement and the standing
/// step commit with the rollover phase that produced them.
/// </remarks>
public interface ISponsorRepository
{
    /// <summary>Loads the deals clubs have signed for a season, tracked, so a settlement can write their outcome.</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="clubIds">The clubs whose deals are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deals that exist, in no particular order.</returns>
    Task<IReadOnlyList<ClubSponsorDeal>> LoadDealsAsync(
        Guid seasonId,
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Finds the deal a club signed for a season, tracked.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deal, or null when the club has not signed.</returns>
    Task<ClubSponsorDeal?> FindDealAsync(Guid clubId, Guid seasonId, CancellationToken cancellationToken);

    /// <summary>Lists the offers a club was made for a season, steady first.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClubSponsorOffer>> ListOffersAsync(Guid clubId, Guid seasonId, CancellationToken cancellationToken);

    /// <summary>Stages the offers made to a club.</summary>
    /// <param name="offers">The offers.</param>
    void AddOffers(IEnumerable<ClubSponsorOffer> offers);

    /// <summary>Stages a signed deal.</summary>
    /// <param name="deal">The deal.</param>
    void AddDeal(ClubSponsorDeal deal);

    /// <summary>Stages one standing step.</summary>
    /// <param name="standingEvent">The step.</param>
    void AddStandingEvent(ClubStandingEvent standingEvent);
}
