using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Searches the global player database with exact public attributes (`SCT-1`, `SCT-2`, `D-2`).
/// </summary>
/// <remarks>
/// The search is server-side and bounded: the caller must hold a club like every other market surface, the
/// page size is capped, and the cursor is decoded here so a hand-edited token is a refusal rather than a
/// silently wrong page. Attribute uncertainty and staffed scouting are post-MVP (`SCT-2`), so what a manager
/// sees is the exact attribute a player has.
/// </remarks>
public sealed class SearchPlayers
{
    /// <summary>The page size a request may ask for, when none is given.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The largest page a request may ask for, so an expensive query stays bounded (`D-2`).</summary>
    public const int MaxPageSize = 50;

    private readonly ResolveOwnedClub _access;
    private readonly IMarketQueries _queries;

    /// <summary>Initializes the query.</summary>
    public SearchPlayers(ResolveOwnedClub access, IMarketQueries queries)
    {
        _access = access;
        _queries = queries;
    }

    /// <summary>Searches, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="filter">The filters and page.</param>
    /// <param name="cursor">The opaque cursor, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SearchPlayersResult> ExecuteAsync(
        Guid userId,
        PlayerSearchFilter filter,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new SearchPlayersResult(access.Outcome.FromAccess(), null);
        }

        if (!MarketCursor.TryDecode(cursor, out var sort, out var sortKey, out var lastId))
        {
            return new SearchPlayersResult(MarketOutcome.InvalidCursor, null);
        }

        if (sort != filter.Sort)
        {
            // A cursor from a different ordering would page into the wrong place; the client replays from the
            // first page instead, so it is refused rather than honoured.
            return new SearchPlayersResult(MarketOutcome.InvalidCursor, null);
        }

        var position = cursor is null ? null : new SearchCursorPosition(sort, sortKey!, lastId);
        var bounded = filter with
        {
            Cursor = position,
            PageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize),
        };

        var page = await _queries.SearchPlayersAsync(bounded, cancellationToken);

        return new SearchPlayersResult(MarketOutcome.Found, page);
    }
}
