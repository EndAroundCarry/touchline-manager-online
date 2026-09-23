using FootballManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FootballManager.Infrastructure.Ops;

public static class JobStore
{
    public static async Task<JobRecord?> ClaimNextJobAsync(
        this GameDbContext db,
        Guid leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var candidates = await db.Jobs
            .FromSqlInterpolated($"""
                SELECT id, job_type, business_key, payload_json, due_at, priority, status,
                       attempt_count, max_attempts, lease_owner, lease_until, last_error,
                       created_at, updated_at, version
                FROM ops.jobs
                WHERE (status = {JobStatus.Pending} AND due_at <= {now})
                   OR (status = {JobStatus.Leased} AND lease_until IS NOT NULL AND lease_until <= {now})
                ORDER BY priority DESC, due_at ASC, created_at ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var job = candidates[0];
        job.Status = JobStatus.Leased;
        job.LeaseOwner = leaseOwner;
        job.LeaseUntil = now + leaseDuration;
        job.AttemptCount += 1;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public static async Task<bool> TryMarkSucceededAsync(
        this GameDbContext db,
        Guid jobId,
        Guid leaseOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE ops.jobs
            SET status = {JobStatus.Succeeded}, lease_owner = NULL, lease_until = NULL,
                last_error = NULL, updated_at = {now}, version = version + 1
            WHERE id = {jobId} AND lease_owner = {leaseOwner} AND status = {JobStatus.Leased}
            """, cancellationToken);
        return affected == 1;
    }

    public static async Task<bool> TryFailAsync(
        this GameDbContext db,
        Guid jobId,
        Guid leaseOwner,
        string error,
        bool dead,
        DateTimeOffset nextDueAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var truncated = error.Length <= 2000 ? error : error[..2000];
        var status = dead ? JobStatus.Dead : JobStatus.Pending;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE ops.jobs
            SET status = {status}, lease_owner = NULL, lease_until = NULL,
                last_error = {truncated}, due_at = {nextDueAt}, updated_at = {now}, version = version + 1
            WHERE id = {jobId} AND lease_owner = {leaseOwner} AND status = {JobStatus.Leased}
            """, cancellationToken);
        return affected == 1;
    }
}
