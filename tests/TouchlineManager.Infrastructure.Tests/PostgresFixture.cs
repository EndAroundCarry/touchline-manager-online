using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests;

/// <summary>
/// A real PostgreSQL 17 instance for the collection.
/// </summary>
/// <remarks>
/// Never an in-memory provider: the behaviour under test is partial unique indexes, check
/// constraints, <c>FOR UPDATE SKIP LOCKED</c>, and lease expiry, none of which an in-memory provider
/// reproduces (master plan §15.2).
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private ServiceProvider? _serviceProvider;

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gets the mutable clock the queue is tested against.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>Starts the container and applies every migration.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _serviceProvider = BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        await dbContext.Database.MigrateAsync();
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

    /// <summary>Creates a scope against the test database.</summary>
    public AsyncServiceScope CreateScope() =>
        (_serviceProvider ?? throw new InvalidOperationException("The fixture has not been initialized."))
        .CreateAsyncScope();

    /// <summary>Creates a service collection wired exactly like the composition roots.</summary>
    public ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        // Override the clock last: the last registration of a single service wins, so tests control
        // lease expiry and due times without sleeping.
        services.AddSingleton<IClock>(Clock);

        return services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

/// <summary>A clock whose instant the test controls.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Advances the clock.</summary>
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Shares one PostgreSQL container across the infrastructure test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "postgres";
}
