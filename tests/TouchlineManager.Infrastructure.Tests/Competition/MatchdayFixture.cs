using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.World;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// A real PostgreSQL 17 instance holding one freshly seeded world that the matchday tests play in.
/// </summary>
/// <remarks>
/// Its own container rather than the world collection's, deliberately. Those tests assert what a *freshly
/// seeded* world looks like — thirty-four rounds, every fixture still scheduled, every club on nought points
/// — and a world somebody has played a round in is no longer that world. A fixture per purpose is cheaper
/// than an assertion that has to be weakened to accommodate the tests that mutate what it checks.
/// </remarks>
public sealed class MatchdayFixture : IAsyncLifetime, IDisposable
{
    /// <summary>The seed this fixture's world is generated from.</summary>
    public const string Seed = "infrastructure-matchday-world-1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_matchday")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private ServiceProvider? _serviceProvider;

    /// <summary>Gets the clock every use case in this fixture reads.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>Gets the seeded world's identity.</summary>
    public Guid WorldId { get; private set; }

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

    /// <summary>Creates a service collection wired like the composition roots.</summary>
    public ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _container.GetConnectionString(),
                ["Auth:SigningKey"] = "matchday-fixture-signing-key-that-is-at-least-32-bytes",
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

/// <summary>Shares one seeded, playable world across the matchday workflow's test classes.</summary>
[CollectionDefinition(Name)]
public sealed class MatchdayCollection : ICollectionFixture<MatchdayFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "matchday";
}
