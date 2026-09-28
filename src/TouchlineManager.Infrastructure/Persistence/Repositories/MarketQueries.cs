using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The market module's read projections: scouting, listings, the caller's activity, and history
/// (master plan §10.6; `SCT-1`, `TRF-4`, `INT-6`).
/// </summary>
/// <remarks>
/// <para>
/// Scouting filters what it can in SQL and finishes ability, ordering, and paging in memory, because a
/// player's ability is the mean of twenty-eight attribute columns rather than a stored value. The candidate
/// set is capped, so the worst case stays bounded (`D-2`); the page itself is keyset-paged so a listing or a
/// transfer arriving between two pages never makes a client skip a row.
/// </para>
/// <para>
/// No query returns a tracked graph, and none of them is used to make a decision: the commands read their
/// aggregates through the write ports.
/// </para>
/// </remarks>
internal sealed class MarketQueries : IMarketQueries
{
    /// <summary>The most scouting candidates one search may materialise before filtering in memory (`D-2`).</summary>
    private const int CandidateCap = 5_000;

    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public MarketQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<PlayerSearchPage> SearchPlayersAsync(
        PlayerSearchFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return new PlayerSearchPage([], null);
        }

        var positions = filter.Family is { } family
            ? Enum.GetValues<PlayerPosition>()
                .Where(position => PlayerPositions.FamilyOf(position) == family)
                .ToList()
            : null;

