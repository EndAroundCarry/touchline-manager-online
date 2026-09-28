using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Market;

/// <summary>Persistence for a manager's private shortlist (`SCT-3`).</summary>
public interface IShortlistRepository
{
    /// <summary>Stages a new shortlist entry.</summary>
    void Add(ShortlistEntry entry);

    /// <summary>Finds a manager's entry for a player, tracked, or null.</summary>
    /// <param name="managerId">The manager.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ShortlistEntry?> FindAsync(Guid managerId, Guid playerId, CancellationToken cancellationToken);

    /// <summary>Removes a shortlist entry.</summary>
    /// <param name="entry">The entry to remove.</param>
    void Remove(ShortlistEntry entry);
}

/// <summary>Persistence for transfer listings (`TRF-1`, `TRF-14`).</summary>
public interface IListingRepository
{
    /// <summary>Stages a new listing.</summary>
    void Add(TransferListing listing);

    /// <summary>Finds a listing, tracked, or null.</summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferListing?> FindAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>Finds the listing a seller opened with an idempotency key, or null (`T-4`).</summary>
    /// <param name="idempotencyKey">The opening request's key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferListing?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Finds one listing for resolution, tracked, or null.
    /// </summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The resolution workflow reads the listing and its bids inside its serializable transaction, so a bid
    /// cannot slip in after the winner is chosen (`TRF-9`).
    /// </remarks>
    Task<TransferListing?> FindForResolutionAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds every open listing whose end is at or before an instant, so the materialiser can enqueue a
    /// resolution job for each (`TRF-2`).
    /// </summary>
    /// <param name="dueAt">The instant listings are due by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TransferListing>> ListDueAsync(DateTimeOffset dueAt, CancellationToken cancellationToken);
}

/// <summary>Persistence for transfer bids (`TRF-4`, `TRF-6`, `TRF-7`).</summary>
public interface IBidRepository
{
    /// <summary>Stages a new bid.</summary>
    void Add(TransferBid bid);

    /// <summary>Finds the bid a club placed with an idempotency key, or null (`T-4`).</summary>
    /// <param name="idempotencyKey">The placing request's key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferBid?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Finds a club's active bid on a listing, tracked, or null (`TRF-6`).</summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="bidderClubId">The bidding club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferBid?> FindActiveBidAsync(
        Guid listingId,
        Guid bidderClubId,
        CancellationToken cancellationToken);

    /// <summary>Finds the current leading bid on a listing, tracked, or null (`TRF-7`).</summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferBid?> FindLeadingBidAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads every bid on a listing, tracked, ordered by the winning order (`TRF-8`).
    /// </summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TransferBid>> ListForListingAsync(Guid listingId, CancellationToken cancellationToken);
}

/// <summary>Persistence for resolved transfers (`TRF-10`).</summary>
/// <remarks>
/// One row per listing, enforced by a unique index, so a retried resolution cannot record a second transfer
/// even if it somehow ran twice (`TRF-9`).
/// </remarks>
public interface ITransferOutcomeRepository
{
    /// <summary>Stages a resolved transfer.</summary>
    void Add(TransferOutcome outcome);
}

/// <summary>Server-only roster facts the market needs to price and revalidate a transfer.</summary>
/// <remarks>
/// A narrow read port of the squad module (`MOD-3`): the market never reads a squad aggregate to make a
/// decision, but it must know whether a listing is legal and whether a resolution keeps both clubs legal.
/// Hidden potential rides along because a listing's buyer terms are priced from it exactly as a renewal quote
/// is (`CON-3`); it is an input here and never a market DTO.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="ClubId">The club the player is contracted to.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="Ability">The player's current ability, 1–20.</param>
/// <param name="Potential">The hidden development ceiling, 1–20. Class C2: server-only.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="MoraleBp">The player's morale in basis points.</param>
/// <param name="Appearances">Matches played this season.</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="CurrentSeasonNumber">The season a sale would begin in.</param>
/// <param name="ContractId">The player's active contract.</param>
/// <param name="ContractEndSeasonNumber">The last season the active contract covers.</param>
/// <param name="HasActiveRegistration">Whether the player has an active registration (`SQ-6`).</param>
/// <param name="RegisteredCount">How many players the club has registered.</param>
/// <param name="GoalkeeperCount">How many of them are goalkeepers.</param>
/// <param name="IsListed">Whether the player already has an open listing (`TRF-14`).</param>
public sealed record ListingEligibilityContext(
    Guid PlayerId,
    Guid ClubId,
    PlayerPosition PrimaryPosition,
    int Ability,
    int Potential,
    int Age,
    int MoraleBp,
    int Appearances,
    int TierNumber,
    int CurrentSeasonNumber,
    Guid ContractId,
    int ContractEndSeasonNumber,
    bool HasActiveRegistration,
    int RegisteredCount,
    int GoalkeeperCount,
    bool IsListed);

/// <summary>A club's registered-squad composition, as a resolution revalidates it (`TRF-9`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="RegisteredCount">How many players are registered.</param>
/// <param name="GoalkeeperCount">How many of them are goalkeepers.</param>
public sealed record RosterComposition(Guid ClubId, int RegisteredCount, int GoalkeeperCount);

/// <summary>The squad facts the market reads, in one query each (`MOD-3`).</summary>
public interface IRosterQueries
{
    /// <summary>Reads the facts a player's listing eligibility is decided from, or null when unknown.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListingEligibilityContext?> GetListingEligibilityAsync(
        Guid playerId,
        CancellationToken cancellationToken);

    /// <summary>Reads a club's registered-squad composition, or null when the club is unknown.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RosterComposition?> GetCompositionAsync(Guid clubId, CancellationToken cancellationToken);
}
