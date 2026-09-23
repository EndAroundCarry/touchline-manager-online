using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Jobs;

/// <summary>
/// PostgreSQL-backed durable job queue (ADR-0003).
/// </summary>
/// <remarks>
/// <para>
/// Claims use <c>FOR UPDATE SKIP LOCKED</c> so concurrent workers never block on each other, and
/// leases expire so a crashed worker's work is retried rather than lost. Delivery is
/// at-least-once, therefore every handler must be idempotent.
/// </para>
/// <para>
/// Commands run on the context's own connection, so an enqueue performed inside a domain
/// transaction participates in that transaction. This is what makes "committed the tenure but
/// lost the follow-up job" impossible.
/// </para>
/// </remarks>
internal sealed partial class PostgresJobQueue : IJobQueue
{
    private const string EnqueueSql = """
        insert into ops.jobs (
            id, job_type, business_key, payload, due_at, priority,
            status, attempt_count, max_attempts, created_at, updated_at, version)
        values (
            @id, @job_type, @business_key, cast(@payload as jsonb), @due_at, @priority,
            'pending', 0, @max_attempts, @now, @now, 1)
        on conflict (job_type, business_key) do nothing;
        """;

    private const string ClaimSql = """
        with ready as (
            select id
            from ops.jobs
            where (status = 'pending' and due_at <= @now)
               or (status = 'leased' and lease_until < @now)
            order by priority asc, due_at asc, created_at asc
            for update skip locked
            limit @batch
        )
        update ops.jobs as job
        set status = 'leased',
            lease_owner = @lease_owner,
            lease_until = @lease_until,
            attempt_count = job.attempt_count + 1,
            updated_at = @now,
            version = job.version + 1
        from ready
        where job.id = ready.id
        returning job.id, job.job_type, job.business_key, job.payload::text, job.attempt_count, job.max_attempts;
        """;

    private const string ReadAttemptSql = """
        select attempt_count, max_attempts
        from ops.jobs
        where id = @id and status = 'leased';
        """;

    private const string CompleteSql = """
        update ops.jobs
        set status = 'completed',
            completed_at = @now,
            lease_owner = null,
            lease_until = null,
            updated_at = @now,
            version = version + 1
        where id = @id and status = 'leased';
        """;

    private const string RescheduleSql = """
        update ops.jobs
        set status = 'pending',
            due_at = @due_at,
            last_error = @error,
            lease_owner = null,
            lease_until = null,
            updated_at = @now,
            version = version + 1
        where id = @id and status = 'leased';
        """;

    private const string DeadLetterSql = """
        update ops.jobs
        set status = 'dead_letter',
            last_error = @error,
            lease_owner = null,
            lease_until = null,
            updated_at = @now,
            version = version + 1
        where id = @id and status = 'leased';
        """;

    private readonly TouchlineManagerDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<PostgresJobQueue> _logger;

    /// <summary>Initializes the queue.</summary>
    public PostgresJobQueue(
        TouchlineManagerDbContext dbContext,
        IClock clock,
        ILogger<PostgresJobQueue> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = UtcNow();
        var connection = await GetOpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = EnqueueSql;
        AddParameter(command, "@id", Guid.CreateVersion7());
        AddParameter(command, "@job_type", request.JobType);
        AddParameter(command, "@business_key", request.BusinessKey);
        AddParameter(command, "@payload", request.PayloadJson);
        AddParameter(command, "@due_at", request.DueAt.ToUniversalTime());
        AddParameter(command, "@priority", request.Priority);
        AddParameter(command, "@max_attempts", request.MaxAttempts);
        AddParameter(command, "@now", now);

        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);

        if (inserted == 0)
        {
            LogAlreadyEnqueued(request.JobType, request.BusinessKey);
        }

        return inserted > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeasedJob>> ClaimAsync(
        string leaseOwner,
        int maxJobs,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxJobs, 1);

        var now = UtcNow();
        var connection = await GetOpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = ClaimSql;
        AddParameter(command, "@now", now);
        AddParameter(command, "@batch", maxJobs);
        AddParameter(command, "@lease_owner", leaseOwner);
        AddParameter(command, "@lease_until", now.Add(leaseDuration));

        var claimed = new List<LeasedJob>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new LeasedJob(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return claimed;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var connection = await GetOpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = CompleteSql;
        AddParameter(command, "@id", jobId);
        AddParameter(command, "@now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailAsync(
        Guid jobId,
        string errorMessage,
        JobFailureKind kind,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var connection = await GetOpenConnectionAsync(cancellationToken);

        var attempts = await ReadAttemptsAsync(connection, jobId, cancellationToken);

        // A permanent domain failure is dead-lettered at once: retrying it would only delay the
        // operations alert by burning through the retry budget (ADR-0003).
        var deadLetter = kind == JobFailureKind.Permanent
            || attempts is null
            || JobRetryPolicy.IsPermanentFailure(attempts.Value.AttemptCount, attempts.Value.MaxAttempts);

        await using var command = connection.CreateCommand();

        if (deadLetter)
        {
            command.CommandText = DeadLetterSql;
            AddParameter(command, "@id", jobId);
            AddParameter(command, "@error", Truncate(errorMessage));
            AddParameter(command, "@now", now);

            await command.ExecuteNonQueryAsync(cancellationToken);

            LogDeadLettered(jobId, attempts?.AttemptCount ?? 0);
        }
        else
        {
            var nextDue = now.Add(JobRetryPolicy.NextDelay(attempts!.Value.AttemptCount, Random.Shared.NextDouble()));

            command.CommandText = RescheduleSql;
            AddParameter(command, "@id", jobId);
            AddParameter(command, "@due_at", nextDue);
            AddParameter(command, "@error", Truncate(errorMessage));
            AddParameter(command, "@now", now);

            await command.ExecuteNonQueryAsync(cancellationToken);

            LogRetryScheduled(jobId, attempts.Value.AttemptCount, attempts.Value.MaxAttempts, nextDue);
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000];

    private static async Task<(int AttemptCount, int MaxAttempts)?> ReadAttemptsAsync(
        DbConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ReadAttemptSql;
        AddParameter(command, "@id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private DateTimeOffset UtcNow() => _clock.UtcNow.ToUniversalTime();

    /// <summary>
    /// Source-generated logging so arguments are not evaluated or formatted when the level is
    /// disabled. The worker logs a lot; this keeps disabled levels free.
    /// </summary>
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Job {JobType}/{BusinessKey} already enqueued; enqueue is idempotent.")]
    private partial void LogAlreadyEnqueued(string jobType, string businessKey);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Job {JobId} dead-lettered after {AttemptCount} attempts. Operations must review it.")]
    private partial void LogDeadLettered(Guid jobId, int attemptCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Job {JobId} failed on attempt {AttemptCount} of {MaxAttempts}; retrying at {NextDue}.")]
    private partial void LogRetryScheduled(Guid jobId, int attemptCount, int maxAttempts, DateTimeOffset nextDue);

    /// <summary>
    /// Returns the context's connection in an open state. The connection is deliberately not
    /// disposed: the context owns it, and disposing it here would break an ambient transaction.
    /// </summary>
    private async Task<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }
}
