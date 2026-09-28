namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// Recorded audit actions for the market module (`TRF-1`…`TRF-15`, `INT-2`, `INT-4`).
/// </summary>
/// <remarks>
/// Every listing, bid, cancellation, and resolution is audited, because money is reserved and a player moves:
/// who acted, against which listing, and why it resolved the way it did has to be answerable when a transfer
/// is disputed (`INT-4`, `R-1`).
/// </remarks>
public static class MarketAuditActions
{
    /// <summary>A player was listed for sale (`TRF-1`).</summary>
    public const string ListingOpened = "market.listing.opened";

    /// <summary>A listing was cancelled, releasing its reservations (`TRF-15`).</summary>
    public const string ListingCancelled = "market.listing.cancelled";

    /// <summary>A bid was placed and its funds reserved (`TRF-4`, `TRF-7`).</summary>
    public const string BidPlaced = "market.bid.placed";

    /// <summary>A leading bid was raised (`TRF-6`).</summary>
    public const string BidRaised = "market.bid.raised";

    /// <summary>A listing resolved to a completed transfer (`TRF-10`).</summary>
    public const string ListingSold = "market.listing.sold";

    /// <summary>A listing resolved without a valid bid (`TRF-11`).</summary>
    public const string ListingExpired = "market.listing.expired";

    /// <summary>A bid was skipped at resolution because it failed a revalidated invariant (`TRF-11`).</summary>
    public const string BidInvalidated = "market.bid.invalidated";
}
