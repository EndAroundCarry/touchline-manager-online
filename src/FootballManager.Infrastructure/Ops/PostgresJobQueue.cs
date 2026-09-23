using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FootballManager.Infrastructure.Ops;

public sealed class PostgresJobQueue : IJobQueue
{
    private readonly GameDbContext _db;
    private readonly IClock _clock;
    private readonly int _maxAttempts;

    public PostgresJobQueue(GameDbContext db, IClock clock, int maxAttempts = 5)
    {
        _db = db;
        _clock = clock;
        _maxAttempts = maxAttempts;
    }

    public async Task<Guid> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.BusinessKey))
        {
            throw new ArgumentException("Business key is required.", nameof(request));
        }

        var job = new JobRecord
        {
            Id = Guid.NewGuid(),
            JobType = request.JobType,
            BusinessKey = request.BusinessKey,
            PayloadJson = request.PayloadJson,
            DueAt = request.DueAt ?? _clock.UtcNow,
            Priority = request.Priority,
            Status = JobStatus.Pending,
            MaxAttempts = _maxAttempts,
        };

        _db.Jobs.Add(job);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return job.Id;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(job).State = EntityState.Detached;
            return await _db.Jobs
                .Where(j => j.JobType == request.JobType && j.BusinessKey == request.BusinessKey)
                .Select(j => j.Id)
                .SingleAsync(cancellationToken);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
}
