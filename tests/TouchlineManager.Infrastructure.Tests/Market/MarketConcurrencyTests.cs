using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// The market's concurrency guarantees against real PostgreSQL 17 (`TRF-6`, `TRF-9`, `FIN-10`).
/// </summary>
/// <remarks>
/// <para>
/// The Stage 10 exit criterion is that concurrent bids and resolutions produce a deterministic winner and
/// exact money movement. Determinism and money are already covered sequentially, so this suite runs the
/// contending operations <em>for real</em> — two transactions on two connections — rather than simulating
/// the race: a serializable resolution and a unique index are database facts, and a test that does not
/// actually race proves nothing about them.
/// </para>
/// <para>
/// A loser may surface as a serialization failure or a unique-constraint violation, which is the intended
/// outcome rather than a bug: the winner is whoever commits, and the loser's retry is a no-op. The tests
/// tolerate that refusal and assert the invariants that must hold regardless of which transaction won.
/// </para>
/// </remarks>
[Collection(MarketCollection.Name)]
public sealed class MarketConcurrencyTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public MarketConcurrencyTests(MarketFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Two_concurrent_resolutions_settle_the_listing_exactly_once()
    {
        var arrangement = await ArrangeListingWithLeadingBidAsync();

        await Task.WhenAll(
            ResolveIgnoringFailureAsync(arrangement.ListingId),
            ResolveIgnoringFailureAsync(arrangement.ListingId));

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.TransferOutcomes.CountAsync(outcome => outcome.ListingId == arrangement.ListingId))
            .Should().Be(1, "TRF-9: a listing resolves once however many workers race it");

        var buyer = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.FirstBidderClubId);
        buyer.CashMinor.Should().Be(arrangement.BuyerCashBefore - arrangement.Fee, "FIN-8: charged once");
        buyer.ReservedMinor.Should().Be(0, "TRF-7: the winning reservation is consumed once");

        var seller = await db.ClubAccounts.SingleAsync(account => account.ClubId == arrangement.SellerClubId);
        seller.CashMinor.Should().Be(arrangement.SellerCashBefore + arrangement.Fee, "FIN-6: credited once");

        await AssertLedgerReplaysAsync(db, buyer.ClubId);
        await AssertLedgerReplaysAsync(db, seller.ClubId);
    }

    [Fact]
    public async Task Two_concurrent_bids_leave_exactly_one_leader_and_one_reservation()
    {
        var arrangement = await ArrangeOpenListingAsync();

        var results = await Task.WhenAll(
            BidIgnoringFailureAsync(arrangement, arrangement.FirstBidderClubId, 1_000_000),
            BidIgnoringFailureAsync(arrangement, arrangement.SecondBidderClubId, 1_300_000));

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var bids = await db.TransferBids
            .Where(bid => bid.ListingId == arrangement.ListingId)
            .ToListAsync();

        bids.Count(bid => bid.Status == BidStatus.Leading)
            .Should().Be(1, "TRF-6: one club may lead a listing");

        var leadingAmount = bids.Single(bid => bid.Status == BidStatus.Leading).AmountMinor;

        var reserved = await db.ClubAccounts
            .Where(account => account.ClubId == arrangement.FirstBidderClubId
                || account.ClubId == arrangement.SecondBidderClubId)
            .SumAsync(account => account.ReservedMinor);

        reserved.Should().Be(leadingAmount, "TRF-7: only the leader's amount is held");

        // At least one bid took hold; the other either arrived second or was refused by the database's own
        // guard, and neither outcome may leave a stale reservation (FIN-10).
        results.Count(result => result.Settled).Should().BeGreaterThanOrEqualTo(1);

        foreach (var bidderClubId in new[] { arrangement.FirstBidderClubId, arrangement.SecondBidderClubId })
        {
            await AssertLedgerReplaysAsync(db, bidderClubId);
        }
    }

    /// <summary>Runs a resolution and swallows the loser's honest refusal.</summary>
    private async Task ResolveIgnoringFailureAsync(Guid listingId)
    {
        await using var scope = Fixture.CreateScope();

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ResolveListing>()
                .ExecuteAsync(listingId, CancellationToken.None);
        }
        catch (Exception exception) when (IsRaceRefusal(exception))
        {
            // Lost the race against the other resolution; the winner settled the listing.
        }
    }

    /// <summary>Runs a bid and swallows the loser's honest refusal.</summary>
    private async Task<BidAttempt> BidIgnoringFailureAsync(Arrangement arrangement, Guid bidderClubId, long amountMinor)
    {
        await using var scope = Fixture.CreateScope();

        try
        {
            var writer = scope.ServiceProvider.GetRequiredService<IBidWriter>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Bids take a transaction-scoped listing lock, so the writer is called inside a transaction
            // (ADR-0026).
            await using var transaction = await unitOfWork.BeginTransactionAsync(
                TransactionIsolation.ReadCommitted,
                CancellationToken.None);

            var write = await writer.BidAsync(
                bidderClubId,
                MarketActor.Service("concurrent-bid"),
                arrangement.ListingId,
                amountMinor,
                idempotencyKey: null,
                CancellationToken.None);

            if (write.Outcome == MarketOutcome.Found)
            {
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }

            await transaction.CommitAsync(CancellationToken.None);

            return new BidAttempt(write.Outcome == MarketOutcome.Found);
        }
        catch (Exception exception) when (IsRaceRefusal(exception))
        {
            return new BidAttempt(Settled: false);
        }
    }

    /// <summary>
    /// Whether an exception is the database refusing the losing side of a race rather than a real fault: a
    /// serialization failure (40001) or a unique-constraint violation (23505). EF and Npgsql wrap the
    /// <see cref="PostgresException"/> at varying depths, so the whole chain is inspected.
    /// </summary>
    private static bool IsRaceRefusal(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task AssertLedgerReplaysAsync(TouchlineManagerDbContext db, Guid clubId)
    {
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);
        var entries = await db.LedgerEntries
            .Where(entry => entry.ClubId == clubId)
            .Select(entry => new { entry.CashDeltaMinor, entry.ReservedDeltaMinor })
            .ToListAsync();

        entries.Sum(entry => entry.CashDeltaMinor).Should().Be(account.CashMinor, "FIN-18");
        entries.Sum(entry => entry.ReservedDeltaMinor).Should().Be(account.ReservedMinor, "FIN-18");
    }

    private async Task<Arrangement> ArrangeOpenListingAsync()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = Fixture.Clock.UtcNow;

        var (sellerClubId, playerId) = await PickSellerAsync(db);
        var bidders = await db.Clubs
            .Where(club => club.Id != sellerClubId)
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .Take(2)
            .ToListAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            minimumFeeMinor: 500_000,
            generatedBuyerWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now,
            idempotencyKey: null,
            now: now);

        db.TransferListings.Add(listing);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Arrangement(listing.Id, sellerClubId, bidders[0], bidders[1], playerId, Fee: 0, 0, 0);
    }

    private async Task<Arrangement> ArrangeListingWithLeadingBidAsync()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = Fixture.Clock.UtcNow;

        const long fee = 1_000_000;

        var (sellerClubId, playerId) = await PickSellerAsync(db);
        var buyerClubId = await db.Clubs
            .Where(club => club.Id != sellerClubId)
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .FirstAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            minimumFeeMinor: fee,
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
            fee,
            LedgerPostings.ReservationCorrelationId(bidId, fee),
            idempotencyKey: null,
            now));

        var buyerAccount = await db.ClubAccounts.SingleAsync(account => account.ClubId == buyerClubId);
        var sellerAccount = await db.ClubAccounts.SingleAsync(account => account.ClubId == sellerClubId);

        db.LedgerEntries.Add(buyerAccount.Post(
            LedgerPostings.BidReservation(Guid.CreateVersion7(), buyerClubId, bidId, fee),
            now));

        var arrangement = new Arrangement(
            listing.Id,
            sellerClubId,
            buyerClubId,
            SecondBidderClubId: Guid.Empty,
            playerId,
            Fee: fee,
            BuyerCashBefore: buyerAccount.CashMinor,
            SellerCashBefore: sellerAccount.CashMinor);

        await db.SaveChangesAsync(CancellationToken.None);

        return arrangement;
    }

    /// <summary>Finds a club and one of its players that holds no open listing, so tests do not collide.</summary>
    private static async Task<(Guid ClubId, Guid PlayerId)> PickSellerAsync(TouchlineManagerDbContext db)
    {
        var clubId = await db.Clubs
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .FirstAsync();

        var playerId = await db.PlayerContracts
            .Where(contract => contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && !db.TransferListings.Any(listing => listing.PlayerId == contract.PlayerId))
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();

        return (clubId, playerId);
    }

    private sealed record Arrangement(
        Guid ListingId,
        Guid SellerClubId,
        Guid FirstBidderClubId,
        Guid SecondBidderClubId,
        Guid PlayerId,
        long Fee,
        long BuyerCashBefore,
        long SellerCashBefore);

    private sealed record BidAttempt(bool Settled);
}
