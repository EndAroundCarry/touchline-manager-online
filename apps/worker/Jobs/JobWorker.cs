using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;

namespace FootballManager.Worker.Jobs;

public sealed class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly JobWorkerOptions _options;
    private readonly ILogger<JobWorker> _logger;

    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        JobWorkerOptions options,
        ILogger<JobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseOwner = Guid.NewGuid();
        _logger.LogInformation(
            "Job worker started (lease {LeaseSeconds}s, poll {PollMs}ms)",
            _options.LeaseDuration.TotalSeconds,
            _options.PollInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await ProcessOneAsync(leaseOwner, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job worker loop failed");
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Job worker stopped");
    }

    private async Task<bool> ProcessOneAsync(Guid leaseOwner, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();

        var job = await db.ClaimNextJobAsync(leaseOwner, _options.LeaseDuration, _clock.UtcNow, cancellationToken);
        if (job is null)
        {
            return false;
        }

        try
        {
            var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType)
                ?? throw new InvalidOperationException($"No job handler registered for job type '{job.JobType}'.");

            var context = new JobContext(job.Id, job.JobType, job.BusinessKey, job.PayloadJson, job.AttemptCount);
            await handler.HandleAsync(context, cancellationToken);
            await db.TryMarkSucceededAsync(job.Id, leaseOwner, _clock.UtcNow, cancellationToken);
            _logger.LogInformation(
                "Job {JobType}/{BusinessKey} succeeded (attempt {Attempt})",
                job.JobType,
                job.BusinessKey,
                job.AttemptCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var dead = job.AttemptCount >= job.MaxAttempts;
            var now = _clock.UtcNow;
            await db.TryFailAsync(
                job.Id,
                leaseOwner,
                ex.ToString(),
                dead,
                now + ComputeBackoff(job.AttemptCount),
                now,
                cancellationToken);
            _logger.LogError(
                ex,
                "Job {JobType}/{BusinessKey} failed (attempt {Attempt}/{MaxAttempts}); {Outcome}",
                job.JobType,
                job.BusinessKey,
                job.AttemptCount,
                job.MaxAttempts,
                dead ? "dead-lettered" : "scheduled for retry");
        }

        return true;
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var exponent = Math.Max(0, attempt - 1);
        var seconds = Math.Min(_options.RetryBaseSeconds * Math.Pow(2, exponent), _options.RetryMaxSeconds);
        var jitter = Random.Shared.NextDouble() * _options.RetryBaseSeconds;
        return TimeSpan.FromSeconds(seconds + jitter);
    }
}
