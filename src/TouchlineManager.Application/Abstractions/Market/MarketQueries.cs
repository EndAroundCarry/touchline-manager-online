using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Market;

/// <summary>How a scouting search is ordered (`SCT-1`).</summary>
public enum PlayerSort
{
    /// <summary>By full name, ascending.</summary>
    Name = 0,

    /// <summary>By age, youngest first.</summary>
    Age = 1,

    /// <summary>By ability, strongest first.</summary>
    Ability = 2,
}

/// <summary>A decoded scouting page position (`SCT-1`).</summary>
/// <param name="Sort">The sort the page was ordered by.</param>
/// <param name="SortKey">The last row's primary sort value, as a string.</param>
/// <param name="PlayerId">The last row's player identity.</param>
public sealed record SearchCursorPosition(PlayerSort Sort, string SortKey, Guid PlayerId);

/// <summary>The filters and page a scouting search is made with (`SCT-1`, `D-2`).</summary>
/// <param name="Name">A name fragment to match, or null.</param>
/// <param name="Family">The position family to match, or null for any.</param>
/// <param name="AgeMin">The youngest age to include, or null.</param>
/// <param name="AgeMax">The oldest age to include, or null.</param>
/// <param name="AbilityMin">The lowest mean ability to include, or null.</param>
/// <param name="ClubId">The club to restrict to, or null for every club.</param>
/// <param name="DivisionId">The division to restrict to, or null for every division.</param>
/// <param name="Sort">The order to return results in.</param>
/// <param name="Cursor">The decoded cursor for the next page, or null for the first.</param>
/// <param name="PageSize">The bounded page size.</param>
public sealed record PlayerSearchFilter(
    string? Name,
    PositionFamily? Family,
    int? AgeMin,
    int? AgeMax,
    int? AbilityMin,
    Guid? ClubId,
    Guid? DivisionId,
    PlayerSort Sort,
    SearchCursorPosition? Cursor,
    int PageSize);

/// <summary>One player as a scouting result reads them (`SCT-1`).</summary>
/// <param name="PlayerId">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="PreferredFoot">The foot the player favours.</param>
/// <param name="Attributes">The exact public attributes (`SCT-1`).</param>
/// <param name="ClubId">The club the player is registered to, or null when unattached.</param>
/// <param name="ClubName">The club's name, or null.</param>
/// <param name="TierNumber">The club's tier, or null.</param>
/// <param name="DivisionId">The club's division, or null.</param>
/// <param name="DivisionName">The division's name, or null.</param>
/// <param name="GameYear">The game year the age is read against (`TIME-3`).</param>
/// <param name="IsListed">Whether the player currently has an open listing (`TRF-14`).</param>
public sealed record PlayerSearchRow(
    Guid PlayerId,
    string FullName,
    string ShortName,
    string NationalityCode,
    int BirthGameYear,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    PreferredFoot PreferredFoot,
    PlayerAttributeSet Attributes,
    Guid? ClubId,
    string? ClubName,
    int? TierNumber,
    Guid? DivisionId,
    string? DivisionName,
    int GameYear,
    bool IsListed);

/// <summary>One page of scouting results (`SCT-1`).</summary>
/// <param name="Players">The players on this page.</param>
/// <param name="NextCursor">The cursor for the next page, or null at the end.</param>
public sealed record PlayerSearchPage(IReadOnlyList<PlayerSearchRow> Players, string? NextCursor);

/// <summary>One shortlisted player as the shortlist screen reads them (`SCT-3`).</summary>
/// <param name="PlayerId">The player identity.</param>
/// <param name="PlayerName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="Notes">The manager's private note, or null.</param>
/// <param name="IsListed">Whether the player currently has an open listing (`TRF-14`).</param>
/// <param name="GameYear">The game year the age is read against.</param>
public sealed record ShortlistRow(
    Guid PlayerId,
    string PlayerName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    int BirthGameYear,
    string? Notes,
    bool IsListed,
    int GameYear);

/// <summary>A manager's private shortlist (`SCT-3`).</summary>
/// <param name="Entries">The shortlisted players, most recently added first.</param>
/// <param name="GameYear">The game year the ages are read against.</param>
public sealed record ShortlistSnapshot(IReadOnlyList<ShortlistRow> Entries, int GameYear);

/// <summary>The filters a listing browse is made with (`TRF-1`, `INT-6`).</summary>
/// <param name="Status">The listing status to show, or null for open only.</param>
/// <param name="SellerClubId">The seller to restrict to, or null for every seller.</param>
/// <param name="Family">The listed player's position family, or null for any.</param>
/// <param name="CursorListingId">The decoded cursor listing identity, or null for the first page.</param>
/// <param name="PageSize">The bounded page size.</param>
public sealed record ListingBrowseFilter(
    ListingStatus? Status,
    Guid? SellerClubId,
    PositionFamily? Family,
    Guid? CursorListingId,
    int PageSize);

