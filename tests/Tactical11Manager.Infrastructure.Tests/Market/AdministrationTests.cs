using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Market;

/// <summary>
/// A club in the red or in administration against real PostgreSQL 17: it may not bid, its board lists its highest earners with a flag its
/// manager cannot lift, and a player nobody buys is released with his agent paying the club (`DEBT-3`, `DEBT-4`).
/// </summary>
/// <remarks>
/// Its own world per test, because the tests put clubs into debt and a shared world would let one test's debt decide another's outcome.
/// </remarks>
public sealed class AdministrationTests : IAsyncLifetime, IDisposable
{
    private readonly AdministrationFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_club_in_the_red_cannot_bid_even_with_the_cash_to_pay()
    {
        var (sellerClubId, bidderClubId, listingId) = await ArrangeListingAsync();

        // The state is what the embargo reads, not the cash: the club still holds more than the fee.
        await SetStateAsync(bidderClubId, FinancialStateKind.InTheRed);

        var write = await BidAsync(bidderClubId, listingId, 1_000_000);

        write.Outcome.Should().Be(MarketOutcome.Embargo);
        write.Created.Should().BeFalse();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.TransferBids.CountAsync(bid => bid.ListingId == listingId)).Should().Be(0, "nothing was placed");
        (await db.ClubAccounts.SingleAsync(account => account.ClubId == bidderClubId)).ReservedMinor.Should().Be(0);

        // Out of the red, the same bid goes through.
        await SetStateAsync(bidderClubId, FinancialStateKind.Healthy);

