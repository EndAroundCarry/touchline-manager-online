namespace TouchlineManager.Domain.Market;

/// <summary>Why a listing resolved the way it did (`TRF-11`).</summary>
/// <remarks>
/// Recorded on the audit trail and, for a sale, on the outcome row, so an operator can explain a resolution
/// without reconstructing it from the ledger (`INT-6`).
/// </remarks>
public static class TransferOutcomeReasons
{
    /// <summary>The listing resolved to a valid winning bid (`TRF-10`).</summary>
    public const string Sold = "sold";

    /// <summary>No bid survived revalidation, so the listing expired (`TRF-11`).</summary>
    public const string NoValidBid = "no_valid_bid";

    /// <summary>The bid failed because the seller would fall below the minimum squad (`SQ-2`, `TRF-9`).</summary>
    public const string SellerBelowMinimum = "seller_below_minimum";

    /// <summary>The bid failed because the buyer would exceed the maximum squad (`SQ-3`, `TRF-9`).</summary>
    public const string BuyerAboveMaximum = "buyer_above_maximum";

    /// <summary>The bid failed because the reserved funds were no longer available (`FIN-10`, `TRF-9`).</summary>
    public const string FundsUnavailable = "funds_unavailable";

    /// <summary>The bid failed because the player or the bidder is no longer eligible (`TRF-9`).</summary>
    public const string PlayerIneligible = "player_ineligible";
}

/// <summary>
/// The immutable record of one listing's resolution: who bought, who sold, and for how much (`TRF-10`).
/// </summary>
/// <remarks>
/// One row per listing (`unique (listing_id)`), which is what makes a retried resolution a no-op rather than a
/// second transfer: the row is the destination-table uniqueness the idempotency relies on (`FIN-17`, `TRF-9`).
/// A listing that resolves without a sale leaves no outcome row; its reason is audited instead.
/// </remarks>
public sealed class TransferOutcome
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TransferOutcome()
    {
    }

    /// <summary>Gets the outcome identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the listing that resolved.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Gets the winning bid.</summary>
    public Guid WinningBidId { get; private set; }

    /// <summary>Gets the player who moved.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club that sold.</summary>
    public Guid SellerClubId { get; private set; }

    /// <summary>Gets the club that bought.</summary>
    public Guid BuyerClubId { get; private set; }

    /// <summary>Gets the settled fee, in minor units (`FIN-6`, `FIN-8`).</summary>
    public long FeeMinor { get; private set; }

    /// <summary>Gets the seller contract that was closed.</summary>
    public Guid OldContractId { get; private set; }

    /// <summary>Gets the buyer contract that was created.</summary>
    public Guid NewContractId { get; private set; }

    /// <summary>Gets why the listing resolved.</summary>
    public string OutcomeReason { get; private set; } = string.Empty;

    /// <summary>Gets the correlation key of the resolution, which the audit trail shares.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets when the listing resolved.</summary>
    public DateTimeOffset ResolvedAt { get; private set; }

    /// <summary>Records a completed transfer (`TRF-10`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="listingId">The listing that resolved.</param>
    /// <param name="winningBidId">The winning bid.</param>
    /// <param name="playerId">The player who moved.</param>
    /// <param name="sellerClubId">The selling club.</param>
    /// <param name="buyerClubId">The buying club.</param>
    /// <param name="feeMinor">The settled fee, which must be positive.</param>
    /// <param name="oldContractId">The closed seller contract.</param>
    /// <param name="newContractId">The created buyer contract.</param>
    /// <param name="correlationId">The resolution's correlation key.</param>
    /// <param name="now">The current instant.</param>
    public static TransferOutcome Record(
        Guid id,
        Guid listingId,
        Guid winningBidId,
        Guid playerId,
        Guid sellerClubId,
        Guid buyerClubId,
        long feeMinor,
        Guid oldContractId,
        Guid newContractId,
        string correlationId,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(feeMinor);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new TransferOutcome
        {
            Id = id,
            ListingId = listingId,
            WinningBidId = winningBidId,
            PlayerId = playerId,
            SellerClubId = sellerClubId,
            BuyerClubId = buyerClubId,
            FeeMinor = feeMinor,
            OldContractId = oldContractId,
            NewContractId = newContractId,
            OutcomeReason = TransferOutcomeReasons.Sold,
            CorrelationId = correlationId,
            ResolvedAt = now,
        };
    }
}
