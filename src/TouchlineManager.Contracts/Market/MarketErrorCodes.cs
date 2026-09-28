namespace TouchlineManager.Contracts.Market;

/// <summary>
/// Stable machine-readable error codes for the market module (master plan §10; `TRF-*`, `INT-2`).
/// </summary>
/// <remarks>
/// A client branches on the code rather than the wording. The authorization refusals the market shares with
/// the squad reads are the world and squad codes; these are the refusals unique to listing and bidding.
/// </remarks>
public static class MarketErrorCodes
{
    /// <summary>No listing exists with the requested identity.</summary>
    public const string ListingNotFound = "LISTING_NOT_FOUND";

    /// <summary>The listing is no longer open, so it accepts no bids and cannot be cancelled (`TRF-9`, `TRF-15`).</summary>
    public const string ListingNotOpen = "LISTING_NOT_OPEN";

    /// <summary>The player cannot be listed: not the caller's, or the sale would break the squad rules (`TRF-1`, `SQ-2`).</summary>
    public const string ListingNotEligible = "LISTING_NOT_ELIGIBLE";

    /// <summary>The player already has an open listing (`TRF-14`).</summary>
    public const string PlayerAlreadyListed = "PLAYER_ALREADY_LISTED";

    /// <summary>A club cannot bid on its own player (`TRF-1`).</summary>
    public const string CannotBidOnOwnPlayer = "CANNOT_BID_ON_OWN_PLAYER";

    /// <summary>The bid does not clear the minimum fee or the minimum raise (`TRF-5`).</summary>
    public const string BidTooLow = "BID_TOO_LOW";

    /// <summary>The club does not hold the cash after existing reservations (`FIN-10`).</summary>
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

    /// <summary>The player does not exist, or is not visible.</summary>
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";

    /// <summary>The shortlist has no entry for that player (`SCT-3`).</summary>
    public const string ShortlistEntryNotFound = "SHORTLIST_ENTRY_NOT_FOUND";

    /// <summary>The cursor did not decode.</summary>
    public const string InvalidCursor = "INVALID_CURSOR";

    /// <summary>A listing, cancellation, or bid command arrived without an idempotency key (`INT-2`).</summary>
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";

    /// <summary>An idempotency key was reused with a different request (`T-4`).</summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
}