        (await BidAsync(bidderClubId, listingId, 1_000_000)).Outcome.Should().Be(MarketOutcome.Found);
        sellerClubId.Should().NotBe(bidderClubId);
    }

    [Fact]
    public async Task The_board_of_a_club_in_administration_lists_its_highest_earners_and_releases_the_ones_nobody_buys()
    {
        var (clubId, _, _) = await ArrangeListingAsync();

        // A fair template squad costs little (plan cheeky-dongarra), so the club is made to pay five times the wages: the board only lists
        // players while the bill is over the wage budget.
        await using (var wageScope = _fixture.CreateScope())
        {
            await wageScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"update squad.player_contracts set season_wage_minor = season_wage_minor * 5 where club_id = {clubId} and status = 'active'");
        }

        var (limit, cashBefore) = await PutInAdministrationAsync(clubId);

        await using (var scope = _fixture.CreateScope())
        {
            var states = scope.ServiceProvider.GetRequiredService<ClubFinancialStates>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await using var transaction = await unitOfWork.BeginTransactionAsync(TransactionIsolation.ReadCommitted, CancellationToken.None);

            var result = await states.ApplyAsync([clubId], MatchdayOrdinal.Of(1, 5), "test", _fixture.Clock.UtcNow, CancellationToken.None);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);

            result.Listed.Should().BeInRange(1, EconomyRuleSet.AdministrationMaxListings);
        }

        List<TransferListing> listings;
        List<Guid> listedPlayerIds;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            listings = await db.TransferListings.Where(listing => listing.SellerClubId == clubId && listing.ListedByBoard).ToListAsync();
            listedPlayerIds = [.. listings.Select(listing => listing.PlayerId)];

            listings.Should().NotBeEmpty().And.OnlyContain(listing => listing.IsOpen);

            // The highest earners go first: nobody left unlisted earns more than the cheapest of the listed.
            var wages = await db.PlayerContracts
                .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
                .ToDictionaryAsync(contract => contract.PlayerId, contract => contract.SeasonWageMinor);

            var cheapestListed = listedPlayerIds.Min(id => wages[id]);

            // A player the club had already put on the market is not the board's to choose, so he is left out of the comparison.
            var alreadyListed = await db.TransferListings
                .Where(candidate => candidate.SellerClubId == clubId)
                .Select(candidate => candidate.PlayerId)
                .ToListAsync();

            wages.Where(pair => !alreadyListed.Contains(pair.Key)).Should().OnlyContain(pair => pair.Value <= cheapestListed);

            (await db.ClubAccounts.SingleAsync(account => account.ClubId == clubId)).FinancialState
                .Should().Be(FinancialStateKind.Administration);
        }

        // Nobody bids. At the window's end the board releases the first player, and his agent pays the club 30% of his value.
        var listing = listings[0];
        var playerId = listing.PlayerId;
        var value = listing.MinimumFeeMinor * EconomyRuleSet.OneHundredPercentBp / EconomyRuleSet.AdministrationAskingBp;
        var fee = EconomyRuleSet.AdministrationReleaseFeeMinor(value);

        await using (var scope = _fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ResolveListing>().ExecuteAsync(listing.Id, CancellationToken.None);
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.TransferListings.SingleAsync(candidate => candidate.Id == listing.Id)).Status.Should().Be(ListingStatus.Expired);

            var contract = await db.PlayerContracts.SingleAsync(candidate => candidate.PlayerId == playerId && candidate.ClubId == clubId);

            contract.Status.Should().Be(ContractStatus.Closed);
            contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Released);

            var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

            account.CashMinor.Should().Be(cashBefore + fee, "the agent paid the club; no payout was owed to the player");

            var entry = await db.LedgerEntries.SingleAsync(candidate => candidate.ClubId == clubId && candidate.SourceId == listing.Id);

            entry.Category.Should().Be(LedgerCategory.TransferProceeds);
            entry.CashDeltaMinor.Should().Be(fee);
            limit.Should().BePositive();
        }
    }

    private async Task<(Guid SellerClubId, Guid BidderClubId, Guid ListingId)> ArrangeListingAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var sellerClubId = await db.Clubs.OrderBy(club => club.Name).Select(club => club.Id).FirstAsync();
        var bidderClubId = await db.Clubs.Where(club => club.Id != sellerClubId).OrderByDescending(club => club.Name).Select(club => club.Id).FirstAsync();
        var playerId = await db.PlayerContracts
            .Where(contract => contract.ClubId == sellerClubId && contract.Status == ContractStatus.Active)
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            1_000_000,
            generatedBuyerSeasonWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now.AddDays(1),
            idempotencyKey: null,
            now: now);

        db.TransferListings.Add(listing);
        await db.SaveChangesAsync(CancellationToken.None);

        return (sellerClubId, bidderClubId, listing.Id);
    }

    private async Task SetStateAsync(Guid clubId, FinancialStateKind state)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

        account.RecordState(new FinancialStep(state, 1, FinancialTransition.None, Dismiss: false, EmergencyGrantMinor: 0), _fixture.Clock.UtcNow);

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Takes a club's cash below minus its overdraft limit, through a recurring cost, as the matchday would.</summary>
    private async Task<(long Limit, long Cash)> PutInAdministrationAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);
        var now = _fixture.Clock.UtcNow;
        const long limit = 5_000_000;

        account.SetOverdraft(limit, now);

        db.LedgerEntries.Add(account.Post(
            LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, new DateOnly(2026, 9, 1), account.CashMinor + limit + 1_000_000),
            now));

        await db.SaveChangesAsync(CancellationToken.None);

        account.CashMinor.Should().Be(-(limit + 1_000_000));

        return (limit, account.CashMinor);
    }

    private async Task<BidWriteResult> BidAsync(Guid bidderClubId, Guid listingId, long amountMinor)
    {
        await using var scope = _fixture.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IBidWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Bids take a transaction-scoped listing lock, so the writer is called inside a transaction (ADR-0026).
        await using var transaction = await unitOfWork.BeginTransactionAsync(TransactionIsolation.ReadCommitted, CancellationToken.None);

        var write = await writer.BidAsync(
            bidderClubId,
            MarketActor.Service("administration-test"),
            listingId,
            amountMinor,
            idempotencyKey: null,
            CancellationToken.None);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        return write;
    }
}

/// <summary>The administration tests' own seeded world, one per test.</summary>
public sealed class AdministrationFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public AdministrationFixture()
        : base("tactical11_administration")
    {
    }
}
