namespace TouchlineManager.Contracts.Market;

/// <summary>The body of a request to list a player (`TRF-1`, `CON-5`).</summary>
/// <param name="PlayerId">The player the caller owns and wishes to sell.</param>
/// <param name="MinimumFeeMinor">The minimum fee the seller will accept, in minor units.</param>
/// <param name="Seasons">The buyer contract length the seller offers, 1–3 game seasons (`CON-1`).</param>
public sealed record CreateListingRequest(Guid PlayerId, long MinimumFeeMinor, int Seasons);

/// <summary>The body of a request to place or raise a bid (`TRF-4`, `TRF-6`).</summary>
/// <param name="AmountMinor">The amount, in minor units. The server decides whether it is acceptable.</param>
public sealed record PlaceBidRequest(long AmountMinor);

/// <summary>
/// One listing as the market reads it (`TRF-4`, `CON-5`, `TRF-5`).
/// </summary>
/// <remarks>
/// The leading amount, bidder count, and the caller's own bid are visible; no other bidder's identity or
/// individual bid is exposed, which is what `TRF-4` allows while the auction runs. The minimum acceptable bid
/// is computed server-side so a client never derives the increment itself (`INT-1`).
/// </remarks>
/// <param name="ListingId">The listing identity.</param>
/// <param name="PlayerId">The listed player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="PlayerShortName">The player's abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="SellerClubId">The selling club.</param>
/// <param name="SellerClubName">The selling club's name.</param>
/// <param name="MinimumFeeMinor">The seller's minimum fee, in minor units.</param>
/// <param name="GeneratedBuyerWageMinor">The weekly wage the buyer will sign, in minor units.</param>
/// <param name="GeneratedContractSeasons">The buyer's contract length, in game seasons.</param>
/// <param name="OpensAt">When the listing opened.</param>
/// <param name="EndsAt">When the listing resolves.</param>
/// <param name="Status">The listing's lifecycle state.</param>
/// <param name="LeadingAmountMinor">The current leading amount, or null when there are no bids.</param>
/// <param name="BidderCount">How many bids the listing has received.</param>
/// <param name="MinimumAcceptableBidMinor">The smallest bid the server will now accept (`TRF-5`).</param>
/// <param name="YourBidMinor">The caller's own active bid, or null (`TRF-6`).</param>
/// <param name="Version">The listing's optimistic-concurrency version.</param>
public sealed record TransferListingResponse(
    Guid ListingId,
    Guid PlayerId,
    string PlayerName,
    string PlayerShortName,
    string PrimaryPosition,
    int Age,
    Guid SellerClubId,
    string SellerClubName,
    long MinimumFeeMinor,
    long GeneratedBuyerWageMinor,
    int GeneratedContractSeasons,
    DateTimeOffset OpensAt,
    DateTimeOffset EndsAt,
    string Status,
    long? LeadingAmountMinor,
    int BidderCount,
    long MinimumAcceptableBidMinor,
    long? YourBidMinor,
    long Version);

/// <summary>One page of listings (`INT-6`).</summary>
/// <param name="Listings">The listings on this page.</param>
/// <param name="NextCursor">The cursor for the next page, or null at the end.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record TransferListingsResponse(
    IReadOnlyList<TransferListingResponse> Listings,
    string? NextCursor,
    DateTimeOffset ServerTime);

/// <summary>One of the caller's own bids (`TRF-4`).</summary>
/// <param name="ListingId">The listing bid on.</param>
/// <param name="PlayerId">The bid-for player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="AmountMinor">The amount bid, in minor units.</param>
/// <param name="Status">The bid's lifecycle state.</param>
/// <param name="EndsAt">When the listing resolves.</param>
public sealed record MyMarketBidResponse(
    Guid ListingId,
    Guid PlayerId,
    string PlayerName,
    long AmountMinor,
    string Status,
    DateTimeOffset EndsAt);

/// <summary>Everything the transfers screen reads about the caller's own activity (`TRF-4`, `TRF-6`).</summary>
/// <param name="MyListings">The caller's own listings.</param>
/// <param name="MyBids">The caller's own bids.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record MyMarketActivityResponse(
    IReadOnlyList<TransferListingResponse> MyListings,
    IReadOnlyList<MyMarketBidResponse> MyBids,
    DateTimeOffset ServerTime);

/// <summary>One completed transfer in the public market history (`INT-6`).</summary>
/// <param name="ListingId">The listing that resolved.</param>
/// <param name="PlayerId">The player who moved.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="SellerClubId">The selling club.</param>
/// <param name="SellerClubName">The selling club's name.</param>
/// <param name="BuyerClubId">The buying club.</param>
/// <param name="BuyerClubName">The buying club's name.</param>
/// <param name="FeeMinor">The settled fee, in minor units.</param>
/// <param name="ResolvedAt">When the transfer completed.</param>
public sealed record TransferHistoryEntryResponse(
    Guid ListingId,
    Guid PlayerId,
    string PlayerName,
    Guid SellerClubId,
    string SellerClubName,
    Guid BuyerClubId,
    string BuyerClubName,
    long FeeMinor,
    DateTimeOffset ResolvedAt);

/// <summary>One page of public transfer history (`INT-6`).</summary>
/// <param name="Transfers">The transfers on this page, most recent first.</param>
/// <param name="NextCursor">The cursor for the next page, or null at the end.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record TransferHistoryResponse(
    IReadOnlyList<TransferHistoryEntryResponse> Transfers,
    string? NextCursor,
    DateTimeOffset ServerTime);
