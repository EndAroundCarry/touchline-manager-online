using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Tests;

/// <summary>
/// Exercises the real queue against real PostgreSQL.
/// </summary>
/// <remarks>
/// The behaviours that matter here — enqueue idempotency from a unique index, claim ordering with
/// <c>FOR UPDATE SKIP LOCKED</c>, and recovery from an expired lease — cannot be observed through an
/// in-memory provider, so they are only meaningful against PostgreSQL (master plan §15.2).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresJobQueueTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly PostgresFixture _fixture;

    public PostgresJobQueueTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Enqueue_inserts_a_pending_job_with_attempts_at_zero()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("enqueue");

        var inserted = await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);

        inserted.Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var job = await db.Jobs.SingleAsync(j => j.BusinessKey == businessKey);

        job.JobType.Should().Be("ops.test");
        job.Status.Should().Be("pending");
        job.AttemptCount.Should().Be(0);
        job.MaxAttempts.Should().Be(JobRetryPolicy.DefaultMaxAttempts);
        job.Payload.Should().Be("{}");
        job.LeaseOwner.Should().BeNull();
        job.LeaseUntil.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Version.Should().Be(1);
    }

    [Fact]
    public async Task Enqueue_is_idempotent_for_the_same_job_type_and_business_key()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("idempotent");

        var first = await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        var second = await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse("re-enqueueing the same business action must be a no-op (ADR-0003)");

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        (await db.Jobs.CountAsync(j => j.BusinessKey == businessKey)).Should().Be(1);
    }

    [Fact]
    public async Task Claim_takes_a_due_job_and_records_the_lease()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("claim");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);

        var claimed = await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None);
        var job = claimed.Single(candidate => candidate.BusinessKey == businessKey);

        job.AttemptCount.Should().Be(1);
        job.JobType.Should().Be("ops.test");

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(j => j.Id == job.Id);

        row.Status.Should().Be("leased");
        row.LeaseOwner.Should().Be("worker-a");
        row.LeaseUntil.Should().Be(_fixture.Clock.UtcNow.Add(LeaseDuration));
    }

    [Fact]
    public async Task A_leased_job_is_not_claimed_again_before_its_lease_expires()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("lease-held");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var secondClaim = await queue.ClaimAsync("worker-b", maxJobs: 100, LeaseDuration, CancellationToken.None);

        secondClaim.Should().NotContain(job => job.BusinessKey == businessKey);
    }

    [Fact]
    public async Task An_expired_lease_becomes_claimable_again()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("lease-expired");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None);

        // The worker crashed. Its lease lapses, and the work must be retried rather than lost.
        _fixture.Clock.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

        var reclaimed = await queue.ClaimAsync("worker-b", maxJobs: 100, LeaseDuration, CancellationToken.None);
        var job = reclaimed.Single(candidate => candidate.BusinessKey == businessKey);

        job.AttemptCount.Should().Be(2, "re-claiming the same row is a second attempt at the same business action");

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        (await db.Jobs.SingleAsync(j => j.Id == job.Id)).LeaseOwner.Should().Be("worker-b");
    }

    [Fact]
    public async Task A_future_due_job_is_not_claimed_early()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("not-due");

        await queue.EnqueueAsync(
            Request(businessKey, dueAt: _fixture.Clock.UtcNow.AddHours(1)),
            CancellationToken.None);

        var claimed = await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None);

        claimed.Should().NotContain(job => job.BusinessKey == businessKey);
    }

    [Fact]
    public async Task Completing_a_job_makes_it_terminal()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("complete");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        var job = (await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None))
            .Single(candidate => candidate.BusinessKey == businessKey);

        await queue.CompleteAsync(job.Id, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(j => j.Id == job.Id);

        row.Status.Should().Be("completed");
        row.CompletedAt.Should().NotBeNull();
        row.LeaseOwner.Should().BeNull();
        row.LeaseUntil.Should().BeNull();

        var afterCompletion = await queue.ClaimAsync("worker-b", maxJobs: 100, LeaseDuration, CancellationToken.None);
        afterCompletion.Should().NotContain(candidate => candidate.Id == job.Id);
    }

    [Fact]
    public async Task A_transient_failure_reschedules_within_the_retry_budget()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("transient");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        var job = (await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None))
            .Single(candidate => candidate.BusinessKey == businessKey);

        await queue.FailAsync(job.Id, "connection reset", JobFailureKind.Transient, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(j => j.Id == job.Id);

        row.Status.Should().Be("pending");
        row.LastError.Should().Be("connection reset");
        row.DueAt.Should().BeAfter(_fixture.Clock.UtcNow, "retrying instantly would stampede a failing dependency");
        row.DueAt.Should().BeOnOrBefore(_fixture.Clock.UtcNow.Add(JobRetryPolicy.MaxDelay));
        row.LeaseOwner.Should().BeNull();
        row.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public async Task A_permanent_failure_is_dead_lettered_without_retrying()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("permanent");

        await queue.EnqueueAsync(Request(businessKey), CancellationToken.None);
        var job = (await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None))
            .Single(candidate => candidate.BusinessKey == businessKey);

        await queue.FailAsync(job.Id, "no handler is registered", JobFailureKind.Permanent, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var row = await db.Jobs.SingleAsync(j => j.Id == job.Id);

        row.Status.Should().Be("dead_letter");
        row.LastError.Should().Be("no handler is registered");
        row.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task Exhausting_the_attempt_budget_dead_letters_the_job()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var businessKey = NewKey("exhausted");

        await queue.EnqueueAsync(Request(businessKey) with { MaxAttempts = 1 }, CancellationToken.None);

        var job = (await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None))
            .Single(candidate => candidate.BusinessKey == businessKey);

        job.AttemptCount.Should().Be(1);
        job.MaxAttempts.Should().Be(1);

        await queue.FailAsync(job.Id, "still failing", JobFailureKind.Transient, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        (await db.Jobs.SingleAsync(j => j.Id == job.Id)).Status.Should().Be("dead_letter");
    }

    [Fact]
    public async Task Claimed_jobs_are_returned_in_priority_then_due_order()
    {
        await using var scope = _fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var lowPriority = NewKey("priority-low");
        var highPriority = NewKey("priority-high");

        await queue.EnqueueAsync(Request(lowPriority, priority: 5), CancellationToken.None);
        await queue.EnqueueAsync(Request(highPriority, priority: 1), CancellationToken.None);

        var claimed = await queue.ClaimAsync("worker-a", maxJobs: 100, LeaseDuration, CancellationToken.None);

        var relevant = claimed
            .Where(job => job.BusinessKey == lowPriority || job.BusinessKey == highPriority)
            .Select(job => job.BusinessKey)
            .ToList();

        relevant.Should().Equal(highPriority, lowPriority);
    }

    [Fact]
    public async Task The_database_rejects_a_lease_without_an_owner()
    {
        // The check constraint is what makes "leased but unowned" impossible, so a crashed worker
        // cannot leave a row nobody can ever claim.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var businessKey = NewKey("constraint");

        db.Jobs.Add(new OpsJob
        {
            Id = Guid.CreateVersion7(),
            JobType = "ops.test",
            BusinessKey = businessKey,
            Payload = "{}",
            DueAt = _fixture.Clock.UtcNow,
            Status = "leased",
            MaxAttempts = 3,
            CreatedAt = _fixture.Clock.UtcNow,
            UpdatedAt = _fixture.Clock.UtcNow,
            Version = 1,
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private JobEnqueueRequest Request(
        string businessKey,
        DateTimeOffset? dueAt = null,
        short priority = 0)
        => new()
        {
            JobType = "ops.test",
            BusinessKey = businessKey,
            DueAt = dueAt ?? _fixture.Clock.UtcNow.AddMinutes(-1),
            Priority = priority,
        };

    private static string NewKey(string prefix) => $"{prefix}:{Guid.NewGuid():N}";
}
