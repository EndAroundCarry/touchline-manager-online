using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// The market's reads: one listing, a page of listings, the caller's own activity, and the public history
/// (`TRF-4`, `TRF-6`, `INT-6`).
/// </summary>
/// <remarks>
/// Every read resolves the caller's club first, so a listing carries the caller's own bid and nothing about
/// any other bidder (`TRF-4`), and the history is read through a bounded, cursor-paged query.
/// </remarks>
public sealed class ListListings
{
    /// <summary>The default and largest page the browse returns (`D-2`).</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The largest page the browse returns (`D-2`).</summary>
    public const int MaxPageSize = 50;

    private readonly ResolveOwnedClub _access;
    private readonly IMarketQueries _queries;

    /// <summary>Initializes the query.</summary>
    public ListListings(ResolveOwnedClub access, IMarketQueries queries)
    {
        _access = access;
        _queries = queries;
    }

    /// <summary>Reads one listing, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingResult> GetListingAsync(
        Guid userId,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListingResult(access.Outcome.FromAccess(), null);
        }

        var listing = await _queries.GetListingAsync(listingId, access.ClubId, cancellationToken);

        return listing is null
            ? new ListingResult(MarketOutcome.NotFound, null)
            : new ListingResult(MarketOutcome.Found, listing);
    }

    /// <summary>Browses listings, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="filter">The filters and page.</param>
    /// <param name="cursor">The opaque cursor, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingPageResult> ExecuteAsync(
        Guid userId,
        ListingBrowseFilter filter,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListingPageResult(access.Outcome.FromAccess(), null);
        }

        if (!MarketCursor.TryDecodeId(cursor, out var cursorId))
        {
            return new ListingPageResult(MarketOutcome.InvalidCursor, null);
        }

        var bounded = filter with
        {
            CursorListingId = cursor is null ? null : cursorId,
            PageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize),
        };

        var page = await _queries.ListListingsAsync(bounded, access.ClubId, cancellationToken);

        return new ListingPageResult(MarketOutcome.Found, page);
    }

    /// <summary>Reads the caller's own listings and bids, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MarketActivityResult> GetActivityAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new MarketActivityResult(access.Outcome.FromAccess(), null);
        }

        return new MarketActivityResult(
            MarketOutcome.Found,
            await _queries.GetMyActivityAsync(access.ClubId, cancellationToken));
    }

    /// <summary>Reads the public transfer history, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cursor">The opaque cursor, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TransferHistoryResult> GetHistoryAsync(
        Guid userId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new TransferHistoryResult(access.Outcome.FromAccess(), null);
        }

        if (!MarketCursor.TryDecodeId(cursor, out var cursorId))
        {
            return new TransferHistoryResult(MarketOutcome.InvalidCursor, null);
        }

        var page = await _queries.GetHistoryAsync(
            cursor is null ? null : cursorId,
            DefaultPageSize,
            cancellationToken);

        return new TransferHistoryResult(MarketOutcome.Found, page);
    }
}
