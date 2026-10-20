using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Market;

/// <summary>
/// The weekly free-agent batch over a real seeded world (`FA-1`…`FA-6`).
/// </summary>
/// <remarks>
/// Its own world per test, like the AI market suite: every test here adds players and listings to the world,
/// and one test's batch would decide the next test's counts.
/// </remarks>
public sealed class FreeAgentBatchTests : IAsyncLifetime, IDisposable
{
    // The fixture clock reads Wednesday 23 September 2026 at noon, so this week opened on Monday the 21st.
    private static readonly DateOnly ThisWeek = new(2026, 9, 21);

    private readonly FreeAgentFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_batch_is_a_tenth_of_the_contracted_players_listed_until_next_monday()
    {
        var contracted = await ContractedCountAsync();

        var result = await RunAsync(ThisWeek);

        result.Created.Should().BeTrue();
        result.Contracted.Should().Be(contracted);
        result.Generated.Should().Be(FreeAgentGenerator.BatchSize(contracted), "FA-1");
        result.Released.Should().Be(0);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var listings = await db.TransferListings.Where(listing => listing.FreeAgentBatchId != null).ToListAsync();

        listings.Should().HaveCount(result.Generated);
        listings.Should().OnlyContain(listing => listing.SellerClubId == null && listing.Status == ListingStatus.Open);
        listings.Should().OnlyContain(listing =>
            listing.OpensAt == new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)
            && listing.EndsAt == new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        listings.Should().OnlyContain(listing => listing.MinimumFeeMinor > 0 && listing.GeneratedBuyerWageMinor > 0);

        var batch = await db.FreeAgentBatches.SingleAsync();

        batch.WeekStart.Should().Be(ThisWeek);
        batch.GeneratedCount.Should().Be(result.Generated);
        batch.GeneratorVersion.Should().Be(FreeAgentGenerator.Version);
        listings.Should().OnlyContain(listing => listing.FreeAgentBatchId == batch.Id);

        var playerIds = listings.Select(listing => listing.PlayerId).ToList();

        var players = await db.Players.Where(player => playerIds.Contains(player.Id)).ToListAsync();

        players.Should().HaveCount(result.Generated);
        players.Should().OnlyContain(player => player.Status == PlayerStatus.FreeAgent);

        (await db.PlayerContracts.CountAsync(contract => playerIds.Contains(contract.PlayerId)))
            .Should().Be(0, "a free agent belongs to no club until somebody signs him");

        var abilities = (await db.PlayerAttributes.Where(attributes => playerIds.Contains(attributes.PlayerId)).ToListAsync())
            .Select(attributes => (int)Math.Round(attributes.ToSet().Values.Average(), MidpointRounding.AwayFromZero))
            .ToList();

        abilities.Should().OnlyContain(ability => ability >= WorldRuleSet.FreeAgentAbilityMin
            && ability <= WorldRuleSet.FreeAgentAbilityMax);

        var goalkeepers = players.Count(player => player.PrimaryPosition == PlayerPosition.Goalkeeper);

        ((double)goalkeepers / players.Count).Should().BeInRange(0.12, 0.18, "FA-2 asks for 15% goalkeepers");

        players.Select(player => player.PrimaryPosition).Distinct().Should().HaveCountGreaterThan(8);
    }

    [Fact]
    public async Task A_second_run_for_the_same_week_adds_nothing()
    {
        var first = await RunAsync(ThisWeek);
        var listingsBefore = await CountAsync(db => db.TransferListings.CountAsync());
        var playersBefore = await CountAsync(db => db.Players.CountAsync());

        var again = await RunAsync(ThisWeek);

        again.Created.Should().BeFalse();
        again.Generated.Should().Be(first.Generated);
        (await CountAsync(db => db.TransferListings.CountAsync())).Should().Be(listingsBefore);
        (await CountAsync(db => db.Players.CountAsync())).Should().Be(playersBefore);
        (await CountAsync(db => db.FreeAgentBatches.CountAsync())).Should().Be(1);
    }

    [Theory]
    [InlineData(PlayerContractCloseReasons.Expired)]
    [InlineData(PlayerContractCloseReasons.Released)]
    public async Task A_released_player_is_listed_once_on_top_of_the_tenth(string closeReason)
    {
        var releasedId = await ReleaseOnePlayerAsync(closeReason);
        var contracted = await ContractedCountAsync();

        var result = await RunAsync(ThisWeek);

        result.Released.Should().Be(1);
        result.Generated.Should().Be(FreeAgentGenerator.BatchSize(contracted), "released players are not counted in the 10%");

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var listing = await db.TransferListings.SingleAsync(candidate => candidate.PlayerId == releasedId);
            var attributes = await db.PlayerAttributes.SingleAsync(candidate => candidate.PlayerId == releasedId);
            var player = await db.Players.SingleAsync(candidate => candidate.Id == releasedId);
            var season = await db.Seasons.OrderBy(candidate => candidate.SequenceNumber).FirstAsync();

            listing.IsFreeAgent.Should().BeTrue();
            listing.Status.Should().Be(ListingStatus.Open);

            var ability = (int)Math.Round(attributes.ToSet().Values.Average(), MidpointRounding.AwayFromZero);
            var expected = FreeAgentTerms.Calculate(ability, season.GameYear - player.BirthGameYear, referenceTier: 1);

            listing.MinimumFeeMinor.Should().Be(expected.MinimumFeeMinor);
            listing.GeneratedBuyerWageMinor.Should().Be(expected.WeeklyWageMinor);
            listing.GeneratedContractSeasons.Should().Be(expected.ContractSeasons);
        }

