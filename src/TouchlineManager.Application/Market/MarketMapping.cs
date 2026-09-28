using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Market;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Maps market read snapshots to their transport projections, and performs the conversions the API layer must
/// not do itself.
/// </summary>
/// <remarks>
/// One place, so a listing cannot be assembled two different ways on two endpoints, and so the hidden
/// potential, reputation, and internal valuation the queries may carry never reach a client (`I-1`). The
/// minimum acceptable bid is derived here from the rule set (`TRF-5`), so a client is told the floor rather
/// than computing it.
/// </remarks>
public static class MarketMapping
{
    /// <summary>Projects one scouting result (`SCT-1`).</summary>
    /// <param name="row">The stored row.</param>
    public static PlayerSearchResultResponse ToResponse(this PlayerSearchRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new PlayerSearchResultResponse(
            row.PlayerId,
            row.FullName,
            row.ShortName,
            row.NationalityCode,
            SquadMapping.AgeIn(row.BirthGameYear, row.GameYear),
            row.PreferredFoot.ToCode(),
            row.PrimaryPosition.ToCode(),
            [.. row.SecondaryPositions.Select(position => position.ToCode())],
            row.ClubId,
            row.ClubName,
            row.TierNumber,
            row.DivisionId,
            row.DivisionName,
            row.IsListed,
            row.Attributes.ToResponse());
    }

    /// <summary>Projects a scouting page (`SCT-1`).</summary>
    /// <param name="page">The stored page.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static PlayerSearchResponse ToResponse(this PlayerSearchPage page, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PlayerSearchResponse(
            [.. page.Players.Select(ToResponse)],
            page.NextCursor,
            serverTime);
    }

    /// <summary>Projects one shortlist entry (`SCT-3`).</summary>
    /// <param name="row">The stored row.</param>
    public static ShortlistEntryResponse ToResponse(this ShortlistRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new ShortlistEntryResponse(
            row.PlayerId,
            row.PlayerName,
            row.ShortName,
            row.PrimaryPosition.ToCode(),
            SquadMapping.AgeIn(row.BirthGameYear, row.GameYear),
            row.Notes,
            row.IsListed);
    }

    /// <summary>Projects a shortlist (`SCT-3`).</summary>
    /// <param name="snapshot">The stored snapshot.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static ShortlistResponse ToResponse(this ShortlistSnapshot snapshot, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ShortlistResponse([.. snapshot.Entries.Select(ToResponse)], serverTime);
    }

    /// <summary>Projects one listing (`TRF-4`, `CON-5`, `TRF-5`).</summary>
    /// <param name="row">The stored row.</param>
    public static TransferListingResponse ToResponse(this ListingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new TransferListingResponse(
            row.ListingId,
            row.PlayerId,
            row.PlayerName,
            row.PlayerShortName,
            row.PrimaryPosition.ToCode(),
            SquadMapping.AgeIn(row.BirthGameYear, row.GameYear),
            row.SellerClubId,
            row.SellerClubName,
            row.MinimumFeeMinor,
            row.GeneratedBuyerWageMinor,
            row.GeneratedContractSeasons,
            row.OpensAt,
            row.EndsAt,
            row.Status.ToCode(),
            row.LeadingAmountMinor,
            row.BidderCount,
            AuctionRules.MinimumAcceptableBid(row.MinimumFeeMinor, row.LeadingAmountMinor),
            row.ViewerBidMinor,
            row.Version);
    }

    /// <summary>Projects a page of listings (`INT-6`).</summary>
    /// <param name="page">The stored page.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static TransferListingsResponse ToResponse(this ListingPage page, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new TransferListingsResponse(
            [.. page.Listings.Select(ToResponse)],
            page.NextCursor,
            serverTime);
    }

    /// <summary>Projects one of the caller's own bids (`TRF-4`).</summary>
    /// <param name="row">The stored row.</param>
    public static MyMarketBidResponse ToResponse(this MyBidRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new MyMarketBidResponse(
            row.ListingId,
            row.PlayerId,
            row.PlayerName,
            row.AmountMinor,
            row.Status.ToCode(),
            row.EndsAt);
    }

    /// <summary>Projects the caller's own market activity (`TRF-4`, `TRF-6`).</summary>
    /// <param name="activity">The stored activity.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static MyMarketActivityResponse ToResponse(this MyMarketActivity activity, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return new MyMarketActivityResponse(
            [.. activity.MyListings.Select(ToResponse)],
            [.. activity.MyBids.Select(ToResponse)],
            serverTime);
    }

    /// <summary>Projects one completed transfer (`INT-6`).</summary>
    /// <param name="row">The stored row.</param>
    public static TransferHistoryEntryResponse ToResponse(this TransferHistoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new TransferHistoryEntryResponse(
            row.ListingId,
            row.PlayerId,
            row.PlayerName,
            row.SellerClubId,
            row.SellerClubName,
            row.BuyerClubId,
            row.BuyerClubName,
            row.FeeMinor,
            row.ResolvedAt);
    }

    /// <summary>Projects a page of transfer history (`INT-6`).</summary>
    /// <param name="page">The stored page.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static TransferHistoryResponse ToResponse(this TransferHistoryPage page, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new TransferHistoryResponse(
            [.. page.Transfers.Select(ToResponse)],
            page.NextCursor,
            serverTime);
    }
}
