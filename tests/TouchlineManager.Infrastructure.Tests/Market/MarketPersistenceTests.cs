using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// The market schema's database-level guarantees, against real PostgreSQL 17 (`TRF-6`, `TRF-8`, `TRF-9`,
/// `TRF-14`).
/// </summary>
/// <remarks>
/// These are the invariants an aggregate cannot provide on its own: two partial unique indexes are what stop
/// a player holding two open listings or a club holding two leading bids when two requests race, and the
/// database-assigned sequence is what breaks a tie between equal amounts.
/// </remarks>
[Collection(MarketCollection.Name)]
public sealed class MarketPersistenceTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public MarketPersistenceTests(MarketFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_player_cannot_hold_two_open_listings()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (sellerClubId, playerId, _) = await PickAsync(db);

        db.TransferListings.Add(Listing(sellerClubId, playerId));
        db.TransferListings.Add(Listing(sellerClubId, playerId));

        var act = async () => await db.SaveChangesAsync();

        ConstraintOf(await act.Should().ThrowAsync<DbUpdateException>())
            .Should().Be("ux_transfer_listings_open_player", "TRF-14");
    }

    [Fact]
    public async Task A_club_cannot_hold_two_leading_bids_on_one_listing()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (sellerClubId, playerId, buyerClubId) = await PickAsync(db);

        var listing = Listing(sellerClubId, playerId);
        db.TransferListings.Add(listing);
        await db.SaveChangesAsync();

        db.TransferBids.Add(Bid(listing.Id, buyerClubId));
        db.TransferBids.Add(Bid(listing.Id, buyerClubId));

        var act = async () => await db.SaveChangesAsync();

        ConstraintOf(await act.Should().ThrowAsync<DbUpdateException>())
            .Should().Be("ux_transfer_bids_leading_listing_club", "TRF-6");
    }

    [Fact]
    public async Task A_listing_resolves_once()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (sellerClubId, playerId, buyerClubId) = await PickAsync(db);
        var now = Fixture.Clock.UtcNow;

        var listing = Listing(sellerClubId, playerId);
        db.TransferListings.Add(listing);
        await db.SaveChangesAsync();

        var win = Guid.CreateVersion7();

        db.TransferOutcomes.Add(Outcome(listing.Id, win, playerId, sellerClubId, buyerClubId, now));
        db.TransferOutcomes.Add(Outcome(listing.Id, win, playerId, sellerClubId, buyerClubId, now));

        var act = async () => await db.SaveChangesAsync();

        ConstraintOf(await act.Should().ThrowAsync<DbUpdateException>())
            .Should().Be("ux_transfer_outcomes_listing", "TRF-9: resolution happens once");
    }

    [Fact]
    public async Task A_bid_is_given_a_database_sequence()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (sellerClubId, playerId, buyerClubId) = await PickAsync(db);

        var listing = Listing(sellerClubId, playerId);
        db.TransferListings.Add(listing);
        await db.SaveChangesAsync();

        var bid = Bid(listing.Id, buyerClubId);
        db.TransferBids.Add(bid);
        await db.SaveChangesAsync();

        bid.BidSequence.Should().BeGreaterThanOrEqualTo(1, "TRF-8: the sequence is database-assigned");
    }

    private TransferListing Listing(Guid sellerClubId, Guid playerId)
    {
        var now = Fixture.Clock.UtcNow;

        return TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            minimumFeeMinor: 1_000_000,
            generatedBuyerWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now.AddDays(1),
            idempotencyKey: null,
            now: now);
    }

    private TransferBid Bid(Guid listingId, Guid bidderClubId) => TransferBid.Place(
        Guid.CreateVersion7(),
        listingId,
        bidderClubId,
        amountMinor: 1_000_000,
        reservationCorrelationId: $"bid:{Guid.NewGuid():N}:reserve",
        idempotencyKey: null,
        now: Fixture.Clock.UtcNow);

    private static TransferOutcome Outcome(
        Guid listingId,
        Guid winningBidId,
        Guid playerId,
        Guid sellerClubId,
        Guid buyerClubId,
        DateTimeOffset now) =>
        TransferOutcome.Record(
            Guid.CreateVersion7(),
            listingId,
            winningBidId,
            playerId,
            sellerClubId,
            buyerClubId,
            feeMinor: 1_000_000,
            oldContractId: Guid.CreateVersion7(),
            newContractId: Guid.CreateVersion7(),
            correlationId: $"transfer:{listingId:D}",
            now);

    private static async Task<(Guid SellerClubId, Guid PlayerId, Guid BuyerClubId)> PickAsync(
        TouchlineManagerDbContext db)
    {
        var seller = await db.Clubs.OrderBy(club => club.Name).Select(club => club.Id).FirstAsync();

        // A player with no listing history, so a leftover open listing from another test in the shared world
        // cannot collide with this one (TRF-14).
        var player = await db.PlayerContracts
            .Where(contract => contract.ClubId == seller
                && contract.Status == ContractStatus.Active
                && !db.TransferListings.Any(listing => listing.PlayerId == contract.PlayerId))
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();

        var buyer = await db.Clubs
            .Where(club => club.Id != seller)
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .FirstAsync();

        return (seller, player, buyer);
    }

    private static string? ConstraintOf(
        FluentAssertions.Specialized.ExceptionAssertions<DbUpdateException> exception) =>
        (exception.Which.InnerException as PostgresException)?.ConstraintName;
}
