using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Abstractions.Market;

/// <summary>One registered player as the AI market evaluation reads them (`TRF-12`).</summary>
/// <remarks>
/// The AI reads what any club can see: skill, age and wage. Hidden potential is not an input (`player-valuation-v2`), so a gem is under-priced
/// and a flop over-priced exactly as they are for a manager.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="Family">The position family the player belongs to.</param>
/// <param name="AbilityTenths">The mean of the player's attributes in tenths of a point, so 115 is 11.5.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="SquadStatus">The player's standing in the squad (`SQ-1`).</param>
/// <param name="ContractEndSeasonNumber">The last season the active contract covers.</param>
/// <param name="SeasonWageMinor">The wage on the active contract for a season.</param>
/// <param name="IsListed">Whether the player already has an open listing (`TRF-14`).</param>
public sealed record AiMarketPlayerRow(
    Guid PlayerId,
    PositionFamily Family,
    int AbilityTenths,
    int Age,
    SquadStatus SquadStatus,
    int ContractEndSeasonNumber,
    long SeasonWageMinor,
    bool IsListed);

/// <summary>One AI-controlled club as the market evaluation reads it (`TRF-12`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="SpendableMinor">Cash available after existing reservations (`FIN-10`).</param>
/// <param name="CurrentSeasonNumber">The season being played, so a contract's remaining term can be read.</param>
/// <param name="FinancialState">The state of the club's money: a club in the red or in administration bids on nothing (`DEBT-3`).</param>
/// <param name="LeadingBidWagesMinor">The season wages of the players the club leads the bidding on, which join its wage bill the moment the bid wins.</param>
/// <param name="Players">The club's registered squad.</param>
public sealed record AiMarketClubRow(
    Guid ClubId,
    long SpendableMinor,
    int CurrentSeasonNumber,
    FinancialStateKind FinancialState,
    long LeadingBidWagesMinor,
    IReadOnlyList<AiMarketPlayerRow> Players);

/// <summary>One open listing as the market evaluation reads it (`TRF-4`, `TRF-8`).</summary>
/// <param name="ListingId">The listing.</param>
/// <param name="SellerClubId">The selling club.</param>
/// <param name="LeadingClubId">The club currently leading, or null, so a club never raises itself.</param>
/// <param name="PlayerId">The listed player.</param>
/// <param name="Family">The listed player's position family.</param>
/// <param name="AbilityTenths">The listed player's skill in tenths of a point.</param>
/// <param name="Age">The listed player's age in game years.</param>
/// <param name="MinimumFeeMinor">The seller's minimum fee, in minor units.</param>
/// <param name="LeadingAmountMinor">The current leading amount, in minor units, or null.</param>
/// <param name="BuyerSeasonWageMinor">The wage for a season the listing quotes the buyer.</param>
public sealed record AiMarketListingRow(
    Guid ListingId,
    Guid SellerClubId,
    Guid? LeadingClubId,
    Guid PlayerId,
    PositionFamily Family,
    int AbilityTenths,
    int Age,
    long MinimumFeeMinor,
    long? LeadingAmountMinor,
    long BuyerSeasonWageMinor);

/// <summary>
/// The AI market evaluation's read (`TRF-12`, `MOD-3`).
/// </summary>
/// <remarks>
/// Flat projections in a fixed number of queries, the same shape the AI club evaluation uses: the clubs
/// nobody holds with their squads and their spendable cash, and the open market. Nothing here returns a
/// tracked graph and nothing mutates; the policy decides, and the shared writers carry the decision out.
/// </remarks>
public interface IAiMarketRepository
{
    /// <summary>Loads every club no human holds, with its squad and spendable cash.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AiMarketClubRow>> LoadAiClubsAsync(CancellationToken cancellationToken);

    /// <summary>Loads every listing that was already open before an instant, with the facts a bidder weighs.</summary>
    /// <param name="before">
    /// The exclusivity bound: a listing opened at or after it is not returned. The evaluation passes the start
    /// of the current UTC day, so a pass is a function of the day rather than of the instant it happens to run,
    /// and a retried pass bids on the same listings and nothing else.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AiMarketListingRow>> LoadOpenListingsAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken);
}

/// <summary>Persistence for the AI market decision record (`TRF-12`, master plan §6.7).</summary>
public interface IAiMarketDecisionRepository
{
    /// <summary>Stages a decision row.</summary>
    /// <param name="decision">The decision to record.</param>
    void Add(AiMarketDecision decision);
}
