using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Runs the real composition root against its own database, so the match reads can play a round without
/// disturbing the shared world.
/// </summary>
/// <remarks>
/// Its own container rather than <see cref="ApiFixture"/>'s, deliberately. The other API tests read a
/// freshly seeded calendar with no results, and publishing a matchday moves a division's table and puts
/// scores on its fixtures — which is exactly what those tests assert is not yet true. A host per purpose is
/// cheaper than weakening an assertion to accommodate the test that mutates what it checks, the same
/// argument the matchday workflow's infrastructure fixture makes.
/// </remarks>
public sealed class MatchApiFixture : IAsyncLifetime
{
    /// <summary>The world this host plays in.</summary>
    public const string WorldSeed = "api-match-world";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_match")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    /// <summary>Gets the host, over this fixture's own database.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Gets the email recorder that replaces the SMTP sender.</summary>
    public RecordingEmailSender Email { get; } = new();

    /// <summary>Starts the container, creates the host, migrates, and seeds a world.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", _container.GetConnectionString());
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
            builder.UseSetting("Diagnostics:EnableJobProbe", "true");
            builder.UseSetting(
                "RateLimiting:AuthPermitLimit",
                ApiFixture.UnthrottledAuthPermitLimit.ToString(CultureInfo.InvariantCulture));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Email);
            });
        });

        await using var scope = Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .Database
            .MigrateAsync();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest(WorldSeed), CancellationToken.None);
    }

    /// <summary>Stops the host and removes the container.</summary>
    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Creates a client that does not follow redirects or keep cookies.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}

/// <summary>Shares one playable world, isolated from the other API tests, across the match read tests.</summary>
[CollectionDefinition(Name)]
public sealed class MatchApiCollection : ICollectionFixture<MatchApiFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "api-match";
}
