using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.World;

/// <summary>
/// Materialises a provisioning job for every pending tier request (`PYR-4`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The request row is created by a takeover (`PYR-2`); this materialiser is what turns it into work. It
/// derives the business key from the country and target tier, so enqueuing the same request twice inserts
/// once and a request that a retried run has already completed simply finds its job gone.
/// </para>
/// <para>
/// Worker-only, like the other schedulers: pyramid growth is a consequence of play, never of a client
/// command, and only the worker may run it (ADR-0001, ADR-0008). Nothing here is reachable from a command.
/// </para>
/// </remarks>
internal sealed partial class ProvisioningScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ProvisioningOptions _options;
    private readonly ILogger<ProvisioningScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public ProvisioningScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<ProvisioningOptions> options,
        ILogger<ProvisioningScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableProvisioning)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed materialisation must not kill the loop: the next pass re-derives the same requests.
                LogFailed(exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var requests = scope.ServiceProvider.GetRequiredService<IDivisionProvisioningRequestRepository>();

        foreach (var request in await requests.ListPendingAsync(cancellationToken))
        {
            var inserted = await queue.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = ProvisionDivisionJobHandler.TypeName,
                    BusinessKey = ProvisioningJobTypes.ProvisionKey(request.CountryId, request.TargetTier),
                    PayloadJson = ProvisioningJobPayload.For(request.Id),
                    DueAt = now,
                },
                cancellationToken);

            if (inserted)
            {
                LogScheduled(request.CountryId, request.TargetTier);
            }
        }
    }

    [LoggerMessage(
        EventId = 3300,
        Level = LogLevel.Information,
        Message = "Provisioning is disabled; no tier-provisioning job will be materialised (PYR-4).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3301,
        Level = LogLevel.Information,
        Message = "The provisioning scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3302,
        Level = LogLevel.Information,
        Message = "Materialised provisioning of tier {TargetTier} for country {CountryId}.")]
    private partial void LogScheduled(Guid countryId, int targetTier);

    [LoggerMessage(
        EventId = 3303,
        Level = LogLevel.Error,
        Message = "The provisioning scheduler failed to materialise pending requests.")]
    private partial void LogFailed(Exception exception);
}
