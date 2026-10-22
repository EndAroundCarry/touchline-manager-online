using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Application.Market;

/// <summary>
/// Puts the viewer's wage impact on the listings they read (`WAGE-1`, plan toasty-scott section 5).
/// </summary>
/// <remarks>
/// The impact is for buying the player at the smallest bid the server would now accept. A listing the viewer's club sells, or one it already leads,
/// carries none: the first is not a purchase, and the second is already in the club's wage bill and in its reserved cash.
/// </remarks>
public sealed class ListingWageImpact
{
    private readonly WageImpactReader _reader;

    /// <summary>Initializes the attachment.</summary>
    public ListingWageImpact(WageImpactReader reader) => _reader = reader;

    /// <summary>Attaches the impact to one listing.</summary>
    /// <param name="listing">The listing.</param>
    /// <param name="viewerClubId">The viewer's club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingRow> AttachAsync(ListingRow listing, Guid viewerClubId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return (await AttachAsync([listing], viewerClubId, cancellationToken))[0];
    }

    /// <summary>Attaches the impact to a page of listings, reading the club's money once.</summary>
    /// <param name="listings">The listings.</param>
    /// <param name="viewerClubId">The viewer's club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ListingRow>> AttachAsync(
        IReadOnlyList<ListingRow> listings,
        Guid viewerClubId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listings);

        if (listings.Count == 0 || !listings.Any(listing => IsPurchase(listing, viewerClubId)))
        {
            return listings;
        }

        var club = await _reader.ReadAsync(viewerClubId, cancellationToken);

        if (club is null)
        {
            return listings;
        }

        return
        [
            .. listings.Select(listing => IsPurchase(listing, viewerClubId)
                ? listing with
                {
                    WageImpact = club.Evaluate(
                        AuctionRules.MinimumAcceptableBid(listing.MinimumFeeMinor, listing.LeadingAmountMinor),
                        listing.GeneratedBuyerSeasonWageMinor),
                }
                : listing),
        ];
    }

    private static bool IsPurchase(ListingRow listing, Guid viewerClubId) =>
        listing.Status == ListingStatus.Open
        && listing.SellerClubId != viewerClubId
        && listing.ViewerBidMinor is null;
}
