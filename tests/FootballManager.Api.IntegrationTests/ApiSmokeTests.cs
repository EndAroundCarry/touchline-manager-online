using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Contracts.Ops;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace FootballManager.Api.IntegrationTests;

public sealed class ApiSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
            builder.ConfigureServices(services =>
            {
                RemoveDbContextRegistrations(services);
                services.AddDbContext<GameDbContext>(options => options.UseNpgsql(_container.GetConnectionString()));
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Liveness_returns_healthy()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }

    [Fact]
    public async Task Readiness_returns_healthy_when_database_available()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }

    [Fact]
    public async Task Valid_correlation_header_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "corr-1234_ABC.def");

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Single().Should().Be("corr-1234_ABC.def");
    }

    [Fact]
    public async Task Oversized_correlation_header_is_replaced()
    {
        var garbage = new string('x', 200);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", garbage);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        var received = values!.Single();
        received.Should().NotBe(garbage);
        received.Should().HaveLength(32);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Noop_endpoint_enqueues_a_pending_job()
    {
        var response = await _client.PostAsync("/api/v1/admin/jobs/noop", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<EnqueueNoOpJobResponse>();
        payload.Should().NotBeNull();
        payload!.BusinessKey.Should().StartWith("dev-noop-");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var job = await FindJobAsync(db, payload.BusinessKey);
        job.Should().NotBeNull();
        job!.Status.Should().Be(JobStatus.Pending);
        job.JobType.Should().Be(NoOpJobHandler.Type);
    }

    private static async Task<JobRecord?> FindJobAsync(GameDbContext db, string businessKey) =>
        await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.BusinessKey == businessKey);

    private static void RemoveDbContextRegistrations(IServiceCollection services)
    {
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(DbContextOptions<GameDbContext>)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.Name == "IDbContextOptionsConfiguration`1"
                    && descriptor.ServiceType.GenericTypeArguments[0] == typeof(GameDbContext)))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
