using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// A real PostgreSQL 17 instance holding one freshly seeded world.
/// </summary>
/// <remarks>
/// <para>
/// Its own container, deliberately, rather than sharing the infrastructure collection's. The world module
/// enforces "there is exactly one world" (`WORLD-1`), so a fixture that seeded into a database other tests
/// had already written worlds into could not seed at all — and a claim concurrency test needs to know
/// exactly what is in the database it is racing against.
/// </para>
/// <para>
/// The world is seeded once during initialization. Every test reads it rather than building its own, which
/// is what makes the concurrency assertions meaningful: they race against real rows in a real pyramid.
/// </para>
/// </remarks>
public sealed class WorldFixture : IAsyncLifetime, IDisposable
{
    /// <summary>The seed the fixture's world is generated from.</summary>
    public const string Seed = "infrastructure-world-1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_world")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private ServiceProvider? _serviceProvider;

    /// <summary>Gets the clock every use case in this fixture reads.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>Gets the seeded world's identity.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the seeded world's countries, in presentation order.</summary>
    public IReadOnlyList<Country> Countries { get; private set; } = [];

    /// <summary>Starts the container, applies migrations, and seeds the world.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _serviceProvider = BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        await dbContext.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<SeedWorld>();
        var result = await seeder.ExecuteAsync(new SeedWorldRequest(Seed), CancellationToken.None);

        WorldId = result.WorldId;
        Countries = await dbContext.Countries
            .Where(country => country.WorldId == WorldId)
            .OrderBy(country => country.SortOrder)
            .ToListAsync();
    }

    /// <summary>Stops and removes the container.</summary>
    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>Creates a scope against the seeded world.</summary>
    public AsyncServiceScope CreateScope() =>
        (_serviceProvider ?? throw new InvalidOperationException("The fixture has not been initialized."))
        .CreateAsyncScope();

    /// <summary>Creates a service collection wired like the composition roots, plus the application layer.</summary>
    public ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _container.GetConnectionString(),

                // The commands under test write audit rows, and audit rows carry a hashed client
                // fingerprint, which is produced by the same HMAC-backed service the auth module uses.
                // It validates its key material at construction (ADR-0002).
                ["Auth:SigningKey"] = "world-fixture-signing-key-that-is-at-least-32-bytes",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Last registration wins, so the fixture's clock is the one every use case reads.
        services.AddSingleton<IClock>(Clock);

        return services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public void Dispose() => _serviceProvider?.Dispose();
}

/// <summary>Shares one seeded world across the world test classes.</summary>
[CollectionDefinition(Name)]
public sealed class WorldCollection : ICollectionFixture<WorldFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "world";
}

/// <summary>
/// Arrangement helpers for tests that need real accounts, managers, and divisions.
/// </summary>
/// <remarks>
/// These write through the fixture's own <c>DbContext</c> rather than through use cases, because the point
/// of most of these tests is the command under test, and every other row is scenery. The commands
/// themselves are always invoked the way the API invokes them.
/// </remarks>
public abstract class WorldTestBase
{
    /// <summary>Initializes the base with the seeded world.</summary>
    protected WorldTestBase(WorldFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world.</summary>
    protected WorldFixture Fixture { get; }

    /// <summary>
    /// Creates a verified account with a manager profile.
    /// </summary>
    /// <returns>The account identity, which is what every command takes: the manager is derived from it.</returns>
    protected async Task<Guid> CreateManagerAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        var userId = await CreateUserAsync(scope, UserStatus.Active, cancellationToken);

        await CreateManagerForAsync(scope, userId, cancellationToken);

        return userId;
    }

    /// <summary>Creates a manager profile for an existing account.</summary>
    protected async Task<Guid> CreateManagerForAsync(
        AsyncServiceScope scope,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = Guid.CreateVersion7();

        db.Managers.Add(Manager.Create(managerId, userId, "en-GB", "Europe/London", Fixture.Clock.UtcNow));

        await db.SaveChangesAsync(cancellationToken);

        return managerId;
    }