        var query =
            from player in _dbContext.Players
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join contract in _dbContext.PlayerContracts on player.Id equals contract.PlayerId
            join club in _dbContext.Clubs on contract.ClubId equals club.Id
            join entry in _dbContext.ClubSeasonEntries on club.Id equals entry.ClubId
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where contract.Status == ContractStatus.Active
                && divisionSeason.SeasonId == season.SeasonId
            select new Candidate
            {
                Player = player,
                Attributes = attributes,
                ClubId = club.Id,
                ClubName = club.Name,
                TierNumber = division.TierNumber,
                DivisionId = division.Id,
                DivisionName = division.DisplayName,
                Age = season.GameYear - player.BirthGameYear,
            };

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var name = filter.Name.Trim();
            query = query.Where(candidate =>
                candidate.Player.FullName.Contains(name) || candidate.Player.ShortName.Contains(name));
        }

        if (positions is not null)
        {
            query = query.Where(candidate => positions.Contains(candidate.Player.PrimaryPosition));
        }

        if (filter.ClubId is { } clubId)
        {
            query = query.Where(candidate => candidate.ClubId == clubId);
        }

        if (filter.DivisionId is { } divisionId)
        {
            query = query.Where(candidate => candidate.DivisionId == divisionId);
        }

        if (filter.AgeMin is { } ageMin)
        {
            query = query.Where(candidate => candidate.Age >= ageMin);
        }

        if (filter.AgeMax is { } ageMax)
        {
            query = query.Where(candidate => candidate.Age <= ageMax);
        }

        var candidates = await query.Take(CandidateCap).ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(candidate => new RankedCandidate(candidate, MeanAbility(candidate.Attributes)))
            .Where(row => filter.AbilityMin is not { } abilityMin || row.Ability >= abilityMin);

        var ordered = Order(ranked, filter.Sort);
        var paged = ApplyCursor(ordered, filter).ToList();

        var fetched = paged.Take(filter.PageSize + 1).ToList();
        var hasMore = fetched.Count > filter.PageSize;
        var page = hasMore ? fetched[..filter.PageSize] : fetched;

        var listed = await ListedPlayerIdsAsync(
            [.. page.Select(row => row.Candidate.Player.Id)],
            cancellationToken);

        var players = page
            .Select(row => new PlayerSearchRow(
                row.Candidate.Player.Id,
                row.Candidate.Player.FullName,
                row.Candidate.Player.ShortName,
                row.Candidate.Player.NationalityCode,
                row.Candidate.Player.BirthGameYear,
                row.Candidate.Player.PrimaryPosition,
                row.Candidate.Player.SecondaryPositions,
                row.Candidate.Player.PreferredFoot,
                row.Candidate.Attributes.ToSet(),
                row.Candidate.ClubId,
                row.Candidate.ClubName,
                row.Candidate.TierNumber,
                row.Candidate.DivisionId,
                row.Candidate.DivisionName,
                season.GameYear,
                listed.Contains(row.Candidate.Player.Id)))
            .ToList();

        var nextCursor = hasMore && page.Count > 0
            ? MarketCursor.Encode(filter.Sort, SortKeyOf(filter.Sort, page[^1]), page[^1].Candidate.Player.Id)
            : null;

        return new PlayerSearchPage(players, nextCursor);
    }

    /// <inheritdoc />
    public async Task<ShortlistSnapshot> GetShortlistAsync(Guid managerId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return new ShortlistSnapshot([], 0);
        }

        var entries = await (
            from entry in _dbContext.ShortlistEntries
            join player in _dbContext.Players on entry.PlayerId equals player.Id
            where entry.ManagerId == managerId
            orderby entry.CreatedAt descending, entry.Id descending
            select new
            {
                entry.PlayerId,
                PlayerName = player.FullName,
                player.ShortName,
                player.PrimaryPosition,
                player.BirthGameYear,
                entry.Notes,
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        var listed = await ListedPlayerIdsAsync([.. entries.Select(entry => entry.PlayerId)], cancellationToken);

        var rows = entries
            .Select(entry => new ShortlistRow(
                entry.PlayerId,
                entry.PlayerName,
                entry.ShortName,
                entry.PrimaryPosition,
                entry.BirthGameYear,
                entry.Notes,
                listed.Contains(entry.PlayerId),
                season.GameYear))
            .ToList();

        return new ShortlistSnapshot(rows, season.GameYear);
    }

    /// <inheritdoc />
    public async Task<ListingRow?> GetListingAsync(
        Guid listingId,
        Guid? viewerClubId,
        CancellationToken cancellationToken)
    {
        var rows = await ListingsAsync([listingId], viewerClubId, cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <inheritdoc />
    public async Task<ListingPage> ListListingsAsync(
        ListingBrowseFilter filter,
        Guid? viewerClubId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var status = filter.Status ?? ListingStatus.Open;
        var positions = filter.Family is { } family
            ? Enum.GetValues<PlayerPosition>()
                .Where(position => PlayerPositions.FamilyOf(position) == family)
                .ToList()
            : null;

        var query =
            from listing in _dbContext.TransferListings
            join player in _dbContext.Players on listing.PlayerId equals player.Id
            where listing.Status == status
            select new
            {
                ListingId = listing.Id,
                listing.SellerClubId,
                player.PrimaryPosition,
            };

        if (filter.SellerClubId is { } sellerClubId)
        {
            query = query.Where(row => row.SellerClubId == sellerClubId);
        }

        if (positions is not null)
        {
            query = query.Where(row => positions.Contains(row.PrimaryPosition));
        }

        if (filter.CursorListingId is { } cursorListingId)
        {
            query = query.Where(row => row.ListingId.CompareTo(cursorListingId) > 0);
        }

        var ids = await query
            .OrderBy(row => row.ListingId)
            .Select(row => row.ListingId)
            .Take(filter.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = ids.Count > filter.PageSize;
        var pageIds = hasMore ? ids[..filter.PageSize] : ids;

        var rows = await ListingsAsync(pageIds, viewerClubId, cancellationToken);

        return new ListingPage(rows, hasMore && pageIds.Count > 0 ? MarketCursor.EncodeId(pageIds[^1]) : null);
    }

    /// <inheritdoc />
    public async Task<MyMarketActivity> GetMyActivityAsync(Guid sellerClubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return new MyMarketActivity([], [], 0);
        }

        var myListingIds = await _dbContext.TransferListings
            .Where(listing => listing.SellerClubId == sellerClubId)
            .OrderByDescending(listing => listing.Id)
            .Select(listing => listing.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var myListings = await ListingsAsync(myListingIds, sellerClubId, cancellationToken);

        var bids = await (
            from bid in _dbContext.TransferBids
            join listing in _dbContext.TransferListings on bid.ListingId equals listing.Id
            join player in _dbContext.Players on listing.PlayerId equals player.Id
            where bid.BidderClubId == sellerClubId
            orderby bid.PlacedAt descending, bid.Id descending
            select new MyBidRow(
                bid.ListingId,
                listing.PlayerId,
                player.FullName,
                bid.AmountMinor,
                bid.Status,
                listing.EndsAt))
            .Take(100)
            .ToListAsync(cancellationToken);

        return new MyMarketActivity(myListings, bids, season.GameYear);
    }

    /// <inheritdoc />
    public async Task<TransferHistoryPage> GetHistoryAsync(
        Guid? cursorId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            from outcome in _dbContext.TransferOutcomes
            join player in _dbContext.Players on outcome.PlayerId equals player.Id
            join seller in _dbContext.Clubs on outcome.SellerClubId equals seller.Id
            join buyer in _dbContext.Clubs on outcome.BuyerClubId equals buyer.Id
            select new
            {
                outcome.ListingId,
                outcome.PlayerId,
                PlayerName = player.FullName,
                outcome.SellerClubId,
                SellerClubName = seller.Name,
                outcome.BuyerClubId,
                BuyerClubName = buyer.Name,
                outcome.FeeMinor,
                outcome.ResolvedAt,
            };

        if (cursorId is { } cursor)
        {
            query = query.Where(row => row.ListingId.CompareTo(cursor) < 0);
        }

        var rows = await query
            .OrderByDescending(row => row.ListingId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows[..pageSize] : rows;

        var transfers = page
            .Select(row => new TransferHistoryRow(
                row.ListingId,
                row.PlayerId,
                row.PlayerName,
                row.SellerClubId,
                row.SellerClubName,
                row.BuyerClubId,
                row.BuyerClubName,
                row.FeeMinor,
                row.ResolvedAt))
            .ToList();

        return new TransferHistoryPage(
            transfers,
            hasMore && page.Count > 0 ? MarketCursor.EncodeId(page[^1].ListingId) : null);
    }

    /// <inheritdoc />
    public Task<bool> HasOpenListingAsync(Guid playerId, CancellationToken cancellationToken) =>
        _dbContext.TransferListings.AnyAsync(
            listing => listing.PlayerId == playerId && listing.Status == ListingStatus.Open,
            cancellationToken);

    /// <summary>Projects a page of listings with their leading amount, bidder count, and the viewer's own bid.</summary>
    private async Task<IReadOnlyList<ListingRow>> ListingsAsync(
        List<Guid> listingIds,
        Guid? viewerClubId,
        CancellationToken cancellationToken)
    {
        if (listingIds.Count == 0)
        {
            return [];
        }

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);
        var gameYear = season?.GameYear ?? 0;

        var baseRows = await (
            from listing in _dbContext.TransferListings
            join player in _dbContext.Players on listing.PlayerId equals player.Id
            join club in _dbContext.Clubs on listing.SellerClubId equals club.Id
            where listingIds.Contains(listing.Id)
            select new
            {
                Listing = listing,
                Player = player,
                SellerClubName = club.Name,
            })
            .ToListAsync(cancellationToken);

        var aggregates = await _dbContext.TransferBids
            .Where(bid => listingIds.Contains(bid.ListingId))
            .GroupBy(bid => bid.ListingId)
            .Select(group => new
            {
                ListingId = group.Key,
                BidderCount = group.Count(),
                LeadingAmount = group
                    .Where(bid => bid.Status == BidStatus.Leading)
                    .Select(bid => (long?)bid.AmountMinor)
                    .FirstOrDefault(),
                LeadingClub = group
                    .Where(bid => bid.Status == BidStatus.Leading)
                    .Select(bid => (Guid?)bid.BidderClubId)
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(row => row.ListingId, cancellationToken);

        return baseRows
            .Select(row =>
            {
                aggregates.TryGetValue(row.Listing.Id, out var aggregate);
                var viewerBid = viewerClubId is { } viewer
                    && aggregate is not null
                    && aggregate.LeadingClub == viewer
                        ? aggregate.LeadingAmount
                        : null;

                return new ListingRow(
                    row.Listing.Id,
                    row.Listing.PlayerId,
                    row.Player.FullName,
                    row.Player.ShortName,
                    row.Player.PrimaryPosition,
                    row.Player.BirthGameYear,
                    row.Listing.SellerClubId,
                    row.SellerClubName,
                    row.Listing.MinimumFeeMinor,
                    row.Listing.GeneratedBuyerWageMinor,
                    row.Listing.GeneratedContractSeasons,
                    row.Listing.OpensAt,
                    row.Listing.EndsAt,
                    row.Listing.Status,
                    aggregate?.LeadingAmount,
                    aggregate?.BidderCount ?? 0,
                    viewerBid,
                    gameYear,
                    row.Listing.Version);
            })
            .OrderByDescending(row => row.ListingId)
            .ToList();
    }

    private async Task<HashSet<Guid>> ListedPlayerIdsAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        var ids = await _dbContext.TransferListings
            .Where(listing => playerIds.Contains(listing.PlayerId) && listing.Status == ListingStatus.Open)
            .Select(listing => listing.PlayerId)
            .ToListAsync(cancellationToken);

        return [.. ids];
    }

    private static int MeanAbility(PlayerAttributes attributes)
    {
        var values = attributes.ToSet().Values;

        return values.Count == 0
            ? 1
            : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }

    private static IEnumerable<RankedCandidate> Order(IEnumerable<RankedCandidate> rows, PlayerSort sort) =>
        sort switch
        {
            PlayerSort.Age => rows.OrderBy(row => row.Candidate.Age)
                .ThenBy(row => row.Candidate.Player.Id),
            PlayerSort.Ability => rows.OrderByDescending(row => row.Ability)
                .ThenBy(row => row.Candidate.Player.Id),
            _ => rows.OrderBy(row => row.Candidate.Player.FullName, StringComparer.Ordinal)
                .ThenBy(row => row.Candidate.Player.Id),
        };

    private static IEnumerable<RankedCandidate> ApplyCursor(
        IEnumerable<RankedCandidate> ordered,
        PlayerSearchFilter filter)
    {
        if (filter.Cursor is not { } cursor)
        {
            return ordered;
        }

        return filter.Sort switch
        {
            PlayerSort.Age => ordered.Where(row =>
                row.Candidate.Age > ParseKey(cursor)
                || (row.Candidate.Age == ParseKey(cursor)
                    && row.Candidate.Player.Id.CompareTo(cursor.PlayerId) > 0)),
            PlayerSort.Ability => ordered.Where(row =>
                row.Ability < ParseKey(cursor)
                || (row.Ability == ParseKey(cursor)
                    && row.Candidate.Player.Id.CompareTo(cursor.PlayerId) > 0)),
            _ => ordered.Where(row =>
                string.CompareOrdinal(row.Candidate.Player.FullName, cursor.SortKey) > 0
                || (string.Equals(row.Candidate.Player.FullName, cursor.SortKey, StringComparison.Ordinal)
                    && row.Candidate.Player.Id.CompareTo(cursor.PlayerId) > 0)),
        };
    }

    private static int ParseKey(SearchCursorPosition cursor) =>
        int.Parse(cursor.SortKey, System.Globalization.CultureInfo.InvariantCulture);

    private static string SortKeyOf(PlayerSort sort, RankedCandidate row) => sort switch
    {
        PlayerSort.Age => row.Candidate.Age.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
        PlayerSort.Ability => row.Ability.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
        _ => row.Candidate.Player.FullName,
    };

    private sealed record Candidate
    {
        public Player Player { get; init; } = null!;

        public PlayerAttributes Attributes { get; init; } = null!;

        public Guid ClubId { get; init; }

        public string ClubName { get; init; } = string.Empty;

        public int TierNumber { get; init; }

        public Guid DivisionId { get; init; }

        public string DivisionName { get; init; } = string.Empty;

        public int Age { get; init; }
    }

    private sealed record RankedCandidate(Candidate Candidate, int Ability);
}
