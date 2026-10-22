using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.Market;

/// <summary>
/// Bidding on and signing a free agent over a real seeded world (`FA-5`).
/// </summary>
/// <remarks>
/// Its own world per test: every test signs or fails to sign a player, and one test's cash and squads would
/// decide the next test's.
/// </remarks>
public sealed class FreeAgentSettlementTests : IAsyncLifetime, IDisposable
{
    private static readonly DateOnly ThisWeek = new(2026, 9, 21);

    private readonly FreeAgentFixture _fixture = new();

    /// <summary>Starts a freshly seeded world with this week's batch open.</summary>
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        await using var scope = _fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<RunFreeAgentBatch>()
            .ExecuteAsync(ThisWeek, CancellationToken.None);
    }

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_winning_bid_signs_the_player_and_the_fee_leaves_the_game()
    {
        var target = await ArrangeAsync();
        var cashBefore = await TotalCashAsync();

        var write = await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor + 100);

        write.Outcome.Should().Be(MarketOutcome.Found);

        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fee = target.MinimumFeeMinor + 100;

        (await db.Players.SingleAsync(player => player.Id == target.PlayerId)).Status
            .Should().Be(PlayerStatus.Active);

        var contract = await db.PlayerContracts.SingleAsync(candidate =>
            candidate.PlayerId == target.PlayerId && candidate.Status == ContractStatus.Active);

        contract.ClubId.Should().Be(target.BuyerClubId);
        contract.SeasonWageMinor.Should().Be(target.WageMinor, "the wage frozen on the listing is the wage signed (CON-5)");
        contract.EndSeasonNumber.Should().Be(contract.StartSeasonNumber + target.ContractSeasons - 1);

        var currentSeason = await db.Seasons.SingleAsync(season => season.Status == SeasonStatus.Active);
        var registration = await db.PlayerRegistrations.SingleAsync(candidate =>
            candidate.PlayerId == target.PlayerId && candidate.Status == RegistrationStatus.Active);

        registration.ClubId.Should().Be(target.BuyerClubId);
        registration.EffectiveSeasonId.Should().Be(currentSeason.Id);

        var buyer = await db.ClubAccounts.SingleAsync(account => account.ClubId == target.BuyerClubId);

        buyer.CashMinor.Should().Be(target.BuyerCashBefore - fee);
        buyer.ReservedMinor.Should().Be(0, "the winning reservation is consumed");
        (await TotalCashAsync()).Should().Be(cashBefore - fee, "no club is credited: the fee leaves the game");

        var outcome = await db.TransferOutcomes.SingleAsync(candidate => candidate.ListingId == target.ListingId);

        outcome.SellerClubId.Should().BeNull();
        outcome.OldContractId.Should().BeNull();
        outcome.BuyerClubId.Should().Be(target.BuyerClubId);
        outcome.FeeMinor.Should().Be(fee);

        (await db.TransferListings.SingleAsync(listing => listing.Id == target.ListingId)).Status
            .Should().Be(ListingStatus.Sold);
    }

    [Fact]
    public async Task A_bid_placed_on_the_last_day_still_signs_him_at_the_close()
    {
        var target = await ArrangeAsync();

        _fixture.Clock.UtcNow = target.EndsAt.AddMinutes(-5);

        (await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor)).Outcome
            .Should().Be(MarketOutcome.Found);

        _fixture.Clock.UtcNow = target.EndsAt;

        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.PlayerContracts.CountAsync(candidate =>
            candidate.PlayerId == target.PlayerId && candidate.Status == ContractStatus.Active)).Should().Be(1);
    }

    [Fact]
    public async Task A_listing_without_a_bid_expires_and_the_player_stays_unsigned()
    {
        var target = await ArrangeAsync();

        _fixture.Clock.UtcNow = target.EndsAt;

        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.TransferListings.SingleAsync(listing => listing.Id == target.ListingId)).Status
            .Should().Be(ListingStatus.Expired);
        (await db.Players.SingleAsync(player => player.Id == target.PlayerId)).Status
            .Should().Be(PlayerStatus.FreeAgent);
        (await db.TransferOutcomes.CountAsync(outcome => outcome.ListingId == target.ListingId)).Should().Be(0);
        (await db.PlayerContracts.CountAsync(contract => contract.PlayerId == target.PlayerId)).Should().Be(0);
    }

    [Fact]
    public async Task A_full_squad_is_refused_at_bid_time()
    {
        var target = await ArrangeAsync();

        await FillSquadAsync(target.BuyerClubId, WorldRuleSet.SquadMaximumRegistered);

        var write = await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor);

        write.Outcome.Should().Be(MarketOutcome.SquadFull);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.TransferBids.CountAsync(bid => bid.ListingId == target.ListingId)).Should().Be(0);
    }

    [Fact]
    public async Task The_ai_may_not_bid_on_a_free_agent()
    {
        var target = await ArrangeAsync();

        var write = await BidAsync(
            target.ListingId,
            target.BuyerClubId,
            target.MinimumFeeMinor,
            MarketActor.Service("ai-test"));

        write.Outcome.Should().Be(MarketOutcome.HumanManagersOnly);
    }

    [Fact]
    public async Task A_squad_that_filled_after_the_bid_loses_the_signing_and_gets_its_money_back()
    {
        var target = await ArrangeAsync();

        (await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor)).Outcome
            .Should().Be(MarketOutcome.Found);

        await FillSquadAsync(target.BuyerClubId, WorldRuleSet.SquadMaximumRegistered);
        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.TransferListings.SingleAsync(listing => listing.Id == target.ListingId)).Status
            .Should().Be(ListingStatus.Expired);
        (await db.TransferBids.SingleAsync(bid => bid.ListingId == target.ListingId)).Status
            .Should().Be(BidStatus.Invalid);
        (await db.Players.SingleAsync(player => player.Id == target.PlayerId)).Status
            .Should().Be(PlayerStatus.FreeAgent);

        var buyer = await db.ClubAccounts.SingleAsync(account => account.ClubId == target.BuyerClubId);

        buyer.CashMinor.Should().Be(target.BuyerCashBefore, "nothing was paid");
        buyer.ReservedMinor.Should().Be(0, "the failed leader's reservation is released (FIN-10)");
    }

    [Fact]
    public async Task A_retried_resolution_does_not_sign_him_twice()
    {
        var target = await ArrangeAsync();

        await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor);
        await ResolveAsync(target.ListingId);
        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.TransferOutcomes.CountAsync(outcome => outcome.ListingId == target.ListingId)).Should().Be(1);
        (await db.PlayerContracts.CountAsync(contract => contract.PlayerId == target.PlayerId)).Should().Be(1);
        (await db.ClubAccounts.SingleAsync(account => account.ClubId == target.BuyerClubId)).CashMinor
            .Should().Be(target.BuyerCashBefore - target.MinimumFeeMinor, "the retry must not charge again");
    }

    [Fact]
    public async Task Resolution_during_a_rollover_throws_and_succeeds_once_the_season_is_active_again()
    {
        var target = await ArrangeAsync();

        await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor);
        await SetSeasonStatusAsync("rollover");

        var during = async () => await ResolveAsync(target.ListingId);

        await during.Should().ThrowAsync<InvalidOperationException>();

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.TransferListings.SingleAsync(listing => listing.Id == target.ListingId)).Status
                .Should().Be(ListingStatus.Open, "a deferred resolution changes nothing");
            (await db.PlayerContracts.CountAsync(contract => contract.PlayerId == target.PlayerId)).Should().Be(0);
        }

        await SetSeasonStatusAsync("active");
        await ResolveAsync(target.ListingId);

        await using var after = _fixture.CreateScope();
        var afterDb = after.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await afterDb.TransferListings.SingleAsync(listing => listing.Id == target.ListingId)).Status
            .Should().Be(ListingStatus.Sold);
    }

    [Fact]
    public async Task A_free_agents_listing_and_card_read_with_no_club()
    {
        var target = await ArrangeAsync();

        await using var scope = _fixture.CreateScope();
        var market = scope.ServiceProvider.GetRequiredService<IMarketQueries>();
        var squad = scope.ServiceProvider.GetRequiredService<ISquadQueries>();

        var listing = await market.GetListingAsync(target.ListingId, viewerClubId: null, CancellationToken.None);

        listing.Should().NotBeNull();
        listing!.SellerClubId.Should().BeNull();
        listing.SellerClubName.Should().BeNull();
        listing.Ability.Should().BeInRange(WorldRuleSet.FreeAgentAbilityMin, WorldRuleSet.FreeAgentAbilityMax);

        var card = await market.GetPlayerAsync(target.PlayerId, CancellationToken.None);

        card.Should().NotBeNull("a free agent has a scouting card like any player (FA-5)");
        card!.ClubId.Should().BeNull();
        card.ClubName.Should().BeNull();
        card.TierNumber.Should().BeNull();
        card.IsListed.Should().BeTrue();

        var profile = await squad.GetPlayerAsync(target.PlayerId, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.ClubId.Should().BeNull();
        profile.Club.Should().BeNull();
        profile.Contract.Should().BeNull();
        profile.Status.Should().Be(PlayerStatus.FreeAgent);
    }

    [Fact]
    public async Task The_kind_filter_separates_free_agents_from_a_clubs_own_listings()
    {
        await using var scope = _fixture.CreateScope();
        var market = scope.ServiceProvider.GetRequiredService<IMarketQueries>();

        var freeAgents = await market.ListListingsAsync(
            new ListingBrowseFilter(null, null, null, ListingKind.FreeAgent, null, 50),
            viewerClubId: null,
            CancellationToken.None);

        freeAgents.Listings.Should().HaveCount(50);
        freeAgents.Listings.Should().OnlyContain(row => row.SellerClubId == null);
        freeAgents.NextCursor.Should().NotBeNull("a batch is about 260 listings, so there is a second page");

        var clubs = await market.ListListingsAsync(
            new ListingBrowseFilter(null, null, null, ListingKind.Club, null, 50),
            viewerClubId: null,
            CancellationToken.None);

        clubs.Listings.Should().BeEmpty("no club has listed a player in this world");

        var goalkeepers = await market.ListListingsAsync(
            new ListingBrowseFilter(null, null, PositionFamily.Goalkeeper, ListingKind.FreeAgent, null, 50),
            viewerClubId: null,
            CancellationToken.None);

        goalkeepers.Listings.Should().NotBeEmpty();
        goalkeepers.Listings.Should().OnlyContain(row => row.PrimaryPosition == PlayerPosition.Goalkeeper);
    }

    [Fact]
    public async Task A_signed_free_agent_shows_in_the_history_with_no_seller_and_reads_with_his_new_club()
    {
        var target = await ArrangeAsync();

        (await BidAsync(target.ListingId, target.BuyerClubId, target.MinimumFeeMinor)).Outcome
            .Should().Be(MarketOutcome.Found);

        await ResolveAsync(target.ListingId);

        await using var scope = _fixture.CreateScope();
        var market = scope.ServiceProvider.GetRequiredService<IMarketQueries>();

        var history = await market.GetHistoryAsync(cursorId: null, pageSize: 10, CancellationToken.None);

        var row = history.Transfers.Should().ContainSingle(entry => entry.ListingId == target.ListingId).Subject;

        row.PlayerId.Should().Be(target.PlayerId);
        row.SellerClubId.Should().BeNull("a free agent has no seller");
        row.SellerClubName.Should().BeNull();
        row.BuyerClubId.Should().Be(target.BuyerClubId);
        row.FeeMinor.Should().Be(target.MinimumFeeMinor);

        var card = await market.GetPlayerAsync(target.PlayerId, CancellationToken.None);

        card.Should().NotBeNull();
        card!.ClubId.Should().Be(target.BuyerClubId, "once signed he is a club's player like any other");

        var listing = await market.GetListingAsync(target.ListingId, viewerClubId: null, CancellationToken.None);

        listing!.SellerClubId.Should().BeNull("the sold listing still reads, with no seller");
    }

    [Fact]
    public async Task A_player_with_no_contract_who_is_not_a_free_agent_is_not_readable()
    {
        var target = await ArrangeAsync();

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                "update squad.players set status = 'retired' where id = {0}",
                target.PlayerId);
        }

        await using var read = _fixture.CreateScope();
        var market = read.ServiceProvider.GetRequiredService<IMarketQueries>();
        var squad = read.ServiceProvider.GetRequiredService<ISquadQueries>();

        (await market.GetPlayerAsync(target.PlayerId, CancellationToken.None)).Should().BeNull();
        (await squad.GetPlayerAsync(target.PlayerId, CancellationToken.None)).Should().BeNull();
    }

    private async Task<Target> ArrangeAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var listing = await db.TransferListings
            .Where(candidate => candidate.FreeAgentBatchId != null)
            .OrderBy(candidate => candidate.Id)
            .FirstAsync();
        var buyerClubId = await db.Clubs.OrderBy(club => club.Name).Select(club => club.Id).FirstAsync();
        var cash = await db.ClubAccounts.Where(account => account.ClubId == buyerClubId)
            .Select(account => account.CashMinor)
            .SingleAsync();

        return new Target(
            listing.Id,
            listing.PlayerId,
            buyerClubId,
            listing.MinimumFeeMinor,
            listing.GeneratedBuyerSeasonWageMinor,
            listing.GeneratedContractSeasons,
            listing.EndsAt,
            cash);
    }

    private async Task<BidWriteResult> BidAsync(
        Guid listingId,
        Guid clubId,
        long amountMinor,
        MarketActor? actor = null)
    {
        await using var scope = _fixture.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IBidWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Bids take a transaction-scoped listing lock, so the writer runs inside a transaction (ADR-0026).
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            CancellationToken.None);

        var write = await writer.BidAsync(
            clubId,
            actor ?? new MarketActor(AuditActorTypes.User, UserId: null, IpHash: null, "free-agent-test"),
            listingId,
            amountMinor,
            idempotencyKey: null,
            CancellationToken.None);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        return write;
    }

    private async Task ResolveAsync(Guid listingId)
    {
        await using var scope = _fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ResolveListing>()
            .ExecuteAsync(listingId, CancellationToken.None);
    }

    private async Task<long> TotalCashAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.ClubAccounts.SumAsync(account => account.CashMinor);
    }

    private async Task SetSeasonStatusAsync(string status)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "update competition.seasons set status = {0} where sequence_number = 1",
            status);
    }

    /// <summary>Moves players from other clubs to this one until it registers the given number.</summary>
    private async Task FillSquadAsync(Guid clubId, int size)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var have = await db.PlayerContracts.CountAsync(contract =>
            contract.ClubId == clubId && contract.Status == ContractStatus.Active);

        var donors = await db.PlayerContracts
            .Where(contract => contract.ClubId != clubId && contract.Status == ContractStatus.Active)
            .OrderBy(contract => contract.PlayerId)
            .Take(Math.Max(0, size - have))
            .ToListAsync();

        foreach (var donor in donors)
        {
            var registration = await db.PlayerRegistrations.FirstAsync(candidate =>
                candidate.PlayerId == donor.PlayerId && candidate.Status == RegistrationStatus.Active);

            donor.Close(PlayerContractCloseReasons.Transferred, now);
            registration.End(now);

            db.PlayerContracts.Add(PlayerContract.Sign(
                Guid.CreateVersion7(),
                donor.PlayerId,
                clubId,
                donor.StartSeasonNumber,
                donor.EndSeasonNumber,
                donor.SeasonWageMinor,
                donor.SquadStatus,
                now));
        }

        await db.SaveChangesAsync();
    }

    private sealed record Target(
        Guid ListingId,
        Guid PlayerId,
        Guid BuyerClubId,
        long MinimumFeeMinor,
        long WageMinor,
        int ContractSeasons,
        DateTimeOffset EndsAt,
        long BuyerCashBefore);
}
