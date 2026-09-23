using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Runs the real composition root against a real PostgreSQL 17 instance.
/// </summary>
/// <remarks>
/// The API is driven through <see cref="Program"/> rather than a hand-built host, so these tests
/// exercise the actual middleware order, configuration validation, and endpoint mapping.
/// </remarks>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    /// <summary>Gets a factory with diagnostics enabled.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Starts the container, creates the host, and applies migrations.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = CreateFactory(enableJobProbe: true);

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .Database
            .MigrateAsync();
    }

    /// <summary>
    /// Creates an additional host over the same database, so a test can vary configuration the way
    /// a different deployment would.
    /// </summary>
    /// <remarks>
    /// Values are supplied with <c>UseSetting</c> rather than <c>ConfigureAppConfiguration</c>: the
    /// composition root reads configuration while <em>building</em> the service collection, and a
    /// configuration source added during <c>Build()</c> arrives too late for that read.
    /// </remarks>
    public WebApplicationFactory<Program> CreateFactory(bool enableJobProbe)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", _container.GetConnectionString());
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
            builder.UseSetting("Diagnostics:EnableJobProbe", enableJobProbe ? "true" : "false");
        });

    /// <summary>Stops the host and removes the container.</summary>
    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>Shares one API host and one database across the API test classes.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "api";
}
