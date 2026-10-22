using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The AI market evaluation's read (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// A handful of flat queries rather than one graph, the same shape the AI club evaluation uses: the clubs,
/// their placements, their accounts, their squads, and the open listings with their leading bids come back
/// in a fixed number of round trips however deep the pyramid gets.
/// </para>
/// <para>
/// "AI-controlled" is the absence of an open tenure, and open spans <c>active</c> and <c>inactive</c>
/// (`OCC-8`): an inactive tenure still occupies its club, so the club's market is not the AI's to run while
/// a manager may still return.
/// </para>
/// </remarks>
internal sealed class AiMarketRepository : IAiMarketRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public AiMarketRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiMarketClubRow>> LoadAiClubsAsync(CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return [];
        }

        var clubIds = await _dbContext.Clubs
            .Where(club => club.Status == ClubStatus.Active
                && !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed))
            .OrderBy(club => club.Id)
            .Select(club => club.Id)
            .ToListAsync(cancellationToken);

        if (clubIds.Count == 0)
        {
            return [];
        }

        var placements = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where clubIds.Contains(entry.ClubId) && divisionSeason.SeasonId == season.SeasonId
            select new { entry.ClubId, division.TierNumber })
            .ToListAsync(cancellationToken);

        var placedClubs = placements.Select(row => row.ClubId).ToHashSet();

        var accounts = await _dbContext.ClubAccounts
            .Where(account => clubIds.Contains(account.ClubId))
            .Select(account => new { account.ClubId, account.CashMinor, account.ReservedMinor, account.FinancialState })
            .ToListAsync(cancellationToken);

        var accountByClub = accounts.ToDictionary(row => row.ClubId);

        // The wages of the players a club leads the bidding on join its bill the moment the bid wins, so a pass counts them now.
        var leadingWages = (await (
                from bid in _dbContext.TransferBids
                join listing in _dbContext.TransferListings on bid.ListingId equals listing.Id
                where bid.Status == BidStatus.Leading && clubIds.Contains(bid.BidderClubId) && listing.Status == ListingStatus.Open
                select new { bid.BidderClubId, listing.GeneratedBuyerSeasonWageMinor })
            .ToListAsync(cancellationToken))
            .GroupBy(row => row.BidderClubId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.GeneratedBuyerSeasonWageMinor));

        // A player is registered when an active contract and an active registration agree on the club
        // (SQ-6, SQ-7). The whole entities are read rather than a projection because the attributes' value
        // set is computed, not a column.
        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join registration in _dbContext.PlayerRegistrations on player.Id equals registration.PlayerId
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            where clubIds.Contains(contract.ClubId)
                && contract.Status == ContractStatus.Active
                && registration.ClubId == contract.ClubId
                && registration.Status == RegistrationStatus.Active
            select new { ClubId = contract.ClubId, Player = player, Attributes = attributes, Contract = contract })
            .ToListAsync(cancellationToken);

        var playerIds = rows.Select(row => row.Player.Id).ToList();

        var listedIds = playerIds.Count == 0
            ? []
            : (await _dbContext.TransferListings
                .Where(listing => listing.Status == ListingStatus.Open && playerIds.Contains(listing.PlayerId))
                .Select(listing => listing.PlayerId)
                .ToListAsync(cancellationToken)).ToHashSet();

        var byClub = rows.GroupBy(row => row.ClubId).ToDictionary(group => group.Key, group => group.ToList());

        var clubs = new List<AiMarketClubRow>(clubIds.Count);

        foreach (var clubId in clubIds)
        {
            // A club with no placement in the season being played is not a market participant.
            if (!placedClubs.Contains(clubId))
            {
                continue;
            }

            accountByClub.TryGetValue(clubId, out var account);

            var squad = byClub.TryGetValue(clubId, out var members) ? members : [];

            clubs.Add(new AiMarketClubRow(
                clubId,
                account is null ? 0 : account.CashMinor - account.ReservedMinor,
                season.SequenceNumber,
                account?.FinancialState ?? FinancialStateKind.Healthy,
                leadingWages.GetValueOrDefault(clubId),
                [.. squad
                    .OrderBy(row => row.Player.Id)
                    .Select(row => new AiMarketPlayerRow(
                        row.Player.Id,
                        PlayerPositions.FamilyOf(row.Player.PrimaryPosition),
                        row.Attributes.ToSet().MeanTenths,
                        season.GameYear - row.Player.BirthGameYear,
                        row.Contract.SquadStatus,
                        row.Contract.EndSeasonNumber,
                        row.Contract.SeasonWageMinor,
                        listedIds.Contains(row.Player.Id)))]));
        }

        return clubs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiMarketListingRow>> LoadOpenListingsAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from listing in _dbContext.TransferListings
            join player in _dbContext.Players on listing.PlayerId equals player.Id
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            where listing.Status == ListingStatus.Open
                && listing.OpensAt < before

                // The AI never sees a free agent: it reads hidden potential and would find every gem (FA-5).
                && listing.FreeAgentBatchId == null
            orderby listing.Id
            select new { Listing = listing, Player = player, Attributes = attributes })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var listingIds = rows.Select(row => row.Listing.Id).ToList();

        var leading = await _dbContext.TransferBids
            .Where(bid => bid.Status == BidStatus.Leading && listingIds.Contains(bid.ListingId))
            .Select(bid => new { bid.ListingId, bid.BidderClubId, bid.AmountMinor })
            .ToListAsync(cancellationToken);

        var leadingByListing = leading.ToDictionary(
            bid => bid.ListingId,
            bid => (bid.BidderClubId, bid.AmountMinor));

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);
        var gameYear = season?.GameYear ?? 0;

        var listings = new List<AiMarketListingRow>(rows.Count);

        foreach (var row in rows)
        {
            Guid? leadingClubId = null;
            long? leadingAmount = null;

            if (leadingByListing.TryGetValue(row.Listing.Id, out var lead))
            {
                leadingClubId = lead.BidderClubId;
                leadingAmount = lead.AmountMinor;
            }

            listings.Add(new AiMarketListingRow(
                row.Listing.Id,
                row.Listing.SellerClubId!.Value,
                leadingClubId,
                row.Player.Id,
                PlayerPositions.FamilyOf(row.Player.PrimaryPosition),
                row.Attributes.ToSet().MeanTenths,
                gameYear - row.Player.BirthGameYear,
                row.Listing.MinimumFeeMinor,
                leadingAmount,
                row.Listing.GeneratedBuyerSeasonWageMinor));
        }

        return listings;
    }
}

/// <summary>Persistence for the AI market decision record (`TRF-12`, master plan §6.7).</summary>
internal sealed class AiMarketDecisionRepository : IAiMarketDecisionRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public AiMarketDecisionRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(AiMarketDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        _dbContext.AiMarketDecisions.Add(decision);
    }
}
