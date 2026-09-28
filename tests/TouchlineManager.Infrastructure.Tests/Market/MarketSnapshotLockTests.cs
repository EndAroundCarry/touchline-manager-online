using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Market;
using TouchlineManager.Application.Match;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// A transfer leaves a frozen fixture snapshot alone (`SQ-7`).
/// </summary>
/// <remarks>
/// <para>
/// The market's snapshot rule: a player transferred after a fixture's snapshot has locked is eligible only
/// for later fixtures. The snapshot is the immutable input a match is simulated from (`MAT-1`), so the
/// transfer must not rewrite it — the player still appears in the side that will play, and only the
/// player's live contract and registration move.
/// </para>
/// <para>
/// Its own world, started per test, because locking a round and moving a player both change the world and
/// the shared collections assert what a freshly seeded world looks like.
/// </para>
/// </remarks>
public sealed class MarketSnapshotLockTests : IAsyncLifetime, IDisposable
{
    private readonly SnapshotLockFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_transfer_does_not_rewrite_a_frozen_snapshot()
    {
        // Lock round one so every fixture in it has an immutable input snapshot.
        Guid fixtureId;
        string snapshotBefore;
        string snapshotHashBefore;
        Guid playerId;
        Guid buyerClubId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var matchday = await db.Matchdays.OrderBy(candidate => candidate.KickoffAt).FirstAsync();
            await scope.ServiceProvider.GetRequiredService<LockMatchday>()
                .ExecuteAsync(matchday.Id, CancellationToken.None);

            var fixture = await db.Fixtures
                .Where(candidate => candidate.MatchdayId == matchday.Id)
                .OrderBy(candidate => candidate.Id)
                .FirstAsync();

            fixtureId = fixture.Id;
            buyerClubId = fixture.AwayClubId;

            var stored = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == fixtureId);
            snapshotBefore = stored.SnapshotJson;
            snapshotHashBefore = stored.SnapshotHash;

            var input = MatchSnapshotDocument.Read(snapshotBefore).Input;

            // A player who is actually in the frozen side, so the transfer is one the rule is about.
            playerId = input.Home.Squad[0].PlayerId;

            var contract = await db.PlayerContracts.SingleAsync(candidate =>
                candidate.PlayerId == playerId && candidate.Status == ContractStatus.Active);
            contract.ClubId.Should().Be(input.Home.ClubId, "the frozen side's players belong to it at lock time");
        }

        await TransferAsync(playerId, buyerClubId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var stored = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == fixtureId);

            // The stored content hash is the immutability guarantee: jsonb normalises formatting, so the raw
            // document is not byte-stable across a round trip, but the hash of the canonical document is.
            stored.SnapshotHash.Should().Be(snapshotHashBefore, "SQ-7: a frozen snapshot is immutable");

            MatchSnapshotDocument.Read(stored.SnapshotJson).Input.Home.Squad
                .Should().Contain(participant => participant.PlayerId == playerId,
                    "the player still plays the fixture that was already frozen");

            var registration = await db.PlayerRegistrations.SingleAsync(candidate =>
                candidate.PlayerId == playerId && candidate.Status == RegistrationStatus.Active);
            registration.ClubId.Should().Be(buyerClubId, "the live registration moves, the snapshot does not");
        }
    }

    private async Task TransferAsync(Guid playerId, Guid buyerClubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var sellerClubId = await db.PlayerContracts
            .Where(contract => contract.PlayerId == playerId && contract.Status == ContractStatus.Active)
            .Select(contract => contract.ClubId)
            .SingleAsync();

        const long fee = 1_000_000;

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
        db.LedgerEntries.Add(buyerAccount.Post(
            LedgerPostings.BidReservation(Guid.CreateVersion7(), buyerClubId, bidId, fee),
            now));

        await db.SaveChangesAsync(CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveListing>()
            .ExecuteAsync(listing.Id, CancellationToken.None);
    }
}

/// <summary>The snapshot-lock tests' own seeded world.</summary>
public sealed class SnapshotLockFixture : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_market_lock")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private ServiceProvider? _serviceProvider;

    /// <summary>Gets the clock every use case in this fixture reads.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>Starts the container, migrates, and seeds a world.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _serviceProvider = BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .Database
            .MigrateAsync();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest("market-lock-world"), CancellationToken.None);
    }

    /// <summary>Creates a scope against the seeded world.</summary>
    public AsyncServiceScope CreateScope() =>
        (_serviceProvider ?? throw new InvalidOperationException("The fixture has not been initialized."))
        .CreateAsyncScope();

    /// <summary>Stops and removes the container.</summary>
    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _serviceProvider?.Dispose();

        GC.SuppressFinalize(this);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _container.GetConnectionString(),
                ["Auth:SigningKey"] = "market-lock-fixture-signing-key-at-least-32-bytes",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IClock>(Clock);

        return services.BuildServiceProvider();
    }
}
