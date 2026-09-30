using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.Market;

/// <summary>
/// Materialises a resolution job for every listing whose window has arrived (`TRF-2`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materialiser's job is to make each due listing's row exist, so the row's <c>due_at</c> — the listing's
/// window — is the deadline and the worker holds no deadline authority itself (ADR-0003). If the worker is
/// down at the window the row waits and runs late rather than being skipped, and a restart re-derives the due
/// listings from the clock instead of remembering where it was.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// first pass for a listing. The rows are never deleted, so a settled listing is simply not re-inserted, and
/// the resolution's own listing-status check covers the rest.
/// </para>
/// </remarks>
internal sealed partial class AuctionScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly MarketOptions _options;
    private readonly ILogger<AuctionScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public AuctionScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<MarketOptions> options,
        ILogger<AuctionScheduler> logger)
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
        if (!_options.EnableAuctions)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaterializeAsync(_clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed materialisation must not kill the loop: the next pass re-derives the same listings.
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

    /// <inheritdoc />
    public async Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.EnableAuctions)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var listings = scope.ServiceProvider.GetRequiredService<IListingRepository>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var due = await listings.ListDueAsync(now, cancellationToken);
        var inserted = 0;

        foreach (var listing in due)
        {
            if (await queue.EnqueueAsync(
                    new JobEnqueueRequest
                    {
                        JobType = AuctionJobTypes.Resolve,
                        BusinessKey = AuctionJobTypes.ResolveKey(listing.Id),
                        DueAt = listing.EndsAt,
                        PayloadJson = AuctionJobPayload.For(listing.Id),
                    },
                    cancellationToken))
            {
                inserted++;
            }
        }

        if (inserted > 0)
        {
            LogScheduled(inserted);
        }
    }

    [LoggerMessage(
        EventId = 3600,
        Level = LogLevel.Information,
        Message = "The transfer-auction resolver is disabled; no resolution job will be materialised (TRF-2).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3601,
        Level = LogLevel.Information,
        Message = "The transfer-auction scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3602,
        Level = LogLevel.Information,
        Message = "Materialised {Count} transfer-auction resolution job(s) (TRF-2).")]
    private partial void LogScheduled(int count);

    [LoggerMessage(
        EventId = 3603,
        Level = LogLevel.Error,
        Message = "The transfer-auction scheduler failed to materialise the due resolutions.")]
    private partial void LogFailed(Exception exception);
}
