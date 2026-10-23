using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Ops;

namespace Tactical11Manager.Infrastructure.Jobs;

/// <summary>
/// Claims and executes durable jobs.
/// </summary>
/// <remarks>
/// <para>
/// This service is the only place in the product that advances a deadline. It claims with row
/// leases, so several workers can run at once without coordinating; safety comes from the
/// database, not from being a single instance (ADR-0003).
/// </para>
/// <para>
/// Each job runs in its own scope so a slow or failing job cannot leak state into the next one.
/// Bounded parallelism matches the architecture rule: independent jobs run concurrently, while
/// each unit of work stays single-threaded.
/// </para>
/// </remarks>
public sealed partial class JobQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobQueueOptions _options;
    private readonly ILogger<JobQueueWorker> _logger;
    /// <summary>How many times a job that collided with another transaction is run again at once, before the queue's backoff takes over.</summary>
    private const int ConflictRetries = 5;

    /// <summary>The longest pause before such a rerun, in milliseconds; each one waits a random time up to this, times the rerun's number.</summary>
    private const int ConflictPauseMaxMilliseconds = 400;

    private readonly string _leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>Initializes the worker.</summary>
    public JobQueueWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<JobQueueOptions> options,
        ILogger<JobQueueWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LogStartupAsync();

        var idleSeconds = _options.PollIntervalSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;

            try
            {
                claimed = await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The queue itself failed. Log and keep polling: a database blip must not stop
                // the worker permanently.
                LogBatchFailed(ex, ex.Message);
            }

            if (claimed > 0)
            {
                // Work is available: poll again immediately rather than waiting out the interval.
                idleSeconds = _options.PollIntervalSeconds;

                continue;
            }

            idleSeconds = Math.Min(idleSeconds * 2, _options.MaxIdlePollIntervalSeconds);

            await DelayAsync(idleSeconds, stoppingToken);
        }

        LogStopped(_leaseOwner);
    }

    private async Task<int> RunBatchAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<LeasedJob> jobs;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            jobs = await queue.ClaimAsync(
                _leaseOwner,
                _options.MaxConcurrentJobs,
                TimeSpan.FromSeconds(_options.LeaseSeconds),
                stoppingToken);
        }

        if (jobs.Count == 0)
        {
            return 0;
        }

        LogClaimed(jobs.Count);

        await Parallel.ForEachAsync(
            jobs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentJobs,
                CancellationToken = stoppingToken,
            },
            async (job, token) => await ExecuteJobAsync(job, token));

        return jobs.Count;
    }

    private async Task ExecuteJobAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        // Resolve the registry from a scope rather than capturing it: handlers touch the scoped unit of
        // work, and a handler invoked with a stale context would corrupt state. The handler itself runs in
        // a scope of its own, so a rerun after a collision starts clean.
        var handlers = scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>();

        if (!handlers.TryGet(job.JobType, out _))
        {
            // An unknown job type is a deployment or configuration defect, not a transient
            // failure: dead-letter it so operations sees it instead of it retrying forever.
            await queue.FailAsync(
                job.Id,
                $"No handler is registered for job type '{job.JobType}'.",
                JobFailureKind.Permanent,
                cancellationToken);

            LogHandlerMissing(job.JobType, job.Id);

            return;
        }

        try
        {
            await HandleThroughConflictsAsync(job, cancellationToken);
            await queue.CompleteAsync(job.Id, cancellationToken);

            LogCompleted(job.JobType, job.BusinessKey, job.Id, job.AttemptCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a business failure. Leave the job leased so it becomes claimable
            // again when the lease expires (ADR-0003).
            throw;
        }
        catch (PermanentJobFailureException ex)
        {
            await queue.FailAsync(job.Id, ex.Message, JobFailureKind.Permanent, cancellationToken);

            LogPermanentFailure(job.JobType, job.BusinessKey, job.Id, ex.Message);
        }
        catch (Exception ex)
        {
            await queue.FailAsync(job.Id, ex.Message, JobFailureKind.Transient, cancellationToken);

            LogTransientFailure(job.JobType, job.BusinessKey, job.Id, ex.Message);
        }
    }

    /// <summary>
    /// Runs the job's handler, and runs it again at once when it lost a collision with another transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A serialization failure or a deadlock rolls the whole transaction back and asks for it to be run again, so it is not a failure of the job.
    /// Handing it to the queue would cost the job a retry and a backoff of half a minute or more; with a dozen divisions publishing at one
    /// kickoff that is a dozen rounds published late, and on a stepped clock, where the queue retries at once, the colliding jobs burn their whole
    /// attempt budget against each other in seconds. A short random pause spreads the reruns so they stop meeting.
    /// </para>
    /// <para>
    /// Each attempt gets a fresh scope: the failed one leaves its unit of work holding entities from a rolled-back transaction, and reusing it
    /// would save them a second time. Every handler is already idempotent, because the queue delivers at least once (ADR-0003), so running one
    /// again here is no different from the queue retrying it.
    /// </para>
    /// </remarks>
    private async Task HandleThroughConflictsAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        for (var rerun = 0; ; rerun++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>().TryGet(job.JobType, out var handler);

                await handler.HandleAsync(job, cancellationToken);

                return;
            }
            catch (Exception ex) when (rerun < ConflictRetries && SerializationConflict.IsConflict(ex))
            {
                LogConflictRerun(job.JobType, job.BusinessKey, job.Id, rerun + 1, ConflictRetries);

                await Task.Delay(Random.Shared.Next(1, ConflictPauseMaxMilliseconds + 1) * (rerun + 1), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Resolves the handler registry once at startup.
    /// </summary>
    /// <remarks>
    /// Building the registry validates that no job type is claimed twice. Doing it here turns a
    /// duplicate registration into an immediate startup failure rather than silently ignored work
    /// the first time that job type is due.
    /// </remarks>
    private async Task LogStartupAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>();

        LogStarted(_leaseOwner, handlers.Count, _options.MaxConcurrentJobs, _options.PollIntervalSeconds);
    }

    private static async Task DelayAsync(int seconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Job queue worker {LeaseOwner} started with {HandlerCount} handler(s), max concurrency {MaxConcurrency}, base poll interval {PollIntervalSeconds}s.")]
    private partial void LogStarted(string leaseOwner, int handlerCount, int maxConcurrency, int pollIntervalSeconds);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Job queue worker {LeaseOwner} stopped.")]
    private partial void LogStopped(string leaseOwner);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Claimed {JobCount} job(s).")]
    private partial void LogClaimed(int jobCount);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Completed job {JobType} ({BusinessKey}) id {JobId} on attempt {AttemptCount}.")]
    private partial void LogCompleted(string jobType, string businessKey, Guid jobId, int attemptCount);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Warning,
        Message = "Job {JobType} ({BusinessKey}) id {JobId} failed transiently: {Reason}")]
    private partial void LogTransientFailure(string jobType, string businessKey, Guid jobId, string reason);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Error,
        Message = "Job {JobType} ({BusinessKey}) id {JobId} dead-lettered permanently: {Reason}")]
    private partial void LogPermanentFailure(string jobType, string businessKey, Guid jobId, string reason);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "No handler is registered for job type {JobType}; job {JobId} dead-lettered.")]
    private partial void LogHandlerMissing(string jobType, Guid jobId);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "The job queue batch failed: {Reason}")]
    private partial void LogBatchFailed(Exception exception, string reason);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Information,
        Message = "Job {JobType} ({BusinessKey}) id {JobId} collided with another transaction; running it again ({Rerun} of {MaxReruns}).")]
    private partial void LogConflictRerun(string jobType, string businessKey, Guid jobId, int rerun, int maxReruns);
}
