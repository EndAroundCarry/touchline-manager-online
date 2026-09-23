using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Testcontainers.PostgreSql;
using Xunit;

namespace FootballManager.Worker.IntegrationTests;

public sealed class JobWorkerEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                RemoveDbContextRegistrations(services);
                services.AddDbContext<GameDbContext>(options => options.UseNpgsql(_container.GetConnectionString()));
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Worker_liveness_and_readiness_report_database_state()
    {
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enqueued_noop_job_is_claimed_and_succeeded_by_worker()
    {
        const string businessKey = "worker-e2e-noop";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var queue = new PostgresJobQueue(db, new SystemClock());
            await queue.EnqueueAsync(new JobRequest(NoOpJobHandler.Type, businessKey));
        }

        using var client = _factory.CreateClient();
        var job = await WaitForStatusAsync(businessKey, JobStatus.Succeeded, TimeSpan.FromSeconds(30));

        job.Should().NotBeNull();
        job!.AttemptCount.Should().Be(1);
        job.LastError.Should().BeNull();
    }

    private async Task<JobRecord?> WaitForStatusAsync(string businessKey, string status, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        JobRecord? job = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.BusinessKey == businessKey);
            if (job is not null && job.Status == status)
            {
                return job;
            }

            await Task.Delay(200);
        }

        return job;
    }

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
