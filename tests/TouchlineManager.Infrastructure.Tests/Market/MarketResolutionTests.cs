using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// The auction resolution transaction against real PostgreSQL 17 (`TRF-7`, `TRF-9`, `TRF-10`).
/// </summary>
/// <remarks>
/// It exercises the whole settlement: the winner pays, the seller is credited, and the player's contract and
/// registration move — atomically, and only once however many times the job is retried.
/// </remarks>
[Collection(MarketCollection.Name)]
public sealed class MarketResolutionTests : WorldTestBase
{
    private const long Fee = 1_000_000;

    /// <summary>Initializes the tests.</summary>
    public MarketResolutionTests(MarketFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_resolution_settles_the_winning_bid_and_moves_the_player()
    {
        var arrangement = await ArrangeAsync();
        await using var resolveScope = Fixture.CreateScope();

        var resolve = resolveScope.ServiceProvider.GetRequiredService<ResolveListing>();
        await resolve.ExecuteAsync(arrangement.ListingId, CancellationToken.None);

        await using var assertScope = Fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var listing = await db.TransferListings.SingleAsync(candidate => candidate.Id == arrangement.ListingId);
        listing.Status.Should().Be(ListingStatus.Sold, "TRF-10");

        var outcome = await db.TransferOutcomes.SingleAsync(candidate => candidate.ListingId == arrangement.ListingId);
        outcome.FeeMinor.Should().Be(Fee);
        outcome.BuyerClubId.Should().Be(arrangement.BuyerClubId);

        var buyer = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.BuyerClubId);
        buyer.CashMinor.Should().Be(arrangement.BuyerCashBefore - Fee, "FIN-8");
        buyer.ReservedMinor.Should().Be(0, "the winning reservation is consumed (TRF-7)");

        var seller = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.SellerClubId);
        seller.CashMinor.Should().Be(arrangement.SellerCashBefore + Fee, "FIN-6");

        var contract = await db.PlayerContracts.SingleAsync(candidate =>
            candidate.PlayerId == arrangement.PlayerId && candidate.Status == ContractStatus.Active);
        contract.ClubId.Should().Be(arrangement.BuyerClubId, "CON-5");
        contract.WeeklyWageMinor.Should().Be(arrangement.GeneratedWageMinor);

        var registration = await db.PlayerRegistrations.SingleAsync(candidate =>
            candidate.PlayerId == arrangement.PlayerId && candidate.Status == RegistrationStatus.Active);
        registration.ClubId.Should().Be(arrangement.BuyerClubId, "SQ-6");

        var old = await db.PlayerContracts.SingleAsync(candidate => candidate.Id == arrangement.OldContractId);
        old.Status.Should().Be(ContractStatus.Closed);
        old.ClosedReason.Should().Be(PlayerContractCloseReasons.Transferred, "CON-5");
    }

    [Fact]
    public async Task A_retried_resolution_does_not_settle_twice()
    {
        var arrangement = await ArrangeAsync();

        await using (var first = Fixture.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<ResolveListing>()
                .ExecuteAsync(arrangement.ListingId, CancellationToken.None);
        }

        await using (var second = Fixture.CreateScope())
        {
            await second.ServiceProvider.GetRequiredService<ResolveListing>()
                .ExecuteAsync(arrangement.ListingId, CancellationToken.None);
        }

        await using var assertScope = Fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var outcomes = await db.TransferOutcomes
            .CountAsync(candidate => candidate.ListingId == arrangement.ListingId);
        outcomes.Should().Be(1, "TRF-9: resolution happens once");

        var buyer = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.BuyerClubId);
        buyer.CashMinor.Should().Be(arrangement.BuyerCashBefore - Fee, "the retry must not charge again");
    }

    private async Task<Arrangement> ArrangeAsync()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = Fixture.Clock.UtcNow;

        var sellerClubId = await db.Clubs.OrderBy(club => club.Name).Select(club => club.Id).FirstAsync();
        var playerId = await db.PlayerContracts
            .Where(contract => contract.ClubId == sellerClubId
                && contract.Status == ContractStatus.Active
                && !db.TransferListings.Any(listing => listing.PlayerId == contract.PlayerId))
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();
        var buyerClubId = await db.Clubs
            .Where(club => club.Id != sellerClubId)
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .FirstAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            minimumFeeMinor: Fee,
            generatedBuyerWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now,
            idempotencyKey: null,
            now: now);

        db.TransferListings.Add(listing);

        var bidId = Guid.CreateVersion7();

        db.TransferBids.Add(TransferBid.Place(
            bidId,
            listing.Id,
            buyerClubId,
            Fee,
            LedgerPostings.ReservationCorrelationId(bidId, Fee),
            idempotencyKey: null,
            now));

        var buyerAccount = await db.ClubAccounts.SingleAsync(account => account.ClubId == buyerClubId);
        db.LedgerEntries.Add(buyerAccount.Post(
            LedgerPostings.BidReservation(Guid.CreateVersion7(), buyerClubId, bidId, Fee),
            now));

        var sellerAccount = await db.ClubAccounts.SingleAsync(account => account.ClubId == sellerClubId);

        var oldContractId = await db.PlayerContracts
            .Where(contract => contract.PlayerId == playerId && contract.Status == ContractStatus.Active)
            .Select(contract => contract.Id)
            .SingleAsync();

        var arrangement = new Arrangement(
            listing.Id,
            playerId,
            sellerClubId,
            buyerClubId,
            oldContractId,
            buyerAccount.CashMinor,
            sellerAccount.CashMinor,
            listing.GeneratedBuyerWageMinor);

        await db.SaveChangesAsync();

        return arrangement;
    }

    private sealed record Arrangement(
        Guid ListingId,
        Guid PlayerId,
        Guid SellerClubId,
        Guid BuyerClubId,
        Guid OldContractId,
        long BuyerCashBefore,
        long SellerCashBefore,
        long GeneratedWageMinor);
}
