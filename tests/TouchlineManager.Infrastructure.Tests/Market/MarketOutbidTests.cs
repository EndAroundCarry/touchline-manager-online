using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// The outbid path against real PostgreSQL 17 (`TRF-7`, `FIN-10`).
/// </summary>
/// <remarks>
/// This is the case the market's other suites never reached: a second club outbidding the first. It is the
/// one place a bid moves money off an account that is not the caller's — the displaced leader's reservation
/// has to come off the account that actually holds it — and the failure it guards against is a 500 that
/// leaves a stale reservation behind.
/// </remarks>
/// <remarks>
/// Its own world per test, started in <see cref="InitializeAsync"/>, rather than a shared collection fixture:
/// a bid changes two accounts and a listing, and a shared world would let one test's reservations decide
/// another's outcome.
/// </remarks>
public sealed class MarketOutbidTests : IAsyncLifetime, IDisposable
{
    private const long OpeningBid = 1_000_000;
    private const long RaisedBid = 1_250_000;
    private const string OutbidIdempotencyKey = "outbid-test";

    private readonly OutbidFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Outbidding_releases_the_displaced_reservation_and_keeps_both_accounts_sound()
    {
        var arrangement = await ArrangeAsync();

        await PlaceBidAsync(arrangement, arrangement.FirstBidderClubId, OpeningBid, "outbid-first");
        await PlaceBidAsync(arrangement, arrangement.SecondBidderClubId, RaisedBid, "outbid-second");

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var displaced = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.FirstBidderClubId);
        displaced.ReservedMinor.Should().Be(0, "the displaced reservation is released (TRF-7)");
        displaced.CashMinor.Should().Be(arrangement.FirstBidderCash, "a reservation never moves cash (FIN-10)");

        var leading = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.SecondBidderClubId);
        leading.ReservedMinor.Should().Be(RaisedBid);
        leading.CashMinor.Should().Be(arrangement.SecondBidderCash);

        var bids = await db.TransferBids
            .Where(bid => bid.ListingId == arrangement.ListingId)
            .ToListAsync();

        bids.Should().ContainSingle(bid => bid.Status == BidStatus.Outbid);
        bids.Should().ContainSingle(bid => bid.Status == BidStatus.Leading)
            .Which.AmountMinor.Should().Be(RaisedBid);

        // The ledger still replays to the balances it is the source of, on both accounts (FIN-18).
        foreach (var account in new[] { displaced, leading })
        {
            var replay = await db.LedgerEntries
                .Where(entry => entry.ClubId == account.ClubId)
                .Select(entry => new { entry.CashDeltaMinor, entry.ReservedDeltaMinor })
                .ToListAsync();

            replay.Sum(entry => entry.CashDeltaMinor).Should().Be(account.CashMinor);
            replay.Sum(entry => entry.ReservedDeltaMinor).Should().Be(account.ReservedMinor);
        }
    }

    private async Task PlaceBidAsync(
        Arrangement arrangement,
        Guid bidderClubId,
        long amountMinor,
        string idempotencyKey)
    {
        await using var scope = _fixture.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IBidWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var write = await writer.BidAsync(
            bidderClubId,
            MarketActor.Service(OutbidIdempotencyKey),
            arrangement.ListingId,
            amountMinor,
            idempotencyKey,
            CancellationToken.None);

        write.Created.Should().BeTrue("the bid is the first this club makes on the listing");

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Arrangement> ArrangeAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var sellerClubId = await db.Clubs.OrderBy(club => club.Name).Select(club => club.Id).FirstAsync();
        var playerId = await db.PlayerContracts
            .Where(contract => contract.ClubId == sellerClubId && contract.Status == ContractStatus.Active)
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();

        var bidderClubIds = await db.Clubs
            .Where(club => club.Id != sellerClubId)
            .OrderByDescending(club => club.Name)
            .Select(club => club.Id)
            .Take(2)
            .ToListAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            OpeningBid,
            generatedBuyerWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now,
            idempotencyKey: null,
            now: now);

        db.TransferListings.Add(listing);

        var firstBidderCash = await db.ClubAccounts
            .Where(account => account.ClubId == bidderClubIds[0])
            .Select(account => account.CashMinor)
            .SingleAsync();

        var secondBidderCash = await db.ClubAccounts
            .Where(account => account.ClubId == bidderClubIds[1])
            .Select(account => account.CashMinor)
            .SingleAsync();

        await db.SaveChangesAsync(CancellationToken.None);

        return new Arrangement(
            listing.Id,
            bidderClubIds[0],
            bidderClubIds[1],
            firstBidderCash,
            secondBidderCash);
    }

    private sealed record Arrangement(
        Guid ListingId,
        Guid FirstBidderClubId,
        Guid SecondBidderClubId,
        long FirstBidderCash,
        long SecondBidderCash);
}

/// <summary>The outbid tests' own seeded world, one per test.</summary>
public sealed class OutbidFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public OutbidFixture()
        : base("touchline_outbid")
    {
    }
}
