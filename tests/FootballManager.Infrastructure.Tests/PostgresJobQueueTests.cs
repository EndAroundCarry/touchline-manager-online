using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FootballManager.Infrastructure.Tests;

public sealed class PostgresJobQueueTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private GameDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = CreateDbContext();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private GameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new GameDbContext(options, new SystemClock());
    }

    [Fact]
    public async Task Enqueue_is_idempotent_for_same_business_key()
    {
        var queue = new PostgresJobQueue(_db, new SystemClock());
        var request = new JobRequest("ops.noop", "business-key-1");

        var firstId = await queue.EnqueueAsync(request);
        var secondId = await queue.EnqueueAsync(request);

        secondId.Should().Be(firstId);
        var jobs = await _db.Jobs.AsNoTracking().Where(j => j.BusinessKey == "business-key-1").ToListAsync();
        jobs.Should().HaveCount(1);
        jobs[0].Status.Should().Be(JobStatus.Pending);
        jobs[0].Version.Should().Be(1);
    }

    [Fact]
    public async Task Claim_leases_due_job_and_bumps_attempt()
    {
        var queue = new PostgresJobQueue(_db, new SystemClock());
        var jobId = await queue.EnqueueAsync(new JobRequest("ops.noop", "claim-key"));
        var owner = Guid.NewGuid();

        var claimed = await _db.ClaimNextJobAsync(owner, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);

        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(jobId);
        claimed.Status.Should().Be(JobStatus.Leased);
        claimed.LeaseOwner.Should().Be(owner);
        claimed.AttemptCount.Should().Be(1);

        var again = await _db.ClaimNextJobAsync(Guid.NewGuid(), TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);
        again.Should().BeNull();
    }

    [Fact]
    public async Task Claim_recovers_expired_lease()
    {
        var queue = new PostgresJobQueue(_db, new SystemClock());
        await queue.EnqueueAsync(new JobRequest("ops.noop", "expired-lease-key"));
        await _db.ClaimNextJobAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(1), DateTimeOffset.UtcNow);

        await Task.Delay(50);

        var recovered = await _db.ClaimNextJobAsync(Guid.NewGuid(), TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);

        recovered.Should().NotBeNull();
        recovered!.BusinessKey.Should().Be("expired-lease-key");
        recovered.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task Success_and_failure_updates_require_matching_lease_owner()
    {
        var queue = new PostgresJobQueue(_db, new SystemClock());
        await queue.EnqueueAsync(new JobRequest("ops.noop", "ownership-key"));
        var owner = Guid.NewGuid();
        var claimed = await _db.ClaimNextJobAsync(owner, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);

        var wrongOwner = await _db.TryMarkSucceededAsync(claimed!.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        wrongOwner.Should().BeFalse();

        var rightOwner = await _db.TryMarkSucceededAsync(claimed.Id, owner, DateTimeOffset.UtcNow);
        rightOwner.Should().BeTrue();

        var job = await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == claimed.Id);
        job.Status.Should().Be(JobStatus.Succeeded);
        job.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task Failing_job_returns_to_pending_with_backoff_until_dead_letter()
    {
        var queue = new PostgresJobQueue(_db, new SystemClock(), maxAttempts: 2);
        await queue.EnqueueAsync(new JobRequest("ops.noop", "retry-key"));
        var owner = Guid.NewGuid();
        var claimed = await _db.ClaimNextJobAsync(owner, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);

        var now = DateTimeOffset.UtcNow;
        await _db.TryFailAsync(claimed!.Id, owner, "boom", dead: false, now.AddSeconds(30), now);

        var retried = await _db.ClaimNextJobAsync(owner, TimeSpan.FromMinutes(1), now.AddSeconds(60));
        retried.Should().NotBeNull();
        retried!.AttemptCount.Should().Be(2);

        await _db.TryFailAsync(retried.Id, owner, "boom again", dead: true, now.AddMinutes(5), now);

        var job = await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == retried.Id);
        job.Status.Should().Be(JobStatus.Dead);
        job.LastError.Should().Be("boom again");
    }
}
