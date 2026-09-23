using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Worker.IntegrationTests;

/// <summary>
/// Proves the Stage 1 walking skeleton end to end across process boundaries: a job enqueued by one
/// component is claimed, executed, and completed by the worker's own composition.
/// </summary>
/// <remarks>
/// The assertion that matters is not "the handler ran" but "the row reached a terminal state
/// without the API's involvement", which is the property every real deadline depends on.
/// </remarks>
public sealed class JobQueueWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _container.GetConnectionString(),
            ["Worker:PollIntervalSeconds"] = "1",
            ["Worker:MaxIdlePollIntervalSeconds"] = "1",
            ["Worker:LeaseSeconds"] = "30",
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddJobQueueWorker();

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>().Database.MigrateAsync();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_worker_executes_and_completes_an_enqueued_job()
    {
        var businessKey = $"walking-skeleton:{Guid.NewGuid():N}";

        await using (var scope = _host!.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await queue.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = NoOpJobHandler.TypeName,
                    BusinessKey = businessKey,
                    DueAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                },
                CancellationToken.None);
        }

        var completed = await WaitForStatusAsync(businessKey, "completed", TimeSpan.FromSeconds(30));

        completed.Should().BeTrue(
            "the worker must claim, execute, and complete an enqueued job without any API involvement");

        await using var assertionScope = _host!.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(job => job.BusinessKey == businessKey);

        row.AttemptCount.Should().Be(1);
        row.CompletedAt.Should().NotBeNull();
        row.LeaseOwner.Should().BeNull();
        row.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public async Task The_worker_leaves_a_job_with_no_handler_in_the_dead_letter_state()
    {
        // An unknown job type is a deployment or configuration defect. It must surface to operations
        // instead of retrying forever and hiding the problem.
        var businessKey = $"orphan:{Guid.NewGuid():N}";

        await using (var scope = _host!.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await queue.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = "ops.no-handler-registered",
                    BusinessKey = businessKey,
                    DueAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                },
                CancellationToken.None);
        }

        var deadLettered = await WaitForStatusAsync(businessKey, "dead_letter", TimeSpan.FromSeconds(30));

        deadLettered.Should().BeTrue();

        await using var assertionScope = _host!.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(job => job.BusinessKey == businessKey);

        row.LastError.Should().Contain("ops.no-handler-registered");
    }

    [Fact]
    public async Task The_clock_is_injected_and_is_utc()
    {
        // A worker that used server local time would move kickoffs on a DST boundary (ADR-0009).
        using var scope = _host!.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        clock.UtcNow.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    private async Task<bool> WaitForStatusAsync(string businessKey, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var status = await db.Jobs
                .Where(job => job.BusinessKey == businessKey)
                .Select(job => job.Status)
                .SingleOrDefaultAsync();

            if (status == expectedStatus)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }
}