/// <summary>One listing as the market reads it (`TRF-4`, `CON-5`).</summary>
/// <param name="ListingId">The listing identity.</param>
/// <param name="PlayerId">The listed player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="PlayerShortName">The player's abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="SellerClubId">The selling club.</param>
/// <param name="SellerClubName">The selling club's name.</param>
/// <param name="MinimumFeeMinor">The seller's minimum fee, in minor units.</param>
/// <param name="GeneratedBuyerWageMinor">The weekly wage the buyer will sign, in minor units.</param>
/// <param name="GeneratedContractSeasons">The buyer's contract length, in game seasons.</param>
/// <param name="OpensAt">When the listing opened.</param>
/// <param name="EndsAt">When the listing resolves.</param>
/// <param name="Status">The listing's lifecycle state.</param>
/// <param name="LeadingAmountMinor">The current leading amount, or null when there are no bids.</param>
/// <param name="BidderCount">How many bids the listing has received (`TRF-4`).</param>
/// <param name="ViewerBidMinor">The viewer's own active bid, or null (`TRF-6`).</param>
/// <param name="GameYear">The game year the age is read against.</param>
/// <param name="Version">The listing's optimistic-concurrency version.</param>
public sealed record ListingRow(
    Guid ListingId,
    Guid PlayerId,
    string PlayerName,
    string PlayerShortName,
    PlayerPosition PrimaryPosition,
    int BirthGameYear,
    Guid SellerClubId,
    string SellerClubName,
    long MinimumFeeMinor,
    long GeneratedBuyerWageMinor,
    int GeneratedContractSeasons,
    DateTimeOffset OpensAt,
    DateTimeOffset EndsAt,
    ListingStatus Status,
    long? LeadingAmountMinor,
    int BidderCount,
    long? ViewerBidMinor,
    int GameYear,
    long Version);

/// <summary>One page of listings (`INT-6`).</summary>
/// <param name="Listings">The listings on this page.</param>
/// <param name="NextCursor">The cursor for the next page, or null at the end.</param>
public sealed record ListingPage(IReadOnlyList<ListingRow> Listings, string? NextCursor);

/// <summary>One of the manager's own bids, as the transfers screen reads it (`TRF-4`).</summary>
/// <param name="ListingId">The listing bid on.</param>
/// <param name="PlayerId">The bid-for player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="AmountMinor">The amount bid, in minor units.</param>
/// <param name="Status">The bid's lifecycle state.</param>
/// <param name="EndsAt">When the listing resolves.</param>
public sealed record MyBidRow(
    Guid ListingId,
    Guid PlayerId,
    string PlayerName,
    long AmountMinor,
    BidStatus Status,
    DateTimeOffset EndsAt);

/// <summary>Everything the transfers screen reads about the caller's own activity (`TRF-4`, `TRF-6`).</summary>
/// <param name="MyListings">The caller's own listings.</param>
/// <param name="MyBids">The caller's own bids.</param>
/// <param name="GameYear">The game year the ages are read against.</param>
public sealed record MyMarketActivity(
    IReadOnlyList<ListingRow> MyListings,
    IReadOnlyList<MyBidRow> MyBids,
    int GameYear);

/// <summary>One completed transfer, as the public market history reads it (`INT-6`).</summary>
/// <param name="ListingId">The listing that resolved.</param>
/// <param name="PlayerId">The player who moved.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="SellerClubId">The selling club.</param>
/// <param name="SellerClubName">The selling club's name.</param>
/// <param name="BuyerClubId">The buying club.</param>
/// <param name="BuyerClubName">The buying club's name.</param>
/// <param name="FeeMinor">The settled fee, in minor units.</param>
/// <param name="ResolvedAt">When the transfer completed.</param>
public sealed record TransferHistoryRow(
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
public sealed record TransferHistoryPage(IReadOnlyList<TransferHistoryRow> Transfers, string? NextCursor);

/// <summary>
/// The read side of the market module (`MOD-3`).
/// </summary>
/// <remarks>
/// Projections, not aggregates: scouting reads the squad tables, and the listing and history reads join the
/// world and squad tables, but none returns a tracked graph and none makes a decision. The use cases decide
/// who may ask; this port answers what is there.
/// </remarks>
public interface IMarketQueries
{
    /// <summary>Searches the global player database (`SCT-1`, `D-2`).</summary>
    /// <param name="filter">The filters and page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerSearchPage> SearchPlayersAsync(PlayerSearchFilter filter, CancellationToken cancellationToken);

    /// <summary>Reads a manager's private shortlist (`SCT-3`).</summary>
    /// <param name="managerId">The manager.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ShortlistSnapshot> GetShortlistAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>Reads one listing as the viewer sees it, or null when it does not exist.</summary>
    /// <param name="listingId">The listing.</param>
    /// <param name="viewerClubId">The viewer's club, for their own bid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListingRow?> GetListingAsync(Guid listingId, Guid? viewerClubId, CancellationToken cancellationToken);

    /// <summary>Browses listings by filter (`INT-6`).</summary>
    /// <param name="filter">The filters and page.</param>
    /// <param name="viewerClubId">The viewer's club, for their own bid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListingPage> ListListingsAsync(
        ListingBrowseFilter filter,
        Guid? viewerClubId,
        CancellationToken cancellationToken);

    /// <summary>Reads the caller's own listings and bids (`TRF-4`, `TRF-6`).</summary>
    /// <param name="sellerClubId">The caller's club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MyMarketActivity> GetMyActivityAsync(Guid sellerClubId, CancellationToken cancellationToken);

    /// <summary>Reads the public history of completed transfers (`INT-6`).</summary>
    /// <param name="cursorId">The decoded cursor identity, or null for the first page.</param>
    /// <param name="pageSize">The bounded page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TransferHistoryPage> GetHistoryAsync(
        Guid? cursorId,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Reports whether a player currently has an open listing (`TRF-14`).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasOpenListingAsync(Guid playerId, CancellationToken cancellationToken);
}