        var nextWeek = await RunAsync(ThisWeek.AddDays(7));

        nextWeek.Released.Should().Be(0, "a player who already has a listing is not picked up twice");
    }

    [Fact]
    public async Task A_week_that_has_already_closed_is_not_opened()
    {
        var result = await RunAsync(ThisWeek.AddDays(-14));

        result.Created.Should().BeFalse();
        (await CountAsync(db => db.TransferListings.CountAsync(listing => listing.FreeAgentBatchId != null)))
            .Should().Be(0);
        (await CountAsync(db => db.FreeAgentBatches.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task The_ai_market_never_sees_a_free_agent()
    {
        await using (var scope = _fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EvaluateAiMarket>().ExecuteAsync(CancellationToken.None);
        }

        await RunAsync(ThisWeek);

        await using var read = _fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubListings = await db.TransferListings.CountAsync(listing => listing.SellerClubId != null);
        var freeAgentListings = await db.TransferListings.CountAsync(listing => listing.FreeAgentBatchId != null);

        clubListings.Should().BeGreaterThan(0);
        freeAgentListings.Should().BeGreaterThan(0);

        var seen = await read.ServiceProvider.GetRequiredService<IAiMarketRepository>()
            .LoadOpenListingsAsync(_fixture.Clock.UtcNow.AddDays(1), CancellationToken.None);

        seen.Should().HaveCount(clubListings, "the AI weighs every club listing and no free agent (FA-5)");
    }

    [Fact]
    public async Task A_listing_is_a_clubs_or_a_batchs_and_never_both_or_neither()
    {
        await RunAsync(ThisWeek);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var listingId = await db.TransferListings
            .Where(listing => listing.FreeAgentBatchId != null)
            .Select(listing => listing.Id)
            .FirstAsync();
        var clubId = await db.Clubs.Select(club => club.Id).FirstAsync();

        var both = async () => await db.Database.ExecuteSqlRawAsync(
            "update market.transfer_listings set seller_club_id = {0} where id = {1}",
            clubId,
            listingId);

        (await both.Should().ThrowAsync<Exception>()).Which
            .GetBaseException().Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_transfer_listings_seller_or_batch");

        var neither = async () => await db.Database.ExecuteSqlRawAsync(
            "update market.transfer_listings set free_agent_batch_id = null where id = {0}",
            listingId);

        (await neither.Should().ThrowAsync<Exception>()).Which
            .GetBaseException().Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_transfer_listings_seller_or_batch");
    }

    private async Task<RunFreeAgentBatchResult> RunAsync(DateOnly week)
    {
        await using var scope = _fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<RunFreeAgentBatch>()
            .ExecuteAsync(week, CancellationToken.None);
    }

    private async Task<int> ContractedCountAsync() =>
        await CountAsync(db => db.PlayerContracts.CountAsync(contract => contract.Status == ContractStatus.Active));

    private async Task<int> CountAsync(Func<Tactical11ManagerDbContext, Task<int>> count)
    {
        await using var scope = _fixture.CreateScope();

        return await count(scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>());
    }

    /// <summary>
    /// Lets one player's contract end, as the rollover does for a player a club will not renew (expired) or a
    /// manager does by releasing him (released).
    /// </summary>
    private async Task<Guid> ReleaseOnePlayerAsync(string closeReason)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var contract = await db.PlayerContracts
            .Where(candidate => candidate.Status == ContractStatus.Active)
            .OrderBy(candidate => candidate.PlayerId)
            .FirstAsync();
        var registration = await db.PlayerRegistrations
            .FirstAsync(candidate => candidate.PlayerId == contract.PlayerId && candidate.Status == RegistrationStatus.Active);
        var player = await db.Players.FirstAsync(candidate => candidate.Id == contract.PlayerId);

        contract.Close(closeReason, now);
        registration.End(now);
        player.ReleaseToFreeAgency(now);

        await db.SaveChangesAsync();

        return player.Id;
    }
}

/// <summary>The free-agent batch tests' own seeded world, one per test.</summary>
public sealed class FreeAgentFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public FreeAgentFixture()
        : base("tactical11_free_agents")
    {
    }
}