    /// <summary>Creates an account in the given state.</summary>
    protected async Task<Guid> CreateUserAsync(
        AsyncServiceScope scope,
        UserStatus status,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var userId = Guid.CreateVersion7();
        var suffix = Guid.NewGuid().ToString("N");

        var user = User.Register(
            userId,
            $"{suffix}@example.com",
            $"Manager{suffix}"[..20],
            "hash",
            $"stamp-{suffix}",
            Fixture.Clock.UtcNow);

        if (status == UserStatus.Active)
        {
            user.MarkEmailVerified(Fixture.Clock.UtcNow);
        }

        if (status == UserStatus.Suspended)
        {
            user.MarkEmailVerified(Fixture.Clock.UtcNow);
            user.Suspend($"stamp-suspended-{suffix}", Fixture.Clock.UtcNow);
        }

        db.Users.Add(user);

        await db.SaveChangesAsync(cancellationToken);

        return userId;
    }

    /// <summary>Lists a country's clubs in a stable order.</summary>
    protected static async Task<IReadOnlyList<Guid>> ClubIdsOfAsync(
        AsyncServiceScope scope,
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Clubs
            .Where(club => club.CountryId == countryId)
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a club in a country that no human holds.
    /// </summary>
    /// <remarks>
    /// Tests share one seeded world, so a test that assumed a particular club was free would fail
    /// depending on which test ran first. Asking for a free one makes each test independent of the order.
    /// </remarks>
    protected static async Task<Guid> FreeClubIdAsync(
        AsyncServiceScope scope,
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Clubs
            .Where(club => club.CountryId == countryId
                && !db.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed))
            .OrderBy(club => club.Name)
            .Select(club => club.Id)
            .FirstAsync(cancellationToken);
    }

    /// <summary>
    /// Creates an active tier in a country, as the provisioning worker will in Stage 11.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than by the provisioning job, because that job is the next stage's deliverable
    /// and this is the scenery for testing which tier is claimable (`WORLD-8`).
    /// </remarks>
    protected async Task<Guid> CreateActiveTierAsync(
        AsyncServiceScope scope,
        Guid countryId,
        int tierNumber,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = Fixture.Clock.UtcNow;

        var country = await db.Countries.SingleAsync(candidate => candidate.Id == countryId, cancellationToken);
        var season = await db.Seasons.SingleAsync(
            candidate => candidate.WorldId == Fixture.WorldId && candidate.SequenceNumber == 1,
            cancellationToken);

        var division = Division.Provision(
            Guid.CreateVersion7(),
            countryId,
            tierNumber,
            country.DisplayName,
            season.Id,
            now);

        division.Activate(now);
        db.Divisions.Add(division);
        await db.SaveChangesAsync(cancellationToken);

        var tier = tierNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var divisionSeason = DivisionSeason.Create(
            Guid.CreateVersion7(),
            division.Id,
            season.Id,
            DeterministicDigest.Of(WorldFixture.Seed, country.Code, tier, "schedule"),
            DeterministicDigest.Of(WorldFixture.Seed, country.Code, tier, "tie-draw"),
            DeterministicDigest.Of(WorldFixture.Seed, country.Code, tier, "tie-draw-hash"),
            now);

        divisionSeason.Activate(now);
        db.DivisionSeasons.Add(divisionSeason);
        await db.SaveChangesAsync(cancellationToken);

        var identities = ClubIdentityGenerator.GenerateDivision(
            WorldFixture.Seed,
            country.NamePoolKey,
            country.Code,
            tierNumber,
            WorldRuleSet.ClubsPerDivision);

        foreach (var identity in identities)
        {
            var clubId = Guid.CreateVersion7();

            db.Clubs.Add(Club.Generate(clubId, Fixture.WorldId, countryId, identity, tierNumber, season.GameYear, now));

            db.ClubSeasonEntries.Add(ClubSeasonEntry.Enter(
                Guid.CreateVersion7(),
                divisionSeason.Id,
                season.Id,
                clubId,
                ClubControlType.Ai,
                now));

            db.ClubAccounts.Add(ClubAccount.Open(
                Guid.CreateVersion7(),
                clubId,
                WorldRuleSet.OpeningCashMinorForTier(tierNumber),
                now));
        }

        await db.SaveChangesAsync(cancellationToken);

        return division.Id;
    }
}
