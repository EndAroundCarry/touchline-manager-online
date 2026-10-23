using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Tactical11Manager.Application;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Infrastructure;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Worker.IntegrationTests;

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
        .WithDatabase("tactical11")
        .WithUsername("tactical11_app")
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
        builder.Services.AddScoped<IJobHandler, CollidingJobHandler>();

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>().Database.MigrateAsync();
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
        var db = assertionScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
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
        var db = assertionScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var row = await db.Jobs.SingleAsync(job => job.BusinessKey == businessKey);

        row.LastError.Should().Contain("ops.no-handler-registered");
    }

    [Fact]
    public async Task A_job_that_collides_with_another_transaction_is_run_again_at_once_and_completes_on_its_first_attempt()
    {
        // A serialization failure rolls the transaction back and asks for it to be run again; it is not the job failing, so it must not
        // spend the job's attempt budget or wait out the queue's backoff.
        var businessKey = $"{CollidingJobHandler.CollideTwicePrefix}{Guid.NewGuid():N}";

        await EnqueueCollidingAsync(businessKey);

        (await WaitForStatusAsync(businessKey, "completed", TimeSpan.FromSeconds(30))).Should().BeTrue();

        await using var assertionScope = _host!.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var row = await db.Jobs.SingleAsync(job => job.BusinessKey == businessKey);

        row.AttemptCount.Should().Be(1, "the reruns happened inside the one attempt");
        CollidingJobHandler.CallsFor(businessKey).Should().Be(3, "two collisions and then the run that went through");
    }

    [Fact]
    public async Task A_job_that_keeps_colliding_is_handed_back_to_the_queue_as_a_transient_failure()
    {
        var businessKey = $"{CollidingJobHandler.CollideAlwaysPrefix}{Guid.NewGuid():N}";

        await EnqueueCollidingAsync(businessKey);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        (string Status, string? LastError) row = default;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            row = await db.Jobs
                .Where(job => job.BusinessKey == businessKey)
                .Select(job => new ValueTuple<string, string?>(job.Status, job.LastError))
                .SingleAsync();

            if (row.LastError is not null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        row.LastError.Should().Contain("transient failure", "the queue's own retry takes over once the reruns are spent");
        row.Status.Should().NotBe("dead_letter", "a collision is transient");
        CollidingJobHandler.CallsFor(businessKey).Should().BeGreaterThanOrEqualTo(6, "the first run and five reruns");
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

    private async Task EnqueueCollidingAsync(string businessKey)
    {
        await using var scope = _host!.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = CollidingJobHandler.TypeName,
                BusinessKey = businessKey,
                DueAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            CancellationToken.None);
    }

    private async Task<bool> WaitForStatusAsync(string businessKey, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

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

/// <summary>A handler that fails with a serialization failure, as a serializable transaction that lost a collision does.</summary>
internal sealed class CollidingJobHandler : IJobHandler
{
    /// <summary>The job type.</summary>
    public const string TypeName = "test.colliding";

    /// <summary>A business key with this prefix collides twice and then succeeds.</summary>
    public const string CollideTwicePrefix = "collide-twice:";

    /// <summary>A business key with this prefix collides every time.</summary>
    public const string CollideAlwaysPrefix = "collide-always:";

    private static readonly ConcurrentDictionary<string, int> Calls = new();

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <summary>How many times a job has been handled.</summary>
    public static int CallsFor(string businessKey) => Calls.GetValueOrDefault(businessKey);

    /// <inheritdoc />
    public Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        var call = Calls.AddOrUpdate(job.BusinessKey, 1, (_, count) => count + 1);
        var collides = job.BusinessKey.StartsWith(CollideAlwaysPrefix, StringComparison.Ordinal)
            || (job.BusinessKey.StartsWith(CollideTwicePrefix, StringComparison.Ordinal) && call <= 2);

        if (collides)
        {
            // Wrapped as EF Core's execution strategy wraps it, so the worker has to look inside.
            throw new InvalidOperationException(
                "An exception has been raised that is likely due to a transient failure.",
                new PostgresException("could not serialize access due to read/write dependencies among transactions", "ERROR", "ERROR", "40001"));
        }

        return Task.CompletedTask;
    }
}
